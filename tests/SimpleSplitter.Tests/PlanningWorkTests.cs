using System;
using System.Collections;

namespace SimpleSplitter
{
    internal static partial class Program
    {
        private static void PlanningWorkTests()
        {
            double now = 0;
            int steps = 0, disposed = 0, frames = 0;
            IEnumerator Child()
            {
                try
                {
                    for (int i = 0; i < 100; i++)
                    { steps++; now += 0.1; yield return null; }
                }
                finally { disposed++; }
            }
            IEnumerator Parent() { yield return Child(); yield return Child(); }
            IEnumerator runner = PlanningWork.Run(Parent(), 4, () => now);
            while (runner.MoveNext()) frames++;
            True(steps == 200 && disposed == 2, "batched work completes and disposes nested computations");
            True(frames >= 4 && frames <= 5, "frame waits depend on work time, not 200 work boundaries");
            runner = PlanningWork.Run(Parent(), 4, () => now);
            True(runner.MoveNext(), "work yields for cancellation");
            ((IDisposable)runner).Dispose();
            True(disposed == 3, "cancellation disposes the active nested computation");
            object wait = new object();
            IEnumerator Waiting() { yield return wait; }
            runner = PlanningWork.Run(Waiting(), 4, () => now);
            True(runner.MoveNext() && ReferenceEquals(runner.Current, wait), "explicit waits are preserved");
            False(runner.MoveNext(), "explicit wait completes on resumption");
        }
    }
}
