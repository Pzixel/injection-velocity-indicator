using System.Reflection;
using HarmonyLib;

namespace InjectionVelocityIndicator
{
    internal static class OrbitTargeterPatchAccessor
    {
        private static readonly FieldInfo? RefPatchField =
            AccessTools.Field(typeof(OrbitTargeter), "refPatch");

        private static readonly FieldInfo? TargetRefPatchField =
            AccessTools.Field(typeof(OrbitTargeter), "tgtRefPatch");

        internal static bool TryGetCurrentPatches(
            OrbitTargeter? orbitTargeter,
            out Orbit? trajectoryPatch,
            out Orbit? targetPatch)
        {
            trajectoryPatch = null;
            targetPatch = null;

            if (orbitTargeter == null ||
                RefPatchField == null ||
                TargetRefPatchField == null)
            {
                return false;
            }

            trajectoryPatch = RefPatchField.GetValue(orbitTargeter) as Orbit;
            targetPatch = TargetRefPatchField.GetValue(orbitTargeter) as Orbit;

            return trajectoryPatch != null && targetPatch != null;
        }
    }
}
