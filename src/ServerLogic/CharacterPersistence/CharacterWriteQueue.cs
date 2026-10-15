using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Serialises asynchronous writes per <see cref="CharacterID"/>: at most one write is in flight for a character,
    /// and a later one waits for every earlier one to complete.
    /// </summary>
    /// <remarks>
    /// Implements CR-CP-7 (at most one save in flight per character; a second call is queued, never run in
    /// parallel) as decided in ADR-006 (the service-layer per-character async queue; the database gives no
    /// independent enforcement). Character Persistence Story 006.
    /// <para>
    /// There is no depth bound. Writes per character are rare (session end, irreversible outcomes, purchases),
    /// <c>ICharacterMutationGate</c> allows one irreversible write at a time, and every write has a 5 s timeout
    /// (<c>PERSISTENCE_WRITE_TIMEOUT_SECONDS</c>, ADR-006), so a queue cannot grow without limit behind a hung write.
    /// </para>
    /// <para>
    /// The queue knows nothing about <c>SaveVersion</c>; the version rule is per method. Story 007 (SaveSession)
    /// takes its expected version at call time; Story 008 (SaveIrreversibleOutcome) reads the latest version at its
    /// turn. The queue is not yet wired into <c>CharacterPersistenceService</c> (Stories 007 and 008).
    /// </para>
    /// <para>
    /// Thread-safe: one lock guards the dictionary. The queue holds no game state. Every await uses
    /// <c>ConfigureAwait(false)</c> and every <see cref="TaskCompletionSource{TResult}"/> uses
    /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> (ADR-011).
    /// </para>
    /// </remarks>
    public sealed class CharacterWriteQueue
    {
        // Per character: the "done" task of the most recently queued write. A done task is only ever completed
        // with SetResult, so it never faults and waiting on it never observes or rethrows a write's exception.
        private readonly Dictionary<CharacterID, Task> _tails = new Dictionary<CharacterID, Task>();
        private readonly object _lock = new object();

        /// <summary>
        /// The number of characters with a write queued or in flight. A character's entry is removed when its last
        /// write completes; the count can lag a caller's task by a moment (cleanup runs just after the caller's task
        /// is completed on the success path). A write cancelled while waiting still counts until its predecessor
        /// completes: it keeps its place in the chain.
        /// </summary>
        public int ActiveCharacterCount
        {
            get
            {
                lock (_lock)
                {
                    return _tails.Count;
                }
            }
        }

        /// <summary>
        /// Queues <paramref name="write"/> behind every write already queued for <paramref name="id"/> and returns
        /// at once with a task carrying that write's own outcome.
        /// </summary>
        /// <typeparam name="T">The write's result type.</typeparam>
        /// <param name="id">The character whose writes are serialised.</param>
        /// <param name="write">
        /// The write. Started only after every earlier write for <paramref name="id"/> has completed in any state
        /// (result, fault or cancellation). When the character has nothing queued, it starts inline inside this
        /// call (no async hop), so any synchronous work before its first <c>await</c> runs on the caller's thread —
        /// keep that prefix short when calling from the tick. When something is queued, it runs later, normally on a
        /// thread-pool thread (and never inside the <c>SetResult</c> that completed its predecessor).
        /// <para><b>A write function must not read live game state</b> (ADR-011 Decision 3): a queued write runs off
        /// the tick thread, so the caller takes its snapshot before calling <c>Enqueue</c> and the function closes over
        /// that copy.</para>
        /// A function that throws synchronously gives a faulted task (an <see cref="OperationCanceledException"/>
        /// gives a cancelled one, as it would after an <c>await</c>). The queue does not interpret versions; which
        /// <c>SaveVersion</c> a write expects is the caller's decision. There is no bound on the queue depth.
        /// </param>
        /// <param name="ct">
        /// Passed to <paramref name="write"/>. If cancelled while the write is still waiting, the returned task is
        /// cancelled, <paramref name="write"/> is never called, and later writes still wait for the earlier ones.
        /// </param>
        /// <returns>
        /// A task that completes with the write's outcome: its result, or faulted with all of the write's own inner
        /// exceptions, or cancelled (keeping the token where the write's cancellation carried one).
        /// </returns>
        public Task<T> Enqueue<T>(CharacterID id, Func<CancellationToken, Task<T>> write, CancellationToken ct)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task predecessor;
            lock (_lock)
            {
                _tails.TryGetValue(id, out predecessor);
                _tails[id] = done.Task;
            }

            // Runs synchronously up to its first await: inline when there is no predecessor.
            _ = RunAsync(id, predecessor, write, ct, result, done);
            return result.Task;
        }

        private async Task RunAsync<T>(
            CharacterID id,
            Task predecessor,
            Func<CancellationToken, Task<T>> write,
            CancellationToken ct,
            TaskCompletionSource<T> result,
            TaskCompletionSource<bool> done)
        {
            CancellationTokenRegistration registration = default;
            try
            {
                if (predecessor != null)
                {
                    // Cancelling while waiting completes the caller's task at once; this slot still holds its place
                    // in the chain (done is completed only below), so the write behind it keeps waiting.
                    if (ct.CanBeCanceled)
                    {
                        registration = ct.Register(() => result.TrySetCanceled(ct));
                    }

                    await predecessor.ConfigureAwait(false);

                    // The wait is over. From here the write's own outcome is the caller's outcome: a cancel during
                    // the write reaches it through ct, and must not cancel the caller's task while the write runs
                    // (it may still commit).
                    registration.Dispose();
                    registration = default;
                }

                if (ct.IsCancellationRequested)
                {
                    result.TrySetCanceled(ct);
                    return;
                }

                Task<T> task;
                try
                {
                    task = write(ct) ?? throw new InvalidOperationException("A write function returned a null task.");
                }
                catch (OperationCanceledException e)
                {
                    // Same outcome as the same exception thrown after the write's first await.
                    result.TrySetCanceled(e.CancellationToken);
                    return;
                }
                catch (Exception e)
                {
                    result.TrySetException(e);
                    return;
                }

                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException e) when (task.IsCanceled)
                {
                    result.TrySetCanceled(e.CancellationToken);
                    return;
                }
                catch (Exception) when (task.IsFaulted)
                {
                    // Every inner exception, not only the first one an await rethrows (a write built on WhenAll).
                    result.TrySetException(task.Exception.InnerExceptions);
                    return;
                }

                result.TrySetResult(task.Result);
            }
            catch (Exception e)
            {
                // Not expected (the paths above catch the write's own failures), but this runner is never awaited:
                // an exception here must reach the caller, or its task would never complete.
                result.TrySetException(e);
            }
            finally
            {
                registration.Dispose();
                lock (_lock)
                {
                    if (_tails.TryGetValue(id, out Task tail) && ReferenceEquals(tail, done.Task))
                    {
                        _tails.Remove(id);
                    }
                }

                done.TrySetResult(true);
            }
        }
    }
}
