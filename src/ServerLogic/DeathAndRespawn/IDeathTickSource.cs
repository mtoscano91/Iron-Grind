namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>Source of the current server tick as a <c>long</c> (CR-DR-2, AC-DR-15; Story 002 AC-7).</summary>
    public interface IDeathTickSource
    {
        /// <summary>The current server tick, widened to <c>long</c>.</summary>
        long CurrentTick { get; }
    }
}
