using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using System;
using System.Reflection;
using UnityEngine;

namespace GrowlerFrit
{
    [BepInPlugin("com.Spiny.GrowlerFrit", "GrowlerIfrit", "0.0.2")]
    [BepInDependency("com.Spiny.MpBlocker")]
    [BepInDependency("com.Spiny.JammerPod2")]
    [BepInDependency("com.Spiny.ECMUpgrade")]
    [BepInDependency("com.Spiny.SparkyDome")]
    public class GrowlerFrit : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<MpBlocker.MultiplayerMode> MpMode;

        private const string TargetAircraftName = "Multirole1";
        private const string AradDoubleKey = "ARM1_double";

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("GrowlerFrit is loading...");

            MpMode = Config.Bind(
                "Multiplayer",
                "MultiplayerMode",
                MpBlocker.MultiplayerMode.MpDisabled,
                "MpDisabled: mod only active in singleplayer. RestrictedMM: mod active in MP with version matching."
            );

            MpBlocker.MpBlocker.SetEnum(MpMode.Value);
            MpMode.SettingChanged += (sender, args) => MpBlocker.MpBlocker.SetEnum(MpMode.Value);

            Harmony harmony = new("com.Spiny.GrowlerFrit");
            Log.LogInfo("GrowlerFrit patching...");

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

            TryPatch(harmony,
                typeof(UnityEngine.SceneManagement.SceneManager).GetMethod("Internal_SceneLoaded",
                    BindingFlags.Static | BindingFlags.NonPublic),
                typeof(SceneLoadPatch), "Postfix",
                "SceneManager.Internal_SceneLoaded (POSTFIX)",
                prefix: false);

