using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SimpleSplitter
{
    // Resume deterministic candidate evaluation by replaying cached integrations.
    // KSP Orbit calls stay on the main thread. Only new integrations spend the
    // frame budget; no worker thread touches Unity or vessel state.
    internal sealed class EvaluationBudget
    {
        internal static EvaluationBudget? Current;
        private readonly List<Result> results = new List<Result>();
        private readonly Stopwatch slice = new Stopwatch();
        private int position, newEvaluations;
        internal int SliceCount { get; private set; }
        internal void BeginSlice()
        {
            position = 0; newEvaluations = 0; slice.Restart(); SliceCount++; Current = this;
        }
        internal bool TryReplay(out FiniteBurnEstimate estimate, out Orbit orbit)
        {
            if (position < results.Count)
            {
                Result result = results[position++]; estimate = result.Estimate; orbit = result.Orbit; return true;
            }
            if (newEvaluations >= 16 || (newEvaluations > 0 && slice.Elapsed.TotalMilliseconds >= 4))
                throw new YieldPlanningException();
            newEvaluations++;
            estimate = default; orbit = null!; return false;
        }
        internal void Store(FiniteBurnEstimate estimate, Orbit orbit)
        { results.Add(new Result(estimate, orbit)); position++; }
        private readonly struct Result
        {
            internal Result(FiniteBurnEstimate estimate, Orbit orbit) { Estimate = estimate; Orbit = orbit; }
            internal FiniteBurnEstimate Estimate { get; }
            internal Orbit Orbit { get; }
        }
    }
    internal sealed class YieldPlanningException : Exception { }
}
