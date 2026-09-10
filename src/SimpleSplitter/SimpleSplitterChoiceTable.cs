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
                "Original Δv: " + choiceRequest.OriginalDeltaV.magnitude.ToString("F1") + " m/s. Errors estimate timed execution; stock encounters are checked.");
            float[] shares = { .06f, .14f, .13f, .14f, .15f, .13f, .13f, .12f };
            string[] headings = { "Burns", "Total Δv", "Extra Δv", "Largest Δv", "Dep. error", "Pos. km", "Cosine", "" };
            string[] tips = { "Total maneuvers, including plane change and departure.", "Total timed Δv, m/s.",
                "Additional Δv relative to the original impulse, m/s; negative means savings.", "Largest timed burn, m/s.",
                "Outgoing excess-velocity vector error in m/s; local velocity error for bound departures.",
                "Full-sequence position error at the end of the departure burn, km.",
                "Maximum cosine loss over all timed burns; reported, not filtered.", "" };
            float x = 12;
            for (int i = 0; i < headings.Length; i++)
            {
                GUI.Label(new Rect(x, 174, (width - 20) * shares[i], 24), new GUIContent(headings[i], tips[i]), headingLabel);
                x += (width - 20) * shares[i];
            }
            float viewportHeight = Math.Max(30, panelRect.height - 236);
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
                        : choiceErrors.TryGetValue(count, out string error) ? "No safe plan found: " + error
                        : planningCoroutine != null ? "Searching / checking..." : "No safe plan found.";
                    GUI.Label(new Rect(rowWidth * shares[0], y, rowWidth * (1 - shares[0]), 26), new GUIContent(status, status));
                    continue;
                }
                double departureError = CandidateRules.IsFinite(candidate.ExcessVelocityError)
                    ? candidate.ExcessVelocityError : candidate.ExecutionEstimate.VelocityError;
                string[] values = { candidate.Score.TotalDeltaV.ToString("F1"),
                    candidate.AdditionalDeltaV.ToString("+0.0;-0.0;0.0"), candidate.Score.LargestBurn.ToString("F1"),
                    departureError.ToString("F2"), (candidate.ExecutionEstimate.PositionError / 1000).ToString("F2"),
                    candidate.MaximumCosineLoss.ToString("P1") };
                x = rowWidth * shares[0];
                for (int i = 0; i < values.Length; i++)
                {
                    GUI.Label(new Rect(x, y, rowWidth * shares[i + 1] - 4, 26), values[i]);
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
                GUI.Label(new Rect(12, panelRect.height - 32, width, 28), GUI.tooltip, wrappedLabel);
        }
    }
}
