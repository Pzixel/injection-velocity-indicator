using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    // A single resource-drain interval exported by the embedded KER simulator.
    internal sealed class EngineerSegment
    {
        internal EngineerSegment(double startMass, double endMass, double deltaV, double seconds,
            string unsupported = "")
        {
            StartMass = startMass; EndMass = endMass; DeltaV = deltaV; Seconds = seconds;
            Unsupported = unsupported;
        }
        internal double StartMass { get; }
        internal double EndMass { get; }
        internal double DeltaV { get; }
        internal double Seconds { get; }
        internal string Unsupported { get; }
    }

    internal static class EngineerSegmentMapper
    {
        internal static void AppendStage(List<BurnStage> destination, int stage, IEnumerable<EngineerSegment> segments)
        {
            int first = destination.Count;
            foreach (EngineerSegment segment in segments)
            {
                if (!string.IsNullOrEmpty(segment.Unsupported))
                    throw new InvalidOperationException("Stage " + stage + ": " + segment.Unsupported);
                if (!CandidateRules.IsFinite(segment.StartMass) || !CandidateRules.IsFinite(segment.EndMass) ||
                    !CandidateRules.IsFinite(segment.DeltaV) || !CandidateRules.IsFinite(segment.Seconds) ||
                    segment.StartMass <= segment.EndMass || segment.EndMass <= 0 || segment.DeltaV <= 0 || segment.Seconds <= 0)
                    throw new InvalidOperationException("The embedded KER simulator returned invalid fuel data for stage " + stage + ".");
                double exhaust = segment.DeltaV / Math.Log(segment.StartMass / segment.EndMass);
                double thrust = (segment.StartMass - segment.EndMass) * exhaust / segment.Seconds;
                var physics = new BurnPhysics(segment.StartMass, thrust, exhaust);
                if (!physics.IsUsable) throw new InvalidOperationException("Invalid engine model for stage " + stage + ".");

                // Tank drain boundaries alone must not consume additional burns.
                // Merge only contiguous intervals with the same effective engine.
                if (destination.Count > first)
                {
                    BurnStage previous = destination[destination.Count - 1];
                    if (Close(previous.Engine.Thrust, thrust) && Close(previous.Engine.ExhaustVelocity, exhaust) &&
                        Close(previous.Engine.MassAfter(previous.DeltaV), segment.StartMass))
                    {
                        destination[destination.Count - 1] = new BurnStage(stage, previous.Engine, previous.DeltaV + segment.DeltaV);
                        continue;
                    }
                }
                destination.Add(new BurnStage(stage, physics, segment.DeltaV));
            }
        }
        private static bool Close(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-9, Math.Abs(a) * 1e-7);
    }
}
