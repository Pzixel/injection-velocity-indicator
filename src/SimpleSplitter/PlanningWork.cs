using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace SimpleSplitter
{
    // Numerical iterators yield work boundaries, not requests to wait a frame.
    // Drain those boundaries within a measured slice, including nested safety
    // checks. Do not wrap coroutines that poll background jobs with this runner.
    internal static class PlanningWork
    {
        internal static IEnumerator Run(IEnumerator work, double milliseconds = 12,
            Func<double>? clock = null)
        {
            var timer = Stopwatch.StartNew();
            clock = clock ?? (() => timer.Elapsed.TotalMilliseconds);
            var stack = new Stack<IEnumerator>();
            stack.Push(work);
            double sliceStart = clock();
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator current = stack.Peek();
                    if (!current.MoveNext())
                    {
                        stack.Pop();
                        (current as IDisposable)?.Dispose();
                    }
                    else if (current.Current is IEnumerator nested) stack.Push(nested);
                    else if (current.Current != null)
                    {
                        // Preserve explicit Unity wait instructions.
                        yield return current.Current;
                        sliceStart = clock();
                    }
                    if (clock() - sliceStart >= milliseconds)
                    {
                        yield return null;
                        sliceStart = clock();
                    }
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }
    }
}
