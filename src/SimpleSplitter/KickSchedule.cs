using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed class KickSchedule
    {
        internal const int MaximumKicks = 9;
        internal readonly List<double> DeltaVs = new List<double>();
        internal readonly List<bool> StageEnds = new List<bool>();
        internal readonly List<double> Periods = new List<double>();
        internal double SetupDeltaV, Elapsed;
        internal int LeadOrbits;
        internal int[] StageKickCounts = new int[0];
        internal double SearchCeiling, DistributionBias;

        internal static KickSchedule? Find(int count, double mu, double radius,
            double speed, double tangentialFraction, double minimumRadius,
            double maximumRadius, double originalPeriod, int availableOrbits,
            double maximumDeltaV, BurnPhysics engine, double loss) => Find(count, mu, radius, speed,
                tangentialFraction, minimumRadius, maximumRadius, originalPeriod, availableOrbits,
                maximumDeltaV, StagedPropulsion.Single(engine), loss);

        internal static KickSchedule? Find(int count, double mu, double radius,
            double speed, double tangentialFraction, double minimumRadius,
            double maximumRadius, double originalPeriod, int availableOrbits,
            double maximumDeltaV, StagedPropulsion propulsion, double loss, int[]? fixedLayout = null, double distributionBias = 0)
        {
            List<KickSchedule> schedules = FindAll(count, mu, radius, speed, tangentialFraction,
                minimumRadius, maximumRadius, originalPeriod, availableOrbits, maximumDeltaV, propulsion, loss, fixedLayout, distributionBias);
            return schedules.Count == 0 ? null : schedules[0];
        }

        internal static List<KickSchedule> FindAll(int count, double mu, double radius,
            double speed, double tangentialFraction, double minimumRadius,
            double maximumRadius, double originalPeriod, int availableOrbits,
            double maximumDeltaV, StagedPropulsion propulsion, double loss, int[]? fixedLayout = null, double distributionBias = 0)
        {
            var schedules = new List<KickSchedule>();
            if (count < 1 || count > MaximumKicks || availableOrbits <= count ||
                !propulsion.IsUsable || loss <= 0 || (loss >= 1 && !double.IsPositiveInfinity(loss)) ||
                !CandidateRules.IsFinite(maximumDeltaV) || maximumDeltaV <= 0) return schedules;
            double ceiling = Math.Min(maximumDeltaV, Math.Sqrt(2 * mu / radius) - speed);
            if (fixedLayout != null)
            {
                if (fixedLayout.Length > propulsion.Stages.Count || fixedLayout.Length == 0) return schedules;
                int slots = 0;
                double prefix = 0;
                for (int i = 0; i < fixedLayout.Length; i++)
                {
                    if (fixedLayout[i] <= 0) return schedules;
                    slots += fixedLayout[i];
                    if (i + 1 < fixedLayout.Length) prefix += propulsion.Stages[i].DeltaV;
                }
                if (slots != count) return schedules;
                Add(FindLayout(new List<int>(fixedLayout), prefix,
                    Math.Min(ceiling, prefix + propulsion.Stages[fixedLayout.Length - 1].DeltaV)));
            }
            else Enumerate(0, count, 0, new List<int>());
            schedules.Sort((a, b) => b.SetupDeltaV.CompareTo(a.SetupDeltaV));
            return schedules;

            void Add(KickSchedule? schedule) { if (schedule != null) schedules.Add(schedule); }

            // Optimize each constant-propulsion segment, then combine its burn
            // count options under the shared node budget. No global equal-dv
            // allocation can suppress an efficient one-burn stage option.
            void Enumerate(int segment, int slots, double prefix, List<int> layout)
            {
                if (segment >= propulsion.Stages.Count || slots <= 0 || prefix >= ceiling) return;
                BurnStage stage = propulsion.Stages[segment];
                layout.Add(slots);
                Add(FindLayout(layout, prefix, Math.Min(ceiling, prefix + stage.DeltaV)));
                layout.RemoveAt(layout.Count - 1);
                if (stage.DeltaV >= ceiling - prefix) return;
                for (int needed = 1; needed < slots; needed++)
                {
                    if (!SegmentFits(stage, prefix, stage.DeltaV, needed)) continue;
                    layout.Add(needed);
                    Enumerate(segment + 1, slots - needed, prefix + stage.DeltaV, layout);
                    layout.RemoveAt(layout.Count - 1);
                }
            }

            bool SegmentFits(BurnStage stage, double priorDeltaV, double total, int slots)
            {
                if (double.IsPositiveInfinity(loss)) return true;
                double spent = 0;
                for (int i = 0; i < slots; i++)
                {
                    double kick = total * Share(i, slots, distributionBias);
                    // Orbital speed includes all preceding stages; mass loss
                    // belongs only to the propulsion segment being evaluated.
                    BurnPhysics engine = new BurnPhysics(stage.Engine.MassAfter(spent),
                        stage.Engine.Thrust, stage.Engine.ExhaustVelocity);
                    if (!engine.KickFits(mu, radius, speed + priorDeltaV + spent,
                        tangentialFraction, 0, kick, loss)) return false;
                    spent += kick;
                }
                return true;
            }

            KickSchedule? FindLayout(List<int> counts, double floor, double ceilingForLayout)
            {
                if (ceilingForLayout <= floor) return null;
                int[] fixedCounts = counts.ToArray();
                KickSchedule? Build(double total)
                {
                    if (total <= floor || total > ceilingForLayout + 1e-7) return null;
                    KickSchedule trial = new KickSchedule { SetupDeltaV = total, StageKickCounts = fixedCounts,
                        SearchCeiling = maximumDeltaV, DistributionBias = distributionBias };
                    double spent = 0;
                    for (int segment = 0; segment < fixedCounts.Length; segment++)
                    {
                        BurnStage stage = propulsion.Stages[segment];
                        double segmentTotal = segment + 1 == fixedCounts.Length ? total - spent : stage.DeltaV;
                        if (segmentTotal <= 1e-7 || segmentTotal > stage.DeltaV + 1e-6 ||
                            !SegmentFits(stage, spent, segmentTotal, fixedCounts[segment])) return null;
                        for (int i = 0; i < fixedCounts[segment]; i++)
                        {
                            double kick = segmentTotal * Share(i, fixedCounts[segment], distributionBias);
                            if (!OrbitScalars.TryCreate(mu, radius, speed + spent + kick, tangentialFraction, out OrbitScalars orbit) ||
                                orbit.Periapsis <= minimumRadius || orbit.Apoapsis >= maximumRadius) return null;
                            trial.DeltaVs.Add(kick);
                            trial.StageEnds.Add(i + 1 == fixedCounts[segment] && Math.Abs(segmentTotal - stage.DeltaV) <= 1e-6);
                            trial.Periods.Add(orbit.Period);
                            trial.Elapsed += orbit.Period;
                            spent += kick;
                        }
                    }
                    return trial;
                }
                // Only the final segment total varies within this layout, so
                // refinement never jumps between different stage-slot assignments.
                const int samples = 512;
                double upperTotal = ceilingForLayout;
                KickSchedule? upper = Build(upperTotal);
                for (int sample = samples - 1; sample >= 0; sample--)
                {
                    double lowerTotal = floor + (ceilingForLayout - floor) * sample / samples;
                    KickSchedule? lower = Build(Math.Max(floor + 1e-7, lowerTotal));
                    if (lower != null && upper == null)
                    {
                        double lo = lowerTotal, hi = upperTotal;
                        for (int i = 0; i < 40; i++)
                        {
                            double middle = (lo + hi) * 0.5;
                            if (Build(middle) != null) lo = middle; else hi = middle;
                        }
                        upperTotal = lo;
                        upper = Build(lo);
                    }
                    if (lower != null && upper != null)
                    {
                        int lead = (int)Math.Min(availableOrbits, Math.Floor(upper.Elapsed / originalPeriod));
                        double target = lead * originalPeriod;
                        if (lead > count && lower.Elapsed <= target)
                        {
                            double lo = lowerTotal, hi = upperTotal;
                            for (int i = 0; i < 50; i++)
                            {
                                double middle = (lo + hi) * 0.5;
                                KickSchedule? trial = Build(middle);
                                if (trial == null || trial.Elapsed > target) hi = middle; else lo = middle;
                            }
                            KickSchedule? result = Build((lo + hi) * 0.5);
                            if (result != null && Math.Abs(result.Elapsed - target) < 0.01)
                            { result.LeadOrbits = lead; return result; }
                        }
                    }
                    upperTotal = lowerTotal;
                    upper = lower;
                }
                return null;
            }
        }
        // Positive bias transfers delta-v into later kicks in each propulsion
        // segment. The segment total and staging boundary remain unchanged.
        private static double Share(int index, int count, double bias)
        {
            if (count <= 1 || bias == 0) return 1.0 / count;
            double sum = 0;
            for (int i = 0; i < count; i++) sum += Math.Exp(bias * (2.0 * i / (count - 1) - 1));
            return Math.Exp(bias * (2.0 * index / (count - 1) - 1)) / sum;
        }
    }
}
