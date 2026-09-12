using System;

namespace SimpleSplitter
{
    internal static class StockTrajectorySafety
    {
        // Inspect the solved patch, not the map marker. A later encounter is
        // irrelevant if the next burn occurs first; an earlier one invalidates
        // the source-body coordinates used to calculate that burn.
        internal static bool CoastIsSafe(Orbit? patch, CelestialBody body, double start, double end, out string error)
        {
            error = string.Empty;
            if (patch == null || patch.referenceBody != body || !CandidateRules.IsFinite(end) || end < start)
            { error = "Missing coast or unexpected SOI."; return false; }
            if (!AboveAtmosphere(patch, start, end))
            { error = "Coast intersects the surface or atmosphere."; return false; }
            if (patch.patchEndTransition != Orbit.PatchTransitionType.FINAL && patch.EndUT < end - 0.01)
            { error = "Intermediate coast impacts or changes SOI before the next burn."; return false; }
            return true;
        }

        internal static bool AboveAtmosphere(Orbit orbit, double start, double end)
        {
            double minimum = orbit.referenceBody.Radius +
                (orbit.referenceBody.atmosphere ? orbit.referenceBody.atmosphereDepth : 0);
            orbit.GetFixedState(start, out Vector3d r0, out _);
            orbit.GetFixedState(end, out Vector3d r1, out _);
            if (!CandidateRules.IsFinite(r0.magnitude) || !CandidateRules.IsFinite(r1.magnitude) ||
                r0.magnitude <= minimum || r1.magnitude <= minimum) return false;
            if (orbit.PeR > minimum) return true;
            double dt = orbit.GetDTforTrueAnomalyAtUT(0, start);
            if (orbit.eccentricity < 1 && dt < 0) dt += orbit.period;
            return CandidateRules.IsFinite(dt) && (dt < 0 || dt > end - start);
        }
    }
}
