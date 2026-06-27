# Auth Sidecar IPC Specification

> **Status**: Draft
> **Author**: Manuel Toscano + agents
> **Last Updated**: 2026-05-20
> **Type**: Architecture primitive — referenced by authentication.md

## Purpose

Specifies the IPC contract between the Unity headless game server and the .NET authentication sidecar. Resolves the architecture gap in `authentication.md` CR-AUTH-4 ("internal IPC channel, unspecified") by naming the transport, defining the thread model, capping concurrent auth requests, and deriving the relationship between auth throughput and `CONNECTING_TIMEOUT_SECONDS`.

---

## Transport

**Mechanism**: Named pipes (.NET `System.IO.Pipes`).

| Platform | Pipe path |
|---|---|
| Windows | `\\.\pipe\irongrind-auth` |
| Linux / macOS | `/tmp/irongrind-auth` (Unix domain socket mode via .NET named pipes) |

**Connection model**: Keep-alive — the game server opens one persistent named pipe connection to the sidecar at startup and reuses it for all auth requests. Per-request reconnects add ~5–10ms handshake overhead per call and are not acceptable at burst load.

**Protocol**: Request–response framing. Each message is length-prefixed: 4-byte little-endian `int` followed by the serialized payload. Message types are defined by `IAuthenticationService`.

---

## Thread Model and Interface

All calls from the game server to the sidecar MUST be `async/await`. The canonical `IAuthenticationService` signatures:

```csharp
Task<AuthResult>          AuthenticateAsync(ConnectionRequest request, CancellationToken ct = default)
Task<RegistrationResult>  RegisterAsync(RegistrationRequest request, CancellationToken ct = default)
Task<CharacterID>         GetCharacterIDAsync(AccountID accountId, CancellationToken ct = default)
```

The sidecar processes Argon2id computations at `parallelism=1` (one at a time by design). Requests queue at the pipe level. The game server's `async/await` call returns control to the caller while waiting; the connection handler thread is not blocked.

`GetCharacterIDAsync` is not an Argon2id operation — it is a fast in-memory or DB lookup. It does not count toward `MAX_AUTH_PENDING` and must complete within 5ms under normal conditions.

---

## Resource Exhaustion Cap — `MAX_AUTH_PENDING`

`MAX_AUTH_PENDING` is the maximum number of concurrent in-flight `AuthenticateAsync` or `RegisterAsync` requests the sidecar will accept. Default: **10**.

**Rejection at cap**: When `MAX_AUTH_PENDING` requests are in-flight, any additional call returns immediately with `AuthResult { Success=false, FailureReason=ServerError }`. The game server translates this to `LoginResult { success:false, errorCode:ServerError=255 }` and closes the client connection. `loginFailureCount` is NOT incremented. This is logged as a capacity event, not an account-level auth failure.

**Memory at cap**: Maximum concurrent Argon2id working memory = `MAX_AUTH_PENDING × 19 MiB = 190 MiB` at default `memorySize=19456 KB`. Sidecar process heap must be provisioned at ≥ 256 MiB.

> **Correction to `authentication.md` EC-AUTH-2**: The claim "completing and discarding is cheaper under attacker control" is only true when concurrent abandoned connections are bounded by `MAX_AUTH_PENDING`. Beyond that cap, additional connections are rejected immediately at zero compute cost. The cap is the primary DoS mitigation; the "complete and discard" policy handles sub-cap load. EC-AUTH-2 should be updated to reference `MAX_AUTH_PENDING` as the binding constraint.

---

## End-to-End Auth Latency Formula

```
E2E_auth_ms = pipe_request_overhead_ms
            + queue_wait_ms              // = queue_depth × argon2id_avg_ms
            + argon2id_compute_ms        // 200–400ms at default parameters
            + pipe_response_overhead_ms

Typical same-machine named pipe values:
  pipe_request_overhead_ms  ≈ 1–5 ms
  argon2id_compute_ms       ≈ 200–400 ms
  pipe_response_overhead_ms ≈ 1–5 ms
  queue_wait_ms at full cap  ≈ (MAX_AUTH_PENDING − 1) × argon2id_avg_ms
                             = 9 × 300 = 2,700 ms (worst case, 9 ahead in queue)

Worst-case E2E at MAX_AUTH_PENDING=10, argon2id_max=400ms:
  E2E_auth_ms ≤ 5 + (9 × 400) + 400 + 5 = 3,810ms ≈ 4 seconds
```

