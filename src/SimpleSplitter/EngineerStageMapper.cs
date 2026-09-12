using System;
using System.Collections.Generic;
using SimpleSplitter.Engineer.VesselSimulator;

namespace SimpleSplitter
{
    internal static class EngineerStageMapper
    {
        internal static StagedPropulsion Map(Stage[] stages, int currentStage)
        {
            var result = new List<BurnStage>();
            // KER returns reverse execution order. The synthetic current-engine
            // stage (-1) is last in the array and must execute first.
            for (int i = stages.Length - 1; i >= 0; i--)
            {
                Stage stage = stages[i];
                if (!CandidateRules.IsFinite(stage.deltaV) || stage.deltaV < 0)
                    throw new InvalidOperationException("KER returned invalid delta-v for stage " + stage.number + ".");
                if (stage.deltaV > 1e-6 && stage.burnSegments.Count == 0)
                    throw new InvalidOperationException("KER stage " + stage.number + " has delta-v but no burn intervals.");
                EngineerSegmentMapper.AppendStage(result, stage.number < 0 ? currentStage : stage.number, stage.burnSegments);
            }
            return new StagedPropulsion(result);
        }
    }
}
