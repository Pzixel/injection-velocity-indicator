using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSP.Localization;
using KSP.UI.Screens.Mapview;
using UnityEngine;

namespace InjectionVelocityIndicator
{
    [HarmonyPatch(
        typeof(OrbitTargeter.ClApprMarker),
        "OnUpdateCaption",
        new Type[]
        {
            typeof(MapNode),
            typeof(MapNode.CaptionData)
        })]
    internal static class ClosestApproachCaptionPatch
    {
        private static readonly ConditionalWeakTable<
            OrbitTargeter.ClApprMarker,
            ClosestApproachCaptionState> MarkerStates =
                new ConditionalWeakTable<
                    OrbitTargeter.ClApprMarker,
                    ClosestApproachCaptionState>();

        [HarmonyPostfix]
        private static void Postfix(
            OrbitTargeter.ClApprMarker? __instance,
            MapNode.CaptionData? __1)
        {
            if (__instance == null || __1 == null)
            {
                return;
            }

            ClosestApproachCaptionState state =
                MarkerStates.GetValue(
                    __instance,
                    marker => new ClosestApproachCaptionState(marker));

            state.Update(__1);
        }
    }

    internal sealed class ClosestApproachCaptionState
    {
        private readonly OrbitTargeter.ClApprMarker marker;

        private string? cachedSeparationLine;
        private CelestialBody? cachedTargetBody;
        private bool hasRelativeSpeed;
        private string? relativeSpeedLine;

        private string? previousStockTimeLine;
        private bool ownsThirdLine;
        private bool calculationErrorLogged;

        internal ClosestApproachCaptionState(
            OrbitTargeter.ClApprMarker marker)
        {
            this.marker = marker;
        }

        internal void Update(MapNode.CaptionData caption)
        {
            RestoreStockCaptionIfNeeded(caption);

            if (marker.atype != MapNode.ApproachNodeType.CloseApproachOwn)
            {
                return;
            }

            CelestialBody? targetBody = FlightGlobals.fetch == null
                ? null
                : FlightGlobals.fetch.VesselTarget as CelestialBody;

            if (targetBody == null)
            {
                return;
            }

            bool targetChanged =
                !ReferenceEquals(cachedTargetBody, targetBody);
            bool separationChanged =
                !string.Equals(
                    cachedSeparationLine,
                    caption.captionLine1,
                    StringComparison.Ordinal);

            if (!hasRelativeSpeed || targetChanged || separationChanged)
            {
                Orbit? trajectoryPatch;
                Orbit? targetPatch;
                string? calculatedLine;

                if (OrbitTargeterPatchAccessor.TryGetCurrentPatches(
                        marker.orbitTargeter,
                        out trajectoryPatch,
                        out targetPatch) &&
                    TryCalculateLine(
                        trajectoryPatch,
                        targetPatch,
                        out calculatedLine))
                {
                    cachedSeparationLine = caption.captionLine1;
                    cachedTargetBody = targetBody;
                    relativeSpeedLine = calculatedLine;
                    hasRelativeSpeed = true;
                }
                else if (targetChanged)
                {
                    // Never show a value cached for a different target. For a
                    // transient failure on the same target, retain the last
                    // valid line and retry on the next stock caption update.
                    cachedTargetBody = targetBody;
                    relativeSpeedLine = null;
                    hasRelativeSpeed = false;
                }
            }

            if (!hasRelativeSpeed || relativeSpeedLine is null)
            {
                return;
            }

            InsertRelativeSpeed(caption, relativeSpeedLine);
        }

        private bool TryCalculateLine(
            Orbit? trajectoryPatch,
            Orbit? targetPatch,
            out string? calculatedLine)
        {
            calculatedLine = null;

            try
            {
                double relativeSpeed;
                if (!RelativeSpeedCalculator.TryCalculate(
                    trajectoryPatch,
                    targetPatch,
                    out relativeSpeed))
                {
                    return false;
                }

                string formattedSpeed = SpeedFormatter.Format(relativeSpeed);
                calculatedLine = Localizer.Format(
                    ModInfo.StockRelativeSpeedLocalizationTag,
                    formattedSpeed);

                if (string.IsNullOrEmpty(calculatedLine) ||
                    calculatedLine ==
                        ModInfo.StockRelativeSpeedLocalizationTag)
                {
                    calculatedLine =
                        "Relative Speed: " + formattedSpeed + "m/s";
                }

                return true;
            }
            catch (Exception exception)
            {
                if (!calculationErrorLogged)
                {
                    calculationErrorLogged = true;
                    Debug.LogWarning(ModInfo.LogPrefix +
                        "Could not calculate closest-approach relative speed; " +
                        "the stock caption was left unchanged. " + exception);
                }

                return false;
            }
        }

        private void RestoreStockCaptionIfNeeded(
            MapNode.CaptionData caption)
        {
            if (ownsThirdLine &&
                string.Equals(
                    caption.captionLine3,
                    previousStockTimeLine,
                    StringComparison.Ordinal))
            {
                // The stock callback has already regenerated line 2. Remove only
                // line 3 previously owned by this mod; preserve other mods' data.
                caption.captionLine3 = null;
            }

            previousStockTimeLine = null;
            ownsThirdLine = false;
        }

        private void InsertRelativeSpeed(
            MapNode.CaptionData caption,
            string speedLine)
        {
            // Match the stock vessel-rendezvous layout: separation, relative
            // speed, then time. This runs directly after stock regenerated the
            // closest-approach separation and time caption.
            if (string.IsNullOrEmpty(caption.captionLine3))
            {
                previousStockTimeLine = caption.captionLine2;
                ownsThirdLine = true;
                caption.captionLine3 = caption.captionLine2;
                caption.captionLine2 = speedLine;
                return;
            }

            // If another mod owns line 3, do not overwrite it.
            caption.captionLine1 = CaptionText.AppendInline(
                caption.captionLine1,
                speedLine);
        }
    }
}
