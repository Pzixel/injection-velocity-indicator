using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace SimpleSplitter
{
    internal static class SafePlanSearch
    {
        // Small redistributions first, wider alternatives later. Completed
        // batches survive cancellation, budget exhaustion and table growth.
        private static readonly double[] Biases = { 0.2, -0.2, 0, 0.45, -0.45, 0.75, -0.75 };

        internal static IEnumerator FindAsync(SplitRequest request,
            Func<SplitCandidate, Action<bool>, IEnumerator> validate,
            Func<bool> contextMatches, Action<int, SplitCandidate> accept,
            double refinementSeconds = 2.0)
        {
            var pending = new List<int>();
            for (int count = 2; count <= request.MaximumBurns; count++)
            {
                bool found = false, rejected = false;
                yield return Check(count, (safe, hadRejection) => { found = safe; rejected = hadRejection; });
                if (!contextMatches()) yield break;
                if ((!found || rejected) && !request.Cache.IsRefined(count)) pending.Add(count);
            }
            // Let the strongest near-solutions repair their coast first. A
            // low-count path with large error cannot monopolize the budget.
            pending.Sort((a, b) => BestError(a).CompareTo(BestError(b)));
            var refinement = Stopwatch.StartNew();
            while (pending.Count > 0)
            {
                for (int index = 0; index < pending.Count;)
                {
                    if (!contextMatches()) yield break;
                    if (refinement.Elapsed.TotalSeconds >= refinementSeconds)
                    { request.Cache.RefinementBudgetReached = true; yield break; }
                    int count = pending[index];
                    int step = request.Cache.RefinementStep(count);
                    if (step >= Biases.Length)
                    { request.Cache.MarkRefined(count); pending.RemoveAt(index); continue; }
                    // Batches are atomic in the numerical cache, but the frame
                    // runner still yields inside them to keep UI responsive.
                    yield return SplitPlanner.PlanAsync(request, _ => { }, count, redistribute: true,
                        refinementBias: Biases[step]);
                    request.Cache.AdvanceRefinement(count);
                    bool found = false;
                    yield return Check(count, (safe, _) => found = safe);
                    if (found)
                    { request.Cache.MarkRefined(count); pending.RemoveAt(index); }
                    else index++;
                }
            }

            double BestError(int count)
            {
                List<SplitCandidate> options = request.Cache.ForCount(count);
                return options.Count == 0 ? double.PositiveInfinity : options[0].ExcessVelocityError;
            }

            IEnumerator Check(int count, Action<bool, bool> completed)
            {
                bool rejected = false;
                foreach (SplitCandidate candidate in request.Cache.ForCount(count))
                {
                    if (!contextMatches()) { completed(false, rejected); yield break; }
                    if (!request.Cache.StockSafety.TryGetValue(candidate, out bool safe))
                    {
                        yield return validate(candidate, value => safe = value);
                        if (!contextMatches()) { completed(false, rejected); yield break; }
                        request.Cache.StockSafety[candidate] = safe;
                    }
                    if (safe)
                    { accept(count, candidate.LiveCandidate ?? candidate); completed(true, rejected); yield break; }
                    rejected = true;
                    yield return null;
                }
                completed(false, rejected);
            }
        }
    }
}
