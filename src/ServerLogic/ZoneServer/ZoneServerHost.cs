using System;
using System.Globalization;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The one zone server host <c>MonoBehaviour</c> (ADR-009 Amendment 1 Decision 2). It builds a
    /// <see cref="ZoneServerComposition"/> in <c>Awake</c> from the Unity seams and the Story 044 stand-ins and only
    /// forwards: <c>Start</c> runs the composition's start coroutine, <c>Update</c> starts
    /// <see cref="ZoneServerComposition.RunTeardown"/> once when a teardown was requested, <c>OnDestroy</c> calls
    /// <see cref="ZoneServerComposition.TeardownNow"/>. <c>Application.wantsToQuit</c> reaches the composition through
    /// <see cref="UnityZoneApplication"/> (subscribed at S1, unsubscribed at teardown). It holds no game logic and
    /// never interrupts a running teardown (AC-14).
    /// </summary>
    /// <remarks>
    /// Put it on one root GameObject in <c>Assets/Scenes/Server/ZoneServer.unity</c>, a scene only the Dedicated
    /// Server build includes (ADR-012). In the Editor the command line comes from the <c>_editorArgs</c> field; in a
    /// player from <c>Environment.GetCommandLineArgs()</c>. In the Editor and development builds the extra argument
    /// <c>-teardownAfterSeconds &lt;n&gt;</c> takes the composition's quit path after n seconds (a drain, then a Clean teardown) (verification trigger, Story 045
    /// AC-6); a release build ignores it.
    /// </remarks>
    /// <example>
    /// <code>
    /// // ZoneServer.exe -batchmode -nographics -zoneTemplate StartingZone -zoneId 1 -port 7777 -serverConfig cfg.json
    /// </code>
    /// </example>
    public sealed class ZoneServerHost : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private string[] _editorArgs;
#endif

        [SerializeField] private ZoneSceneRegistry _registry;

        private ZoneServerComposition _composition;
        private NgoZoneNetwork _network;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private const string FLAG_TEARDOWN_AFTER = "-teardownAfterSeconds";
        private float _teardownAfterSeconds = -1f;
        private float _startTime;
        private bool _teardownTriggerFired;
#endif

        private void Awake()
        {
            try
            {
                var slot = new ZoneDefinitionSlot();
                _network = new NgoZoneNetwork();
                _composition = ZoneServerComposition.CreateWithRealBackend(
                    new UnityZoneApplication(gameObject),
                    _network,
                    new StandInZoneNavigation(),
                    null, // close steps: the Zone Instancing wiring built at S5 (Story 014)
                    new UnitySceneLoader(gameObject, _registry, slot),
                    new ZoneDefinitionBoundsValidator(slot),
                    new StandInZoneStaticDataLoader(),
                    null, // zone state owner: the Zone Instancing wiring built at S5 (Story 014)
                    new StandInGatewayRegistration(),
                    new FileZoneConfigSource(),
                    new ProcessZoneEnvironment(),
                    null,
                    slot); // the composition builds the respawn provider over it at S5 (zone id known by then)
            }
            catch (Exception ex)
            {
                // No composition: nothing was started, so there is nothing to tear down. A player exits Fatal instead
                // of running headless with a dead host; Start and Update do nothing (the host stays enabled, AC-14).
                _composition = null;
                Debug.LogError("[ZoneServer] host Awake failed: " + ex.GetType().Name);
                Application.Quit((int)ZoneExitCode.Fatal);
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _composition.LogTeardownSteps = true;
#endif
        }

        private void Start()
        {
            if (_composition == null)
            {
                return;
            }

            string[] args = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                args = _editorArgs ?? Array.Empty<string>();
            }
#endif
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            ReadTeardownTrigger(args);
#endif
            StartCoroutine(_composition.Start(args));
        }

        private void Update()
        {
            if (_composition == null)
            {
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (!_teardownTriggerFired && _teardownAfterSeconds >= 0f
                && Time.unscaledTime - _startTime >= _teardownAfterSeconds)
            {
                _teardownTriggerFired = true;
                Debug.Log("[ZoneServer] -teardownAfterSeconds " + _teardownAfterSeconds.ToString(CultureInfo.InvariantCulture)
                    + " reached: requesting a quit (drain)");
                _composition.RequestQuit();
            }
#endif
            _composition.PollQuitStall();
            if (_composition.TryBeginTeardown())
            {
                StartCoroutine(_composition.RunTeardown());
            }
        }

        private void OnDestroy()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Story 045 AC-11: which OnDestroy ran first (a destroyed NetworkManager reads as not alive).
            Debug.Log("[ZoneServer] host OnDestroy: NetworkManager alive=" + (_network != null && _network.IsAlive));
#endif
            _composition?.TeardownNow();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Verification trigger (AC-6): "-teardownAfterSeconds <n>"; a missing or invalid value leaves it off.
        private void ReadTeardownTrigger(string[] args)
        {
            _startTime = Time.unscaledTime;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == FLAG_TEARDOWN_AFTER
                    && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds)
                    && seconds >= 0f)
                {
                    _teardownAfterSeconds = seconds;
                    return;
                }
            }
        }
#endif
    }
}
