using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The real <see cref="IZoneCloseSteps"/>: CR-ZI-12 teardown steps 1-8 (<c>zone-instancing.md</c>, Zone Instancing
    /// Story 012). Runs at composition teardown step 4, after navigation teardown and before the outbound flush.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Saves.</b> Every save (step 2 <c>SaveSession</c>, step 3 ghost snapshot) is passed to
    /// <see cref="ITickCompletionQueue.Track{T}"/> with its own <see cref="CancellationTokenSource"/>, disposed by the
    /// callback. The callback runs inside <c>DrainOnShutdown</c> (composition teardown step 6), after the slots were
    /// freed here, so it uses only the <c>PartyID</c> / <c>EntityID</c> captured before step 6. <c>NotifyZoneExit</c>
    /// for a saved or ghost character fires from that callback, also when the save failed, faulted or was cancelled
    /// (one <c>LogError</c>). A save that is still running when the drain times out gets no <c>NotifyZoneExit</c>.
    /// An entering slot, or a slot whose save call threw synchronously, is notified at once in step 5.
    /// </para>
    /// <para>
    /// <b>Connections.</b> Step 4 queues <c>ZoneSessionEnded(ZoneClosed, 0)</c> cap-exempt and expires the session
    /// through <see cref="IZoneTeardownSessionSink"/>; it closes nothing. The outbound flush sends the message and NGO
    /// shutdown (teardown step 7) closes the connections.
    /// </para>
    /// <para>
    /// <b>Throw policy.</b> Each step has its own try/catch (<c>LogError</c> with the step number and exception type;
    /// the next step runs). Steps 2-7 also catch per slot. A second <see cref="RunCloseSteps"/> returns at once.
    /// Tick thread only; no <c>await</c>, <c>.Result</c> or <c>ContinueWith</c>.
    /// </para>
    /// <para>
    /// <b>Known limits.</b> A save still running at the drain timeout gets no callback, so its
    /// <see cref="CancellationTokenSource"/> is never disposed (harmless: the process exits). A slot that is both ghost
    /// and entering is treated as a ghost. If <see cref="IPartyZonePresence.TryGetFormalParty"/> throws for a
    /// character, that character gets no <c>NotifyZoneExit</c> (logged as a step 5 failure).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var steps = new ZoneCloseSteps(pool, directory, roster, reclamation, persistence, queue, party, timers,
    ///     sessionSink, drainLog, outbox, zone);
    /// steps.RunCloseSteps();                          // composition teardown step 4
    /// queue.DrainOnShutdown(TimeSpan.FromSeconds(5)); // composition teardown step 6 delivers NotifyZoneExit
    /// </code>
    /// </example>
    public sealed class ZoneCloseSteps : IZoneCloseSteps
    {
        private const string LOG_PREFIX = "[ZoneInstancing]";

        private sealed class Work
        {
            public EntityID Entity;
            public CharacterID Character;
            public uint ClientId;
            public ZoneSessionState State;
            public bool IsGhost;
            public bool IsEntering;
            public bool AwaitingSave;
            public bool HasParty;
            public PartyID Party;
        }

        private readonly ZoneSlotPool _pool;
        private readonly ZoneConnectionCharacterDirectory _directory;
        private readonly ZoneTickRoster _roster;
        private readonly ZoneEntryReclamation _reclamation;
        private readonly ICharacterPersistence _persistence;
        private readonly ITickCompletionQueue _queue;
        private readonly IPartyZonePresence _party;
        private readonly IZoneMobSpawnTimers _timers;
        private readonly IZoneTeardownSessionSink _sessionSink;
        private readonly IZoneDrainNoticeLog _drainLog;
        private readonly IClientMessageOutbox _outbox;
        private readonly ZoneID _zone;
        private readonly List<Work> _work = new List<Work>();
        private bool _ran;

        /// <summary>Creates the close steps for one zone.</summary>
        /// <param name="pool">The player and mob slot pool (one pool holds both).</param>
        /// <param name="directory">Connection-to-character map; entries are removed in step 6.</param>
        /// <param name="roster">The tick roster; entities are deactivated in step 6.</param>
        /// <param name="reclamation">Source of <see cref="ZoneEntryReclamation.IsEntering"/>.</param>
        /// <param name="persistence">Character persistence (<c>SaveSession</c>).</param>
        /// <param name="queue">The completion queue the saves are tracked in.</param>
        /// <param name="party">The party seam (<c>NotifyZoneExit</c>).</param>
        /// <param name="timers">Mob Spawning timers (step 1).</param>
        /// <param name="sessionSink">Session expiry and ghost snapshot seam (steps 3 and 4).</param>
        /// <param name="drainLog">Which clients already got <c>ZoneSessionEnded</c> at T-4.</param>
        /// <param name="outbox">Receives <c>ZoneSessionEnded</c>.</param>
        /// <param name="zone">The zone being closed; not <see cref="ZoneID.Invalid"/>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid.</exception>
        public ZoneCloseSteps(ZoneSlotPool pool, ZoneConnectionCharacterDirectory directory, ZoneTickRoster roster,
            ZoneEntryReclamation reclamation, ICharacterPersistence persistence, ITickCompletionQueue queue,
            IPartyZonePresence party, IZoneMobSpawnTimers timers, IZoneTeardownSessionSink sessionSink,
            IZoneDrainNoticeLog drainLog, IClientMessageOutbox outbox, ZoneID zone)
        {
            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _reclamation = reclamation ?? throw new ArgumentNullException(nameof(reclamation));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _timers = timers ?? throw new ArgumentNullException(nameof(timers));
            _sessionSink = sessionSink ?? throw new ArgumentNullException(nameof(sessionSink));
            _drainLog = drainLog ?? throw new ArgumentNullException(nameof(drainLog));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _zone = zone;
        }

        /// <summary>
        /// Runs CR-ZI-12 steps 1-8 once; a second call returns at once and makes no calls. Never throws for a seam failure.
        /// </summary>
        public void RunCloseSteps()
        {
            if (_ran)
            {
                return;
            }

            _ran = true;

            CaptureSlots();
            RunStep(1, Step1CancelSpawnTimers);
            RunStep(2, Step2SaveSessions);
            RunStep(3, Step3SaveGhosts);
            RunStep(4, Step4EndSessions);
            RunStep(5, Step5NotifyParty);
            RunStep(6, Step6FreePlayerSlots);
            RunStep(7, Step7FreeMobSlots);
            RunStep(8, Step8Retire);
        }

        private static void RunStep(int step, Action body)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} zone close step {step} failed ({ex.GetType().Name}); the next step runs.\n{ex}");
            }
        }

        private void CaptureSlots()
        {
            try
            {
                // Copy first: freeing a slot mutates the pool's roster view.
                var ids = new List<EntityID>(_pool.GetPlayerEntityIDsInZone(_zone));
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!_pool.TryGetPlayer(ids[i], out ZonePlayerSlot slot))
                    {
                        continue;
                    }

                    _work.Add(new Work
                    {
                        Entity = slot.Entity,
                        Character = slot.Character,
                        ClientId = slot.ClientId,
                        State = slot.State,
                        IsGhost = slot.State == ZoneSessionState.Disconnected_SessionActive,
                        IsEntering = _reclamation.IsEntering(slot.Entity),
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} zone close slot snapshot failed ({ex.GetType().Name}); slots captured so far are processed.\n{ex}");
            }
        }

        private void Step1CancelSpawnTimers()
        {
            _timers.CancelAllSpawnTimers(_zone);
        }

        private void Step2SaveSessions()
        {
            for (int i = 0; i < _work.Count; i++)
            {
                Work w = _work[i];
                if (w.IsGhost || w.IsEntering)
                {
                    continue;
                }

                var cts = new CancellationTokenSource();
                Task<CharacterSaveResult> save = null;
                try
                {
                    save = w.State == ZoneSessionState.Dead || w.State == ZoneSessionState.Respawning
                        ? _persistence.SaveSession(w.Character, SessionEndReason.ZoneClosed, 0f, cts.Token)
                        : _persistence.SaveSession(w.Character, SessionEndReason.ZoneClosed, cts.Token);
                    TrackSave(w, cts, save, "SaveSession");
                }
                catch (Exception ex)
                {
                    SaveStartFailed(w, cts, "SaveSession", 2, ex, save != null);
                }
            }
        }

        private void Step3SaveGhosts()
        {
            for (int i = 0; i < _work.Count; i++)
            {
                Work w = _work[i];
                if (!w.IsGhost)
                {
                    continue;
                }

                var cts = new CancellationTokenSource();
                Task<CharacterSaveResult> save = null;
                try
                {
                    save = _sessionSink.SaveGhostSnapshotAndExpire(w.Character, w.ClientId);
                    TrackSave(w, cts, save, "SaveGhostSnapshotAndExpire");
                }
                catch (Exception ex)
                {
                    SaveStartFailed(w, cts, "SaveGhostSnapshotAndExpire", 3, ex, save != null);
                }
            }
        }

        private void TrackSave(Work w, CancellationTokenSource cts, Task<CharacterSaveResult> save, string what)
        {
            _queue.Track(save, cts, result => OnSaveFinished(w, cts, result, what));
            w.AwaitingSave = true; // set after Track returned: a Track that throws leaves the slot to the immediate path
        }
        private void SaveStartFailed(Work w, CancellationTokenSource cts, string what, int step, Exception ex, bool taskExists)
        {
            if (!taskExists)
            {
                cts.Dispose();
            }

            // else: Track threw after the save started; the running task may still observe the token, so it stays undisposed.
            cts.Dispose();
            w.AwaitingSave = false; // a save that never started counts as finished and failed: notify at once in step 5
            Debug.LogError($"{LOG_PREFIX} zone close step {step}: {what} for {w.Character} threw {ex.GetType().Name}; the party is notified without waiting.\n{ex}");
        }

        private void OnSaveFinished(Work w, CancellationTokenSource cts, TickTaskResult<CharacterSaveResult> result, string what)
        {
            try
            {
                cts.Dispose();
                if (result.Status != TickTaskStatus.Completed || result.Value != CharacterSaveResult.Success)
                {
                    string outcome = result.Status == TickTaskStatus.Completed ? result.Value.ToString() : result.Status.ToString();
                    string detail = result.Error != null ? "\n" + result.Error : string.Empty;
                    Debug.LogError($"{LOG_PREFIX} zone close: {what} for {w.Character} did not succeed ({outcome}); the party is notified anyway.{detail}");
                }

                w.AwaitingSave = false;
                NotifyExit(w);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} zone close: save completion for {w.Character} threw {ex.GetType().Name}.\n{ex}");
            }
        }

        private void Step4EndSessions()
        {
            var body = new byte[ZoneSessionEnded.BodySize];
            int size = ZoneSessionEndedCodec.WriteBody(body, new ZoneSessionEnded(_zone, DisconnectReason.ZoneClosed, 0));
            for (int i = 0; i < _work.Count; i++)
            {
                Work w = _work[i];
                if (w.IsGhost)
                {
                    continue;
                }

                try
                {
                    if (!_drainLog.WasNotifiedAtDrain(w.ClientId))
                    {
                        _outbox.Enqueue(w.ClientId, ZoneSessionEnded.MessageTypeId, new ReadOnlySpan<byte>(body, 0, size), isCapExempt: true);
                    }

                    _sessionSink.ExpireConnectedSession(w.ClientId);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} zone close step 4 failed for client {w.ClientId} ({ex.GetType().Name}); the next slot runs.\n{ex}");
                }
            }
        }

        private void Step5NotifyParty()
        {
            for (int i = 0; i < _work.Count; i++)
            {
                Work w = _work[i];
                try
                {
                    w.HasParty = _party.TryGetFormalParty(w.Character, out PartyID party);
                    w.Party = w.HasParty ? party : PartyID.Uninitialized;
                    if (w.HasParty && !w.AwaitingSave)
                    {
                        _party.NotifyZoneExit(w.Party, w.Entity);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} zone close step 5 failed for {w.Entity} ({ex.GetType().Name}); the next slot runs.\n{ex}");
                }
            }
        }

        private void NotifyExit(Work w)
        {
            if (!w.HasParty)
            {
                return;
            }

            try
            {
                _party.NotifyZoneExit(w.Party, w.Entity);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} zone close: NotifyZoneExit for {w.Entity} threw {ex.GetType().Name}.\n{ex}");
            }
        }

        private void Step6FreePlayerSlots()
        {
            for (int i = 0; i < _work.Count; i++)
            {
                Work w = _work[i];
                try
                {
                    try
                    {
                        _directory.Remove(w.ClientId);
                        _roster.Deactivate(w.Entity);
                    }
                    finally
                    {
                        _pool.ReleasePlayer(w.Entity);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} zone close step 6 failed for {w.Entity} ({ex.GetType().Name}); the next slot runs.\n{ex}");
                }
            }
        }

        private void Step7FreeMobSlots()
        {
            var mobs = new List<EntityID>(_pool.GetMobEntityIDsInZone(_zone));
            for (int i = 0; i < mobs.Count; i++)
            {
                try
                {
                    _pool.ReleaseMob(mobs[i]);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} zone close step 7 failed for mob {mobs[i]} ({ex.GetType().Name}); the next slot runs.\n{ex}");
                }
            }
        }

        private void Step8Retire()
        {
            Debug.Log($"{LOG_PREFIX} zone {_zone.RawValue} retired");
        }
    }
}
