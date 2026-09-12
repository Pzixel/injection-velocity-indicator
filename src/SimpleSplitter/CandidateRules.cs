using System;

namespace SimpleSplitter
{
    internal readonly struct CandidateScore : IComparable<CandidateScore>
    {
        internal CandidateScore(
            double largestBurn,
            double totalDeltaV,
            double arrivalError,
            double firstBurnUt, int nodeCount = 0, double precisionBand = 0.0)
        {
            LargestBurn = largestBurn;
            TotalDeltaV = totalDeltaV;
            ArrivalError = arrivalError;
            FirstBurnUt = firstBurnUt;
            NodeCount = nodeCount;
            PrecisionBand = precisionBand;
        }

        internal double LargestBurn { get; }
        internal double TotalDeltaV { get; }
        internal double ArrivalError { get; }
        internal double FirstBurnUt { get; }
        internal int NodeCount { get; }
        internal double PrecisionBand { get; }

        public int CompareTo(CandidateScore other)
        {
            // Within the requested precision band, another maneuver is not
            // worth an asymptotically tiny reduction in the departure burn.
            double band = Math.Max(PrecisionBand, other.PrecisionBand);
            int comparison = band > 0.0
                ? Math.Floor(LargestBurn / band).CompareTo(Math.Floor(other.LargestBurn / band))
                : LargestBurn.CompareTo(other.LargestBurn);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = NodeCount.CompareTo(other.NodeCount);
            if (comparison != 0) return comparison;

            comparison = TotalDeltaV.CompareTo(other.TotalDeltaV);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ArrivalError.CompareTo(other.ArrivalError);
            if (comparison != 0)
            {
                return comparison;
            }

            // Prefer the later first burn when all performance values tie.
            return other.FirstBurnUt.CompareTo(FirstBurnUt);
        }
    }

    internal static class CandidateRules
    {
        // One day in the player's calendar (Kerbin/Earth/custom formatter).
        internal static double ArrivalToleranceSeconds => KSPUtil.dateTimeFormatter.Day;
        internal const double MeaningfulImprovement = 0.01;

        internal static bool ArrivalIsWithinTolerance(
            double originalNodeUt,
            double originalArrivalUt,
            double candidateArrivalUt)
        {
            double duration = originalArrivalUt - originalNodeUt;
            return IsFinite(duration) && duration > 0.0 &&
                IsFinite(candidateArrivalUt) &&
                Math.Abs(candidateArrivalUt - originalArrivalUt) <=
                    ArrivalToleranceSeconds;
        }

        internal static bool DeltaVIsAllowed(
            double originalDeltaV,
            double totalDeltaV,
            double largestBurn)
        {
            return IsFinite(originalDeltaV) && originalDeltaV > 0.0 &&
                IsFinite(totalDeltaV) && totalDeltaV > 0.0 &&
                IsFinite(largestBurn) && largestBurn > 0.0 && largestBurn <= totalDeltaV;
        }

        internal static bool NormalSplitSavesDeltaV(
            double twoNodeTotal,
            double threeNodeTotal)
        {
            return IsFinite(twoNodeTotal) && IsFinite(threeNodeTotal) &&
                threeNodeTotal + MeaningfulImprovement < twoNodeTotal;
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
