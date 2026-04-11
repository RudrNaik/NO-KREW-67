using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.Debugger", "Debugging Helpers", "1.0.0")]
    internal class DebuggingHelpers : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static bool run = false;

        internal static bool patchWeapons = false;

        internal static bool patchPlaneParts = false;
        private void Awake()
        {
            Log = Logger;
            if (run) {
                Log.LogInfo("DebuggingLoading...");
                Harmony harmony = new Harmony("com.Spiny.Debugger");

                if (patchWeapons) { 
                try
                {
                    var method = typeof(Encyclopedia).GetMethod("AfterLoad",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(EncyclopediaPatch), "Postfix"));
                        Log.LogInfo("Patched Encyclopedia.AfterLoad (POSTFIX) for getting weapons");
                    }
                    else Log.LogError("Could not find Encyclopedia.AfterLoad.");
                }
                catch (Exception e) { Log.LogError("Failed to patch Encyclopedia.AfterLoad: " + e); }
                }
                if (patchPlaneParts) {
                try
                {
                    var method = typeof(Encyclopedia).GetMethod("AfterLoad",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(FindAirplaneParts), "Postfix"));
                        Log.LogInfo("Patched Encyclopedia.AfterLoad (POSTFIX)");
                    }
                    else Log.LogError("Could not find Encyclopedia.AfterLoad.");
                }
                catch (Exception e) { Log.LogError("Failed to patch Encyclopedia.AfterLoad: " + e); }
                }
                Log.LogInfo("DebuggerLoaded.");
            }
        }
    }

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad")]
    internal static class EncyclopediaPatch
    {
        [HarmonyPostfix]
        static void Postfix(Encyclopedia __instance)
        {
            DebuggingHelpers.Log.LogInfo("=== Weapon Dump ===");

            LogWeapons(__instance.weaponMounts);
        }

        static void LogWeapons(List<WeaponMount> mounts)
        {
            DebuggingHelpers.Log.LogInfo($"--- WeaponMounts ({mounts.Count}) ---");
            foreach (var wm in mounts)
            {
                if (wm == null) continue;
                DebuggingHelpers.Log.LogInfo($"  [{wm.jsonKey}] {wm.name}");
            }
        }
    }

    [HarmonyPatch(typeof(Encyclopedia), "AfterLoad")]
    internal static class FindAirplaneParts
    {
        internal static string AirplaneSearchName = "CAS1";

        [HarmonyPostfix]
        static void Postfix(Encyclopedia __instance)
        {
            DebuggingHelpers.Log.LogInfo("=== Aircraft Part Dump ===");

            AircraftDefinition plane = SparkyDome.FindAircraftDefinition(__instance, AirplaneSearchName);
            if (plane == null)
            {
                DebuggingHelpers.Log.LogError($"Could not find aircraft matching '{AirplaneSearchName}'");
                return;
            }

            DebuggingHelpers.Log.LogInfo($"Found: [{plane.jsonKey}] {plane.name}");

            // Dump top-level prefab transforms
            DebuggingHelpers.Log.LogInfo("--- Prefab Transforms ---");
            LogTransforms(plane.unitPrefab.transform, 0);

            // Dump WeaponManager hardpoint sets
            var wm = plane.unitPrefab.GetComponentInChildren<WeaponManager>();
            if (wm == null)
            {
                DebuggingHelpers.Log.LogError("No WeaponManager found on prefab.");
                return;
            }

            DebuggingHelpers.Log.LogInfo($"--- HardpointSets ({wm.hardpointSets.Length}) ---");
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                var set = wm.hardpointSets[i];
                DebuggingHelpers.Log.LogInfo($"  [{i}] '{set.name}' — {set.hardpoints.Count} hardpoint(s), {set.weaponOptions.Count} option(s)");
                for (int j = 0; j < set.hardpoints.Count; j++)
                {
                    var hp = set.hardpoints[j];
                    DebuggingHelpers.Log.LogInfo($"    hp[{j}] transform='{hp.transform?.name}' part='{hp.part?.name}' builtInWeapons={hp.BuiltInWeapons?.Length} builtInTurrets={hp.BuiltInTurrets?.Length}");
                }
                for (int k = 0; k < set.weaponOptions.Count; k++)
                {
                    var opt = set.weaponOptions[k];
                    DebuggingHelpers.Log.LogInfo($"    opt[{k}] {(opt == null ? "<null>" : $"[{opt.jsonKey}] {opt.name}")}");
                }
            }
        }

        // Recursively logs the transform hierarchy with indentation
        static void LogTransforms(Transform t, int depth)
        {
            string indent = new string(' ', depth * 3);
            DebuggingHelpers.Log.LogInfo($"{indent}{t.name} (pos={t.localPosition})");

            // Stop recursing into deep sub-trees to avoid log spam, set to 6 to make sure we can get actually important parts.
            if (depth >= 6) return;

            foreach (Transform child in t)
                LogTransforms(child, depth + 1);
        }
    }
}