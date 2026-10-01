using System;
using System.Collections.Generic;

namespace SimpleSplitter
{
    // The Apply contract: the exact mapped nodes and finite commands checked
    // by Compare. Mapping is performed before exposing the row, never on Apply.
    internal sealed class ValidatedPlan
    {
        internal ValidatedPlan(SplitRequest request, SplitCandidate candidate)
        {
            Request = request;
            Commands = new List<NodeSpec>(candidate.Nodes).AsReadOnly();
            seeds = candidate.ConversionSeed == null ? null : new List<NodeSpec>(candidate.ConversionSeed.Nodes).AsReadOnly();
            Nodes = candidate.IsConverted ? StockManeuverMapping.Create(request, candidate).AsReadOnly()
                : new List<NodeSpec>(candidate.Nodes).AsReadOnly();
            ArrivalUt = request.ArrivalUt + candidate.TimedArrivalOffsetSeconds;
        }

        internal SplitRequest Request { get; }
        private readonly IList<NodeSpec>? seeds;
        internal IList<NodeSpec> Commands { get; }
        internal IList<NodeSpec> Nodes { get; }
        internal double ArrivalUt { get; }

        internal List<NodeSpec> Remap(Orbit source, int completed)
            => StockManeuverMapping.Create(Request, Commands, seeds, source, completed);

        internal bool CanApply(Orbit source, StagedPropulsion propulsion, int maximumBurns, double now, out string error)
        {
            error = string.Empty;
            if (Nodes.Count == 0 || !CandidateRules.IsFinite(ArrivalUt))
                error = "This route has not completed encounter validation. Compare plans again.";
            else if (Nodes.Count > maximumBurns)
                error = "The selected route exceeds Max burns.";
            else if (Nodes[0].Ut - Nodes[0].StartOffset <= now + 30)
                error = "The first burn is now too close. Compare plans again.";
            else if (!PlanSearchCache.MatchesPropulsion(Request.Propulsion, propulsion))
                error = "Fuel, engines or staging changed. Compare plans again.";
            else if (!OrbitState.MatchesInputs(Request.SourceOrbit, source, Request.Now))
                error = "The vessel's orbit changed. Compare plans again.";
            return error.Length == 0;
        }
    }

    internal static class ManeuverPlanTransaction
    {
        // Preview and Apply use the identical write-and-verify operation. A
        // preview always restores; a failed commit restores before returning.
        internal static bool Write(IList<NodeSpec> original, IList<NodeSpec> proposed,
            Action<IList<NodeSpec>> replace, Func<IList<NodeSpec>, bool> matches, bool preview, out string error)
        {
            error = string.Empty;
            bool retained = false;
            try
            {
                replace(proposed);
                retained = matches(proposed);
                if (!retained) error = "KSP did not retain every generated maneuver with its checked time and vector.";
            }
            catch (Exception exception) { error = "Unable to plot the checked route: " + exception.Message; }
            if (preview || !retained)
            {
                try
                {
                    replace(original);
                    if (!matches(original)) throw new InvalidOperationException("KSP did not retain the restored original maneuver.");
                }
                catch (Exception exception)
                { error += " Restoring the original maneuver failed: " + exception.Message; return false; }
            }
            return retained;
        }
    }
}
