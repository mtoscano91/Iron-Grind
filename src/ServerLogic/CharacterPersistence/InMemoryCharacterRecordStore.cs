using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// <see cref="ICharacterRecordStore"/> and <see cref="IPendingPurchaseStore"/> over dictionaries, for EditMode tests
    /// and a server run with no database.
    /// </summary>
    /// <remarks>
    /// Keeps and returns copies of records. The injected clock is the only time source. Store calls can arrive
    /// from thread-pool continuations, so all state is guarded by one lock. The fault controls
    /// (<see cref="FailNext"/>, <see cref="HoldNext"/>, <see cref="NeverCompleteNext"/>) are test and diagnostic
    /// controls; each applies to one store call only — the next one that is not turned away for an
    /// already-cancelled token (such a call leaves the control pending) — and setting one replaces any that is
    /// still pending. Every <see cref="TaskCompletionSource{TResult}"/> uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> (ADR-011).
    /// <para>The purchase calls (Story 011) share the character rows and the lock with the record calls, so a purchase
    /// write and a <see cref="FetchAsync"/> see the same gold, and they go through the same fault controls. Stored purchase
    /// records are always in state <see cref="PendingPurchaseState.GoldDebited"/>; complete and refund delete them.
    /// <see cref="FailNextAfterInsert"/> is a separate control for the two-part purchase writes.</para>
    /// </remarks>
    public sealed class InMemoryCharacterRecordStore : ICharacterRecordStore, IPendingPurchaseStore
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
        private Exception _failAfterInsert;
        private readonly Dictionary<long, PendingPurchaseRecord> _purchases = new Dictionary<long, PendingPurchaseRecord>();
        private long _nextPurchaseId = 1;

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

        /// <summary>
        /// Test and diagnostic seam showing the atomicity of the purchase writes: the next <see cref="BeginPurchaseAsync"/> or
        /// <see cref="RefundPurchaseAsync"/> stages its record change, then fails with <paramref name="exception"/> before the
        /// gold write, and rolls the staged change back, so nothing is applied. Other calls ignore it and do not consume it;
        /// setting it again replaces a pending one. It is independent of <see cref="FailNext"/>.
        /// </summary>
        /// <param name="exception">The exception to fault with.</param>
        public void FailNextAfterInsert(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            lock (_lock)
            {
                _failAfterInsert = exception;
            }
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

        /// <inheritdoc/>
        public Task<long> BeginPurchaseAsync(PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct)
        {
            return Run(ct, () => DoBeginPurchase(record, goldBalance, goldVersion));
        }

        /// <inheritdoc/>
        public Task<PendingPurchaseWriteOutcome> CompletePurchaseAsync(long purchaseId, CharacterID charId, CancellationToken ct)
        {
            return Run(ct, () => DoCompletePurchase(purchaseId, charId));
        }

        /// <inheritdoc/>
        public Task<PendingPurchaseWriteOutcome> RefundPurchaseAsync(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct)
        {
            return Run(ct, () => DoRefundPurchase(purchaseId, charId, goldBalance, goldVersion));
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingAsync(CharacterID charId, CancellationToken ct)
        {
            return Run<IReadOnlyList<PendingPurchaseRecord>>(ct, () =>
            {
                lock (_lock)
                {
                    // Only GoldDebited records are ever stored (complete and refund delete), so no state filter is needed.
                    var list = new List<PendingPurchaseRecord>();
                    foreach (PendingPurchaseRecord stored in _purchases.Values)
                    {
                        if (stored.CharId.Equals(charId))
                        {
                            list.Add(stored);
                        }
                    }

                    list.Sort((a, b) => a.Id.CompareTo(b.Id));
                    return list;
                }
            });
        }

        private long DoBeginPurchase(PendingPurchaseRecord record, uint goldBalance, uint goldVersion)
        {
            lock (_lock)
            {
                if (!_rows.TryGetValue(record.CharId, out CharacterRecord row))
                {
                    return 0L;
                }

                // A retry of a request that is already recorded (after an unknown outcome): same id, nothing written.
                foreach (PendingPurchaseRecord existing in _purchases.Values)
                {
                    if (existing.CharId.Equals(record.CharId) && existing.RequestId == record.RequestId)
                    {
                        return existing.Id;
                    }
                }

                // Stage the record, then the optional fault point, then the gold write; a fault rolls the record back.
                long id = _nextPurchaseId++;
                _purchases[id] = new PendingPurchaseRecord(
                    id, record.CharId, record.RequestId, record.ItemId, record.Quantity, record.TotalCost, PendingPurchaseState.GoldDebited);
                Exception fault = TakeFailAfterInsert();
                if (fault != null)
                {
                    _purchases.Remove(id);
                    throw fault;
                }

                row.GoldBalance = goldBalance;
                row.GoldVersion = goldVersion;
                return id;
            }
        }

        private PendingPurchaseWriteOutcome DoCompletePurchase(long purchaseId, CharacterID charId)
        {
            lock (_lock)
            {
                if (!_purchases.TryGetValue(purchaseId, out PendingPurchaseRecord stored))
                {
                    return PendingPurchaseWriteOutcome.NotFound;
                }

                if (!stored.CharId.Equals(charId))
                {
                    return PendingPurchaseWriteOutcome.OtherCharacter;
                }

                if (!_rows.ContainsKey(charId))
                {
                    throw new InvalidOperationException("Purchase " + purchaseId + " exists but the character row of " + charId + " is missing.");
                }

                _purchases.Remove(purchaseId);
                return PendingPurchaseWriteOutcome.Applied;
            }
        }

        private PendingPurchaseWriteOutcome DoRefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion)
        {
            lock (_lock)
            {
                if (!_purchases.TryGetValue(purchaseId, out PendingPurchaseRecord stored))
                {
                    return PendingPurchaseWriteOutcome.NotFound;
                }

                if (!stored.CharId.Equals(charId))
                {
                    return PendingPurchaseWriteOutcome.OtherCharacter;
                }

                if (!_rows.TryGetValue(charId, out CharacterRecord row))
                {
                    // A record without its character row is a corrupt store, not "nothing to refund": fault the call.
                    throw new InvalidOperationException("Purchase " + purchaseId + " exists but the character row of " + charId + " is missing.");
                }

                _purchases.Remove(purchaseId);
                Exception fault = TakeFailAfterInsert();
                if (fault != null)
                {
                    _purchases[purchaseId] = stored;
                    throw fault;
                }

                row.GoldBalance = goldBalance;
                row.GoldVersion = goldVersion;
                return PendingPurchaseWriteOutcome.Applied;
            }
        }

        // The caller holds _lock.
        private Exception TakeFailAfterInsert()
        {
            Exception fault = _failAfterInsert;
            _failAfterInsert = null;
            return fault;
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
