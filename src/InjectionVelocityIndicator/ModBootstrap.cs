using System;
using HarmonyLib;
using UnityEngine;

namespace InjectionVelocityIndicator
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal sealed class ModBootstrap : MonoBehaviour
    {
        private static bool initialized;

        private void Awake()
        {
            if (initialized)
            {
                Destroy(this);
                return;
            }

            initialized = true;

            if (CompatibilityDetector.IsPrincipiaInstalled())
            {
                Debug.LogWarning(ModInfo.LogPrefix +
                    "Principia was detected. The stock-conics relative-speed feature is disabled.");
                Destroy(this);
                return;
            }

            try
            {
                Harmony harmony = new Harmony(ModInfo.HarmonyId);
                harmony.PatchAll(typeof(ModBootstrap).Assembly);

                DontDestroyOnLoad(gameObject);
                Debug.Log(ModInfo.LogPrefix + "Harmony patches applied.");
            }
            catch (Exception exception)
            {
                initialized = false;
                Debug.LogError(ModInfo.LogPrefix + "Failed to apply Harmony patches: " + exception);
                Destroy(this);
            }
        }
    }
}

