using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal static class PropulsionReader
    {
        internal static bool IsReady(Vessel vessel) => vessel.VesselDeltaV != null &&
            vessel.VesselDeltaV.IsReady && !vessel.VesselDeltaV.SimulationRunning;
        internal static StagedPropulsion Read(Vessel vessel, out string error)
        {
            error = string.Empty;
            List<BurnStage> result = new List<BurnStage>();
            VesselDeltaV simulation = vessel.VesselDeltaV;
            if (!IsReady(vessel))
            {
                error = "Waiting for KSP's staged delta-v calculation. Try splitting again when it is ready.";
                return new StagedPropulsion(result);
            }
            List<DeltaVStageInfo> stages = new List<DeltaVStageInfo>(simulation.OperatingStageInfo);
            stages.Sort((a, b) => b.stage.CompareTo(a.stage));
            foreach (DeltaVStageInfo stage in stages)
            {
                if (stage.stage > vessel.currentStage) continue;
                // Chronological resource/engine segments; masses include decoupling.
                foreach (DeltaVCalc calc in stage.deltaVCalcs)
                {
                    if (calc.dVinVac <= 1e-6) continue;
                    foreach (DeltaVEngineInfo active in calc.activeEngines)
                    {
                        ModuleEngines engine = active.engine;
                        if (engine == null || engine.throttleLocked || engine.atmChangeFlow || engine.useThrustCurve)
                        {
                            error = "Stage " + stage.stage + " uses propulsion that cannot be modeled at constant throttle.";
                            return new StagedPropulsion(new BurnStage[0]);
                        }
                    }
                    // Stock delta-v accounts for opposing/canted thrust vectors.
                    // Derive effective exhaust/thrust while preserving mass flow.
                    double exhaust = calc.dVinVac / Math.Log(calc.startMass / calc.endMass);
                    double flow = calc.thrustVac / (calc.ispVAC * PhysicsGlobals.GravitationalAcceleration);
                    BurnPhysics physics = new BurnPhysics(calc.startMass, flow * exhaust, exhaust);
                    if (!physics.IsUsable || !CandidateRules.IsFinite(calc.dVinVac))
                    {
                        error = "KSP returned unusable propulsion data for stage " + stage.stage + ".";
                        return new StagedPropulsion(new BurnStage[0]);
                    }
                    result.Add(new BurnStage(stage.stage, physics, calc.dVinVac));
                }
            }
            if (result.Count == 0) error = "KSP reports no remaining usable stage delta-v.";
            return new StagedPropulsion(result);
        }

        internal static bool Matches(StagedPropulsion before, StagedPropulsion after)
        {
            if (!after.IsUsable || before.Stages.Count != after.Stages.Count) return false;
            for (int i = 0; i < before.Stages.Count; i++)
            {
                BurnStage a = before.Stages[i], b = after.Stages[i];
                if (a.Stage != b.Stage || !Close(a.DeltaV, b.DeltaV) ||
                    !Close(a.Engine.Mass, b.Engine.Mass) || !Close(a.Engine.Thrust, b.Engine.Thrust) ||
                    !Close(a.Engine.ExhaustVelocity, b.Engine.ExhaustVelocity)) return false;
            }
            return true;
        }
        private static bool Close(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(a) * 1e-4);
    }
}
