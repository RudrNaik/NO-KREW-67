using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using System;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.ECMUpgrade", "ECMUpgrade", "0.0.1")]
    internal class ECMUpgrade : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private const string OriginalWeaponMountKey = "JammingPod1";
        internal const string NewWeaponMountKey = "ECMkit";
        internal const string NewWeaponMountName = "ECM Upgrade Package";
        internal const string NewWeaponMountShort = "ECM++";
        internal const string NewWeaponMountInfoKey = "ECMkitInfo";
        internal const string NewWeaponMountDesc = "Advanced ECM kit that provides more capacitance and power to the internal radar jammer.";
        private const float CapacitanceMultiplier = 3.5f;
        private const float JammingIntensityMultiplier = 4.0f;

        internal static WeaponMount newWeaponMount = null;

        // Guard against stacking boosts on the shared prefab across multiple spawns. Resets to false if the scene reloads
        private static bool _prefabBoosted = false;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("ECMUpgrade loading...");

            Harmony harmony = new("com.Spiny.ECMUpgrade");

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
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(VetLoadoutBoostPatch), "Postfix"));
                    Log.LogInfo("Patched WeaponChecker.VetLoadout (POSTFIX)");
                }
                else Log.LogError("Could not find WeaponChecker.VetLoadout.");
            }
            catch (Exception e) { Log.LogError("Failed to patch WeaponChecker.VetLoadout: " + e); }

            Log.LogInfo("ECMUpgrade loaded.");
        }

        /// <summary>
        /// Clones JammingPod1 or whatever the source mount is into a new invisible WeaponMount before AfterLoad runs.
        /// This makes the mount soley condition flag so we dont need to worry about any other details.
        /// Encyclopedia.AfterLoad handles LookupIndex and WeaponLookup registration.
        /// </summary>
        public class EncyclopediaPatch
        {
            public static void Prefix(Encyclopedia __instance)
            {
                if (newWeaponMount != null) return; // only run once

                try
                {
                    // Find source WeaponMount
                    WeaponMount originalMount = null;
                    foreach (var m in __instance.weaponMounts)
                    {
                        if (m != null && m.jsonKey == OriginalWeaponMountKey)
                        {
                            originalMount = m; break;
                        }
                    }

                    // Safety net
                    if (originalMount == null)
                    {
                        Log.LogError("Could not find JammingPod1 to clone from.");
                        return;
                    }

                    // Clone WeaponInfo and its metadata
                    WeaponInfo clonedInfo = null;
                    if (originalMount.info != null)
                    {
                        //Apparently even if it's greyed out- I still need to keep the 'UnityEngine.Object'. Noted.
                        clonedInfo = UnityEngine.Object.Instantiate(originalMount.info);
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

                    WeaponMount clonedMount = UnityEngine.Object.Instantiate(originalMount);
                    clonedMount.name = NewWeaponMountKey;
                    clonedMount.jsonKey = NewWeaponMountKey;
                    clonedMount.mountName = NewWeaponMountName;
                    clonedMount.dontAutomaticallyAddToEncyclopedia = false;
                    clonedMount.RCS = 0f;
                    clonedMount.emptyRCS = 0f;

                    //Log.LogInfo($"Mass: {clonedMount.mass}");

                    if (clonedMount.prefab != null)
                    {
                        // Clone the prefab into a new GameObject so we don't touch the shared JammingPod1 asset
                        GameObject clonedPrefab = UnityEngine.Object.Instantiate(clonedMount.prefab);
                        clonedPrefab.name = "ECMkit_prefab";
                        clonedPrefab.hideFlags = HideFlags.HideAndDontSave;
                        clonedPrefab.SetActive(false);
                        clonedMount.prefab = clonedPrefab;

                        // Destroy JammingPod components on our clone only so the original JammingPod1 asset is untouched.
                        var jammers = clonedPrefab.GetComponentsInChildren<JammingPod>(includeInactive: true);
                        foreach (var j in jammers)
                            UnityEngine.Object.Destroy(j);
                        Log.LogInfo($"Destroyed {jammers.Length} JammingPod component(s) on ECMkit prefab.");
                    }

                    if (clonedInfo != null) clonedMount.info = clonedInfo;

                    __instance.weaponMounts.Add(clonedMount);
                    newWeaponMount = clonedMount;
                    Log.LogInfo($"'{NewWeaponMountKey}' registered in encyclopedia.");
                }
                catch (Exception e) { Log.LogError("EncyclopediaPatch.Prefix failed: " + e); }
            }
        }

        /// <summary>
        /// Postfix on WeaponChecker.VetLoadout. By this point loadout.weapons[i] is
        /// the final validated selection. If ECMkit is in slot [3] we boost the
        /// RadarJammer on the shared prefab so Awake() reads the boosted values on spawn.
        ///
        /// _prefabBoosted guards against the multiplier stacking on repeat spawns since
        /// we are writing to the shared prefab component, not a per-instance copy.
        /// If the player changes their loadout away from ECMkit and back, the prefab
        /// is already boosted so no double-apply occurs.
        /// </summary>
        public class VetLoadoutBoostPatch
        {
            public static void Postfix(AircraftDefinition definition, Loadout loadout)
            {
                try
                {
                    if (loadout?.weapons == null || loadout.weapons.Count <= 3) return;

                    WeaponMount selected = loadout.weapons[3];
                    bool hasECMPlus = selected != null && selected.jsonKey == NewWeaponMountKey;

                    if (!hasECMPlus)
                    {
                        if (_prefabBoosted)
                        {
                            _prefabBoosted = false;
                            Log.LogInfo("ECM boost flag reset.");
                        }
                        return;
                    }

                    if (_prefabBoosted)
                    {
                        Log.LogInfo("ECM is boosted, skipping.");
                        return;
                    }

                    var radarJammer = definition.unitPrefab
                        .GetComponentInChildren<RadarJammer>(includeInactive: true);

                    if (radarJammer == null)
                    {
                        Log.LogWarning("No RadarJammer found on aircraft prefab.");
                        return;
                    }

                    float baseCap = Traverse.Create(radarJammer).Field<float>("capacitance").Value;
                    Traverse.Create(radarJammer).Field("capacitance").SetValue(baseCap * CapacitanceMultiplier);
                    Log.LogInfo($"capacitance: {baseCap} → {baseCap * CapacitanceMultiplier} ({CapacitanceMultiplier}x)");

                    float baseIntensity = Traverse.Create(radarJammer).Field<float>("jammingIntensity").Value;
                    Traverse.Create(radarJammer).Field("jammingIntensity").SetValue(baseIntensity * JammingIntensityMultiplier);
                    Log.LogInfo($"jammingIntensity: {baseIntensity} → {baseIntensity * JammingIntensityMultiplier} ({JammingIntensityMultiplier}x)");

                    _prefabBoosted = true;
                    Log.LogInfo("Prefab boost applied.");
                }
                catch (Exception e) { Log.LogError("VetLoadoutBoostPatch failed: " + e); }
            }
        }
    }
}