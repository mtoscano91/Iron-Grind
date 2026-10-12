using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// <see cref="ICharacterRecordStore"/> over a dictionary, for EditMode tests and a server run with no database.
    /// </summary>
    /// <remarks>
    /// Keeps and returns copies of records. The injected clock is the only time source. Store calls can arrive
    /// from thread-pool continuations, so all state is guarded by one lock. The fault controls
    /// (<see cref="FailNext"/>, <see cref="HoldNext"/>, <see cref="NeverCompleteNext"/>) are test and diagnostic
    /// controls; each applies to one store call only — the next one that is not turned away for an
    /// already-cancelled token (such a call leaves the control pending) — and setting one replaces any that is
    /// still pending. Every <see cref="TaskCompletionSource{TResult}"/> uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> (ADR-011).
    /// </remarks>
    public sealed class InMemoryCharacterRecordStore : ICharacterRecordStore
    {
        private enum ControlKind
        {
            Fail,
            Hold,
            Never,
        }

        private sealed class HeldCall
        {
            private readonly object _gate = new object();
            private Action _run;

            public void Arm(Action run)
            {
                lock (_gate)
                {
                    _run = run;
                }
            }

            public void Release()
            {
                Action run;
                lock (_gate)
                {
                    run = _run;
                    _run = null;
                }

                run?.Invoke();
            }
        }

        private sealed class FaultControl
        {
            public ControlKind Kind;
            public Exception Exception;
            public HeldCall Held;
        }

        private readonly Func<DateTime> _utcNow;
        private readonly Dictionary<CharacterID, CharacterRecord> _rows = new Dictionary<CharacterID, CharacterRecord>();
        private readonly object _lock = new object();
        private FaultControl _pending;

        /// <summary>Creates an empty store.</summary>
        /// <param name="utcNow">The UTC clock; the only time source of this store.</param>
        public InMemoryCharacterRecordStore(Func<DateTime> utcNow)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        /// <summary>Makes the next store call return a task faulted with <paramref name="exception"/>; nothing is written.</summary>
        /// <param name="exception">The exception to fault with.</param>
        public void FailNext(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            SetPending(new FaultControl { Kind = ControlKind.Fail, Exception = exception });
        }

        /// <summary>
        /// Makes the next store call return an incomplete task until the returned action is invoked; the operation
        /// then runs against the record cloned at call time, and checks the row's existence and version as they
        /// are at release. Invoking the action twice, or before a call was held,
        /// does nothing. If the call's token is cancelled while held, the task is cancelled and a later release
        /// writes nothing.
        /// </summary>
        /// <returns>The release action.</returns>
        public Action HoldNext()
        {
            var held = new HeldCall();
            SetPending(new FaultControl { Kind = ControlKind.Hold, Held = held });
            return held.Release;
        }

        /// <summary>
        /// Makes the next store call return a task that never completes, except that it is cancelled if its token
        /// is cancelled. Nothing is written.
        /// </summary>
        public void NeverCompleteNext()
        {
            SetPending(new FaultControl { Kind = ControlKind.Never });
        }

        /// <summary>Changes a stored row's <c>SaveVersion</c> at once (the "separate connection" of AC-CP-19).</summary>
        /// <param name="id">The character.</param>
        /// <param name="saveVersion">The new stored version.</param>
        /// <returns><c>false</c> for an unknown id.</returns>
        public bool OverwriteSaveVersion(CharacterID id, uint saveVersion)
        {
            lock (_lock)
            {
                if (!_rows.TryGetValue(id, out CharacterRecord row))
                {
                    return false;
                }

                row.SaveVersion = saveVersion;
                return true;
            }
        }

        /// <inheritdoc/>
        public Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
        {
            return Run(ct, () =>
            {
                lock (_lock)
                {
                    return _rows.TryGetValue(id, out CharacterRecord row) ? row.Clone() : null;
                }
            });
        }

        /// <inheritdoc/>
        public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            CharacterRecord copy = record.Clone();
            return Run(ct, () => DoInsert(copy));
        }

        /// <inheritdoc/>
        public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            CharacterRecord copy = record.Clone();
            return Run(ct, () => DoUpdate(copy, expectedSaveVersion));
        }

        private bool DoInsert(CharacterRecord copy)
        {
            lock (_lock)
            {
                if (_rows.ContainsKey(copy.CharacterID))
                {
                    return false;
                }

                DateTime now = _utcNow();
                copy.CreatedAtUtc = now;
                copy.LastSavedAtUtc = now;
                _rows[copy.CharacterID] = copy;
                return true;
            }
        }

        private int DoUpdate(CharacterRecord copy, uint expectedSaveVersion)
        {
            lock (_lock)
            {
                if (!_rows.TryGetValue(copy.CharacterID, out CharacterRecord stored) || stored.SaveVersion != expectedSaveVersion)
                {
                    return 0;
                }

                copy.SaveVersion = unchecked(expectedSaveVersion + 1u);
                copy.LastSavedAtUtc = _utcNow();
                copy.CreatedAtUtc = stored.CreatedAtUtc;
                _rows[copy.CharacterID] = copy;
                return 1;
            }
        }

        private void SetPending(FaultControl control)
        {
            lock (_lock)
            {
                _pending = control;
            }
        }

        private FaultControl TakePending()
        {
            lock (_lock)
            {
                FaultControl control = _pending;
                _pending = null;
                return control;
            }
        }

        private Task<T> Run<T>(CancellationToken ct, Func<T> operation)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(ct);
            }

            FaultControl control = TakePending();
            if (control == null)
            {
                return RunNow(operation);
            }

            if (control.Kind == ControlKind.Fail)
            {
                return Task.FromException<T>(control.Exception);
            }

            return Defer(ct, operation, control);
        }

        // A held or never-completing call. Cancelling and completing both happen under the store's lock, so a
        // call that ends cancelled has written nothing, whichever thread cancels.
        private Task<T> Defer<T>(CancellationToken ct, Func<T> operation, FaultControl control)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ct.CanBeCanceled)
            {
                CancellationTokenRegistration registration = ct.Register(() =>
                {
                    lock (_lock)
                    {
                        tcs.TrySetCanceled(ct);
                    }
                });
                tcs.Task.ContinueWith(
                    (_, state) => ((CancellationTokenRegistration)state).Dispose(),
                    registration,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            if (control.Kind == ControlKind.Hold)
            {
                control.Held.Arm(() => CompleteHeld(tcs, operation));
            }

            return tcs.Task;
        }

        // A throw from the operation (the injected clock, for one) faults the task instead of escaping the call,
        // as a database error would.
        private static Task<T> RunNow<T>(Func<T> operation)
        {
            try
            {
                return Task.FromResult(operation());
            }
            catch (Exception e)
            {
                return Task.FromException<T>(e);
            }
        }

        private void CompleteHeld<T>(TaskCompletionSource<T> tcs, Func<T> operation)
        {
            lock (_lock)
            {
                if (tcs.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    tcs.TrySetResult(operation());
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }
        }
    }
}
