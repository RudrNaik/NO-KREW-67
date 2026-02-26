using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.JammerPod2", "JammerPod2", "0.0.1")]
    internal class JammerPod2 : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private const string OriginalWeaponMountKey = "JammingPod1";
        internal const string NewWeaponMountKey = "JammingPod2";
        internal const string NewWeaponMountName = "Mini Jammer Pod";
        internal const string NewWeaponMountShort = "M-JMMR";
        internal const string NewWeaponMountInfoKey = "JammingPod2Info";
        internal const string NewWeaponMountDesc =
            "Offensive jammer capable of blinding and jamming targets up to 50km each. Don't mind that these ones cut through the floor of the weapon bays-";

        /// <summary>
        /// The cloned WeaponMount. Populated by EncyclopediaPatch before AfterLoad runs.
        /// GrowlerFrit reads this directly to inject the pod into hardpoints.
        /// </summary>
        internal static WeaponMount newWeaponMount = null;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("JammerPod2 loading...");

            Harmony harmony = new("com.Spiny.JammerPod2");

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

            Log.LogInfo("JammerPod2 loaded.");
        }

        /// <summary>
        /// Clones the original weaponmount designated by the weapon key at the top into a new named WeaponMount before AfterLoad runs.
        /// Only the display metadata (name, short name, description) is changed right now stats, prefab, and icon are all inherited from the source via Instantiate.
        /// To change stats we'll add assignments to clonedMount (cost, mass, etc.).
        /// or try and patch JammingPod.FixedUpdate at runtime.
        /// </summary>
        public class EncyclopediaPatch
        {
            public static void Prefix(Encyclopedia __instance)
            {
                if (newWeaponMount != null) return; // only run once

                try
                {
                    // Find source WeaponMount
                    WeaponMount sourceMount = null;
                    foreach (var m in __instance.weaponMounts) { 
                        if (m != null && m.jsonKey == OriginalWeaponMountKey) { 
                            sourceMount = m; break; 
                        }
                    }

                    if (sourceMount == null)
                    {
                        Log.LogError("Could not find JammingPod1 to clone from.");
                        return;
                    }

                    // Clone WeaponInfo and its metadata
                    WeaponInfo clonedInfo = null;
                    if (sourceMount.info != null)
                    {
                        //Apparently even if it's greyed out- I still need to keep the 'UnityEngine.Object'. Noted.
                        clonedInfo = UnityEngine.Object.Instantiate(sourceMount.info);
                        clonedInfo.name = NewWeaponMountInfoKey;
                        clonedInfo.weaponName = NewWeaponMountName;
                        clonedInfo.shortName = NewWeaponMountShort;
                        clonedInfo.description = NewWeaponMountDesc;

                        // icon, weaponPrefab, and other statfields are inherited via the source mount.

                        Log.LogInfo($"Cloned WeaponInfo as '{NewWeaponMountInfoKey}'");
                    }
                    else
                    {
                        Log.LogWarning("sourceMount.info is null.");
                    }

                    // Clone WeaponMount
                    WeaponMount clonedMount = UnityEngine.Object.Instantiate(sourceMount);
                    clonedMount.name = NewWeaponMountKey;
                    clonedMount.jsonKey = NewWeaponMountKey;
                    clonedMount.mountName = NewWeaponMountName;
                    clonedMount.dontAutomaticallyAddToEncyclopedia = false;

                    if (clonedInfo != null)
                    {
                        clonedMount.info = clonedInfo;
                    }

                    // By adding directly to the encyclopedia's weaponMounts, we basically skip all the other steps needed to add the weapon in. Encyclopedia.Afterload does the heavy lifting.
                    __instance.weaponMounts.Add(clonedMount);
                    newWeaponMount = clonedMount;

                    Log.LogInfo($"'{NewWeaponMountKey}' registered in encyclopedia.");
                }
                catch (Exception e) { Log.LogError("EncyclopediaPatch.Prefix failed: " + e); }
            }
        }
    }
}