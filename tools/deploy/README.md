# Zone launch deploy files

How the gateway starts zone servers on the VPS (ADR-016 Decision 3, ADR-007; Gateway Story 008). The gateway never runs
zones as its own child processes in production: it writes a small env file and asks systemd to start a template unit
instance. A zone is outside the gateway's control group, so restarting the gateway leaves zones running.

Not yet verified on the VPS. Installing these files and checking them is part of the VPS deploy work
(Authentication Story 017 or a later deploy story); record the results there.

## Files

| File | Installed as | Purpose |
|------|--------------|---------|
| `systemd/irongrind-zone@.service` | `/etc/systemd/system/irongrind-zone@.service` (root, 0644) | Template unit. `%i` = ZoneID. `Restart=no`. |
| `polkit/49-irongrind-gateway.rules` | `/etc/polkit-1/rules.d/49-irongrind-gateway.rules` (root, 0644) | Lets user `gateway` run `systemctl start irongrind-zone@<digits>.service`, nothing else. |
| `tmpfiles/irongrind.conf` | `/etc/tmpfiles.d/irongrind.conf` (root, 0644) | Creates `/run/irongrind` (0750, `gateway:gateway`) at every boot. |

## Start sequence

1. The warm-spare keeper picks a ZoneID and a UDP port and calls `IZoneLauncher.StartAsync`.
2. `SystemdZoneLauncher` writes `/run/irongrind/zone-<id>.env`, mode 0640, replacing any old file:
   ```
   ZONE_TEMPLATE=StartingZone
   ZONE_PORT=7801
   ```
   No token, password or connection string goes in this file or on the command line.
3. It runs `systemctl --no-ask-password start irongrind-zone@<id>.service` (killed after 10 s). A non-zero exit or a timeout becomes an exception with the exit code and
   stderr; the keeper logs it and retries after `ZONE_LOAD_TIMEOUT_SECONDS`.
4. systemd (as root) reads `/etc/irongrind/zone-secrets.env` and the per-zone file, drops to `irongrind-zone`, and runs
   `IronGrind.x86_64 ... -zoneId <id> -zoneTemplate <t> -port <p> -serverConfig /etc/irongrind/zone-server.json`.
5. The zone connects to the gateway link and sends `RegisterZone`; that, not the `systemctl` exit, means "ready".

## Gateway configuration

| Knob | Production | Dev |
|------|------------|-----|
| `ZONE_LAUNCHER` | `systemd` | `none` (default; start zones by hand or from the Editor) or `process` |
| `ZONE_PROCESS_PATH` | unused | zone build executable, required with `process` |
| `ZONE_SERVER_CONFIG` | unused (the unit names its path) | `-serverConfig` path, required with `process` |

An unknown `ZONE_LAUNCHER` value, or `process` without both paths, fails gateway startup. With `none`, a hand-started
zone passes its own `-zoneId`; ZoneID uniqueness is not guaranteed in dev (ADR-016).

## Install (proposed paths, confirm on the VPS)

```
sudo useradd --system --no-create-home --shell /usr/sbin/nologin gateway
sudo useradd --system --no-create-home --shell /usr/sbin/nologin irongrind-zone

sudo install -d -m 0755 /opt/irongrind/zone /etc/irongrind
# copy the Linux server build into /opt/irongrind/zone (IronGrind.x86_64 + IronGrind_Data)
sudo install -m 0640 -o root -g irongrind-zone zone-server.json /etc/irongrind/zone-server.json
sudo install -m 0600 -o root -g root /dev/null /etc/irongrind/zone-secrets.env   # then add IRONGRIND_PG_PASSWORD=...

sudo install -m 0644 tools/deploy/systemd/irongrind-zone@.service /etc/systemd/system/
sudo install -m 0644 tools/deploy/polkit/49-irongrind-gateway.rules /etc/polkit-1/rules.d/
sudo install -m 0644 tools/deploy/tmpfiles/irongrind.conf /etc/tmpfiles.d/
sudo systemd-tmpfiles --create irongrind.conf
sudo systemctl daemon-reload
```

The gateway's own unit (not in this folder yet) runs as `User=gateway` with `ZONE_LAUNCHER=systemd`.

## Check the polkit version first

JavaScript `.rules` files need polkit 0.106 or later. Ubuntu 22.04 ships polkit 0.105 (`policykit-1`), which reads only
`.pkla` files and ignores this rule; `.pkla` files cannot match a unit name, so they cannot express "start only
`irongrind-zone@*`". ADR-007 names Ubuntu 22.04 for the VPS. Check on the day:

```
pkaction --version
```

If it reports 0.105, choose one before the VPS story: use Ubuntu 24.04 (polkit 124), or let `gateway` run, through
sudoers, a small root-owned wrapper script that checks the ZoneID is digits only and then runs `systemctl start`
(the launcher would call `sudo -n <wrapper> <id>`, a code change). Do not put a `*` wildcard on a `systemctl` line in
sudoers: sudoers wildcards also match spaces, so extra unit names could be appended. Either choice needs an ADR-016 note.

## VPS verification checklist (for the deploy story)

- `sudo -u gateway systemctl start irongrind-zone@99999.service` succeeds (unit starts, or fails only because the env file is missing).
- `sudo -u gateway systemctl stop irongrind-zone@99999.service` and `... start ssh.service` are refused.
- A zone killed with `SIGKILL` stays dead (`Restart=no`).
- `systemctl restart <gateway unit>` leaves running zones running; they re-register.
- `/run/irongrind` exists after a reboot with mode 0750, owner `gateway`.
