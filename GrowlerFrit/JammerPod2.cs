using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using System.Security.Cryptography;
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
            "Offensive jammer capable of jamming targets up to 20km each. They are coated in radar absorbent material, and have a significantly lower RCS at the cost of effectivness over distance.";

        // Notes: 

        // power: controls how much power the pod draws per tick. The jam amount  is scaled by (drawnPower / power),
        // so lowering this reduces effectiveness if the plane's capacitor isnt big enough for it or we dont pull enough power 'for' it.
        // but the main 'dropoff' is handled via the rangeFalloff animation curve.
        //        
        // rangeFalloff: an AnimationCurve that maps distance in meters to the jam multiplier.
        // This is the sole range limiter for the pod. Far as I can tell it's best used with the power multiplier at default of 13f
        //
        // For reference, the JammingPod1 curve is 0m = 1.0, 80000m = 0.0. This had our jamming curve being linear 
        // and just a straight y=x line from 100% to 0 when we hit 80km.
        //
        // Keyframe format: (distance in metres, jam multiplier 0–1):
        //      0 m  → 0.00  (burnthrough up to around 17km)
        //  17000 m  → 0.06  (jams, but only useful if you have a full capacitor)
        //  20000 m  → 1.00  (full effectiveness)
        //  80000 m  → 1.00  (flat cap to match the medusa's jammer's curve.)
        //
        // We don't smooth the curve evenly and only smoothen out the middle keyframes where we make the jump. This is to give it a bit more flair as you can see the jammer flicker out within 20km.
        // Also adds to the stress :)

        private static readonly Keyframe[] RangeFalloffKeyframes = new Keyframe[]
        {
            new Keyframe(    0f, 0.00f),
            new Keyframe( 5000f, 0.06f),
            new Keyframe(10000f, 0.06f),
            new Keyframe(17000f, 0.06f),
            new Keyframe(20000f, 1.00f),
            new Keyframe(80000f, 1.00f),
        };
        // ─────────────────────────────────────────────────────────────────────────

        // Reflected field infos are resolved once at startup so we don't repeat GetField on every patch call.
        private static readonly FieldInfo RangeFalloffField = typeof(JammingPod)
            .GetField("rangeFalloff", BindingFlags.NonPublic | BindingFlags.Instance);

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
        /// Applies our tuned values to a JammingPod component via reflection.
        /// Called once on the cloned prefab's component during encyclopedia setup.
        /// 
        /// The jam calculation is: rangeFalloff.Evaluate(distance) * (drawnPower / power).
        /// </summary>
        /// <param name="component">
        /// The specific jammingpod component we are trying to tune via the curve.
        /// </param>
        private static void ApplyTunedValues(JammingPod component)
        {
            if (component == null) return;

            // Replace the orginal rangeFalloff AnimationCurve 
            if (RangeFalloffField != null)
            {
                var curve = new AnimationCurve(RangeFalloffKeyframes);
                int len = curve.keys.Length;

                // Pin the near-zero keyframes down to the floor so the curve stays near zero at at the starting end of the curve. SmoothTangents on these.
                if(len > 2) { 
                // MoveKey is the correct way to edit a keyframe during runtime.
                // We also use curve.keys to return a copy so we aren't playing with direct assignment.
                curve.MoveKey(0, SetLinearKeyframe(curve.keys[0]));
                curve.MoveKey(1, SetLinearKeyframe(curve.keys[1]));
                }

                // Smooth the rising middle section for a natural S-curve feel.
                curve.SmoothTangents((len/2), 0.5f);
                curve.SmoothTangents((len/2+1), 0.5f);

                // Pin the end keyframes flat so the cap stays at 1.0.
                curve.MoveKey(len-1, SetLinearKeyframe(curve.keys[len-1]));
                curve.MoveKey(len-1, SetLinearKeyframe(curve.keys[len-1]));

                curve.preWrapMode = WrapMode.ClampForever;
                curve.postWrapMode = WrapMode.ClampForever;
                RangeFalloffField.SetValue(component, curve);
                //Log.LogInfo($"[M-JMMR] rangeFalloff replaced.");
            }
            else Log.LogWarning("[M-JMMR] Could not set 'rangeFalloff' — field not found.");
        }

        /// <summary>
        /// Returns a copy of the keyframe with both tangents set to zero.
        /// </summary>
        /// <param name="k">
        /// The keyframe we want to make linear.
        /// </param>
        private static Keyframe SetLinearKeyframe(Keyframe k)
        {
            k.inTangent = 0f;
            k.outTangent = 0f;
            return k;
        }

        /// <summary>
        /// Clones the original weaponmount designated by the weapon key at the top into a new named WeaponMount before AfterLoad runs.
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

                    // Clone WeaponMount
                    WeaponMount clonedMount = UnityEngine.Object.Instantiate(originalMount);
                    clonedMount.name = NewWeaponMountKey;
                    clonedMount.jsonKey = NewWeaponMountKey;
                    clonedMount.mountName = NewWeaponMountName;
                    clonedMount.dontAutomaticallyAddToEncyclopedia = false;

                    //Reduce the RCS value (stealth coating)
                    clonedMount.RCS = (float)0.0020;
                    clonedMount.emptyRCS = (float)0.0020;


                    // Apply tuned power and rangeFalloff values to the cloned component.
                    // We use GetComponentInChildren<T>(includeInactive: true) rather than GetComponent in case the JammingPod sits on a child transform.
                    if (clonedMount.prefab != null)
                    {
                        var jammingPod = clonedMount.prefab.GetComponentInChildren<JammingPod>(includeInactive: true);
                        if (jammingPod != null)
                            ApplyTunedValues(jammingPod);
                        else
                            Log.LogWarning("[M-JMMR] No JammingPod component found on cloned prefab.");
                    }
                    else Log.LogWarning("[M-JMMR] clonedMount.prefab is null — cannot modify JammingPod component.");

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