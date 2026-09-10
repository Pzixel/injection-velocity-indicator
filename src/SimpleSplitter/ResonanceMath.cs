using System;

namespace SimpleSplitter
{
    internal static class ResonanceMath
    {
        internal static double DeltaVForPeriod(
            double gravitationalParameter,
            double periapsisRadius,
            double initialSpeed,
            double period)
        {
            if (!CandidateRules.IsFinite(gravitationalParameter) ||
                !CandidateRules.IsFinite(periapsisRadius) ||
                !CandidateRules.IsFinite(initialSpeed) ||
                !CandidateRules.IsFinite(period) ||
                gravitationalParameter <= 0.0 || periapsisRadius <= 0.0 ||
                initialSpeed < 0.0 || period <= 0.0)
            {
                return double.NaN;
            }

            double semiMajorAxis = Math.Pow(
                gravitationalParameter *
                    Math.Pow(period / (2.0 * Math.PI), 2.0),
                1.0 / 3.0);
            double speedSquared = gravitationalParameter *
                (2.0 / periapsisRadius - 1.0 / semiMajorAxis);
            return speedSquared > 0.0
                ? Math.Sqrt(speedSquared) - initialSpeed
                : double.NaN;
        }

        internal static double TwoNodeCompletionUt(
            double firstBurnUt,
            double raisedPeriod)
        {
            return firstBurnUt + raisedPeriod;
        }

        internal static double ThreeNodeCompletionUt(
            double firstBurnUt,
            double raisedPeriod,
            double planeChangedPeriod)
        {
            return firstBurnUt +
                (raisedPeriod + planeChangedPeriod) * 0.5;
        }
    }
}
