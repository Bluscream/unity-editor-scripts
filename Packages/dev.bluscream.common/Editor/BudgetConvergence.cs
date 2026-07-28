using System;
using System.Collections.Generic;
using System.Linq;

namespace Bluscream.Budgeting
{
    /// <summary>
    /// One named constraint: a hard limit plus the most recently measured actual value.
    /// </summary>
    public class BudgetItem
    {
        public string Name;
        /// <summary>Hard cap. Exceeding this is a failure.</summary>
        public long Limit;
        /// <summary>Most recently measured value.</summary>
        public long Actual;
        /// <summary>Aim this fraction under the limit so estimate error can't push us over.</summary>
        public double SafetyFraction;
        /// <summary>Human-readable formatter for log output (defaults to MB).</summary>
        public Func<long, string> Format = bytes => $"{bytes / (1024.0 * 1024.0):F2} MB";

        public long Target => Limit == long.MaxValue ? long.MaxValue : (long)(Limit * (1.0 - SafetyFraction));
        public bool IsWithinLimit => Limit == long.MaxValue || Actual <= Limit;
        public bool IsWithinTarget => Target == long.MaxValue || Actual <= Target;
        /// <summary>How far past the safety target we are (0 when comfortably inside).</summary>
        public long Excess => Target == long.MaxValue ? 0 : Math.Max(0, Actual - Target);

        public override string ToString() => $"{Name} {Format(Actual)} / {(Limit == long.MaxValue ? "unlimited" : Format(Limit))}{(IsWithinTarget ? "" : " ⚠")}";
    }

    /// <summary>A measured set of budgets at one point in time.</summary>
    public class BudgetSnapshot
    {
        public List<BudgetItem> Items = new List<BudgetItem>();

        public BudgetItem this[string name] => Items.FirstOrDefault(i => i.Name == name);
        public bool AllWithinTarget => Items.All(i => i.IsWithinTarget);
        public bool AllWithinLimit => Items.All(i => i.IsWithinLimit);
        public IEnumerable<BudgetItem> Exceeded => Items.Where(i => !i.IsWithinTarget);

        public string Describe() => string.Join(", ", Items.Select(i => i.ToString()));
    }

    /// <summary>
    /// A strategy that can shrink something to bring budgets back in line. Reducers are tried in
    /// priority order; each is asked whether it can still make progress before being invoked.
    /// </summary>
    public interface IBudgetReducer
    {
        string Name { get; }

        /// <summary>False once this reducer has nothing left to give (e.g. everything at its floor).</summary>
        bool CanReduce(BudgetSnapshot snapshot);

        /// <summary>
        /// Attempt a reduction sized against the snapshot's excess.
        /// Return a short description of what was done, or null if nothing could be changed.
        /// </summary>
        string Reduce(BudgetSnapshot snapshot, int attempt);
    }

    /// <summary>
    /// Generic measure → check → reduce → re-measure loop.
    ///
    /// Measurement is assumed to be expensive (e.g. a real asset bundle build), so the loop only
    /// re-measures after a reducer actually changed something, and stops as soon as everything fits.
    /// </summary>
    public static class BudgetConvergence
    {
        public class Options
        {
            /// <summary>Maximum number of measure/reduce rounds. 0 = measure once, never reduce.</summary>
            public int MaxAttempts = 3;
            public Action<string> Log;
            public Action<string> Warn;
            public Action<string> Progress;
        }

        public enum StopReason
        {
            /// <summary>Everything was already inside its budget.</summary>
            AlreadyWithinBudget,
            /// <summary>Reducers brought every budget inside its limit.</summary>
            Converged,
            /// <summary>No reducer could make further progress.</summary>
            ReducersExhausted,
            /// <summary>Ran out of attempts while still over budget.</summary>
            AttemptsExhausted,
            /// <summary>A measurement failed; the loop cannot continue.</summary>
            MeasurementFailed,
            /// <summary>A reduction ran but the measured values did not improve.</summary>
            NoProgress
        }

