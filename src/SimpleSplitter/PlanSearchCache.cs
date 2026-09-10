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
        internal readonly Dictionary<SplitCandidate, bool> StockSafety = new Dictionary<SplitCandidate, bool>();
        internal int ReusedCounts { get; private set; }

        internal void Prepare(SplitRequest request)
        {
            if (context == null || !Matches(context, request))
            {
                counts.Clear();
                StockSafety.Clear();
            }
            context = request;
            var expired = new List<int>();
            foreach (var pair in counts)
                if (pair.Value.Exists(c => c.Nodes[0].Ut - c.Nodes[0].StartOffset <= request.Now + 30))
                    expired.Add(pair.Key);
            foreach (int count in expired)
            {
                foreach (var candidate in counts[count]) StockSafety.Remove(candidate);
                counts.Remove(count);
            }
            ReusedCounts = 0;
            foreach (int count in counts.Keys) if (count <= request.MaximumBurns) ReusedCounts++;
        }

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
                Math.Abs(a.OriginalUt - b.OriginalUt) > 1e-6 || Math.Abs(a.ArrivalUt - b.ArrivalUt) > 1e-6 ||
                (a.OriginalDeltaV - b.OriginalDeltaV).magnitude > 1e-9 ||
                !MatchesPropulsion(a.Propulsion, b.Propulsion)) return false;
            a.SourceOrbit.GetOrbitalStateVectorsAtUT(a.OriginalUt, out Vector3d ar, out Vector3d av);
            b.SourceOrbit.GetOrbitalStateVectorsAtUT(b.OriginalUt, out Vector3d br, out Vector3d bv);
            if ((ar - br).magnitude > 1e-4 || (av - bv).magnitude > 1e-7) return false;
            return true;
        }

        internal static bool MatchesPropulsion(StagedPropulsion a, StagedPropulsion b)
        {
            if (!a.IsUsable || !b.IsUsable || a.Stages.Count != b.Stages.Count) return false;
            for (int i = 0; i < a.Stages.Count; i++)
            {
                BurnStage x = a.Stages[i], y = b.Stages[i];
                if (x.Stage != y.Stage || x.DeltaV != y.DeltaV || x.Engine.Mass != y.Engine.Mass ||
                    x.Engine.Thrust != y.Engine.Thrust || x.Engine.ExhaustVelocity != y.Engine.ExhaustVelocity) return false;
            }
            return true;
        }
    }
}