**Derived constraint on `CONNECTING_TIMEOUT_SECONDS`**:

```
min_CONNECTING_TIMEOUT_SECONDS = (MAX_AUTH_PENDING × argon2id_max_ms / 1000) + pipe_overhead_safety_margin_s
                                = (10 × 0.4) + 1.0 = 5.0 seconds minimum
```

Default `CONNECTING_TIMEOUT_SECONDS = 30s` satisfies this with 25 seconds of headroom.

**Re-derive this formula when any of the following change:** `MAX_AUTH_PENDING`, Argon2id `iterations`, or Argon2id `memorySize`.

---

## Health Check

The game server polls the sidecar health endpoint at startup and during runtime.

| Parameter | Default | Safe Range | Description |
|---|---|---|---|
| `SIDECAR_HEALTH_POLL_INTERVAL_SECONDS` | 5 | [1, 30] | Polling frequency |
| `SIDECAR_HEALTH_CHECK_TIMEOUT_SECONDS` | 3 | [1, 10] | Per-check timeout; must be < poll interval |
| `SIDECAR_HEALTH_FAILURE_THRESHOLD` | 3 | [1, 10] | Consecutive failures before gate closes |
| `SIDECAR_HEALTH_RECOVERY_THRESHOLD` | 2 | [1, 5] | Consecutive successes before gate reopens |

**Gate-closed behavior**: While the sidecar is unhealthy, `OnNewConnection` returns `ServerError=255` without allocating a session slot. `loginFailureCount` is NOT incremented. Logged as infrastructure event (not account-level failure), consistent with `authentication.md` EC-AUTH-10.

**Degraded sidecar** (responding but queue near `MAX_AUTH_PENDING`): The health endpoint should report a warning state when `current_auth_pending ≥ MAX_AUTH_PENDING × 0.8`. The game server does NOT close the gate on a warning — it is informational only. Full gate closure requires `SIDECAR_HEALTH_FAILURE_THRESHOLD` consecutive hard failures (no response within `SIDECAR_HEALTH_CHECK_TIMEOUT_SECONDS`).

---

## Startup Sequence

1. Sidecar process starts; initializes Argon2id parameters; computes one dummy hash for timing calibration and timing-oracle prevention (see `authentication.md` CR-AUTH-9 step 4 — the dummy hash is generated here, at startup, using the live parameters).
2. Sidecar opens named pipe listener.
3. Game server starts; begins polling sidecar health check.
4. After `SIDECAR_HEALTH_RECOVERY_THRESHOLD` (default: 2) consecutive healthy responses, game server opens the connection gate and begins accepting `OnNewConnection` events.
5. Typical gate-open delay after sidecar startup: < 10 seconds at default poll interval.

**Sidecar restart handling**: If the named pipe connection is lost while the game server is running, the game server closes the connection gate (treating it as consecutive health failures) and attempts to reconnect to the pipe on each poll cycle. In-flight `AuthenticateAsync` calls that were awaiting a response return `ServerError=255` via `CancellationToken` cancellation.

---

## Dependencies

| Document | Relationship |
|---|---|
| `authentication.md` | Parent GDD. This primitive is extracted from and supersedes CR-AUTH-4's "internal IPC channel" description. The auth GDD must reference this doc for the IPC contract. |
| `networking-session-token.md` | `ActiveSessionTokens` is in the game server process, not the sidecar. Token lookup (`ActiveSessionTokens[AccountID]`) does NOT cross the IPC boundary — only `AuthenticateAsync` and `RegisterAsync` do. |
| `auth-wire-messages.md` | Defines the message types carried over this IPC channel. |

---

## Tuning Knobs Summary

| Knob | Default | Safe Range | Re-derive constraint |
|---|---|---|---|
| `MAX_AUTH_PENDING` | 10 | [5, 50] | `min_CONNECTING_TIMEOUT_SECONDS` |
| `SIDECAR_HEALTH_POLL_INTERVAL_SECONDS` | 5 | [1, 30] | — |
| `SIDECAR_HEALTH_CHECK_TIMEOUT_SECONDS` | 3 | [1, 10] | Must be < poll interval |
| `SIDECAR_HEALTH_FAILURE_THRESHOLD` | 3 | [1, 10] | — |
| `SIDECAR_HEALTH_RECOVERY_THRESHOLD` | 2 | [1, 5] | — |
