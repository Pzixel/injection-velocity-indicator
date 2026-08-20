using System;

namespace InjectionVelocityIndicator
{
    internal static class SpeedFormatter
    {
        internal static string Format(double value)
        {
            if (!NumericGuard.IsFiniteNonNegative(value))
            {
                return "N/A";
            }

            // Match OrbitTargeter.ISectMarker.OnUpdateCaption.
            return value.ToString("N1");
        }
    }
}
