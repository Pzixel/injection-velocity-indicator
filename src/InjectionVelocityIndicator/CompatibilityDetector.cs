using System;
using System.Reflection;

namespace InjectionVelocityIndicator
{
    internal static class CompatibilityDetector
    {
        internal static bool IsPrincipiaInstalled()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int index = 0; index < assemblies.Length; index++)
            {
                string name = assemblies[index].GetName().Name ?? string.Empty;

                if (name.IndexOf("principia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.Equals("ksp_plugin_adapter", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

