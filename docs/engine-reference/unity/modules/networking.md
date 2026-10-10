# Unity 6.3 — Networking Module Reference

**Last verified:** 2026-02-13 (general sections); 2026-10-09 for the section "NGO 2.13 — Custom Messaging, Transport and Frame Timing", researched from source and manual pages, not run
**Knowledge Gap:** Unity 6 uses Netcode for GameObjects (UNet deprecated)

---

## Overview

Unity 6 networking options:
- **Netcode for GameObjects** (RECOMMENDED): Official Unity multiplayer framework
- **Mirror**: Community-driven (UNet successor)
- **Photon**: Third-party service (PUN2)
- **Custom**: Low-level sockets

**UNet (Legacy)**: Deprecated, do not use.

---

## Netcode for GameObjects

### Installation
1. `Window > Package Manager`
2. Search "Netcode for GameObjects"
3. Install `com.unity.netcode.gameobjects`

---

## Basic Setup

### NetworkManager

```csharp
// Add to scene: GameObject > Add Component > NetworkManager

// Or create custom NetworkManager:
using Unity.Netcode;

public class CustomNetworkManager : MonoBehaviour {
    void Start() {
        NetworkManager.Singleton.StartHost(); // Server + client
        // OR
        NetworkManager.Singleton.StartServer(); // Dedicated server
        // OR
        NetworkManager.Singleton.StartClient(); // Client only
    }
}
```

---

## NetworkObject (Networked GameObjects)

### Mark GameObject as Networked

1. Add `NetworkObject` component to GameObject
2. Must be in root of prefab (not nested)
3. Register prefab in `NetworkManager > NetworkPrefabs List`

### Spawn Network Objects

```csharp
using Unity.Netcode;

public class GameManager : NetworkBehaviour {
    public GameObject playerPrefab;

    [ServerRpc(RequireOwnership = false)]
    public void SpawnPlayerServerRpc(ulong clientId) {
        GameObject player = Instantiate(playerPrefab);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}
```

---

## NetworkBehaviour (Networked Scripts)

### NetworkBehaviour Base Class

```csharp
using Unity.Netcode;

public class Player : NetworkBehaviour {
    // Called when spawned on network
    public override void OnNetworkSpawn() {
        if (IsOwner) {
            // Only run on owner's client
            GetComponent<Camera>().enabled = true;
        }
    }

    void Update() {
        if (!IsOwner) return; // Only owner can control

        // Handle input
        if (Input.GetKey(KeyCode.W)) {
            MoveServerRpc(Vector3.forward);
        }
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 direction) {
        // Runs on server
        transform.position += direction * Time.deltaTime;
    }
}
```

---

## Network Variables (Synchronized State)

### NetworkVariable<T>

```csharp
using Unity.Netcode;

public class Player : NetworkBehaviour {
    // ✅ Auto-synced across clients
    private NetworkVariable<int> health = new NetworkVariable<int>(100);

    public override void OnNetworkSpawn() {
        // Subscribe to value changes
        health.OnValueChanged += OnHealthChanged;
    }

    void OnHealthChanged(int oldValue, int newValue) {
        Debug.Log($"Health changed: {oldValue} -> {newValue}");
        UpdateHealthUI(newValue);
    }

    [ServerRpc]
    public void TakeDamageServerRpc(int damage) {
        // Only server can modify NetworkVariable
        health.Value -= damage;
    }
}
```

### NetworkVariable Permissions

```csharp
// Server can write, clients read-only (default)
private NetworkVariable<int> score = new NetworkVariable<int>();

// Owner can write
private NetworkVariable<int> ammo = new NetworkVariable<int>(
    default,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Owner
);
```

---

## RPCs (Remote Procedure Calls)

### ServerRpc (Client → Server)

```csharp
// Client calls, server executes
[ServerRpc]
void FireWeaponServerRpc() {
    // Runs on server
    Debug.Log("Server: Weapon fired");
}

// Call from client:
if (IsOwner && Input.GetKeyDown(KeyCode.Space)) {
    FireWeaponServerRpc();
}
```

### ClientRpc (Server → All Clients)

```csharp
// Server calls, all clients execute
[ClientRpc]
void PlayExplosionClientRpc(Vector3 position) {
    // Runs on all clients
    Instantiate(explosionPrefab, position, Quaternion.identity);
}

// Call from server:
[ServerRpc]
void ExplodeServerRpc(Vector3 position) {
    // Server logic
    DealDamageToNearbyPlayers(position);

    // Notify all clients
    PlayExplosionClientRpc(position);
}
```

