using System;
using System.Globalization;

namespace InjectionVelocityIndicator
{
    internal static class Program
    {
        private static int failureCount;

        private static int Main()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            Equal("0.0", SpeedFormatter.Format(0.0), "zero speed");
            Equal("42.4", SpeedFormatter.Format(42.44), "stock one-decimal speed");
            Equal("4,229.6", SpeedFormatter.Format(4229.6), "stock grouped speed");
            Equal("N/A", SpeedFormatter.Format(double.NaN), "NaN speed");
            Equal("N/A", SpeedFormatter.Format(double.PositiveInfinity), "infinite speed");
            Equal("N/A", SpeedFormatter.Format(-1.0), "negative speed");

            True(NumericGuard.IsUsableUt(150.0), "finite closest-approach UT");
            True(NumericGuard.IsUsableUt(-150.0), "past closest-approach UT");
            False(NumericGuard.IsUsableUt(0.0), "unset closest-approach UT");
            False(NumericGuard.IsUsableUt(double.NaN), "NaN closest-approach UT");
            False(
                NumericGuard.IsUsableUt(double.PositiveInfinity),
                "infinite closest-approach UT");

            Equal(
                "Separation: 1,000 km · Relative speed: 2,000 m/s",
                CaptionText.AppendInline(
                    "Separation: 1,000 km",
                    "Relative speed: 2,000 m/s"),
                "non-destructive compatibility fallback");
            Equal(
                "Separation: 1,000 km · Relative speed: 2,000 m/s",
                CaptionText.AppendInline(
                    "Separation: 1,000 km · Relative speed: 2,000 m/s",
                    "Relative speed: 2,000 m/s"),
                "do not duplicate inline caption");
            Equal(
                "Relative speed: 2,000 m/s",
                CaptionText.AppendInline(
                    null,
                    "Relative speed: 2,000 m/s"),
                "empty stock caption");

            if (failureCount == 0)
            {
                Console.WriteLine("All InjectionVelocityIndicator tests passed.");
                return 0;
            }

            Console.Error.WriteLine(failureCount + " test(s) failed.");
            return 1;
        }

        private static void Equal(string? expected, string? actual, string name)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Fail(name + ": expected '" + expected + "', got '" + actual + "'.");
            }
        }

        private static void True(bool value, string name)
        {
            if (!value)
            {
                Fail(name + ": expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                Fail(name + ": expected false.");
            }
        }

        private static void Fail(string message)
        {
            failureCount++;
            Console.Error.WriteLine("FAIL " + message);
        }
    }
}
