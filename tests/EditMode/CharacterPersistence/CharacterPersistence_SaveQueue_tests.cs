using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 006: the per-character write queue (CR-CP-7, AC-CP-21 no-overlap half, ADR-006).
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_SaveQueue_Tests
    {
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_B = new CharacterID(102u);
        private static readonly TimeSpan WAIT_LIMIT = TimeSpan.FromSeconds(5);
        private const int SETTLE_MILLISECONDS = 50;
        private const int MANY_CHARACTERS = 1000;
        private const int STRESS_WRITES = 200;
        private const int CONCURRENT_THREADS = 8;
        private const int WRITES_PER_THREAD = 50;
        private const int CONCURRENT_CHARACTERS = 4;

        private CharacterWriteQueue _queue;
        private List<string> _events;
        private List<TaskCompletionSource<int>> _gates;

        [SetUp]
        public void SetUp()
        {
            _queue = new CharacterWriteQueue();
            // A new list per test: a write left running by a failed test cannot log into the next one.
            _events = new List<string>();
            _gates = new List<TaskCompletionSource<int>>();
        }

        [TearDown]
        public void TearDown()
        {
            // Release anything a failed test left held, so no background write outlives its test.
            foreach (TaskCompletionSource<int> gate in _gates)
            {
                gate.TrySetCanceled();
            }
        }

        private TaskCompletionSource<int> NewGate(bool runContinuationsAsynchronously = true)
        {
            var gate = runContinuationsAsynchronously
                ? new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
                : new TaskCompletionSource<int>();
            _gates.Add(gate);
            return gate;
        }

        private void Log(string entry)
        {
            List<string> events = _events;
            lock (events)
            {
                events.Add(entry);
            }
        }

        private List<string> Events()
        {
            lock (_events)
            {
                return new List<string>(_events);
            }
        }

        // A write that records start and end, and completes when its gate does. "end" is logged before the task completes.
        private Func<CancellationToken, Task<int>> Write(string tag, TaskCompletionSource<int> gate)
        {
            return async ct =>
            {
                Log("start:" + tag);
                try
                {
                    return await gate.Task.ConfigureAwait(false);
                }
                finally
                {
                    Log("end:" + tag);
                }
            };
        }

        private Func<CancellationToken, Task<int>> Instant(string tag, int value)
        {
            return ct =>
            {
                Log("start:" + tag);
                Log("end:" + tag);
                return Task.FromResult(value);
            };
        }

        // Waits with a limit, so a task that never completes fails the test instead of hanging the suite.
        // Does not rethrow: a faulted or cancelled task counts as completed.
        private static void Complete(Task task)
        {
            Assert.IsTrue(((IAsyncResult)task).AsyncWaitHandle.WaitOne(WAIT_LIMIT), "the task did not complete within " + WAIT_LIMIT);
        }

        private static T Result<T>(Task<T> task)
        {
            Complete(task);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            return task.Result;
        }

        private static void Settle()
        {
            // Lets an unwanted background start show itself; the positive assertions never depend on this.
            Thread.Sleep(SETTLE_MILLISECONDS);
        }

        private void WaitForEvent(string entry)
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => Events().Contains(entry), WAIT_LIMIT), "never saw " + entry);
        }

        // ActiveCharacterCount can drop slightly after the caller's task completes (cleanup runs just after), so poll.
        private void AssertCountReaches(int expected)
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => _queue.ActiveCharacterCount == expected, WAIT_LIMIT),
                "ActiveCharacterCount stayed at " + _queue.ActiveCharacterCount + ", expected " + expected);
        }

        // Cancelled task's token, read the way a caller would see it.
        private static CancellationToken CancellationTokenOf(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException e)
            {
                return e.CancellationToken;
            }

            Assert.Fail("the task was not cancelled");
            return default;
        }

        // ---------------------------------------------------------------- overlap and order

        [Test]
        public void Enqueue_SecondWriteSameCharacter_StartsOnlyAfterFirstCompleted()
        {
            var gate1 = NewGate();
            var gate2 = NewGate();
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            bool firstCompletedAtSecondStart = false;
            Task<int> t2 = _queue.Enqueue(CHAR_A, async ct =>
            {
                firstCompletedAtSecondStart = t1.IsCompleted;
                Log("start:2");
                int v = await gate2.Task.ConfigureAwait(false);
                Log("end:2");
                return v;
            }, CancellationToken.None);
            Settle();

            CollectionAssert.AreEqual(new[] { "start:1" }, Events());

            gate1.SetResult(10);
            gate2.SetResult(20);

            Assert.AreEqual(10, Result(t1));
            Assert.AreEqual(20, Result(t2));
            Assert.IsTrue(firstCompletedAtSecondStart, "write 2 started before write 1's caller task completed");
            CollectionAssert.AreEqual(new[] { "start:1", "end:1", "start:2", "end:2" }, Events());
        }

        [Test]
        public void Enqueue_ThreeWrites_StartInQueuedOrderEachAfterThePrevious()
        {
            var gate1 = NewGate();
            var gate2 = NewGate();
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            Task<int> t2 = _queue.Enqueue(CHAR_A, Write("2", gate2), CancellationToken.None);
            Task<int> t3 = _queue.Enqueue(CHAR_A, Instant("3", 3), CancellationToken.None);
            Settle();
            CollectionAssert.AreEqual(new[] { "start:1" }, Events());

            gate1.SetResult(1);
            WaitForEvent("start:2");
            Settle();
            Assert.IsFalse(Events().Contains("start:3"), "write 3 started while write 2 was still running");

            gate2.SetResult(2);

            Assert.AreEqual(1, Result(t1));
            Assert.AreEqual(2, Result(t2));
            Assert.AreEqual(3, Result(t3));
            CollectionAssert.AreEqual(
                new[] { "start:1", "end:1", "start:2", "end:2", "start:3", "end:3" }, Events());
            AssertCountReaches(0);
        }

        [Test]
        public void Enqueue_DifferentCharacters_DoNotWaitForEachOther()
        {
            var gateA = NewGate();
            Task<int> a = _queue.Enqueue(CHAR_A, Write("A", gateA), CancellationToken.None);
            Task<int> b = _queue.Enqueue(CHAR_B, Instant("B", 7), CancellationToken.None);

            Assert.AreEqual(7, Result(b));
            Assert.IsFalse(a.IsCompleted);

            gateA.SetResult(1);
            Assert.AreEqual(1, Result(a));
        }

        [Test]
        public void Enqueue_InterleavedCharacters_EachChainIsIndependent()
        {
            var gateA1 = NewGate();
            var gateB1 = NewGate();
            Task<int> a1 = _queue.Enqueue(CHAR_A, Write("A1", gateA1), CancellationToken.None);
            Task<int> b1 = _queue.Enqueue(CHAR_B, Write("B1", gateB1), CancellationToken.None);
            Task<int> a2 = _queue.Enqueue(CHAR_A, Instant("A2", 2), CancellationToken.None);
            Task<int> b2 = _queue.Enqueue(CHAR_B, Instant("B2", 4), CancellationToken.None);

            gateB1.SetResult(3);
            Assert.AreEqual(3, Result(b1));
            Assert.AreEqual(4, Result(b2));
            Assert.IsFalse(a2.IsCompleted, "A2 must still wait for A1");
            Assert.IsFalse(Events().Contains("start:A2"));

            gateA1.SetResult(1);
            Assert.AreEqual(1, Result(a1));
            Assert.AreEqual(2, Result(a2));
            AssertCountReaches(0);
        }

        // ---------------------------------------------------------------- failures

        [TestCase("fault")]
        [TestCase("cancel")]
        [TestCase("throw")]
        public void Enqueue_FirstWriteFailsInEveryWay_SecondStillRunsAndSucceeds(string mode)
        {
            var gate1 = NewGate();
            var boom = new InvalidOperationException("boom");
            Func<CancellationToken, Task<int>> first = mode == "throw"
                ? (Func<CancellationToken, Task<int>>)(ct => throw boom)
                : Write("1", gate1);

            Task<int> t1 = _queue.Enqueue(CHAR_A, first, CancellationToken.None);
            Task<int> t2 = _queue.Enqueue(CHAR_A, Instant("2", 22), CancellationToken.None);

            if (mode == "fault")
            {
                gate1.TrySetException(boom);
            }
            else if (mode == "cancel")
            {
                gate1.TrySetCanceled();
            }

            Assert.AreEqual(22, Result(t2));
            Complete(t1);
            if (mode == "cancel")
            {
                Assert.IsTrue(t1.IsCanceled);
            }
            else
            {
                Assert.IsTrue(t1.IsFaulted);
                Assert.AreSame(boom, t1.Exception.InnerException);
            }

            AssertCountReaches(0);
        }

        [Test]
        public void Enqueue_SyncThrowBehindHeldWrite_FaultsThatCallerAndNextRuns()
        {
            var gate1 = NewGate();
            var boom = new InvalidOperationException("boom");
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            Task<int> t2 = _queue.Enqueue<int>(CHAR_A, ct => throw boom, CancellationToken.None);
            Task<int> t3 = _queue.Enqueue(CHAR_A, Instant("3", 3), CancellationToken.None);

            gate1.SetResult(1);

            Assert.AreEqual(1, Result(t1));
            Assert.AreEqual(3, Result(t3));
            Complete(t2);
            Assert.IsTrue(t2.IsFaulted);
            Assert.AreSame(boom, t2.Exception.InnerException);
            AssertCountReaches(0);
        }

        [Test]
        public void Enqueue_SyncOperationCanceledException_IsCancelledWithItsToken()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                CancellationToken token = cts.Token;

                Task<int> t = _queue.Enqueue<int>(CHAR_A, ct => throw new OperationCanceledException(token), CancellationToken.None);

                Complete(t);
                Assert.IsTrue(t.IsCanceled);
                Assert.AreEqual(token, CancellationTokenOf(t));
            }
        }

        [TestCase("faulted")]
        [TestCase("canceled")]
        [TestCase("null")]
        public void Enqueue_WriteReturningCompletedOrNullTask_CallerGetsMatchingOutcome(string mode)
        {
            var boom = new InvalidOperationException("boom");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Func<CancellationToken, Task<int>> write;
                switch (mode)
                {
                    case "faulted": write = ct => Task.FromException<int>(boom); break;
                    case "canceled": write = ct => Task.FromCanceled<int>(cts.Token); break;
                    default: write = ct => null; break;
                }

                Task<int> t = _queue.Enqueue(CHAR_A, write, CancellationToken.None);
                Task<int> next = _queue.Enqueue(CHAR_A, Instant("next", 5), CancellationToken.None);

                Complete(t);
                Assert.AreEqual(5, Result(next));
                switch (mode)
                {
                    case "faulted":
                        Assert.AreSame(boom, t.Exception.InnerException);
                        break;
                    case "canceled":
                        Assert.IsTrue(t.IsCanceled);
                        Assert.AreEqual(cts.Token, CancellationTokenOf(t));
                        break;
                    default:
                        Assert.IsInstanceOf<InvalidOperationException>(t.Exception.InnerException);
                        break;
                }
            }
        }

        [Test]
        public void Enqueue_WriteFaultingWithSeveralExceptions_CallerGetsThemAll()
        {
            var first = new InvalidOperationException("first");
            var second = new ArgumentException("second");

            Task<int[]> t = _queue.Enqueue(CHAR_A, ct => Task.WhenAll(
                Task.FromException<int>(first),
                Task.FromException<int>(second)), CancellationToken.None);

            Complete(t);
            Assert.IsTrue(t.IsFaulted);
            CollectionAssert.AreEquivalent(new Exception[] { first, second }, t.Exception.InnerExceptions);
        }

        // ---------------------------------------------------------------- never blocks, no inline continuation

        [Test]
        public void Enqueue_WhileFirstHeld_ReturnsIncompleteTaskAndDoesNotCallWrite()
        {
            var gate1 = NewGate();
            _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            bool called = false;
            Task<int> t2 = null;

            // On another thread with a limit: an Enqueue that blocked on its predecessor fails here instead of hanging.
            Task enqueue = Task.Run(() =>
            {
                t2 = _queue.Enqueue(CHAR_A, ct =>
                {
                    called = true;
                    return Task.FromResult(2);
                }, CancellationToken.None);
            });
            Complete(enqueue);

            Assert.IsFalse(t2.IsCompleted);
            Assert.IsFalse(called, "the second write ran inline inside Enqueue");

            gate1.SetResult(1);
            Assert.AreEqual(2, Result(t2));
        }

        [Test]
        public void Completion_OfFirstWrite_DoesNotStartSecondOnTheCompletingThread()
        {
            // The gate runs continuations synchronously, so write 1 finishes, and its slot completes, inside SetResult
            // on this thread. Only the queue's own RunContinuationsAsynchronously keeps write 2 off this thread.
            var gate1 = NewGate(runContinuationsAsynchronously: false);
            int testThread = Thread.CurrentThread.ManagedThreadId;
            int secondThread = 0;
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            Task<int> t2 = _queue.Enqueue(CHAR_A, ct =>
            {
                secondThread = Thread.CurrentThread.ManagedThreadId;
                return Task.FromResult(2);
            }, CancellationToken.None);

            gate1.SetResult(1);

            Assert.AreEqual(1, Result(t1));
            Assert.AreEqual(2, Result(t2));
            Assert.AreNotEqual(testThread, secondThread, "the second write started synchronously inside SetResult");
        }

        // ---------------------------------------------------------------- no leak

        [Test]
        public void ActiveCharacterCount_WriteHeld_IsOneThenReturnsToZero()
        {
            var gate1 = NewGate();
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);

            Assert.AreEqual(1, _queue.ActiveCharacterCount);

            gate1.SetResult(1);
            Result(t1);
            AssertCountReaches(0);
        }

        [Test]
        public void ActiveCharacterCount_ThousandCharactersHeldThenReleased_IsThousandThenZero()
        {
            var gates = new TaskCompletionSource<int>[MANY_CHARACTERS];
            var tasks = new Task<int>[MANY_CHARACTERS];
            for (int i = 0; i < MANY_CHARACTERS; i++)
            {
                gates[i] = NewGate();
                TaskCompletionSource<int> gate = gates[i];
                tasks[i] = _queue.Enqueue(new CharacterID((uint)(i + 1)), ct => gate.Task, CancellationToken.None);
            }

            Assert.AreEqual(MANY_CHARACTERS, _queue.ActiveCharacterCount);

            for (int i = 0; i < MANY_CHARACTERS; i++)
            {
                gates[i].SetResult(i);
            }

            for (int i = 0; i < MANY_CHARACTERS; i++)
            {
                Assert.AreEqual(i, Result(tasks[i]));
            }

            AssertCountReaches(0);
        }

        // ---------------------------------------------------------------- cancellation

        [Test]
        public void Enqueue_SecondCancelledWhileWaiting_IsCancelledNeverCalledAndThirdRunsAfterFirst()
        {
            var gate1 = NewGate();
            using (var cts = new CancellationTokenSource())
            {
                bool secondCalled = false;
                Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
                Task<int> t2 = _queue.Enqueue(CHAR_A, ct =>
                {
                    secondCalled = true;
                    return Task.FromResult(2);
                }, cts.Token);
                Task<int> t3 = _queue.Enqueue(CHAR_A, Instant("3", 3), CancellationToken.None);

                cts.Cancel();
                Complete(t2);
                Assert.IsTrue(t2.IsCanceled);
                Assert.IsFalse(t3.IsCompleted, "the third write must still wait for the first");

                gate1.SetResult(1);
                Assert.AreEqual(3, Result(t3));
                Assert.AreEqual(1, Result(t1));
                Assert.IsFalse(secondCalled);
                CollectionAssert.AreEqual(new[] { "start:1", "end:1", "start:3", "end:3" }, Events());
                AssertCountReaches(0);
            }
        }

        [Test]
        public void Enqueue_TwoCancelledInARow_FourthStillRunsAfterFirst()
        {
            var gate1 = NewGate();
            using (var cts2 = new CancellationTokenSource())
            using (var cts3 = new CancellationTokenSource())
            {
                Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
                Task<int> t2 = _queue.Enqueue(CHAR_A, Instant("2", 2), cts2.Token);
                Task<int> t3 = _queue.Enqueue(CHAR_A, Instant("3", 3), cts3.Token);
                Task<int> t4 = _queue.Enqueue(CHAR_A, Instant("4", 4), CancellationToken.None);

                cts2.Cancel();
                cts3.Cancel();
                Complete(t2);
                Complete(t3);
                Assert.IsTrue(t2.IsCanceled);
                Assert.IsTrue(t3.IsCanceled);
                Settle();
                Assert.IsFalse(t4.IsCompleted, "the fourth write must still wait for the first");

                gate1.SetResult(1);
                Assert.AreEqual(1, Result(t1));
                Assert.AreEqual(4, Result(t4));
                CollectionAssert.AreEqual(new[] { "start:1", "end:1", "start:4", "end:4" }, Events());
                AssertCountReaches(0);
            }
        }

        [Test]
        public void Enqueue_TokenAlreadyCancelledBehindHeldWrite_IsCancelledAtOnceAndNeverCalled()
        {
            var gate1 = NewGate();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);

                Task<int> t2 = _queue.Enqueue(CHAR_A, Instant("2", 2), cts.Token);

                Complete(t2);
                Assert.IsTrue(t2.IsCanceled);

                gate1.SetResult(1);
                Assert.AreEqual(1, Result(t1));
                AssertCountReaches(0);
                CollectionAssert.AreEqual(new[] { "start:1", "end:1" }, Events());
            }
        }

        [Test]
        public void Enqueue_TokenCancelledAfterQueuedWriteStarted_CallerGetsTheWritesOwnOutcome()
        {
            // Code review 2026-10-15: the wait-time cancel registration must be gone once the write runs, or a write
            // that ignores the token (and may commit) would be reported as cancelled.
            var gate1 = NewGate();
            var gate2 = NewGate();
            using (var cts = new CancellationTokenSource())
            {
                Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
                Task<int> t2 = _queue.Enqueue(CHAR_A, Write("2", gate2), cts.Token);
                gate1.SetResult(1);
                WaitForEvent("start:2");

                cts.Cancel();
                Settle();
                Assert.IsFalse(t2.IsCompleted, "a cancel during the write must not complete the caller's task");

                gate2.SetResult(20);

                Assert.AreEqual(1, Result(t1));
                Assert.AreEqual(20, Result(t2));
            }
        }

        [Test]
        public void Enqueue_TokenCancelledBeforeCall_IsCancelledAndWriteNotCalled()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool called = false;

                Task<int> t = _queue.Enqueue(CHAR_A, ct =>
                {
                    called = true;
                    return Task.FromResult(1);
                }, cts.Token);

                Complete(t);
                Assert.IsTrue(t.IsCanceled);
                Assert.IsFalse(called);
                AssertCountReaches(0);
            }
        }

        [Test]
        public void Enqueue_Token_IsPassedToTheWrite()
        {
            using (var cts = new CancellationTokenSource())
            {
                CancellationToken seen = default;

                Task<int> t = _queue.Enqueue(CHAR_A, ct =>
                {
                    seen = ct;
                    return Task.FromResult(1);
                }, cts.Token);

                Result(t);
                Assert.AreEqual(cts.Token, seen);
            }
        }

        // ---------------------------------------------------------------- re-entrancy

        [Test]
        public void Enqueue_FromInsideARunningWrite_RunsAfterItAndCompletes()
        {
            var gate1 = NewGate();
            Task<int> inner = null;
            Task<int> outer = _queue.Enqueue(CHAR_A, async ct =>
            {
                Log("start:1");
                inner = _queue.Enqueue(CHAR_A, Instant("2", 2), CancellationToken.None);
                int v = await gate1.Task.ConfigureAwait(false);
                Log("end:1");
                return v;
            }, CancellationToken.None);

            Assert.IsNotNull(inner);
            Assert.IsFalse(inner.IsCompleted);

            gate1.SetResult(1);

            Assert.AreEqual(1, Result(outer));
            Assert.AreEqual(2, Result(inner));
            CollectionAssert.AreEqual(new[] { "start:1", "end:1", "start:2", "end:2" }, Events());
            AssertCountReaches(0);
        }

        [Test]
        public void Enqueue_FromTheCompletionContinuationOfAHeldWrite_RunsAndCompletes()
        {
            var gate1 = NewGate();
            Task<int> t1 = _queue.Enqueue(CHAR_A, Write("1", gate1), CancellationToken.None);
            Task<Task<int>> chained = t1.ContinueWith(
                _ => _queue.Enqueue(CHAR_A, Instant("2", 2), CancellationToken.None),
                TaskContinuationOptions.ExecuteSynchronously);
            Assert.IsFalse(chained.IsCompleted);

            gate1.SetResult(1);

            Complete(chained);
            Assert.AreEqual(2, Result(chained.Result));
            CollectionAssert.AreEqual(new[] { "start:1", "end:1", "start:2", "end:2" }, Events());
            AssertCountReaches(0);
        }

        // ---------------------------------------------------------------- stress

        [Test]
        public void Enqueue_ManyYieldingWritesOneCharacter_NeverOverlapAndStartInOrder()
        {
            int inFlight = 0;
            int maxInFlight = 0;
            var starts = new List<int>();
            var tasks = new Task<int>[STRESS_WRITES];
            for (int i = 0; i < STRESS_WRITES; i++)
            {
                int index = i;
                tasks[i] = _queue.Enqueue(CHAR_A, async ct =>
                {
                    int now = Interlocked.Increment(ref inFlight);
                    InterlockedMax(ref maxInFlight, now);
                    lock (starts)
                    {
                        starts.Add(index);
                    }

                    // A real hop to the pool (not Task.Yield, which would post to Unity's context on this thread).
                    await Task.Run(() => { }).ConfigureAwait(false);
                    Interlocked.Decrement(ref inFlight);
                    return index;
                }, CancellationToken.None);
            }

            for (int i = 0; i < STRESS_WRITES; i++)
            {
                Assert.AreEqual(i, Result(tasks[i]));
            }

            Assert.AreEqual(1, maxInFlight, "two writes for one character ran at once");
            CollectionAssert.AreEqual(Enumerable.Range(0, STRESS_WRITES), starts);
            AssertCountReaches(0);
        }

        [Test]
        public void Enqueue_FromSeveralThreadsOverSeveralCharacters_NoOverlapPerCharacterAndAllComplete()
        {
            var inFlight = new int[CONCURRENT_CHARACTERS];
            int overlaps = 0;
            var all = new List<Task<int>>();
            var producers = new Task[CONCURRENT_THREADS];
            for (int p = 0; p < CONCURRENT_THREADS; p++)
            {
                int producer = p;
                producers[p] = Task.Run(() =>
                {
                    for (int w = 0; w < WRITES_PER_THREAD; w++)
                    {
                        int character = (producer + w) % CONCURRENT_CHARACTERS;
                        Task<int> t = _queue.Enqueue(new CharacterID((uint)(character + 1)), async ct =>
                        {
                            if (Interlocked.Increment(ref inFlight[character]) > 1)
                            {
                                Interlocked.Increment(ref overlaps);
                            }

                            await Task.Run(() => { }).ConfigureAwait(false);
                            Interlocked.Decrement(ref inFlight[character]);
                            return character;
                        }, CancellationToken.None);
                        lock (all)
                        {
                            all.Add(t);
                        }
                    }
                });
            }

            foreach (Task producerTask in producers)
            {
                Complete(producerTask);
            }

            foreach (Task<int> t in all)
            {
                Result(t);
            }

            Assert.AreEqual(CONCURRENT_THREADS * WRITES_PER_THREAD, all.Count);
            Assert.AreEqual(0, overlaps, "two writes for one character ran at once");
            AssertCountReaches(0);
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value > current)
            {
                int seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current)
                {
                    return;
                }

                current = seen;
            }
        }

        // ---------------------------------------------------------------- arguments

        [Test]
        public void Enqueue_NullWrite_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => _queue.Enqueue<int>(CHAR_A, null, CancellationToken.None));
        }
    }
}
