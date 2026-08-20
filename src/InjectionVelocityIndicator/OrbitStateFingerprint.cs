using System;

namespace InjectionVelocityIndicator
{
    internal struct OrbitStateFingerprint : IEquatable<OrbitStateFingerprint>
    {
        private Orbit? orbit;
        private CelestialBody? referenceBody;
        private long startUt;
        private long endUt;
        private long closestApproachUt;
        private long semiMajorAxis;
        private long eccentricity;
        private long inclination;
        private long longitudeOfAscendingNode;
        private long argumentOfPeriapsis;
        private long epoch;
        private long meanAnomalyAtEpoch;

        internal static OrbitStateFingerprint Capture(Orbit? orbit)
        {
            return new OrbitStateFingerprint
            {
                orbit = orbit,
                referenceBody = orbit == null ? null : orbit.referenceBody,
                startUt = Bits(orbit == null ? double.NaN : orbit.StartUT),
                endUt = Bits(orbit == null ? double.NaN : orbit.EndUT),
                closestApproachUt = Bits(
                    orbit == null ? double.NaN : orbit.closestTgtApprUT),
                semiMajorAxis = Bits(
                    orbit == null ? double.NaN : orbit.semiMajorAxis),
                eccentricity = Bits(
                    orbit == null ? double.NaN : orbit.eccentricity),
                inclination = Bits(
                    orbit == null ? double.NaN : orbit.inclination),
                longitudeOfAscendingNode = Bits(
                    orbit == null ? double.NaN : orbit.LAN),
                argumentOfPeriapsis = Bits(
                    orbit == null ? double.NaN : orbit.argumentOfPeriapsis),
                epoch = Bits(orbit == null ? double.NaN : orbit.epoch),
                meanAnomalyAtEpoch = Bits(
                    orbit == null ? double.NaN : orbit.meanAnomalyAtEpoch)
            };
        }

        public bool Equals(OrbitStateFingerprint other)
        {
            return ReferenceEquals(orbit, other.orbit) &&
                   ReferenceEquals(referenceBody, other.referenceBody) &&
                   startUt == other.startUt &&
                   endUt == other.endUt &&
                   closestApproachUt == other.closestApproachUt &&
                   semiMajorAxis == other.semiMajorAxis &&
                   eccentricity == other.eccentricity &&
                   inclination == other.inclination &&
                   longitudeOfAscendingNode ==
                       other.longitudeOfAscendingNode &&
                   argumentOfPeriapsis == other.argumentOfPeriapsis &&
                   epoch == other.epoch &&
                   meanAnomalyAtEpoch == other.meanAnomalyAtEpoch;
        }

        private static long Bits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }
    }
}
