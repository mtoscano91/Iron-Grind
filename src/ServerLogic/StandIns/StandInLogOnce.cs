using UnityEngine;

namespace IronGrind.ServerLogic.StandIns
{
    /// <summary>
    /// Logs "[ZoneServer] &lt;step&gt; not implemented" the first time it is asked, then stays silent.
    /// One instance per stand-in, so the log is once per stand-in instance across all its methods.
    /// </summary>
    internal sealed class StandInLogOnce
    {
        private readonly string _step;
        private bool _logged;

        public StandInLogOnce(string step)
        {
            _step = step;
        }

        public void Log()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            Debug.Log("[ZoneServer] " + _step + " not implemented");
        }
    }
}
