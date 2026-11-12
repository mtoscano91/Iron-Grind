using System;

namespace IronGrind.DevHarness.Session
{
    /// <summary>
    /// The harness settings after the config asset and the command line are merged (Authentication Story 017):
    /// a pure value so the trimming and override rules are unit-testable.
    /// </summary>
    public readonly struct ZoneEntryHarnessSettings
    {
        /// <summary>Command-line flag: username.</summary>
        public const string ARG_USER = "-zeUser";

        /// <summary>Command-line flag: password.</summary>
        public const string ARG_PASSWORD = "-zePassword";

        /// <summary>Command-line flag: character name.</summary>
        public const string ARG_CHARACTER = "-zeCharacter";

        /// <summary>Command-line flag: gateway base URL.</summary>
        public const string ARG_GATEWAY = "-zeGateway";

        /// <summary>Command-line flag: zone host override.</summary>
        public const string ARG_ZONE_HOST = "-zeZoneHost";

        /// <summary>Command-line flag (no value): run the login load driver.</summary>
        public const string ARG_LOAD_DRIVER = "-zeLoadDriver";

        /// <summary>Username, trimmed. Never null.</summary>
        public string Username { get; }

        /// <summary>Password, NOT trimmed (a password may contain spaces). Never null.</summary>
        public string Password { get; }

        /// <summary>Character name, trimmed. Never null.</summary>
        public string CharacterName { get; }

        /// <summary>Gateway base URL, trimmed. Never null.</summary>
        public string GatewayUrl { get; }

        /// <summary>Zone host override, trimmed. Empty means none. Never null.</summary>
        public string ZoneHostOverride { get; }

        /// <summary>True when the load driver runs instead of a zone entry.</summary>
        public bool LoadDriver { get; }

        private ZoneEntryHarnessSettings(
            string username, string password, string characterName, string gatewayUrl, string zoneHost, bool loadDriver)
        {
            Username = username;
            Password = password;
            CharacterName = characterName;
            GatewayUrl = gatewayUrl;
            ZoneHostOverride = zoneHost;
            LoadDriver = loadDriver;
        }

        /// <summary>
        /// Merges the command line over the config values. A command-line value that is null, empty or only white space
        /// falls back to the config value; every value but the password is trimmed (a stray trailing space in the asset
        /// made <c>"127.0.0.1 "</c> an invalid host); a missing value ends up as an empty string.
        /// </summary>
        /// <param name="args">The process arguments (may be null).</param>
        /// <param name="configUsername">Config asset username.</param>
        /// <param name="configPassword">Config asset password.</param>
        /// <param name="configCharacterName">Config asset character name.</param>
        /// <param name="configGatewayUrl">Config asset gateway base URL.</param>
        /// <param name="configZoneHost">Config asset zone host override.</param>
        /// <param name="loadDriverMode">True when the scene forces the load driver mode.</param>
        /// <returns>The merged settings.</returns>
        public static ZoneEntryHarnessSettings Resolve(
            string[] args,
            string configUsername,
            string configPassword,
            string configCharacterName,
            string configGatewayUrl,
            string configZoneHost,
            bool loadDriverMode)
        {
            string[] safeArgs = args ?? Array.Empty<string>();
            return new ZoneEntryHarnessSettings(
                Pick(GetArg(safeArgs, ARG_USER), configUsername).Trim(),
                Pick(GetArg(safeArgs, ARG_PASSWORD), configPassword),
                Pick(GetArg(safeArgs, ARG_CHARACTER), configCharacterName).Trim(),
                Pick(GetArg(safeArgs, ARG_GATEWAY), configGatewayUrl).Trim(),
                Pick(GetArg(safeArgs, ARG_ZONE_HOST), configZoneHost).Trim(),
                loadDriverMode || Array.IndexOf(safeArgs, ARG_LOAD_DRIVER) >= 0);
        }

        private static string Pick(string argValue, string configValue)
        {
            if (!string.IsNullOrWhiteSpace(argValue))
            {
                return argValue;
            }

            return configValue ?? "";
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
