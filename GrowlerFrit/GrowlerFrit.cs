using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MpBlocker;
using NuclearOption.SavedMission;
using System;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.GrowlerFrit", "GrowlerIfrit", "0.0.1")]
    [BepInDependency("com.Spiny.MpBlocker")]
    public class GrowlerFrit : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<MultiplayerMode> MpMode;

        private const string TargetAircraftName = "Multirole1";
        private const string JammerKey = "JammingPod1";
        private const string AradDoubleKey = "ARM1_double";

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("GrowlerFrit is loading...");

            MpMode = Config.Bind(
                "Multiplayer",
                "MultiplayerMode",
                MultiplayerMode.MpDisabled,
                "MpDisabled: mod only active in singleplayer. RestrictedMM: mod active in MP with version matching."
            );
            Plugin.setEnum(MpMode.Value);
            MpMode.SettingChanged += (sender, args) => Plugin.setEnum(MpMode.Value);

            Harmony harmony = new Harmony("com.Spiny.GrowlerFrit");
            Log.LogMessage("GrowlerFrit patching...");

            
            TryPatch(harmony,
                typeof(WeaponSelector).GetMethod("PopulateOptions",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                typeof(WeaponSelectorPopulatePatch), "Prefix",
                "WeaponSelector.PopulateOptions (PREFIX)",
                prefix: true);

           
            TryPatch(harmony,
                typeof(WeaponChecker).GetMethod("VetLoadout",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
                typeof(VetLoadoutPatch), "Prefix",
                "WeaponChecker.VetLoadout (PREFIX)",
                prefix: true);

            Log.LogInfo("GrowlerFrit loaded.");
        }

        private void TryPatch(Harmony harmony, MethodInfo method, Type patchClass,
            string patchMethod, string label, bool prefix = false)
        {
            try
            {
                if (method != null)
                {
                    if (prefix)
                        harmony.Patch(method, prefix: new HarmonyMethod(patchClass, patchMethod));
                    else
                        harmony.Patch(method, postfix: new HarmonyMethod(patchClass, patchMethod));
                    Log.LogInfo($"Patched {label}");
                }
                else Log.LogError($"Could not find {label}!");
            }
            catch (Exception e) { Log.LogError($"Failed to patch {label}: {e}"); }
        }

        internal static (WeaponMount jammer, WeaponMount aradDouble) FindMounts(Encyclopedia enc)
        {
            WeaponMount jammer = null, aradDouble = null;
            if (enc?.weaponMounts == null) return (null, null);
            foreach (var mount in enc.weaponMounts)
            {
                if (mount == null) continue;
                if (mount.jsonKey == JammerKey) jammer = mount;
                if (mount.jsonKey == AradDoubleKey) aradDouble = mount;
            }
            return (jammer, aradDouble);
        }

        internal static void AddOption(HardpointSet set, WeaponMount mount)
        {
            if (set == null || mount == null) return;
            foreach (var o in set.weaponOptions)
                if (o != null && o.jsonKey == mount.jsonKey) return;
            set.weaponOptions.Add(mount);
            Log.LogInfo($"[GrowlerFrit] Added {mount.jsonKey} to '{set.name}'");
        }

        internal static void RemoveOption(HardpointSet set, WeaponMount mount)
        {
            if (set == null || mount == null) return;
            if (set.weaponOptions.Remove(mount))
                Log.LogInfo($"[GrowlerFrit] Removed {mount.jsonKey} from '{set.name}'");
        }

        internal static void InjectIntoWeaponManager(WeaponManager wm, Encyclopedia enc)
        {
            if (wm == null || wm.hardpointSets == null || wm.hardpointSets.Length < 6) return;
            var (jammer, aradDouble) = FindMounts(enc);
            if (jammer == null) Log.LogWarning("[GrowlerFrit] Could not find JammingPod1!");
            if (aradDouble == null) Log.LogWarning("[GrowlerFrit] Could not find ARM1_double!");

            AddOption(wm.hardpointSets[1], jammer);     // Forward Weapon Bay
            AddOption(wm.hardpointSets[2], jammer);     // Rear Weapon Bay
            AddOption(wm.hardpointSets[5], aradDouble); // Outer Wing Pylons
        }

        internal static void RemoveFromWeaponManager(WeaponManager wm, Encyclopedia enc)
        {
            if (wm == null || wm.hardpointSets == null || wm.hardpointSets.Length < 6) return;
            var (jammer, aradDouble) = FindMounts(enc);
            RemoveOption(wm.hardpointSets[1], jammer);
            RemoveOption(wm.hardpointSets[2], jammer);
            RemoveOption(wm.hardpointSets[5], aradDouble);
        }

        internal static bool IsTargetAircraft(AircraftDefinition def)
        {
            return def != null && def.name != null &&
                   def.name.IndexOf(TargetAircraftName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Injects the selected weapons onto the selected hardpoints on an aircraft.
        public class WeaponSelectorPopulatePatch
        {
            public static void Prefix(HardpointSet hardpointSet)
            {
                try
                {
                    if (Plugin.IsMultiplayer()) return;
                    if (Encyclopedia.i == null) return;

                    var (jammer, aradDouble) = FindMounts(Encyclopedia.i);

                    if (hardpointSet.name == "Forward Weapon Bay")
                        AddOption(hardpointSet, jammer);
                    else if (hardpointSet.name == "Rear Weapon Bay")
                        AddOption(hardpointSet, jammer);
                    else if (hardpointSet.name == "Outer Wing Pylons")
                        AddOption(hardpointSet, aradDouble);
                }
                catch (Exception e) { Log.LogError("[GrowlerFrit] WeaponSelectorPopulatePatch failed: " + e); }
            }
        }

        //Injects into the weapon manager such that we can use the weapons. Uses ismultiplayer() from the MpBlocker to ensure that you can't use it in MP unless on MpRestricted mode. 
        public class VetLoadoutPatch
        {
            public static void Prefix(AircraftDefinition definition, Loadout loadout)
            {
                try
                {
                    if (!IsTargetAircraft(definition)) return;
                    if (Plugin.IsMultiplayer()) return;

                    var wm = definition.unitPrefab?.GetComponentInChildren<WeaponManager>();
                    if (wm == null) return;

                    InjectIntoWeaponManager(wm, Encyclopedia.i);
                    Log.LogInfo("[GrowlerFrit] VetLoadout: injected into prefab for spawn validation.");
                }
                catch (Exception e) { Log.LogError("[GrowlerFrit] VetLoadoutPatch failed: " + e); }
            }
        }
    }
}