            Log.LogInfo("GrowlerFrit loaded.");
        }

        /// <summary>
        /// Quick and repeatable method to condense the try-catch patching method for individual patches.
        /// </summary>
        /// <param name="harmony"> The harmony patcher object</param>
        /// <param name="method"> the specific method that we are attempting to patch. </param>
        /// <param name="patchClass"> the individual class in file that TryPatch is called in that is to be patched in.</param>
        /// <param name="patchMethod"> the way that the patch is injected (prefix, postfix, etc.)</param>
        /// <param name="label">Specific label for the patch. Essentially the name of it for debugging purposes.</param>
        /// <param name="prefix"> Defines wether or not this is a prefix patch or a postfix patch.</param>
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

        /// <summary>
        /// Finds a specific individual mount by its key in the dictionary.
        /// </summary>
        /// <param name="enc"> the encylopedia singleton</param>
        /// <param name="key"> the specific JSON key of the weapon</param>
        /// <returns> the individual weapon mount object based off of the key provided. </returns>
        internal static WeaponMount FindMount(Encyclopedia enc, string key)
        {
            if (enc?.weaponMounts == null) return null;
            foreach (var mount in enc.weaponMounts)
                if (mount != null && mount.jsonKey == key) return mount;
            return null;
        }

        /// <summary>
        /// Adds an option to a selected hardpoint set object
        /// </summary>
        /// <param name="set"> The selected hardpoint set.</param>
        /// <param name="mount"> The selected mount to add to the hardpoint set.</param>
        internal static void AddOption(HardpointSet set, WeaponMount mount)
        {
            if (set == null || mount == null) return;
            foreach (var o in set.weaponOptions)
                if (o != null && o.jsonKey == mount.jsonKey) return;
            set.weaponOptions.Add(mount);
        }

        /// <summary>
        /// Injects the weapons into the weaponmanager for the aircraft.
        /// </summary>
        /// <param name="wm"></param>
        /// <param name="enc"> The Encyclopedia Singleton</param>
        internal static void InjectIntoWeaponManager(WeaponManager wm, Encyclopedia enc)
        {
            if (MpBlocker.MpBlocker.IsMultiplayer()) return;

            if (wm == null || wm.hardpointSets == null || wm.hardpointSets.Length < 6) return;

            
            var pod = JammerPod2.newWeaponMount;
            var aradDouble = FindMount(enc, AradDoubleKey);
            var ecmKit = ECMUpgrade.newWeaponMount;
            var radome = SparkyDome.clonedMount;

            if (pod == null) Log.LogWarning("GrowlerPodMount is null — JammerPod2 may not have loaded correctly.");
            if (aradDouble == null) Log.LogWarning("Could not find ARM1_double.");

            AddOption(wm.hardpointSets[1], pod);        // Forward Weapon Bay
            AddOption(wm.hardpointSets[2], pod);        // Rear Weapon Bay
            AddOption(wm.hardpointSets[3], ecmKit);     // Side Weapon Bays
            AddOption(wm.hardpointSets[5], aradDouble); // Outer Wing Pylons

            foreach (var hs in wm.hardpointSets)
            {
                if (hs?.name == "Dorsal Mount")
                {
                    AddOption(hs, radome);
                    break;
                }
            }
        }

        /// <summary>
        /// Checks if the aircraft defintiion passed in is the same as the target aircraft selected by the definition name.
        /// </summary>
        /// <param name="def"> The definition of the aircraft that you want to compare against the target aircraft.</param>
        /// <returns></returns>
        internal static bool IsTargetAircraft(AircraftDefinition def)
        {
            return def != null && def.name != null &&
                   def.name.IndexOf(TargetAircraftName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Patch to add the weapon options to the hardpoints for the Ifrit, so that it can then be used via the dropdown.
        /// </summary>
        public class WeaponSelectorPopulatePatch
        {
            public static void Prefix(HardpointSet hardpointSet)
            {
                try
                {
                    if (MpBlocker.MpBlocker.IsMultiplayer()) return;
                    if (Encyclopedia.i == null) return;

                    var pod = JammerPod2.newWeaponMount;
                    var aradDouble = FindMount(Encyclopedia.i, AradDoubleKey);
                    var ECMUpgradeKit = ECMUpgrade.newWeaponMount;
                    var radome = SparkyDome.clonedMount;

                    if (hardpointSet.name == "Forward Weapon Bay")
                        AddOption(hardpointSet, pod);
                    else if (hardpointSet.name == "Rear Weapon Bay")
                        AddOption(hardpointSet, pod);
                    else if (hardpointSet.name == "Side Weapon Bays")
                        AddOption(hardpointSet, ECMUpgradeKit);
                    else if (hardpointSet.name == "Outer Wing Pylons")
                        AddOption(hardpointSet, aradDouble);
                    else if (hardpointSet.name == "Dorsal Mount")
                        AddOption(hardpointSet, radome);
                }
                catch (Exception e) { Log.LogError("WeaponSelectorPopulatePatch failed: " + e); }
            }
        }

        /// <summary>
        /// Injects into the aircraft definition such that we can use the weapons. 
        /// Uses IsMultiplayer() from the MpBlocker to ensure that you can't use it in MP unless on MpRestricted mode. 
        /// </summary>
        public class VetLoadoutPatch
        {
            public static void Prefix(AircraftDefinition definition)
            {
                try
                {
                    if (!IsTargetAircraft(definition)) return;
                    if (MpBlocker.MpBlocker.IsMultiplayer()) return;

                    var wm = definition.unitPrefab?.GetComponentInChildren<WeaponManager>();
                    if (wm == null) return;

                    InjectIntoWeaponManager(wm, Encyclopedia.i);
                    Log.LogInfo("VetLoadout: injected pod into prefab for spawn validation.");
                }
                catch (Exception e) { Log.LogError("VetLoadoutPatch failed: " + e); }
            }
        }

        /// <summary>
        /// On every scene load, check if we're in MP and if so strip the Dorsal Radome
        /// hardpoint set from the Ifrit prefab. By scene load time the network state is
        /// fully established so IsMultiplayer() returns an accurate result.
        /// </summary>
        public class SceneLoadPatch
        {
            public static void Postfix()
            {
                try
                {
                    if (Encyclopedia.i == null) return;

                    foreach (var def in Encyclopedia.i.aircraft)
                    {
                        if (def == null) continue;
                        if (def.name.IndexOf(TargetAircraftName, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        var wm = def.unitPrefab?.GetComponentInChildren<WeaponManager>();
                        if (wm?.hardpointSets == null) continue;

                        if (MpBlocker.MpBlocker.IsMultiplayer())
                        {
                            // Strip the dorsal mount in MP so the server doesn't reject the spawn
                            var list = new System.Collections.Generic.List<HardpointSet>(wm.hardpointSets);
                            int removed = list.RemoveAll(hs => hs?.name == "Dorsal Mount");
                            if (removed > 0)
                            {
                                wm.hardpointSets = list.ToArray();
                                Log.LogInfo("[GrowlerFrit] MP — stripped Dorsal Mount hardpoint set.");
                            }
                        }
                        else
                        {
                            // Re-add the dorsal mount in singleplayer if it was previously stripped
                            bool exists = false;
                            foreach (var hs in wm.hardpointSets)
                                if (hs?.name == "Dorsal Mount") { exists = true; break; }

                            if (!exists && SparkyDome.dorsalHardpointSet != null)
                            {
                                var list = new System.Collections.Generic.List<HardpointSet>(wm.hardpointSets);
                                list.Add(SparkyDome.dorsalHardpointSet);
                                wm.hardpointSets = list.ToArray();
                                Log.LogInfo("[GrowlerFrit] SP — re-added Dorsal Mount hardpoint set.");
                            }
                        }
                    }
                }
                catch (Exception e) { Log.LogError("SceneLoadPatch failed: " + e); }
            }
        }
    }
}