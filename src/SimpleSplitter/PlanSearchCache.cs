using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    // The maximum row count is deliberately not part of the key. Each completed
    // count is independent, so growing the table computes only the new counts.
    internal sealed class PlanSearchCache
    {
        private SplitRequest? context;
        private readonly Dictionary<int, List<SplitCandidate>> counts = new Dictionary<int, List<SplitCandidate>>();
        private readonly HashSet<int> refined = new HashSet<int>();
        private readonly Dictionary<int, int> refinementSteps = new Dictionary<int, int>();
        internal bool RefinementBudgetReached { get; set; }
        internal readonly Dictionary<SplitCandidate, bool> StockSafety = new Dictionary<SplitCandidate, bool>();
        internal int ReusedCounts { get; private set; }

        internal void Prepare(SplitRequest request)
        {
            // Geometry/integration work survives harmless input roundoff, but
            // safety belongs to the live comparison. In particular a rejection
            // against a stale derived arrival must not become permanent.
            StockSafety.Clear();
            RefinementBudgetReached = false;
            if (context == null || !Matches(context, request))
            {
                counts.Clear();
                refined.Clear();
                refinementSteps.Clear();
                StockSafety.Clear();
                context = request;
            }
            var expired = new List<int>();
            foreach (var pair in counts)
                if (pair.Value.Exists(c => c.Nodes[0].Ut - c.Nodes[0].StartOffset <= request.Now + 30))
                    expired.Add(pair.Key);
            foreach (int count in expired)
            {
                foreach (var candidate in counts[count]) StockSafety.Remove(candidate);
                counts.Remove(count);
                refined.Remove(count);
                refinementSteps.Remove(count);
            }
            ReusedCounts = 0;
            foreach (int count in counts.Keys) if (count <= request.MaximumBurns) ReusedCounts++;
        }

        internal bool IsRefined(int count) => refined.Contains(count);
        internal int RefinementStep(int count) => refinementSteps.TryGetValue(count, out int step) ? step : 0;
        internal void AdvanceRefinement(int count) => refinementSteps[count] = RefinementStep(count) + 1;
        internal void MarkRefined(int count) => refined.Add(count);
        internal List<SplitCandidate> ForCount(int count) => counts.TryGetValue(count, out List<SplitCandidate>? result)
            ? new List<SplitCandidate>(result) : new List<SplitCandidate>();

        internal bool Contains(int count) => counts.ContainsKey(count);
        internal void Store(int count, List<SplitCandidate> candidates) => counts[count] = candidates;
        internal List<SplitCandidate> GetCandidates(int maximum)
        {
            var result = new List<SplitCandidate>();
            for (int count = 2; count <= maximum; count++)
                if (counts.TryGetValue(count, out List<SplitCandidate>? candidates)) result.AddRange(candidates);
            return result;
        }

        internal static bool Matches(SplitRequest a, SplitRequest b)
        {
            if (a.TargetBody != b.TargetBody || a.SourceOrbit.referenceBody != b.SourceOrbit.referenceBody ||
                Math.Abs(a.OriginalUt - b.OriginalUt) > 1e-6 ||
                (a.OriginalDeltaV - b.OriginalDeltaV).magnitude > 1e-9 ||
                !MatchesPropulsion(a.Propulsion, b.Propulsion)) return false;
            // Keep the original capture epoch as the anchor, so repeated
            // accepted roundoff cannot accumulate into an undetected edit.
            return OrbitState.MatchesInputs(a.SourceOrbit, b.SourceOrbit, a.Now);
        }

        internal static bool MatchesPropulsion(StagedPropulsion a, StagedPropulsion b)
        {
            if (!a.IsUsable || !b.IsUsable || a.Stages.Count != b.Stages.Count) return false;
            for (int i = 0; i < a.Stages.Count; i++)
            {
                BurnStage x = a.Stages[i], y = b.Stages[i];
                if (x.Stage != y.Stage || !SameInput(x.DeltaV, y.DeltaV) || !SameInput(x.Engine.Mass, y.Engine.Mass) ||
                    !SameInput(x.Engine.Thrust, y.Engine.Thrust) || !SameInput(x.Engine.ExhaustVelocity, y.Engine.ExhaustVelocity)) return false;
            }
            return true;
        }

        // KER's thrust vectors use Unity floats. Allow one part per million of
        // numerical noise against the original cache context, never accumulating drift.
        private static bool SameInput(double a, double b) => a == b ||
            (CandidateRules.IsFinite(a) && CandidateRules.IsFinite(b) && Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Abs(a)));
    }
}
