using System;
using UnityEngine;

namespace SimpleSplitter
{
    internal sealed partial class SimpleSplitterAddon
    {
        private Vector2 choiceScroll;

        private void DrawChoices(float width)
        {
            if (choiceRequest == null) return;
            GUI.Label(new Rect(12, 148, width, 22),
                "Original: " + choiceRequest.OriginalDeltaV.magnitude.ToString("F1") + " m/s | " +
                choiceRequest.TargetBody.bodyName + " arrival within " + FormatPlanDuration(CandidateRules.ArrivalToleranceSeconds) +
                "; offered plans pass safety checks.");
            float[] shares = { .07f, .18f, .25f, .19f, .19f, .12f };
            string[] headings = { "Burns", "Total Δv", "Added Δv", "Longest burn", "Arrival shift", "" };
            string[] tips = { "Total maneuvers, including plane change and departure.",
                "Total Δv consumed by the timed burns. Compare with the original maneuver above.",
                "Extra Δv and percentage of the original maneuver's Δv. Negative means savings; this is not encounter error.",
                "Longest continuous engine firing, at full throttle. Hover a value for total engine-on time.",
                "Simulated arrival early/late at the target's sphere of influence, relative to the live original maneuver. Rechecked on Apply.", "" };
            float x = 12;
            for (int i = 0; i < headings.Length; i++)
            {
                GUI.Label(new Rect(x, 174, (width - 20) * shares[i], 24), new GUIContent(headings[i], tips[i]), headingLabel);
                x += (width - 20) * shares[i];
            }
            float viewportHeight = Math.Max(30, panelRect.height - 252);
            choiceScroll.x = 0;
            choiceScroll = GUI.BeginScrollView(new Rect(12, 200, width, viewportHeight), choiceScroll,
                new Rect(0, 0, width - 20, (maximumBurns - 1) * 30), false, true);
            float rowWidth = width - 20;
            for (int count = 2; count <= maximumBurns; count++)
            {
                float y = (count - 2) * 30;
                GUI.Label(new Rect(0, y, rowWidth * shares[0], 26), count.ToString());
                if (!choices.TryGetValue(count, out SplitCandidate candidate))
                {
                    string status = count > choiceRequest.MaximumBurns ? "Compare to search this count."
                        : planningCoroutine != null ? "Searching / checking distributions..."
                        : choiceErrors.TryGetValue(count, out string error) ? "Tested alternatives rejected: " + error
                        : "No executable option in tested distributions.";
                    GUI.Label(new Rect(rowWidth * shares[0], y, rowWidth * (1 - shares[0]), 26), new GUIContent(status, status));
                    continue;
                }
                double longest = 0, totalDuration = 0;
                foreach (NodeSpec node in candidate.Nodes)
                {
                    longest = Math.Max(longest, node.Duration);
                    totalDuration += node.Duration;
                }
                double extraPercent = 100 * candidate.AdditionalDeltaV / candidate.OriginalDeltaV;
                string[] values = { candidate.Score.TotalDeltaV.ToString("F1") + " m/s",
                    candidate.AdditionalDeltaV.ToString("+0.0;-0.0;0.0") + " m/s (" + extraPercent.ToString("+0.0;-0.0;0.0") + "%)",
                    FormatPlanDuration(longest), FormatArrivalShift(candidate.TimedArrivalOffsetSeconds) };
                // Preserve the geometric diagnostic in a tooltip, explicitly
                // distinguishing it from integrated fuel cost or arrival error.
                double spread = Math.Acos(Math.Max(-1, Math.Min(1, 1 - candidate.MaximumCosineLoss))) * 180 / Math.PI;
                string[] valueTips = { tips[1], tips[2],
                    "Total engine-on time: " + FormatPlanDuration(totalDuration) + ". Peak angle from node position: " + spread.ToString("F1") +
                    "° (1 − cos: " + candidate.MaximumCosineLoss.ToString("P1") + "). Geometry only; not Δv wasted or encounter error.", tips[4] };
                x = rowWidth * shares[0];
                for (int i = 0; i < values.Length; i++)
                {
                    GUI.Label(new Rect(x, y, rowWidth * shares[i + 1] - 4, 26), new GUIContent(values[i], valueTips[i]));
                    x += rowWidth * shares[i + 1];
                }
                bool enabledBefore = GUI.enabled;
                GUI.enabled = enabledBefore && planningCoroutine == null &&
                    candidate.Nodes[0].Ut - candidate.Nodes[0].StartOffset > Planetarium.GetUniversalTime() + 30;
                if (GUI.Button(new Rect(x, y, rowWidth - x, 24), "Apply")) applyRequested = candidate;
                GUI.enabled = enabledBefore;
            }
            GUI.EndScrollView();
            if (!string.IsNullOrEmpty(GUI.tooltip))
                GUI.Label(new Rect(12, panelRect.height - 48, width, 44), GUI.tooltip, wrappedLabel);
        }

        private static string FormatPlanDuration(double seconds)
        {
            if (!CandidateRules.IsFinite(seconds)) return "—";
            long rounded = (long)Math.Round(Math.Abs(seconds));
            if (rounded < 60) return rounded + " s";
            if (rounded < 3600) return rounded / 60 + "m " + (rounded % 60).ToString("00") + "s";
            return rounded / 3600 + "h " + ((rounded % 3600) / 60).ToString("00") + "m";
        }

        private static string FormatArrivalShift(double seconds)
        {
            if (!CandidateRules.IsFinite(seconds)) return "Not checked";
            if (Math.Abs(seconds) < 0.5) return "<1 s";
            return FormatPlanDuration(seconds) + (seconds < 0 ? " early" : " late");
        }
    }
}
