using System;

namespace InjectionVelocityIndicator
{
    internal static class NumericGuard
    {
        internal static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value >= 0.0;
        }

        internal static bool IsUsableUt(double universalTime)
        {
            return !double.IsNaN(universalTime) &&
                   !double.IsInfinity(universalTime) &&
                   universalTime != 0.0;
        }
    }
}
