using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.DorsalRadome", "DorsalRadome", "0.0.1")]
    [BepInDependency("com.Spiny.ECMUpgrade")]
    internal class SparkyDome : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal const string RadomeMountKey = "Radome1";
        internal const string ClonedMountKey = "DorsalRadome1";
        internal const string ClonedMountName = "Dorsal Radome";
        internal const string ClonedMountShort = "RADOME";
        internal const string ClonedMountInfoKey = "DorsalRadome1Info";
        internal const string ClonedMountDesc =
            "Powerful long-range all-aspect radar, capable of spotting stealhier aircraft and planes as low as 5m (16f).";

        internal static float x = 0f;
        internal static float y = 0.6f;
        internal static float z = -3.0f;

        // Local position of the radome hardpoint relative to fuselage_F
        // X=0 (centerline), Y=0.8 (dorsal), Z=-1.0 (places it just behind the cockpit for now
        private static readonly Vector3 RadomeLocalPos = new Vector3(x, y, z);
        private static readonly Quaternion RadomeLocalRot = Quaternion.Euler(0f, 180f, 0f);


        internal static WeaponMount clonedMount = null;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("DorsalRadome loading...");

            Harmony harmony = new("com.Spiny.DorsalRadome");

            try
            {
                var method = typeof(Encyclopedia).GetMethod("AfterLoad",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, Type.EmptyTypes, null);

                if (method != null)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(EncyclopediaPatch), "Prefix"));
                    Log.LogInfo("Patched Encyclopedia.AfterLoad (PREFIX)");
                }
                else Log.LogError("Could not find Encyclopedia.AfterLoad.");
            }
            catch (Exception e) { Log.LogError("Failed to patch Encyclopedia.AfterLoad: " + e); }

            Log.LogInfo("DorsalRadome loaded.");
        }

        /// <summary>
        /// Finds a specific aircraft definition based off of the internal name. IE: Multirole 1.
        /// </summary>
        /// <param name="enc">Encyclopedia instance</param>
        /// <param name="AircraftName">Internal name of the aircraft.</param>
        /// <returns>Aircraft Definition based off of the name provided. Null if not found.</returns>
        public static AircraftDefinition findAircraftDefinition(Encyclopedia enc, String AircraftName)
        {
            foreach (var def in enc.aircraft)
            {
                if (def != null && def.name.IndexOf(AircraftName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return def;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a specific weaponMount object from the encyclopedia based off of the internal JSON key
        /// </summary>
        /// <param name="enc">Encyclopedia instance</param>
        /// <param name="key">internal JSON key for the weaponMount</param>
        /// <returns>weaponMount based off of the JSON key, null if none found.</returns>
        public static WeaponMount findMount(Encyclopedia enc, String key)
        {
            foreach (var m in enc.weaponMounts)
            {
                if (m != null && m.jsonKey == key)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>
        /// Clones Radome1 into a new WeaponMount and registers it, then adds a new
        /// HardpointSet to the Ifrit's WeaponManager with a single dorsal hardpoint.
        /// The hardpoint transform is created as a child of fuselage_F at RadomeLocalPos.
        /// </summary>
        public class EncyclopediaPatch
        {
            public static void Prefix(Encyclopedia __instance)
            {
                if (clonedMount != null) return;

                try
                {
                    //Find ifrit definition
                    AircraftDefinition ifrit = findAircraftDefinition(__instance, "Multirole1");

                    if(ifrit == null)
                    {
                        Log.LogError("Ifrit defintiion returned null");
                    }

                    // Find source WeaponMount
                    WeaponMount sourceMount = findMount(__instance, RadomeMountKey);
                    if (sourceMount == null)
                    {
                        Log.LogError($"[DorsalRadome] Could not find '{RadomeMountKey}' in encyclopedia.");
                        return;
                    }

                    // Clone WeaponInfo
                    WeaponInfo clonedInfo = null;
                    if (sourceMount.info != null)
                    {
                        clonedInfo = UnityEngine.Object.Instantiate(sourceMount.info);
                        clonedInfo.name = ClonedMountInfoKey;
                        clonedInfo.weaponName = ClonedMountName;
                        clonedInfo.shortName = ClonedMountShort;
                        clonedInfo.description = ClonedMountDesc;
                        Log.LogInfo($"[DorsalRadome] Cloned WeaponInfo as '{ClonedMountInfoKey}'");
                    }
                    else Log.LogWarning("[DorsalRadome] sourceMount.info is null.");

                    // Clone WeaponMount
                    clonedMount = UnityEngine.Object.Instantiate(sourceMount);
                    clonedMount.name = ClonedMountKey;
                    clonedMount.jsonKey = ClonedMountKey;
                    clonedMount.mountName = ClonedMountName;
                    clonedMount.dontAutomaticallyAddToEncyclopedia = false;
                    if (clonedInfo != null) clonedMount.info = clonedInfo;

                    __instance.weaponMounts.Add(clonedMount);
                    Log.LogInfo($"[DorsalRadome] '{ClonedMountKey}' registered in encyclopedia.");


                    //Multiplayer blocker.
                    if (MpBlocker.MpBlocker.IsMultiplayer()) return;

                    // Find fuselage_F part on the Ifrit prefab to place the radome
                    Transform fuselageF = ifrit.unitPrefab.transform.Find("fuselage_F");
                    if (fuselageF == null)
                    {
                        Log.LogError("[DorsalRadome] Could not find fuselage_F on Ifrit prefab.");
                        return;
                    }

                    // Borrow the UnitPart from fuselage_F to attach the radome for mass/drag accounting.
                    UnitPart fuselagePart = fuselageF.GetComponent<UnitPart>();
                    if (fuselagePart == null)
                        fuselagePart = fuselageF.GetComponentInChildren<UnitPart>();

                    if (fuselagePart == null)
                    {
                        Log.LogError("[DorsalRadome] Could not find UnitPart on fuselage_F.");
                        return;
                    }

                    // Create dorsal hardpoint transform
                    var hardpointGO = new GameObject("hardpoint_dorsal_radome");
                    hardpointGO.transform.SetParent(fuselageF, worldPositionStays: false);
                    hardpointGO.transform.localPosition = RadomeLocalPos;
                    hardpointGO.transform.localRotation = RadomeLocalRot;
                    hardpointGO.hideFlags = HideFlags.HideAndDontSave;

                    var hardpoint = new Hardpoint
                    {
                        transform = hardpointGO.transform,
                        part = fuselagePart,
                        bayDoors = new BayDoor[0],
                        doorOpenDuration = 0f,
                        BuiltInWeapons = new Weapon[0],
                        BuiltInTurrets = new Turret[0]
                    };

                    // Create and register the new HardpointSet
                    var newSet = new HardpointSet
                    {
                        name = "Dorsal Mount",
                        hardpoints = new List<Hardpoint> { hardpoint },
                        weaponOptions = new List<WeaponMount> { null }, 
                        precludingHardpointSets = new List<byte>()
                    };

                    var wm = ifrit.unitPrefab.GetComponentInChildren<WeaponManager>();
                    if (wm == null)
                    {
                        Log.LogError("[DorsalRadome] Could not find WeaponManager on Ifrit prefab.");
                        return;
                    }

                    // Resize hardpointSets array and append the new set
                    var oldSets = wm.hardpointSets;
                    var newSets = new HardpointSet[oldSets.Length + 1];
                    Array.Copy(oldSets, newSets, oldSets.Length);
                    newSets[newSets.Length - 1] = newSet;
                    wm.hardpointSets = newSets;

                    Log.LogInfo($"[DorsalRadome] Added 'Dorsal Radome' hardpoint set at index [{newSets.Length - 1}].");
                }
                catch (Exception e) { Log.LogError("DorsalRadome.EncyclopediaPatch failed: " + e); }
            }
        }              
    }
}