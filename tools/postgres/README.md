# Development PostgreSQL

A local `postgres:16` container bound to `127.0.0.1:5432` for Character Persistence work
(ADR-006, Story 012).

1. Copy `tools/postgres/.env.example` to `tools/postgres/.env` and set a password. The `.env` file is git-ignored.
2. Start it from the repository root:

   ```
   docker compose -f tools/postgres/docker-compose.yml up -d
   ```

   `docker compose -f tools/postgres/docker-compose.yml ps` shows `healthy` when it is ready.
3. Set the environment variable `IRONGRIND_DEV_PG` to the connection string in the last comment of `.env.example`
   (with your password), then start Unity from that shell or restart it so the Editor sees the variable.
4. In the Unity Test Runner (EditMode), run the `Database` category: `test_select_one_through_dapper_returns_one`.
   Without the variable the test is ignored.

Stop with `docker compose -f tools/postgres/docker-compose.yml down` (data stays in the `irongrind-pg-data` volume;
add `-v` to delete it).

## Without Docker (portable binaries, Windows)

Docker on Windows needs Docker Desktop or WSL. Without them, use the EnterpriseDB PostgreSQL 16 Windows x64
binaries zip (https://www.enterprisedb.com/download-postgresql-binaries). No admin rights, no Windows service, nothing
in the repository. EDB publishes no checksum for the zip and the binaries are not code-signed; use the EDB installer
if you need signed binaries. Record the zip's SHA-256.

1. Unpack it (it holds a `pgsql/` folder) to `%LOCALAPPDATA%\IronGrind\pgsql16`.
2. Create the cluster once, listening on 127.0.0.1 only:

   ```
   initdb -D %LOCALAPPDATA%\IronGrind\pgdata -U postgres --auth=scram-sha-256 --pwfile=<file with the postgres password> -E UTF8 --no-locale
   ```

   then append `listen_addresses = '127.0.0.1'` and `port = 5432` to `pgdata\postgresql.conf`.
3. Start it from PowerShell, in its own hidden console:

   ```
   Start-Process -WindowStyle Hidden -FilePath "$env:LOCALAPPDATA\IronGrind\pgsql16\bin\pg_ctl.exe" -ArgumentList 'start','-D',"`"$env:LOCALAPPDATA\IronGrind\pgdata`"",'-l',"`"$env:LOCALAPPDATA\IronGrind\postgres.log`"",'-w'
   ```

   **Do not start it from a Git Bash or other console you will close**: its server processes die with exception
   0xC0000142 when that console goes away. Do not pipe `pg_ctl start` into another command either (the server holds
   the pipe open). It does not restart after a reboot; run the command again.
4. As `postgres`, `CREATE ROLE irongrind LOGIN PASSWORD '...'` and `CREATE DATABASE irongrind OWNER irongrind`.
5. Set `IRONGRIND_DEV_PG` as in step 3 above (`setx` for the user environment; programs see it after a restart).
6. Run the round trip: in the Test Runner (after restarting Unity) as in step 4 above, or with the Editor closed, in batch
   mode (a filter on the full name selects the Explicit test):

   ```
   Unity.exe -batchmode -projectPath <repo> -runTests -testPlatform EditMode -testFilter IronGrind.Tests.EditMode.CharacterPersistence.CharacterPersistence_NpgsqlRoundTrip_Tests.test_select_one_through_dapper_returns_one -testResults <file> -logFile <file>
   ```

Stop with `pg_ctl -D %LOCALAPPDATA%\IronGrind\pgdata stop`.
