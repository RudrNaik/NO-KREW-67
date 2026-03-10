using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MpBlocker
{
    public enum MultiplayerMode
    {
        MpDisabled,     // Mod disabled in all multiplayer but accessible in singleplayer.
        RestrictedMM    // Mod enabled in MP but requires a version match.
    }

    [BepInPlugin("com.Spiny.MpBlocker", "MpBlocker", "0.0.1")]
    public class MpBlocker : BaseUnityPlugin
    {
        internal static ManualLogSource Log;                //da loggerrrrrr
        internal static MultiplayerMode MpMode;             //Enum to track the selected MP mode.

        private static bool? _cachedIsMultiplayer = null;
        internal static bool _hasLoggedMpState = false;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo("MpBlocker Loading.");

            Harmony harmony = new Harmony("com.Spiny.MpBlocker");
            harmony.PatchAll();

            Logger.LogInfo("MpBlocker loaded.");
        }

        /// <summary>
        /// Setter for MultiplayerMode such that you can change it from other classes.
        /// </summary>
        /// <param name="mode">the specific mode you want to set it to. (MpDisabled or RestrictedMM)</param>
        internal static void SetEnum(MultiplayerMode mode)
        {
            MpMode = mode;
        }

        /// <summary>
        /// Resets the multiplayer cache so that we dont spam the logs.
        /// </summary>
        internal static void ResetMultiplayerCache()
        {
            _cachedIsMultiplayer = null;
            _hasLoggedMpState = false;
        }

        /// <summary>
        /// Checks if the game is in multiplayer via mirage.networkserver and mirage.networkclient. If you are hosting or are connected as a client somewhere else, the mod disables.
        /// This check is bypassed if the MpMode is set to RestrictedMM, which appends the version string.
        /// </summary>
        /// <returns></returns>
        internal static bool IsMultiplayer()
        {
            // If RestrictedMM mode, dont block as we're on a separate version.
            if (MpMode == MultiplayerMode.RestrictedMM)
            {
                return false;
            }

            //if (_cachedIsMultiplayer.HasValue)
            //{
            //    return _cachedIsMultiplayer.Value;
            //}

            try
            {
                // Check if hosting with remote players.
                Type serverType = AccessTools.TypeByName("Mirage.NetworkServer");
                if (serverType != null)
                {
                    var serverInstances = UnityEngine.Object.FindObjectsOfType(serverType);
                    if (serverInstances.Length > 0)
                    {
                        object server = serverInstances[0];
                        PropertyInfo activeProperty = serverType.GetProperty("Active");
                        if (activeProperty != null)
                        {
                            object activeValue = activeProperty.GetValue(server);
                            if (activeValue != null && (bool)activeValue)
                            {
                                PropertyInfo connectionsProperty = serverType.GetProperty("connections");
                                if (connectionsProperty != null)
                                {
                                    object connections = connectionsProperty.GetValue(server);
                                    if (connections is System.Collections.ICollection collection && collection.Count > 1)
                                    {
                                        _cachedIsMultiplayer = true;
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // Check if connected as client to a remote server.
                Type clientType = AccessTools.TypeByName("Mirage.NetworkClient");
                if (clientType != null)
                {
                    var clientInstances = UnityEngine.Object.FindObjectsOfType(clientType);
                    if (clientInstances.Length > 0)
                    {
                        object client = clientInstances[0];
                        PropertyInfo activeProperty = clientType.GetProperty("Active");
                        PropertyInfo isLocalClientProperty = clientType.GetProperty("IsLocalClient");

                        if (activeProperty != null)
                        {
                            object activeValue = activeProperty.GetValue(client);
                            if (activeValue != null && (bool)activeValue)
                            {
                                if (isLocalClientProperty != null)
                                {
                                    object isLocalValue = isLocalClientProperty.GetValue(client);
                                    if (isLocalValue != null && !(bool)isLocalValue)
                                    {
                                        _cachedIsMultiplayer = true;
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Error checking multiplayer state: {ex.Message}");
            }

            _cachedIsMultiplayer = false;
            return false;
        }
    }

    /// <summary>
    /// Appends a version string to the current version string to force the game into restricted matchmaking with other clients who have the same string of mods installed.
    /// Credit to Nikkorap for developing the original version to make a restricted MM mode.
    /// </summary>
    [HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
    internal static class VersionGetterPatch
    {
        static void Postfix(ref string __result)
        {
            if (MpBlocker.MpMode == MultiplayerMode.RestrictedMM)
            {
                __result += "_GrowlerFrit-v1.0.0";
            }
        }
    }

    /// <summary>
    /// Reset the MP cache on every scene load.
    /// </summary>
    [HarmonyPatch(typeof(UnityEngine.SceneManagement.SceneManager), "Internal_SceneLoaded")]
    internal static class SceneLoadPatch
    {
        static void Postfix()
        {
            MpBlocker.ResetMultiplayerCache();
        }
    }
}