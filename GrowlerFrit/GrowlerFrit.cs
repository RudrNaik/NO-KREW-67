using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MpBlocker;
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
            Log.LogMessage("GrowlerFrit loading...");

            // prefix on Encyclopedia.AfterLoad — runs before IndexLookup is built
            // This is where we inject into the prefab's WeaponManager
            TryPatch(harmony,
                typeof(Encyclopedia).GetMethod("AfterLoad",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null, Type.EmptyTypes, null),
                typeof(EncyclopediaPrefabInjectPatch), "Prefix",
                "Encyclopedia.AfterLoad (PREFIX)",
                prefix: true);

            Log.LogInfo("GrowlerFrit loaded.");
        }

        //Individual patching method as some methods don't work with PatchAll().
        //Don't know why harmony is like this, but it just is.
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

        // Finds jammer and ARAD mounts from the encyclopedia
        internal static (WeaponMount jammer, WeaponMount aradDouble) FindMounts(Encyclopedia enc)
        {
            WeaponMount jammer = null, aradDouble = null;
            if (enc?.weaponMounts == null) return (null, null);
            foreach (var mount in enc.weaponMounts)
            {
                if (mount == null) continue;
                if (mount.jsonKey == "JammingPod1") jammer = mount;
                if (mount.jsonKey == "ARM1_double") aradDouble = mount;
            }
            return (jammer, aradDouble);
        }

        // Adds a mount to a hardpoint set's weaponOptions if not already present
        internal static void AddOption(HardpointSet set, WeaponMount mount)
        {
            if (set == null || mount == null) return;
            foreach (var o in set.weaponOptions) { 
                if (o != null && o.jsonKey == mount.jsonKey) return;
            }
            set.weaponOptions.Add(mount);
            Log.LogInfo($"[GrowlerFrit] Added {mount.jsonKey} to hardpointSet '{set.name}'");
        }

        // Injects weapons into the correct hardpoint sets on a WeaponManager
        internal static void InjectIntoWeaponManager(WeaponManager wm, Encyclopedia enc)
        {
            if (wm == null || wm.hardpointSets == null || wm.hardpointSets.Length < 6) return;
            var (jammer, aradDouble) = FindMounts(enc);
            if (jammer == null) Log.LogWarning("[GrowlerFrit] Could not find JammingPod1!");
            if (aradDouble == null) Log.LogWarning("[GrowlerFrit] Could not find ARM1_double!");

            AddOption(wm.hardpointSets[1], jammer);      // Forward Weapon Bay
            AddOption(wm.hardpointSets[2], jammer);      // Rear Weapon Bay
            //AddOption(wm.hardpointSets[4], aradDouble);  // Inner Wing Pylons (removed as it causes clipping with the airbrake)
            AddOption(wm.hardpointSets[5], aradDouble);  // Outer Wing Pylons
        }

        internal static bool IsTargetAircraft(AircraftDefinition def)
        {
            return def != null && def.name != null &&
                   def.name.IndexOf(TargetAircraftName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        
        // Injects into the Encyclopedia's prefab WeaponManager so the weapons are accessible to you.
        public class EncyclopediaPrefabInjectPatch
        {
            public static void Prefix(Encyclopedia __instance)
            {
                try
                {
                    //Log.LogInfo("Running Encyclopedia.AfterLoad Prefix");

                    // Find the Ifrit definition
                    AircraftDefinition ifrit = null;
                    foreach (var def in __instance.aircraft)
                    {
                        if (IsTargetAircraft(def)) { 
                            ifrit = def; 
                            break; 
                        }
                    }
                    if (ifrit == null) { 
                        Log.LogError("Could not find Ifrit definition."); 
                        return; 
                    }

                    // Get the WeaponManager from the prefab itself
                    var wm = ifrit.unitPrefab?.GetComponentInChildren<WeaponManager>();
                    if (wm == null) {
                        Log.LogError("Could not find WeaponManager on Ifrit prefab."); 
                        return; 
                    }

                    InjectIntoWeaponManager(wm, __instance);
                    //Log.LogInfo("Injected weapons into Ifrit prefab WeaponManager.");
                }
                catch (Exception e) { 
                    Log.LogError("EncyclopediaPrefabInjectPatch failed: " + e); 
                }
            }
        }
    }
}