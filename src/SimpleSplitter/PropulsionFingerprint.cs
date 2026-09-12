using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    internal sealed class PropulsionFingerprint
    {
        private readonly List<double> values = new List<double>();
        private readonly List<string> names = new List<string>();
        private readonly List<double> nonFuelMasses = new List<double>();
        private readonly List<double> partMasses = new List<double>();

        internal static PropulsionFingerprint Capture(Vessel vessel)
        {
            var snapshot = new PropulsionFingerprint();
            var propellants = new HashSet<int>();
            foreach (Part part in vessel.parts)
                foreach (PartModule module in part.Modules)
                    if (module is ModuleEngines engine)
                        foreach (Propellant propellant in engine.propellants) propellants.Add(propellant.id);
            List<double> v = snapshot.values;
            v.Add(vessel.currentStage);
            v.Add(vessel.parts.Count);
            foreach (Part part in vessel.parts)
            {
                v.Add(part.flightID); v.Add(part.parent == null ? -1 : part.parent.flightID);
                v.Add(part.inverseStage); v.Add(part.mass); v.Add(part.fuelCrossFeed ? 1 : 0);
                v.Add(part.GetResourcePriority());
                double nonFuelMass = 0, totalMass = part.mass;
                foreach (PartResource resource in part.Resources)
                {
                    // KER excludes electricity/air from engine fuel drainage.
                    if (resource.info.density <= 0) continue;
                    double mass = resource.amount * resource.info.density;
                    totalMass += mass;
                    bool fuel = propellants.Contains(resource.info.id);
                    if (!fuel) nonFuelMass += mass;
                    v.Add(resource.info.id); v.Add(fuel ? resource.amount : 0); v.Add(resource.maxAmount);
                    v.Add(resource.flowState ? 1 : 0);
                }
                snapshot.nonFuelMasses.Add(nonFuelMass);
                snapshot.partMasses.Add(totalMass);
                foreach (PartModule module in part.Modules)
                {
                    if (!(module is ModuleEngines engine)) continue;
                    snapshot.names.Add(engine.engineID);
                    v.Add(engine.isEnabled ? 1 : 0); v.Add(engine.isOperational ? 1 : 0);
                    v.Add(engine.thrustPercentage); v.Add(engine.maxFuelFlow); v.Add(engine.minFuelFlow);
                    v.Add(engine.atmosphereCurve.Evaluate(0)); v.Add(engine.g);
                    v.Add(engine.throttleLocked ? 1 : 0); v.Add(engine.useThrustCurve ? 1 : 0);
                    v.Add(engine.atmChangeFlow ? 1 : 0);
                    foreach (Propellant propellant in engine.propellants)
                    {
                        v.Add(propellant.id); v.Add(propellant.ratio); v.Add((int)propellant.GetFlowMode());
                    }
                }
            }
            return snapshot;
        }

        internal bool Matches(PropulsionFingerprint other)
        {
            if (values.Count != other.values.Count || names.Count != other.names.Count ||
                nonFuelMasses.Count != other.nonFuelMasses.Count) return false;
            for (int i = 0; i < names.Count; i++) if (names[i] != other.names[i]) return false;
            // Life support consumes cabin supplies continuously. Their units
            // are not an engine-fuel constraint; their mass still matters to
            // every stage carrying this part. Use the same one-ppm scale as
            // the KER stage comparison, anchored to the original snapshot.
            for (int i = 0; i < nonFuelMasses.Count; i++)
                if (!CandidateRules.IsFinite(nonFuelMasses[i]) || !CandidateRules.IsFinite(other.nonFuelMasses[i]) ||
                    Math.Abs(nonFuelMasses[i] - other.nonFuelMasses[i]) > 1e-6 * Math.Max(1, partMasses[i])) return false;
            for (int i = 0; i < values.Count; i++)
                if (!CandidateRules.IsFinite(values[i]) || !CandidateRules.IsFinite(other.values[i]) ||
                    Math.Abs(values[i] - other.values[i]) > 1e-8 * Math.Max(1, Math.Abs(values[i]))) return false;
            return true;
        }
    }
}