### RPC Parameters

```csharp
// ✅ Supported: Primitives, structs, strings, arrays
[ServerRpc]
void SetNameServerRpc(string playerName) { }

[ClientRpc]
void UpdateScoresClientRpc(int[] scores) { }

// ❌ Not supported: MonoBehaviour, GameObject (use NetworkObjectReference)
```

---

## Network Ownership

### Check Ownership

```csharp
if (IsOwner) {
    // This client owns this NetworkObject
}

if (IsServer) {
    // Running on server
}

if (IsClient) {
    // Running on client
}

if (IsLocalPlayer) {
    // This is the local player object
}
```

### Transfer Ownership

```csharp
// Server transfers ownership
NetworkObject netObj = GetComponent<NetworkObject>();
netObj.ChangeOwnership(newOwnerClientId);
```

---

## NetworkObjectReference (Pass GameObjects in RPCs)

```csharp
using Unity.Netcode;

[ServerRpc]
void AttackTargetServerRpc(NetworkObjectReference targetRef) {
    if (targetRef.TryGet(out NetworkObject target)) {
        // Got the target object
        target.GetComponent<Health>().TakeDamage(10);
    }
}

// Call:
NetworkObject targetNetObj = target.GetComponent<NetworkObject>();
AttackTargetServerRpc(targetNetObj);
```

---

## Client-Server Architecture

### Server-Authoritative Pattern (RECOMMENDED)

```csharp
public class Player : NetworkBehaviour {
    private NetworkVariable<Vector3> position = new NetworkVariable<Vector3>();

    void Update() {
        if (IsOwner) {
            // Client: Send input to server
            Vector3 input = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
            MoveServerRpc(input);
        }

        // All clients: Sync to networked position
        transform.position = position.Value;
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 input) {
        // Server: Validate and apply movement
        position.Value += input * Time.deltaTime * moveSpeed;
    }
}
```

---

## Network Transport

### Unity Transport (Default)

```csharp
// Configured in NetworkManager:
// - Transport: Unity Transport
// - Address: 127.0.0.1 (localhost) or server IP
// - Port: 7777 (default)
```

### Connection Events

```csharp
void Start() {
    NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
}

void OnClientConnected(ulong clientId) {
    Debug.Log($"Client {clientId} connected");
}

void OnClientDisconnected(ulong clientId) {
    Debug.Log($"Client {clientId} disconnected");
}
```

---

## Performance Tips

### Reduce Network Traffic
- Use `NetworkVariable` for state that changes infrequently
- Batch multiple changes before syncing
- Use delta compression for large data

### Prediction & Reconciliation
- Run movement locally for responsiveness
- Reconcile with server authoritative state
- Use interpolation for smooth movement

---

## Debugging

### Network Profiler
- `Window > Analysis > Network Profiler`
- Monitor bandwidth, RPC calls, variable updates

### Network Simulator (Test Latency/Packet Loss)
- `NetworkManager > Network Simulator`
- Add artificial lag and packet loss for testing

---

## NGO 2.13 — Custom Messaging, Transport and Frame Timing

**Researched:** 2026-10-09, for ADR-004 OQ-ADR4-3 and the ADR-014 transport adapter.
**Evidence:** read from the NGO source on GitHub (branch `develop-2.0.0`, `package.json` 2.13.4 unreleased; latest changelog entry `[2.13.3] - 2026-09-14`) and from Unity manual pages. The files were not compared with the `v2.13.3` tag one by one. **Nothing here was compiled or run**, and no installed package was read. Where a statement comes from source and no manual page says it, it is marked *(source)*; treat it as true for 2.13.x and re-check on a package upgrade.

The sections above this one describe the `NetworkObject` / `NetworkVariable` / RPC model. This project does not use it for gameplay messages (ADR-004 Decision 4): it sends its own binary messages through `CustomMessagingManager`.

### Versions
- The Unity 6000.3 manual lists `com.unity.netcode.gameobjects` **2.13.3**. The page showed it as "pre-release" on the day of the research — confirm in the Package Manager.
- NGO 2.13.3 `package.json`: `"unity": "6000.0"`, `"com.unity.transport": "2.6.0"`. The 6000.3 manual lists transport 2.7.4 as released. Which transport version a 6000.3 project resolves: not found in the documentation.
- **Resolved in this project, 2026-10-09** (`Packages/packages-lock.json`): `com.unity.netcode.gameobjects` 2.13.3 with `com.unity.transport` 2.6.0. The project compiled with them and the EditMode suite passed (2383 / 2383); no project code references NGO yet. On import NGO created `Assets/DefaultNetworkPrefabs.asset`.

