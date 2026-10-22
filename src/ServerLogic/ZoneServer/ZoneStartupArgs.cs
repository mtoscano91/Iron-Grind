using System.Globalization;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The command-line arguments of a zone process: <c>-zoneTemplate</c>, <c>-zoneId</c>, <c>-port</c> and
    /// <c>-serverConfig</c> (startup S0, ADR-009 Amendment 1). Unknown flags (Unity's <c>-batchmode</c>,
    /// <c>-logFile</c>, ...) are ignored. A value that starts with <c>-</c> counts as missing, so a flag is
    /// never taken as another flag's value. Parsing never throws and error text names the flag, never the value.
    /// </summary>
    /// <example>
    /// <code>
    /// string[] cl = { "-zoneTemplate", "Forest", "-zoneId", "7", "-port", "7777", "-serverConfig", "cfg.json" };
    /// if (ZoneStartupArgs.Parse(cl, out ZoneStartupArgs args, out string error)) { /* args.Port == 7777 */ }
    /// </code>
    /// </example>
    public sealed class ZoneStartupArgs
    {
        private const string FLAG_ZONE_TEMPLATE = "-zoneTemplate";
        private const string FLAG_ZONE_ID = "-zoneId";
        private const string FLAG_PORT = "-port";
        private const string FLAG_SERVER_CONFIG = "-serverConfig";

        /// <summary>Zone template (scene) name.</summary>
        public string ZoneTemplate { get; private set; }

        /// <summary>Zone id; never 0 (<c>ZoneID.Invalid</c>).</summary>
        public uint ZoneId { get; private set; }

        /// <summary>Port clients connect to; never 0.</summary>
        public ushort Port { get; private set; }

        /// <summary>Path of the server config file.</summary>
        public string ServerConfigPath { get; private set; }

        /// <summary>Parses the command line.</summary>
        /// <param name="args">The raw arguments; null fails.</param>
        /// <param name="result">The parsed arguments, or null on failure.</param>
        /// <param name="error">Empty on success; on failure a message naming the flag (never its value).</param>
        /// <returns>True when all four flags are present once with valid values.</returns>
        public static bool Parse(string[] args, out ZoneStartupArgs result, out string error)
        {
            result = null;
            if (args == null)
            {
                error = "command line is missing";
                return false;
            }

            string template = null;
            string zoneIdText = null;
            string portText = null;
            string configPath = null;

            for (int i = 0; i < args.Length; i++)
            {
                bool taken;
                switch (args[i])
                {
                    case FLAG_ZONE_TEMPLATE:
                        taken = TryTakeValue(args, ref i, ref template, out error);
                        break;
                    case FLAG_ZONE_ID:
                        taken = TryTakeValue(args, ref i, ref zoneIdText, out error);
                        break;
                    case FLAG_PORT:
                        taken = TryTakeValue(args, ref i, ref portText, out error);
                        break;
                    case FLAG_SERVER_CONFIG:
                        taken = TryTakeValue(args, ref i, ref configPath, out error);
                        break;
                    default:
                        continue;
                }

                if (!taken)
                {
                    return false;
                }
            }

            return TryBuild(template, zoneIdText, portText, configPath, out result, out error);
        }

        // Takes the value after the flag at args[i] into slot and advances i past it. Fails when the flag was
        // already given or has no value (end of the line, empty, or another flag).
        private static bool TryTakeValue(string[] args, ref int i, ref string slot, out string error)
        {
            string flag = args[i];
            if (slot != null)
            {
                error = flag + " is given more than once";
                return false;
            }

            if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1]) || args[i + 1][0] == '-')
            {
                error = flag + " has no value";
                return false;
            }

            slot = args[++i];
            error = string.Empty;
            return true;
        }

        private static bool TryBuild(
            string template, string zoneIdText, string portText, string configPath,
            out ZoneStartupArgs result, out string error)
        {
            result = null;
            error = MissingFlag(template, zoneIdText, portText, configPath);
            if (error.Length > 0)
            {
                return false;
            }

            if (!uint.TryParse(zoneIdText, NumberStyles.None, CultureInfo.InvariantCulture, out uint zoneId) || zoneId == 0u)
            {
                error = FLAG_ZONE_ID + " is not a valid zone id";
                return false;
            }

            if (!ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) || port == 0)
            {
                error = FLAG_PORT + " is not a valid port";
                return false;
            }

            result = new ZoneStartupArgs
            {
                ZoneTemplate = template,
                ZoneId = zoneId,
                Port = port,
                ServerConfigPath = configPath
            };
            return true;
        }

        private static string MissingFlag(string template, string zoneIdText, string portText, string configPath)
        {
            if (template == null) { return FLAG_ZONE_TEMPLATE + " is missing"; }
            if (zoneIdText == null) { return FLAG_ZONE_ID + " is missing"; }
            if (portText == null) { return FLAG_PORT + " is missing"; }
            if (configPath == null) { return FLAG_SERVER_CONFIG + " is missing"; }
            return string.Empty;
        }
    }
}
