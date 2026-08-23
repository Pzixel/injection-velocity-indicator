using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ManeuverOrbitJumper
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal sealed class ManeuverOrbitJumperAddon : MonoBehaviour
    {
        internal const string HarmonyId =
            "com.github.pzixel.maneuverorbitjumper";
        internal const string LogPrefix = "[ManeuverOrbitJumper] ";

        private void Awake()
        {
            Harmony harmony = new Harmony(HarmonyId);
            try
            {
                harmony.PatchAll(typeof(ManeuverOrbitJumperAddon).Assembly);
                Debug.Log(LogPrefix + "Harmony patches applied.");
            }
            catch (Exception exception)
            {
                try
                {
                    harmony.UnpatchAll(HarmonyId);
                }
                catch (Exception rollbackException)
                {
                    Debug.LogError(
                        LogPrefix + "Patch rollback failed: " +
                        rollbackException);
                }

                Debug.LogError(LogPrefix + "Patch failed: " + exception);
            }
        }
    }

    [HarmonyPatch]
    internal static class ManeuverButtonPatches
    {
        private static readonly FieldInfo OrbitsAdded =
            RequireField(typeof(ManeuverGizmo), "orbitsAdded");
        private static readonly FieldInfo OrbitPeriod =
            RequireField(typeof(Orbit), "period");
        private static readonly FieldInfo TimeStep =
            RequireField(
                typeof(ManeuverNodeEditorTabVectorHandles),
                "baseTimeStepValue");
        private static readonly MethodInfo GetStepMultiplierMethod =
            RequireMethod(
                typeof(ManeuverButtonPatches),
                nameof(GetStepMultiplier));

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return RequireMethod(
                typeof(ManeuverGizmo),
                "OnPlusOrbitPress");
            yield return RequireMethod(
                typeof(ManeuverGizmo),
                "OnMinusOrbitPress");
            yield return RequireMethod(
                typeof(ManeuverNodeEditorTabVectorHandles),
                "TimeStepUp");
            yield return RequireMethod(
                typeof(ManeuverNodeEditorTabVectorHandles),
                "TimeStepDown");
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            bool orbitButton = original.DeclaringType == typeof(ManeuverGizmo);
            OpCode operation =
                original.Name == "OnPlusOrbitPress" ||
                original.Name == "TimeStepUp"
                    ? OpCodes.Add
                    : OpCodes.Sub;

            if (orbitButton)
            {
                int counter = FindUnique(
                    codes,
                    4,
                    index =>
                        codes[index].LoadsField(OrbitsAdded) &&
                        codes[index + 1].opcode == OpCodes.Ldc_I4_1 &&
                        codes[index + 2].opcode == operation &&
                        codes[index + 3].opcode == OpCodes.Stfld &&
                        Equals(codes[index + 3].operand, OrbitsAdded),
                    original);
                codes[counter + 1].opcode = OpCodes.Call;
                codes[counter + 1].operand = GetStepMultiplierMethod;
            }

            FieldInfo stepField = orbitButton ? OrbitPeriod : TimeStep;
            int step = FindUnique(
                codes,
                2,
                index =>
                    codes[index].LoadsField(stepField) &&
                    codes[index + 1].opcode == operation,
                original);
            CodeInstruction arithmetic = codes[step + 1];
            CodeInstruction multiplier = new CodeInstruction(
                OpCodes.Call,
                GetStepMultiplierMethod);
            arithmetic.MoveLabelsTo(multiplier);
            arithmetic.MoveBlocksTo(multiplier);
            codes.InsertRange(
                step + 1,
                new[]
                {
                    multiplier,
                    new CodeInstruction(OpCodes.Conv_R8),
                    new CodeInstruction(OpCodes.Mul)
                });

            return codes;
        }

        private static int GetStepMultiplier()
        {
            bool tenfoldModifierPressed =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);
            bool hundredfoldModifierPressed =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);
            return (tenfoldModifierPressed ? 10 : 1) *
                (hundredfoldModifierPressed ? 100 : 1);
        }

        private static int FindUnique(
            List<CodeInstruction> codes,
            int width,
            Func<int, bool> matches,
            MethodBase original)
        {
            int result = -1;
            for (int index = 0; index <= codes.Count - width; index++)
            {
                if (!matches(index))
                {
                    continue;
                }

                if (result >= 0)
                {
                    throw PatternMismatch(original);
                }

                result = index;
            }

            return result >= 0 ? result : throw PatternMismatch(original);
        }

        private static InvalidOperationException PatternMismatch(
            MethodBase original)
        {
            return new InvalidOperationException(
                ManeuverOrbitJumperAddon.LogPrefix + "Refused to patch " +
                original.DeclaringType?.Name + "." + original.Name +
                ": stock KSP 1.12.5 instructions did not match exactly once.");
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            return AccessTools.Field(type, name) ??
                throw new MissingFieldException(type.FullName, name);
        }

        private static MethodInfo RequireMethod(Type type, string name)
        {
            return AccessTools.Method(type, name) ??
                throw new MissingMethodException(type.FullName, name);
        }
    }
}
