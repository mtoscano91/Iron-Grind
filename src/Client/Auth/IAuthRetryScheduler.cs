using System;

namespace IronGrind.Client.Auth
{
    /// <summary>Seam for the wait between ServerStarting retries. Tests replace it with a fake fired by hand.</summary>
    public interface IAuthRetryScheduler
    {
        /// <summary>Runs <paramref name="callback"/> once after <paramref name="delaySeconds"/>.</summary>
        /// <param name="delaySeconds">Delay in whole seconds.</param>
        /// <param name="callback">The action to run.</param>
        void Schedule(int delaySeconds, Action callback);
    }
}
