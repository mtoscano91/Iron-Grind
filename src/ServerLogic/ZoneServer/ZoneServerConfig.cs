using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The server config file (ADR-009 Amendment 1 Decision 5, ADR-006 Amendment 3): database connection
    /// fields, pool values, the warm-up timeout and the NGO shutdown wait. Parsed with <c>JsonUtility</c>, so
    /// fields absent from the JSON keep the initialiser values below. The database password is never part of
    /// it (it comes from <c>IRONGRIND_PG_PASSWORD</c>); a <c>Password</c> key is rejected. Validation errors
    /// name the field only, never its value.
    /// </summary>
    /// <example>
    /// <code>
    /// if (ZoneServerConfig.TryLoad(json, out ZoneServerConfig config, out string error))
    /// {
    ///     int frames = config.NgoShutdownWaitFrames;
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public sealed class ZoneServerConfig
    {
        /// <summary>Default NGO_SHUTDOWN_WAIT_FRAMES (5 s at 20 frames per second).</summary>
        public const int DEFAULT_NGO_SHUTDOWN_WAIT_FRAMES = 100;

        private const int MAX_PORT = 65535;

        // Any key that contains "password", "passwd" or "pwd" (e.g. "Password", "PgPassword", "db_password").
        // JsonUtility would ignore such a key, so it is caught on the raw text before parsing.
        private static readonly Regex PASSWORD_KEY = new Regex(
            "\"[^\"]*(password|passwd|pwd)[^\"]*\"\\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Database host. Required.</summary>
        public string Host;

        /// <summary>Database port (default 5432).</summary>
        public int Port = 5432;

        /// <summary>Database name. Required.</summary>
        public string Database;

        /// <summary>Database user name. Required.</summary>
        public string Username;

        /// <summary>Connection timeout in seconds (default 5).</summary>
        public int Timeout = 5;

        /// <summary>Command timeout in seconds (default 5).</summary>
        public int CommandTimeout = 5;

        /// <summary>Minimum pool size (default 2).</summary>
        public int MinPoolSize = 2;

        /// <summary>Maximum pool size (default 10).</summary>
        public int MaxPoolSize = 10;

        /// <summary>Connection idle lifetime in seconds (default 60).</summary>
        public int ConnectionIdleLifetime = 60;

        /// <summary>DB_WARMUP_TIMEOUT_MS (default 5000).</summary>
        public int DbWarmupTimeoutMs = 5000;

        /// <summary>NGO_SHUTDOWN_WAIT_FRAMES (default 100).</summary>
        public int NgoShutdownWaitFrames = DEFAULT_NGO_SHUTDOWN_WAIT_FRAMES;

        /// <summary>Parses and validates a config JSON text. Never throws.</summary>
        /// <param name="json">The file text.</param>
        /// <param name="config">The parsed config, or null on failure.</param>
        /// <param name="error">Empty on success; on failure a message naming the field, never its value.</param>
        /// <returns>True when the text parsed and every rule held.</returns>
        public static bool TryLoad(string json, out ZoneServerConfig config, out string error)
        {
            config = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "server config is empty";
                return false;
            }

            if (PASSWORD_KEY.IsMatch(json))
            {
                error = "Password must not be in the server config";
                return false;
            }

            ZoneServerConfig parsed;
            try
            {
                parsed = JsonUtility.FromJson<ZoneServerConfig>(json);
            }
            catch (Exception)
            {
                error = "server config is not valid JSON";
                return false;
            }

            if (parsed == null)
            {
                error = "server config is not valid JSON";
                return false;
            }

            error = parsed.Validate();
            if (error.Length > 0)
            {
                return false;
            }

            config = parsed;
            return true;
        }

        /// <summary>Checks every rule.</summary>
        /// <returns>Empty when valid; otherwise a message naming the first offending field (no digits, no value).</returns>
        public string Validate()
        {
            if (string.IsNullOrEmpty(Host)) { return "Host must not be empty"; }
            if (string.IsNullOrEmpty(Database)) { return "Database must not be empty"; }
            if (string.IsNullOrEmpty(Username)) { return "Username must not be empty"; }
            if (Port < 1 || Port > MAX_PORT) { return "Port is out of range"; }
            if (Timeout <= 0) { return "Timeout must be positive"; }
            if (CommandTimeout <= 0) { return "CommandTimeout must be positive"; }
            if (ConnectionIdleLifetime <= 0) { return "ConnectionIdleLifetime must be positive"; }
            if (DbWarmupTimeoutMs <= 0) { return "DbWarmupTimeoutMs must be positive"; }
            if (NgoShutdownWaitFrames <= 0) { return "NgoShutdownWaitFrames must be positive"; }
            if (MaxPoolSize < 1) { return "MaxPoolSize must be positive"; }
            if (MinPoolSize < 0) { return "MinPoolSize must not be negative"; }
            if (MinPoolSize > MaxPoolSize) { return "MinPoolSize must not exceed MaxPoolSize"; }
            return string.Empty;
        }
    }
}