### `CustomMessagingManager` *(source: `Runtime/Messaging/CustomMessageManager.cs`)*
Reached through `NetworkManager.CustomMessagingManager` (created in `Initialize`, `null` after shutdown).

```csharp
// Unnamed — one channel, one handler list, no added bytes
public delegate void UnnamedMessageDelegate(ulong clientId, FastBufferReader reader);
public event UnnamedMessageDelegate OnUnnamedMessage;
public void SendUnnamedMessage(ulong clientId, FastBufferWriter messageBuffer, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced);
public void SendUnnamedMessage(IReadOnlyList<ulong> clientIds, FastBufferWriter messageBuffer, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced);
public void SendUnnamedMessageToAll(FastBufferWriter messageBuffer, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced);

// Named — 8 bytes (a ulong hash) added per message, one string hash per send
public delegate void HandleNamedMessageDelegate(ulong senderClientId, FastBufferReader messagePayload);
public void RegisterNamedMessageHandler(string name, HandleNamedMessageDelegate callback);
public void UnregisterNamedMessageHandler(string name);
public void SendNamedMessage(string messageName, ulong clientId, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced);
```

- The manual says of unnamed messages: "There's only one receiver handler per unnamed message, which can help when building a custom messaging system where you can define your own message headers."
- Framing NGO adds to every message, named or not *(source)*: a bit-packed header (`uint MessageType`, `uint MessageSize`), a share of one 16-byte batch header per batch, and a 4-byte length prefix at the transport level.
- An exception thrown by a handler is caught and logged by NGO; it does not escape the receive loop *(source)*.
- NGO 2.0.0 added a size check to the named and unnamed send functions; it throws `OverflowException` in DEBUG builds only.

### `FastBufferReader` / `FastBufferWriter` *(source: `Runtime/Serialization/`)*
- Both are `struct … : IDisposable`.
- **The reader passed to a handler is disposed as soon as the handler returns.** Copy or consume the bytes inside the handler.
- Reader members: `int Position`, `int Length`, `void Seek(int where)`, `void ReadBytesSafe(ref byte[] value, int size, int offset = 0)`, `byte* GetUnsafePtrAtCurrentPosition()`, `byte[] ToArray()` (allocates). There is no `Span<byte>` overload. Without allocating: `ReadBytesSafe(ref existingArray, reader.Length - reader.Position)`, or a span over the unsafe pointer (needs `allowUnsafeCode`).
- Writer: `new FastBufferWriter(int size, Allocator allocator, int maxSize = -1)` — one native allocation, no managed one; the manual's example uses `Allocator.Temp` inside `using`. `WriteBytesSafe(byte[] value, int size = -1, int offset = 0)`.
- NGO's own cost per received message: one native `Allocator.TempJob` allocation and a copy; for unnamed messages also `OnUnnamedMessage.GetInvocationList()` per message. Not measured.

### `NetworkDelivery` *(source: `Runtime/Transports/NetworkDelivery.cs`, `UnityTransport.cs`)*
Members: `Unreliable`, `UnreliableSequenced`, `Reliable`, `ReliableSequenced`, `ReliableFragmentedSequenced`.

- ⚠️ **`UnityTransport` has no reliable-unordered delivery.** `Reliable`, `ReliableSequenced` and `ReliableFragmentedSequenced` all use the one reliable-sequenced pipeline. `Reliable` is ordered in practice.
- Size: every delivery except `ReliableFragmentedSequenced` is capped at about 1296 bytes per message including NGO's headers (`NetworkManager.MaximumTransmissionUnitSize`, default 1296) — about 1272 bytes of payload for an unnamed message (computed from the source, not quoted anywhere). Larger messages need `ReliableFragmentedSequenced`.
- A reliable send queue that overflows **disconnects the client** ("Closing connection … as reliability guarantees can't be maintained").
- Defaults: reliable window 64 packets; max packet queue 128; send queue sized dynamically.
- Reliable ordered: the Unity Transport manual says the reliable sequenced stage "will guarantee the delivery and order of their packets". Exactly-once is not stated on the pages read.

### Frame timing *(source: `NetworkManager.NetworkUpdate`; the manual lists the stages but not what runs in them)*
| Stage | What NGO does |
|---|---|
| `EarlyUpdate` | transport update, connection approvals, then `ProcessIncomingMessageQueue()` — **custom message handlers run here** |
| `PreUpdate` | network time update; `NetworkTickSystem.Tick` fires (can fire more than once in a slow frame) |
| `PostLateUpdate` | `ProcessSendQueues()`, transport flush, then deferred client disconnects |

