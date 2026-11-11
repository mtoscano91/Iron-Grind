using UnityEngine;

namespace IronGrind.DevHarness.Session
{
    /// <summary>
    /// Settings of <see cref="ZoneEntryDevHarness"/> (Authentication Story 018). The harness lets command-line
    /// arguments override username, password, character name, gateway URL and zone host. The password lives in a
    /// local dev asset only; the harness never logs it.
    /// </summary>
    [CreateAssetMenu(fileName = "ZoneEntryHarnessConfig", menuName = "IronGrind/Dev Harness/Zone Entry Config")]
    public sealed class ZoneEntryHarnessConfig : ScriptableObject
    {
        [SerializeField] private string _username = "";
        [SerializeField] private string _password = "";
        [SerializeField] private string _characterName = "";
        [SerializeField] private byte _classType;
        [SerializeField] private string _gatewayBaseUrl = "";
        [SerializeField] private string _zoneHostOverride = "";
        [SerializeField] private int _heartbeatIntervalSeconds = 3;
        [SerializeField] private int _fragmentReassemblyTimeoutSeconds = 10;
        [SerializeField] private int _connectTimeoutSeconds = 10;
        [SerializeField] private int _maxClientSnapshotFragments = 32;
        [SerializeField] private int _loadDriverLogins = 10;
        [SerializeField] private int _loadDriverDurationSeconds = 30;

        /// <summary>Account username (the load driver uses <c>username_index</c>).</summary>
        public string Username => _username;

        /// <summary>Account password.</summary>
        public string Password => _password;

        /// <summary>Character name used when the account is registered.</summary>
        public string CharacterName => _characterName;

        /// <summary>Class type byte used when the account is registered.</summary>
        public byte ClassType => _classType;

        /// <summary>Gateway base URL (https).</summary>
        public string GatewayBaseUrl => _gatewayBaseUrl;

        /// <summary>When non-empty, the zone host used instead of the gateway URL's host.</summary>
        public string ZoneHostOverride => _zoneHostOverride;

        /// <summary>HEARTBEAT_INTERVAL_SECONDS (default 3).</summary>
        public int HeartbeatIntervalSeconds => _heartbeatIntervalSeconds;

        /// <summary>FRAGMENT_REASSEMBLY_TIMEOUT_SECONDS (default 10).</summary>
        public int FragmentReassemblyTimeoutSeconds => _fragmentReassemblyTimeoutSeconds;

        /// <summary>CONNECT_TIMEOUT_SECONDS (default 10).</summary>
        public int ConnectTimeoutSeconds => _connectTimeoutSeconds;

        /// <summary>MAX_CLIENT_SNAPSHOT_FRAGMENTS (default 32).</summary>
        public int MaxClientSnapshotFragments => _maxClientSnapshotFragments;

        /// <summary>Load driver N: concurrent logins kept in flight (default 10, the gateway's MAX_AUTH_PENDING).</summary>
        public int LoadDriverLogins => _loadDriverLogins;

        /// <summary>Load driver D: seconds the load runs (default 30).</summary>
        public int LoadDriverDurationSeconds => _loadDriverDurationSeconds;
    }
}
