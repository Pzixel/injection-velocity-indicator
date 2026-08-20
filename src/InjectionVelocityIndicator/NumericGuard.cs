using System;

namespace InjectionVelocityIndicator
{
    internal static class NumericGuard
    {
        private const double PatchBoundaryTolerance = 0.01;

        internal static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value >= 0.0;
        }

        internal static bool IsUsableUt(
            double universalTime,
            double patchStartUt,
            double patchEndUt)
        {
            if (double.IsNaN(universalTime) ||
                double.IsInfinity(universalTime) ||
                universalTime == 0.0 ||
                double.IsNaN(patchStartUt) ||
                double.IsInfinity(patchStartUt))
            {
                return false;
            }

            if (universalTime < patchStartUt - PatchBoundaryTolerance)
            {
                return false;
            }

            if (double.IsNaN(patchEndUt))
            {
                return false;
            }

            return double.IsInfinity(patchEndUt) ||
                   universalTime <= patchEndUt + PatchBoundaryTolerance;
        }
    }
}

