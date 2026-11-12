using System;
using System.Globalization;
using System.Text;
using IronGrind.Authentication;
using IronGrind.Client.Auth;
using IronGrind.Client.Session;
using IronGrind.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace IronGrind.DevHarness.Session
{
    /// <summary>
    /// Dev harness for Authentication Story 018 (AC-9, AC-10): register-if-needed, login, zone entry, heartbeat,
    /// with one timestamped <c>[ZoneEntry]</c> line per milestone (Story 017's evidence reads them). With
    /// <c>-zeLoadDriver</c> (or by the load flag below) it runs the login load driver instead of a zone entry.
    /// Lives in the Editor-only <c>IronGrind.DevHarness</c> assembly. Never logs the password or the token.
    /// </summary>
    /// <remarks>
    /// Settings come from a <see cref="ZoneEntryHarnessConfig"/> asset; command-line arguments override them:
    /// <c>-zeUser</c>, <c>-zePassword</c>, <c>-zeCharacter</c>, <c>-zeGateway</c>, <c>-zeZoneHost</c> (each followed by a
    /// value) and <c>-zeLoadDriver</c> (flag).
    /// </remarks>
    public sealed class ZoneEntryDevHarness : MonoBehaviour
    {
        private const string LOG_PREFIX = "[ZoneEntry] ";
        // 30 fps: enough to drive Update (heartbeat 3 s, NGO at 20 Hz) while leaving CPU to the zone on one machine.
        private const int HARNESS_FRAME_RATE = 30;
        private const string TIMESTAMP_FORMAT = "yyyy-MM-ddTHH:mm:ss.fffZ";
        private const int LETTER_COUNT = 26;
        private const char FIRST_LETTER = 'a';
        private const float BUTTON_X = 10f;
        private const float BUTTON_Y = 10f;
        private const float BUTTON_WIDTH = 120f;
        private const float BUTTON_HEIGHT = 40f;

        [SerializeField] private ZoneEntryHarnessConfig _config;
        [SerializeField] private bool _loadDriverMode;

        /// <summary>JsonUtility overlay: sets the private serialized URL field of <see cref="AuthClientConfig"/>.</summary>
        [Serializable]
        private sealed class GatewayUrlOverlay
        {
            // The name must equal AuthClientConfig's private serialized field for JsonUtility.FromJsonOverwrite.
            public string _gatewayBaseUrl;
        }

        private string _username;
        private string _password;
        private string _characterName;
        private string _gatewayUrl;
        private string _zoneHostOverride;
        private bool _loadDriver;
        private AuthClient _authClient;
        private AuthClientLoginService _login;
        private UnityClientSessionClock _clock;
        private ClientZoneSession _session;
        private LoginLoadDriver _driver;
        private GameObject _networkGo;
        private readonly System.Collections.Generic.List<string> _nameLines = new System.Collections.Generic.List<string>();

        private bool _overridesApplied;
        private bool _previousRunInBackground;
        private int _previousVSyncCount;
        private int _previousTargetFrameRate;

        /// <summary>
        /// The client <c>NetworkConfig</c>: built only from <see cref="NgoSharedSettings"/> (plus the transport and the
        /// scene-less, prefab-less shape), so it matches the zone's config hash. Public for the parity test.
        /// </summary>
        /// <param name="transport">The transport the manager uses (null in a test that only reads the values).</param>
        /// <returns>The config.</returns>
        public static NetworkConfig CreateNetworkConfig(NetworkTransport transport)
        {
            return new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
                ConnectionApproval = NgoSharedSettings.CONNECTION_APPROVAL,
                EnableSceneManagement = NgoSharedSettings.ENABLE_SCENE_MANAGEMENT,
                TickRate = NgoSharedSettings.TICK_RATE,
                // ForceSamePrefabs is part of the config hash: setting it on one side alone fails every connect.
                ForceSamePrefabs = NgoSharedSettings.FORCE_SAME_PREFABS,
            };
        }

        private void OnEnable()
        {
            ApplyOverrides();
        }

        private void OnDisable()
        {
            RestoreOverrides();
        }

        // Several harness windows run at once (Story 017): an unfocused player must keep running (heartbeats, load
        // driver) instead of pausing, and an uncapped frame rate per window starves the zone of CPU on the same machine.
        // The previous values come back on disable/destroy, so the Editor is not left in the harness's state.
        private void ApplyOverrides()
        {
            if (_overridesApplied)
            {
                return;
            }

            _previousRunInBackground = Application.runInBackground;
            _previousVSyncCount = QualitySettings.vSyncCount;
            _previousTargetFrameRate = Application.targetFrameRate;
            _overridesApplied = true;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = HARNESS_FRAME_RATE;
        }

        private void RestoreOverrides()
        {
            if (!_overridesApplied)
            {
                return;
            }

            _overridesApplied = false;
            Application.runInBackground = _previousRunInBackground;
            QualitySettings.vSyncCount = _previousVSyncCount;
            Application.targetFrameRate = _previousTargetFrameRate;
        }

        private void Start()
        {
            if (_config == null)
            {
                Debug.LogError("ZoneEntryDevHarness: assign a ZoneEntryHarnessConfig asset.");
                enabled = false;
                return;
            }

            ResolveSettings();
            try
            {
                BuildAuth();
            }
            catch (Exception e)
            {
                Milestone("setup failed: " + e.GetType().Name + ": " + e.Message);
                enabled = false;
                return;
            }

            if (_loadDriver)
            {
                RegisterAccounts(0);
            }
            else
            {
                RegisterAccount(_username, _characterName, StartSession);
            }
        }

        private void Update()
        {
            _session?.Update();
            _driver?.Update();
        }

        private void OnGUI()
        {
            if (_session != null && _session.State != ClientZoneSessionState.Ended
                && GUI.Button(new Rect(BUTTON_X, BUTTON_Y, BUTTON_WIDTH, BUTTON_HEIGHT), "Leave"))
            {
                _session.Leave();
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                // Unsubscribe first: Leave raises SessionEnded and the harness is going away.
                _session.EntryFailed -= OnEntryFailed;
                _session.SnapshotAssembled -= OnSnapshotAssembled;
                _session.Entered -= OnEntered;
                _session.SessionEnded -= OnSessionEnded;
                _session.Leave();
                _session = null;
            }

            if (_networkGo != null)
            {
                Destroy(_networkGo);
            }

            RestoreOverrides();
        }

        private void OnEntryFailed(ZoneEntryFailureReason reason)
        {
            Milestone("entry failed reason=" + reason);
        }

        private void OnEntered()
        {
            Milestone("entered");
        }

        private void OnSessionEnded()
        {
            Milestone("session ended");
        }

        private void ResolveSettings()
        {
            ZoneEntryHarnessSettings settings = ZoneEntryHarnessSettings.Resolve(
                Environment.GetCommandLineArgs(), _config.Username, _config.Password, _config.CharacterName,
                _config.GatewayBaseUrl, _config.ZoneHostOverride, _loadDriverMode);
            _username = settings.Username;
            _password = settings.Password;
            _characterName = settings.CharacterName;
            _gatewayUrl = settings.GatewayUrl;
            _zoneHostOverride = settings.ZoneHostOverride;
            _loadDriver = settings.LoadDriver;
        }

        private void BuildAuth()
        {
            var authConfig = ScriptableObject.CreateInstance<AuthClientConfig>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new GatewayUrlOverlay { _gatewayBaseUrl = _gatewayUrl }), authConfig);

            var authTransport = gameObject.AddComponent<UnityWebRequestAuthTransport>();
            _authClient = new AuthClient(authConfig, authTransport, authTransport);
            _login = new AuthClientLoginService(_authClient);
            _clock = new UnityClientSessionClock();
        }

        private void RegisterAccounts(int index)
        {
            if (index >= _config.LoadDriverLogins)
            {
                StartLoadDriver();
                return;
            }

            string user = _username + "_" + index.ToString(CultureInfo.InvariantCulture);
            RegisterAccount(user, _characterName + LettersFor(index), () => RegisterAccounts(index + 1));
        }

        private void RegisterAccount(string user, string character, Action proceed)
        {
            _authClient.Register(user, _password, character, _config.ClassType, result =>
            {
                if (result.Success)
                {
                    Milestone("register ok user=" + user);
                    proceed();
                }
                else if (result.ErrorCode == RegistrationError.UsernameTaken)
                {
                    Milestone("register skipped (username taken) user=" + user);
                    proceed();
                }
                else
                {
                    Milestone("register failed user=" + user + " error=" + result.ErrorCode);
                }
            });
        }

        private void StartSession()
        {
            try
            {
                var sessionConfig = new ClientZoneSessionConfig
                {
                    HeartbeatIntervalSeconds = _config.HeartbeatIntervalSeconds,
                    FragmentReassemblyTimeoutSeconds = _config.FragmentReassemblyTimeoutSeconds,
                    ConnectTimeoutSeconds = _config.ConnectTimeoutSeconds,
                    MaxClientSnapshotFragments = _config.MaxClientSnapshotFragments,
                    ZoneHostOverride = _zoneHostOverride ?? "",
                    GatewayBaseUrl = _gatewayUrl,
                };

                NgoZoneClientTransport transport = CreateNetworkObjects();
                _session = new ClientZoneSession(sessionConfig, _login, transport, _clock, Milestone);
                _session.EntryFailed += OnEntryFailed;
                _session.SnapshotAssembled += OnSnapshotAssembled;
                _session.Entered += OnEntered;
                _session.SessionEnded += OnSessionEnded;
                _session.Begin(_username, _password);
            }
            catch (Exception e)
            {
                Milestone("session setup failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private void OnSnapshotAssembled(byte[] buffer, int length)
        {
            _nameLines.Clear();
            SnapshotNameLog.FormatPlayerNames(new ReadOnlySpan<byte>(buffer, 0, length), _nameLines);
            for (int i = 0; i < _nameLines.Count; i++)
            {
                Milestone(_nameLines[i]);
            }
        }

        private void StartLoadDriver()
        {
            _driver = new LoginLoadDriver(
                _login, _clock, Debug.Log, _username, _password, _config.LoadDriverLogins, _config.LoadDriverDurationSeconds);
            _driver.Start();
        }

        private NgoZoneClientTransport CreateNetworkObjects()
        {
            // Inactive first so NetworkManager.Awake reads a NetworkConfig that is already in place.
            _networkGo = new GameObject("ZoneEntryNetworkManager");
            _networkGo.SetActive(false);
            var manager = _networkGo.AddComponent<NetworkManager>();
            var unityTransport = _networkGo.AddComponent<UnityTransport>();
            // Must equal the zone server's NetworkConfig (NgoZoneNetwork): NGO compares a config hash on connect.
            manager.NetworkConfig = CreateNetworkConfig(unityTransport);
            var transport = _networkGo.AddComponent<NgoZoneClientTransport>();
            _networkGo.SetActive(true);
            transport.Init(manager, unityTransport);
            return transport;
        }

        private static void Milestone(string line)
        {
            Debug.Log(LOG_PREFIX + DateTime.UtcNow.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture) + " " + line);
        }


        /// <summary>Letters only (a, b ... z, ba ...) so the character name stays free of digits.</summary>
        private static string LettersFor(int index)
        {
            var sb = new StringBuilder();
            int value = index;
            do
            {
                sb.Insert(0, (char)(FIRST_LETTER + value % LETTER_COUNT));
                value /= LETTER_COUNT;
            }
            while (value > 0);
            return sb.ToString();
        }
    }
}
