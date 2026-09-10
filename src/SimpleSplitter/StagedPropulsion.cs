using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed class BurnStage
    {
        internal BurnStage(int stage, BurnPhysics engine, double deltaV)
        { Stage = stage; Engine = engine; DeltaV = deltaV; }
        internal int Stage { get; }
        internal BurnPhysics Engine { get; }
        internal double DeltaV { get; }
    }

    internal sealed class StagedPropulsion
    {
        internal StagedPropulsion(IList<BurnStage> stages) { Stages = new List<BurnStage>(stages); }
        internal List<BurnStage> Stages { get; }
        internal bool IsUsable => Stages.Count > 0 && Stages.TrueForAll(s => s.Engine.IsUsable && s.DeltaV > 0 && !double.IsNaN(s.DeltaV));
        internal static StagedPropulsion Single(BurnPhysics engine) =>
            new StagedPropulsion(new[] { new BurnStage(0, engine, double.PositiveInfinity) });
        internal StageCursor Cursor() => new StageCursor(this);

    }

    internal sealed class StageCursor
    {
        private readonly StagedPropulsion propulsion;
        private int index;
        private double used;
        internal StageCursor(StagedPropulsion propulsion) { this.propulsion = propulsion; }
        internal bool IsUsable => index < propulsion.Stages.Count;
        internal int Stage => propulsion.Stages[index].Stage;
        internal int SegmentIndex => index;
        internal double Remaining => IsUsable ? propulsion.Stages[index].DeltaV - used : 0;
        internal BurnPhysics Engine => new BurnPhysics(propulsion.Stages[index].Engine.MassAfter(used),
            propulsion.Stages[index].Engine.Thrust, propulsion.Stages[index].Engine.ExhaustVelocity);
        internal bool Consume(double deltaV)
        {
            if (!IsUsable || !CandidateRules.IsFinite(deltaV) || deltaV < 0 || deltaV > Remaining + 1e-6) return false;
            used += deltaV;
            if (Remaining <= 1e-6) { index++; used = 0; }
            return true;
        }
    }

    internal sealed class StageCapacityAdjustment : Exception
    {
        internal StageCapacityAdjustment(int index, double adjustment) { Index = index; Adjustment = adjustment; }
        internal int Index { get; }
        internal double Adjustment { get; }
    }
}