        public class Result
        {
            public bool Converged;
            public StopReason Reason;
            public int AttemptsUsed;
            public BudgetSnapshot Final;
            public List<string> Actions = new List<string>();
            public string Message;
        }

        /// <summary>
        /// Runs the loop. <paramref name="measure"/> should perform the (expensive) measurement and
        /// return the current snapshot, or null if measurement failed.
        /// </summary>
        public static Result Run(Func<BudgetSnapshot> measure, IReadOnlyList<IBudgetReducer> reducers, Options options)
        {
            options = options ?? new Options();
            var result = new Result();

            BudgetSnapshot snapshot = measure();
            result.Final = snapshot;

            if (snapshot == null)
            {
                result.Reason = StopReason.MeasurementFailed;
                result.Message = "Initial measurement failed.";
                return result;
            }

            if (snapshot.AllWithinLimit)
            {
                result.Converged = true;
                result.Reason = StopReason.AlreadyWithinBudget;
                result.Message = $"Already within budget: {snapshot.Describe()}";
                options.Log?.Invoke(result.Message);
                return result;
            }

            for (int attempt = 1; attempt <= Math.Max(0, options.MaxAttempts); attempt++)
            {
                options.Warn?.Invoke($"Attempt {attempt}/{options.MaxAttempts}: over budget — {string.Join("; ", snapshot.Exceeded.Select(e => e.ToString()))}");

                // Try reducers in priority order until one actually does something
                string action = null;
                foreach (IBudgetReducer reducer in reducers)
                {
                    if (reducer == null || !reducer.CanReduce(snapshot)) continue;

                    options.Progress?.Invoke($"Applying '{reducer.Name}' (attempt {attempt}/{options.MaxAttempts})...");
                    action = reducer.Reduce(snapshot, attempt);
                    if (!string.IsNullOrEmpty(action))
                    {
                        result.Actions.Add($"[{attempt}] {reducer.Name}: {action}");
                        options.Log?.Invoke($"{reducer.Name} — {action}");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(action))
                {
                    result.Reason = StopReason.ReducersExhausted;
                    result.Message = $"No further reduction is possible. Still over: {string.Join("; ", snapshot.Exceeded.Select(e => e.ToString()))}";
                    options.Warn?.Invoke(result.Message);
                    return result;
                }

                var previous = snapshot;
                snapshot = measure();
                result.AttemptsUsed = attempt;

                if (snapshot == null)
                {
                    result.Final = previous;
                    result.Reason = StopReason.MeasurementFailed;
                    result.Message = $"Measurement failed on attempt {attempt}.";
                    return result;
                }
                result.Final = snapshot;
                options.Log?.Invoke($"Attempt {attempt} result: {snapshot.Describe()}");

                if (snapshot.AllWithinLimit)
                {
                    result.Converged = true;
                    result.Reason = StopReason.Converged;
                    result.Message = $"Converged after {attempt} attempt(s): {snapshot.Describe()}";
                    options.Log?.Invoke(result.Message);
                    return result;
                }

                // Bail out if a reduction produced no measurable improvement anywhere
                bool improvedSomewhere = snapshot.Items.Any(item =>
                {
                    BudgetItem before = previous[item.Name];
                    return before != null && item.Actual < before.Actual;
                });

                if (!improvedSomewhere)
                {
                    result.Reason = StopReason.NoProgress;
                    result.Message = $"'{reducers.FirstOrDefault(r => r != null && r.CanReduce(previous))?.Name ?? "reduction"}' did not shrink anything measurable — stopping. Still over: {string.Join("; ", snapshot.Exceeded.Select(e => e.ToString()))}";
                    options.Warn?.Invoke(result.Message);
                    return result;
                }
            }

            result.Reason = StopReason.AttemptsExhausted;
            result.Message = $"Ran out of attempts ({options.MaxAttempts}). Still over: {string.Join("; ", snapshot.Exceeded.Select(e => e.ToString()))}";
            options.Warn?.Invoke(result.Message);
            return result;
        }
    }
}
