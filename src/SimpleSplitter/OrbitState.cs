namespace SimpleSplitter
{
    internal static class OrbitState
    {
        // Flight physics uses float vectors. Compare input states near capture
        // time, allowing eight float ULPs; propagating their roundoff for months
        // before comparison amplifies it into a false maneuver edit.
        private const double InputRoundoff = 8.0 / (1 << 23);

        internal static bool MatchesInputs(Orbit a, Orbit b, double captureUt)
        {
            if (a.referenceBody != b.referenceBody) return false;
            a.GetFixedState(captureUt, out Vector3d ar, out Vector3d av);
            b.GetFixedState(captureUt, out Vector3d br, out Vector3d bv);
            return SameVector(ar, br) && SameVector(av, bv);
        }

        private static bool SameVector(Vector3d a, Vector3d b) =>
            CandidateRules.IsFinite(a.magnitude) && CandidateRules.IsFinite(b.magnitude) &&
            (a - b).magnitude <= InputRoundoff * System.Math.Max(1, a.magnitude);

        // The vector overload returns Planetarium.ZupAtT coordinates: in low
        // orbit that frame rotates with the body, and differs at each burn UT.
        // Orbit.State uses OrbitFrame's fixed axes. Pair it with
        // UpdateFromFixedVectors throughout planning, integration and SOI checks.
        internal static void GetFixedState(this Orbit orbit, double ut,
            out Vector3d position, out Vector3d velocity)
        {
            orbit.GetOrbitalStateVectorsAtUT(ut, out Orbit.State state);
            position = state.pos;
            velocity = state.vel;
        }
    }
}
