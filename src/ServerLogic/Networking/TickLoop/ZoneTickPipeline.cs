using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The order of one server tick (ADR-014 Decision 6, Networking Core Story 037). Its
    /// <see cref="Tick"/> is the one tick-driven delegate of <see cref="ServerTickLoop"/>. It runs
    /// four steps in a fixed order: (1) completion, <see cref="ITickCompletionQueue.Drain"/>;
    /// (2) requests, <see cref="IInboundRequestDispatcher.DispatchTick"/>; (3) simulation,
    /// <see cref="IZoneSimulationStep.Run"/>; (4) outbound, <see cref="IZoneOutboundStep.Flush"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each step is isolated:</b> every step runs in its own <c>try</c>/<c>catch</c> that logs one
    /// server error and continues. <see cref="ServerTickLoop.AdvanceTick"/> lets an exception from a
    /// tick-driven delegate escape and then skips the TTL pass, the drift sample and the observer;
    /// because this class is the only tick-driven delegate and never lets an exception escape, a
    /// failing step skips neither the later steps nor the rest of <c>AdvanceTick</c>. A failure of
    /// the log call itself (an exception whose <c>Message</c> getter throws, a throwing log handler)
    /// is swallowed: that step then leaves no log, and the later steps still run.
    /// </para>
    /// <para>
    /// <b>One tick number:</b> the tick is read from the <see cref="IServerTickSource"/> once, before
    /// the first step, and the same value goes to all four steps.
    /// </para>
    /// <para>
    /// <b>Allocation:</b> none per tick when no step throws. The error message is built only inside
    /// a <c>catch</c>.
    /// </para>
    /// <para>
    /// <b>Threading:</b> not thread-safe; runs on the tick thread.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pipeline = new ZoneTickPipeline(completionQueue, dispatcher, simulationStep, outboundStep, tickLoop);
    /// tickLoop.RegisterTickDriven(pipeline.Tick);
    /// tickLoop.AdvanceTick();   // Drain, then Pass A and Pass B, then simulation, then outbound
    /// </code>
    /// </example>
    public sealed class ZoneTickPipeline
    {
        private const string STEP_COMPLETION = "Completion";
        private const string STEP_REQUESTS = "Requests";
        private const string STEP_SIMULATION = "Simulation";
        private const string STEP_OUTBOUND = "Outbound";

        private readonly ITickCompletionQueue _completionQueue;
        private readonly IInboundRequestDispatcher _dispatcher;
        private readonly IZoneSimulationStep _simulation;
        private readonly IZoneOutboundStep _outbound;
        private readonly IServerTickSource _tickSource;

        /// <summary>Creates the pipeline. No argument may be null.</summary>
        /// <param name="completionQueue">Step 1: drained first.</param>
        /// <param name="dispatcher">Step 2: Pass A and Pass B.</param>
        /// <param name="simulation">Step 3: ADR-002 phases 1 to 4.</param>
        /// <param name="outbound">Step 4: the per-connection writers.</param>
        /// <param name="tickSource">Where the tick number is read; usually the <see cref="ServerTickLoop"/>.</param>
        /// <exception cref="ArgumentNullException">An argument is null; the exception names the parameter.</exception>
        /// <example>
        /// <code>
        /// var pipeline = new ZoneTickPipeline(completionQueue, dispatcher, simulationStep, outboundStep, tickLoop);
        /// </code>
        /// </example>
        public ZoneTickPipeline(
            ITickCompletionQueue completionQueue,
            IInboundRequestDispatcher dispatcher,
            IZoneSimulationStep simulation,
            IZoneOutboundStep outbound,
            IServerTickSource tickSource)
        {
            _completionQueue = completionQueue ?? throw new ArgumentNullException(nameof(completionQueue));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            _outbound = outbound ?? throw new ArgumentNullException(nameof(outbound));
            _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));
        }

        /// <summary>
        /// Runs one tick: completion, requests, simulation, outbound, in that order, each in its own
        /// <c>try</c>/<c>catch</c>. Never throws for a step failure; a failed step logs one error
        /// (<c>[ZoneTickPipeline] StepFailed: step=NAME tick=N - ExceptionType: message</c>) and the
        /// next step still runs. Has the shape of <c>Action&lt;float&gt;</c>, so it is registered
        /// without a lambda.
        /// </summary>
        /// <param name="deltaTime">Passed unchanged to <see cref="IZoneSimulationStep.Run"/> and to no other step.</param>
        /// <example>
        /// <code>
        /// tickLoop.RegisterTickDriven(pipeline.Tick);   // the composition root, once
        /// </code>
        /// </example>
        public void Tick(float deltaTime)
        {
            uint currentTick = _tickSource.ServerTickNumber;

            try
            {
                _completionQueue.Drain(currentTick);
            }
            catch (Exception ex)
            {
                LogStepFailed(STEP_COMPLETION, currentTick, ex);
            }

            try
            {
                _dispatcher.DispatchTick(currentTick);
            }
            catch (Exception ex)
            {
                LogStepFailed(STEP_REQUESTS, currentTick, ex);
            }

            try
            {
                _simulation.Run(currentTick, deltaTime);
            }
            catch (Exception ex)
            {
                LogStepFailed(STEP_SIMULATION, currentTick, ex);
            }

            try
            {
                _outbound.Flush(currentTick);
            }
            catch (Exception ex)
            {
                LogStepFailed(STEP_OUTBOUND, currentTick, ex);
            }
        }

        // Called only from a catch block, so the message is built only when a step failed.
        private static void LogStepFailed(string stepName, uint tick, Exception ex)
        {
            try
            {
                Debug.LogError($"[ZoneTickPipeline] StepFailed: step={stepName} tick={tick} - {ex.GetType().Name}: {ex.Message}");
            }
            catch (Exception)
            {
                // Logging must not break the tick: an exception whose Message getter throws, or a
                // throwing log handler, would otherwise escape Tick and skip the later steps.
            }
        }
    }
}
