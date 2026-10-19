# Story 012 - Npgsql and Dapper Verification (a) to (d)

> **Story**: `production/epics/character-persistence/story-012-npgsql-and-dapper-in-the-project.md`
> **Run file**: `tests/EditMode/CharacterPersistence/CharacterPersistence_NpgsqlRoundTrip_tests.cs`; test `test_select_one_through_dapper_returns_one` (`[Explicit]`, category `Database`)
> **Code under test**: `src/ServerLogic/CharacterPersistence/NpgsqlRoundTripProbe.cs`; DLL set in `docs/server-libraries.md`
> **Date**: 2026-10-19
> **Status**: RUN 2026-10-19. (a) PASS; (b), (c), (d) NOT RUN (user decision 2026-10-16: they do not block `/story-done` and are carried by the epic's headless-build gate). One deviation: (a) ran against a portable PostgreSQL, not the Docker container (see Deviation).

## How it is run

- Unity 6000.3.10f1 in batch mode from the Bash tool (Editor closed), EditMode, `-testFilter IronGrind.Tests.EditMode.CharacterPersistence.CharacterPersistence_NpgsqlRoundTrip_Tests.test_select_one_through_dapper_returns_one` (a filter on the full name is what selects an `[Explicit]` test).
- `IRONGRIND_DEV_PG` passed to the Unity process only, read from the User environment; the password is never printed.
- Server: PostgreSQL 16.15 (`SELECT version()`: "PostgreSQL 16.15, compiled by Visual C++ build 1944, 64-bit"), listening on 127.0.0.1:5432 only (`SHOW listen_addresses` = `127.0.0.1`), SCRAM-SHA-256 authentication, role and database `irongrind`. Set up as `tools/postgres/README.md` "Without Docker" describes.

## (a) Editor round trip

**What is run**: `NpgsqlRoundTripProbe.SelectOne`: `NpgsqlDataSource.Create(connectionString)`, `OpenConnection()`, Dapper `ExecuteScalar<int>("SELECT 1")`, both disposed.

**Observed**:
- First run (12:29:18Z): FAILED, `Npgsql.NpgsqlException : Failed to connect to 127.0.0.1:5432 ---> SocketException: ... actively refused it`. The server was not running: `postgres.log` shows it shut down at 09:27:05 -03 after `server process ... was terminated by exception 0xC0000142` (a DLL failed to initialise), when the Git Bash console that had started it was closed. Not a library fault: Npgsql loaded, built its pool and attempted the connection. The server was restarted from PowerShell `Start-Process -WindowStyle Hidden` (own console) and recovered cleanly.
- Second run: `test-run` start 2026-10-19 12:32:07Z, **total 1, passed 1, failed 0**, test duration 0.28 s, Unity exit code 0.
- After the run: `SELECT count(*) FROM pg_stat_activity WHERE usename='irongrind'` = **0** (no connection left open; the data source's disposal closed its pool).

**Result**: PASS

## (b) ADR-006 Verification 1: Npgsql on a headless Linux server build

**Result**: NOT RUN. The project has no headless Linux server build yet (the same open gate as Networking Core's). Carried by the epic.

## (c) ADR-006 Verification 2: Dapper under IL2CPP

**Result**: NOT RUN. Needs an IL2CPP player build. `Assets/link.xml` has the two `preserve="all"` entries (Npgsql, Dapper). Carried by the epic.

## (d) ADR-006 Verification 3: the pool across start and shutdown of the headless process

**Result**: NOT RUN. Needs (b). The Editor run above only shows one open/close cycle releasing its connection. Carried by the epic.

## Deviation: no Docker on the development machine

- The story's Development PostgreSQL criterion runs (a) against `tools/postgres/docker-compose.yml`. On 2026-10-19 the user cannot use the Docker Desktop app and the machine has no WSL, so the Docker engine cannot run. `docker-compose.yml` and `README.md` are written and committed but **have not been run on this machine**. `tools/postgres/.env` is ignored by git (`git check-ignore -v`: `.gitignore:117:tools/postgres/.env`).
- Instead (user decision 2026-10-19, option 1 of three: portable binaries, the EDB installer, a hosted instance): the EnterpriseDB PostgreSQL 16.15 Windows x64 binaries zip, unpacked under `%LOCALAPPDATA%\IronGrind\` (outside the repository), no admin rights, no Windows service.
  - Download: `https://sbp.enterprisedb.com/getfile.jsp?fileid=1260623` (linked from `https://www.enterprisedb.com/download-postgresql-binaries`), over HTTPS.
  - SHA-256 of the zip: `43bb45f173a6f08cf1d29a97a6d8deb119e8e8093a24c00d2d1001a0ccaa8281`.
  - **EDB publishes no checksum for the zip, and its binaries are not Authenticode-signed** (`Get-AuthenticodeSignature`: `NotSigned` for `postgres.exe`, `pg_ctl.exe`, `psql.exe`, `initdb.exe`, `libpq.dll`). The download's integrity rests on HTTPS from EDB's own link only. The signed EDB installer is the alternative if that is not enough.
- The server version (16) matches the compose file's `postgres:16`; Npgsql 8 supports 12 and later.
