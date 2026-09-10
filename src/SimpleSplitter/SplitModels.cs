using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed class NodeSpec
    {
        internal NodeSpec(double ut, Vector3d deltaV, double duration = 0.0,
            double startOffset = 0.0, string purpose = "Maneuver", double burnDeltaV = double.NaN)
        {
            Ut = ut;
            DeltaV = deltaV;
            Duration = duration;
            StartOffset = startOffset;
            Purpose = purpose;
            BurnDeltaV = double.IsNaN(burnDeltaV) ? deltaV.magnitude : burnDeltaV;
        }

        internal double Ut { get; }
        internal Vector3d DeltaV { get; }
        internal double Duration { get; }
        internal double StartOffset { get; }
        internal string Purpose { get; }
        internal double BurnDeltaV { get; }
    }

    internal sealed class SplitCandidate
    {
        internal SplitCandidate(
            List<NodeSpec> nodes,
            CandidateScore score,
            int leadOrbits, double setupDeltaV, double maximumSetupLoss,
            FiniteBurnEstimate departureEstimate, FiniteBurnEstimate executionEstimate, double excessVelocityError, double originalDeltaV)
        {
            Nodes = nodes;
            Score = score;
            LeadOrbits = leadOrbits;
            SetupDeltaV = setupDeltaV;
            MaximumSetupLoss = maximumSetupLoss;
            DepartureEstimate = departureEstimate;
            ExecutionEstimate = executionEstimate;
            ExcessVelocityError = excessVelocityError;
            OriginalDeltaV = originalDeltaV;
        }

        internal List<NodeSpec> Nodes { get; }
        internal CandidateScore Score { get; }
        internal int LeadOrbits { get; }
        internal double SetupDeltaV { get; }
        internal double MaximumSetupLoss { get; }
        internal FiniteBurnEstimate DepartureEstimate { get; }
        internal FiniteBurnEstimate ExecutionEstimate { get; }
        internal double ExcessVelocityError { get; }
        internal double MaximumCosineLoss => Math.Max(MaximumSetupLoss, ExecutionEstimate.CosineLoss);
        internal double AdditionalDeltaV => Score.TotalDeltaV - OriginalDeltaV;
        internal double OriginalDeltaV { get; }
    }

    internal sealed class SplitRequest
    {
        internal SplitRequest(
            ManeuverNode node,
            CelestialBody targetBody,
            double arrivalUt,
            double now, BurnPhysics engine, int maximumBurns = 5, PlanSearchCache? cache = null)
            : this(node, targetBody, arrivalUt, now, StagedPropulsion.Single(engine), maximumBurns, cache) { }

        internal SplitRequest(ManeuverNode node, CelestialBody targetBody, double arrivalUt,
            double now, StagedPropulsion propulsion, int maximumBurns = 5, PlanSearchCache? cache = null)
        {
            Node = node;
            TargetBody = targetBody;
            ArrivalUt = arrivalUt;
            Now = now;
            OriginalUt = node.UT;
            OriginalDeltaV = node.DeltaV;
            Propulsion = propulsion;
            Cache = cache ?? new PlanSearchCache();
            MaximumBurns = maximumBurns;
            node.patch.GetOrbitalStateVectorsAtUT(node.UT, out Vector3d r, out Vector3d v);
            SourceOrbit = SplitPlanner.OrbitFromState(r, v, node.patch.referenceBody, node.UT);
        }

        internal ManeuverNode Node { get; }
        internal CelestialBody TargetBody { get; }
        internal double ArrivalUt { get; }
        internal double Now { get; }
        internal double OriginalUt { get; }
        internal Vector3d OriginalDeltaV { get; }
        internal Orbit SourceOrbit { get; }
        internal StagedPropulsion Propulsion { get; }
        internal PlanSearchCache Cache { get; }
        internal int MaximumBurns { get; }
        internal string Progress { get; set; } = "Preparing staged burns...";
    }

    internal sealed class PlanResult
    {
        private PlanResult(List<SplitCandidate> candidates, string error)
        {
            Candidates = candidates;
            Error = error;
        }

        internal SplitCandidate? Candidate => Candidates.Count > 0 ? Candidates[0] : null;
        internal List<SplitCandidate> Candidates { get; }
        internal string Error { get; }

        internal static PlanResult Success(List<SplitCandidate> candidates)
        {
            return new PlanResult(candidates, string.Empty);
        }

        internal static PlanResult Failure(string error)
        {
            return new PlanResult(new List<SplitCandidate>(), error);
        }
    }
}
