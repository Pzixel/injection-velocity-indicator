using System;

namespace InjectionVelocityIndicator
{
    internal static class CaptionText
    {
        private const string InlineSeparator = " · ";

        internal static string? AppendInline(string? existing, string? addition)
        {
            if (string.IsNullOrEmpty(existing))
            {
                return addition;
            }

            if (string.IsNullOrEmpty(addition) ||
                existing!.IndexOf(addition!, StringComparison.Ordinal) >= 0)
            {
                return existing;
            }

            return existing! + InlineSeparator + addition!;
        }
    }
}
