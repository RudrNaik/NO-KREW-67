using BepInEx;
using BepInEx.Logging;
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
        internal const string ClonedMountDesc = "High power radome mmount placed right behind the cockpit. Scans in all directions and has a lower radar floor than the mounted radar. Also really heavy. Make sure to scan your pilots for cancer after flying.";


        // Local position of the radome hardpoint relative to fuselage_F
        // X=0 (centerline), Y=0.8 (dorsal), Z=-1.0 (places it just behind the cockpit for now
        private static readonly Vector3 RadomeLocalPos = new Vector3(0f, 0.8f, -1.0f);
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

            try
            {
                var method = typeof(WeaponChecker).GetMethod("VetLoadout",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (method != null)
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(VetLoadoutPatch), "Prefix"));
                    Log.LogInfo("Patched WeaponChecker.VetLoadout (PREFIX)");
                }
                else Log.LogError("Could not find WeaponChecker.VetLoadout.");
            }
            catch (Exception e) { Log.LogError("Failed to patch WeaponChecker.VetLoadout: " + e); }

            Log.LogInfo("DorsalRadome loaded.");
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
                    // Find the Ifrit prefab
                    AircraftDefinition ifrit = null;
                    foreach (var def in __instance.aircraft)
                    {
                        if (def != null && def.name.IndexOf("Multirole1",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        { ifrit = def; break; }
                    }

                    if (ifrit?.unitPrefab == null)
                    {
                        Log.LogError("[DorsalRadome] Could not find Ifrit prefab.");
                        return;
                    }

                    // Find source WeaponMount
                    WeaponMount sourceMount = null;
                    foreach (var m in __instance.weaponMounts)
                    {
                        if (m != null && m.jsonKey == RadomeMountKey)
                        { sourceMount = m; break; }
                    }

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
                        name = "Dorsal Radome",
                        hardpoints = new List<Hardpoint> { hardpoint },
                        weaponOptions = new List<WeaponMount> { null, clonedMount },
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

        /// <summary>
        /// Ensures DorsalRadome1 is in the new hardpoint set's weaponOptions on the
        /// prefab before VetLoadout strips it.
        /// </summary>
        public class VetLoadoutPatch
        {
            public static void Prefix(AircraftDefinition definition)
            {
                try
                {
                    if (clonedMount == null) return;
                    if (definition?.unitPrefab == null) return;
                    if (definition.name.IndexOf("Multirole1", StringComparison.OrdinalIgnoreCase) < 0) return;

                    var wm = definition.unitPrefab.GetComponentInChildren<WeaponManager>();
                    if (wm?.hardpointSets == null) return;

                    // Find the Dorsal Radome set by name and ensure mount is present
                    foreach (var hs in wm.hardpointSets)
                    {
                        if (hs?.name == "Dorsal Radome")
                        {
                            if (!hs.weaponOptions.Contains(clonedMount))
                                hs.weaponOptions.Add(clonedMount);
                            return;
                        }
                    }
                }
                catch (Exception e) { Log.LogError("DorsalRadome.VetLoadoutPatch failed: " + e); }
            }
        }
    }
}