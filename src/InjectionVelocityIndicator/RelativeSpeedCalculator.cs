namespace InjectionVelocityIndicator
{
    internal static class RelativeSpeedCalculator
    {
        internal static bool TryCalculate(
            Orbit? trajectoryPatch,
            Orbit? targetOrbit,
            out double relativeSpeed)
        {
            relativeSpeed = 0.0;

            if (trajectoryPatch == null || targetOrbit == null)
            {
                return false;
            }

            // KSP 1.12.5's OrbitTargeter uses this exact UT for the marker's
            // position, separation and displayed T-minus value.
            double closestApproachUt = trajectoryPatch.closestTgtApprUT;

            // OrbitTargeter only requires a non-zero closest-approach UT and
            // evaluates both orbit equations there even when the UT lies past
            // a patch's formal bounds. Do not reject a value stock displays.
            if (!NumericGuard.IsUsableUt(closestApproachUt))
            {
                return false;
            }

            // Match OrbitTargeter.UpdateISectMarkers, which supplies the stock
            // vessel-rendezvous marker's Relative Speed value. Stock only
            // compares patches in the same reference-body frame.
            if (trajectoryPatch.referenceBody != targetOrbit.referenceBody)
            {
                return false;
            }

            Vector3d trajectoryVelocity =
                trajectoryPatch.getOrbitalVelocityAtUT(closestApproachUt);
            Vector3d targetVelocity =
                targetOrbit.getOrbitalVelocityAtUT(closestApproachUt);

            relativeSpeed = (trajectoryVelocity - targetVelocity).magnitude;
            return NumericGuard.IsFiniteNonNegative(relativeSpeed);
        }
    }
}