- ⚠️ **Handlers can also run inside a transport disconnect event**: NGO flushes the incoming queue of all clients before it raises the disconnect callback. This path is reachable from `PostLateUpdate` too (a send-queue overflow raises a disconnect).
- A message sent with `SendUnnamedMessage` is queued; it goes on the wire in `PostLateUpdate`.

### Connections *(source: `Runtime/Connection/NetworkConnectionManager.cs`)*
- Client ids are `ulong`. The server is `NetworkManager.ServerClientId` = 0; clients get 1, 2, 3 … from a counter that is never decremented or reset in that file. Whether the counter survives a shutdown and restart in one process: not checked. The NGO client id is not the transport's connection id.
- `event Action<ulong> OnClientConnectedCallback`, `event Action<ulong> OnClientDisconnectCallback`, `event Action<NetworkManager, ConnectionEventData> OnConnectionEvent`.
- ⚠️ **`DisconnectClient(ulong clientId)` is deferred**: it sends a reason message and the disconnect and its callback happen in `PostLateUpdate` of that frame. `DisconnectClient(clientId, null)` (empty reason) disconnects and raises the callback inside the call.
- After a server-initiated disconnect, messages of that client already in the incoming queue can still reach handlers for the rest of that frame. After a transport-initiated disconnect, later data of that connection is dropped.
- Messages of a client that is not approved yet are rejected by NGO.

### Clock and RTT *(source)*
- `NetworkConfig.TickRate` is `uint`, default 30; no range restriction seen.
- `NetworkManager.ServerTime` is a `NetworkTime`; `NetworkTime.Tick` is `int`. `NetworkTickSystem` has `event Action Tick`.
- `NetworkTransport.GetCurrentRtt(ulong clientId)` returns `ulong`.

### A server with no `NetworkObject`
- The source says "it is valid to not have any player spawned upon connection", and `ConnectionApproval` defaults to `false`.
- No manual page states that a server with no `NetworkObject` and only custom messages is a supported configuration. `NetworkConfig.EnableSceneManagement` defaults to `true`; whether it must be off for this setup: not verified.

### Going under NGO (not viable as is)
- `UnityTransport.GetNetworkDriver()` is public, but `UnityTransport` pops every event of its driver and NGO rejects a payload without its batch header, so raw sends on NGO's connection do not arrive.
- A custom `NetworkTransport` subclass is allowed by the manual ("write your own"); it has ten abstract members and sees NGO's batches, not individual messages.
- A second, separate `NetworkDriver` would not share NGO's client ids, connection approval or clock.

### Testing
- `NetcodeIntegrationTest` (`Unity.Netcode.TestHelpers.Runtime`, assembly `Unity.Netcode.Runtime.Tests`) runs a server and clients in one process. It is coroutine-based (`[UnitySetUp]`), so it is shaped for PlayMode, and the assembly needs a `testables` entry in the manifest. No manual page documents it.
- `NetworkManager.NetworkUpdate(NetworkUpdateStage)` is public, so the stages can be stepped by hand; nothing documents doing so.

### Not verified (as of 2026-10-09)
- Whether 2.13.3 is released for 6000.3, and which transport version resolves.
- Any manual statement on: reader lifetime, where in the frame messages are dispatched, client id reuse, exactly-once delivery, the valid `TickRate` range.
- Managed allocations of NGO's full send and receive path.
- Whether `NetcodeIntegrationTest` ships in the registry package and whether it can run in EditMode.
- Everything that needs the package installed and a real run.

---

## Sources
- NGO manual, custom messages (2.13.3): https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.13/manual/advanced-topics/message-system/custom-messages.html
- NGO source, branch `develop-2.0.0`: https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/tree/develop-2.0.0/com.unity.netcode.gameobjects
- Unity 6000.3 manual, NGO package page: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.netcode.gameobjects.html
- Unity 6000.3 manual, Unity Transport package page: https://docs.unity3d.com/6000.3/Documentation/Manual/com.unity.transport.html
- Unity Transport manual, pipelines (2.6): https://docs.unity3d.com/Packages/com.unity.transport@2.6/manual/pipelines-usage.html
- https://docs-multiplayer.unity3d.com/netcode/current/about/
- https://docs-multiplayer.unity3d.com/netcode/current/learn/bossroom/
