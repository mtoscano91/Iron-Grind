using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using Npgsql;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// <see cref="ICharacterRecordStore"/> over PostgreSQL, through Npgsql and Dapper (Character Persistence Story 014;
    /// ADR-006 Decisions 2 and 4; GDD CR-CP-6, F-CP-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SQL.</b> The statements are explicit text constants in this file (ADR-006 Decision 2); Dapper only binds
    /// parameters and rows. The <c>uint</c> fields live in <c>BIGINT</c> columns and are range-checked back to
    /// <c>uint</c> on read; a value outside the range, or a slot document of the wrong shape, throws
    /// <see cref="CharacterRecordCorruptException"/>. The slot JSON is written and read by
    /// <see cref="CharacterSlotJsonCodec"/> (no reflection, no serialiser library).
    /// </para>
    /// <para>
    /// <b>Pool.</b> The store takes an <see cref="NpgsqlDataSource"/> and never builds, owns or disposes it. Each call
    /// opens a connection, uses it and returns it to the pool before the task completes; none is held between calls.
    /// </para>
    /// <para>
    /// <b>Cancellation.</b> The token reaches every Npgsql and Dapper async call, and every command — the load
    /// <c>SELECT</c> included — carries the <see cref="CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS"/>
    /// command timeout as a backstop. The service's write timeout, which cancels the token, must not be longer than that
    /// constant, or Npgsql's own command timeout fires first and the service sees an ordinary fault instead of its
    /// timeout. A token that is already cancelled ends the call before a connection is opened. Every <c>await</c> uses
    /// <c>ConfigureAwait(false)</c> (ADR-011), and the record is copied into parameters before the first
    /// <c>await</c>, on the calling thread. The store reads no configuration and holds no connection string.
    /// </para>
    /// <para>
    /// <b>Disposal.</b> A failure while returning the connection or ending the transaction is logged as a warning and
    /// never replaces the call's result: a committed update is reported as committed.
    /// </para>
    /// <para>
    /// <b>Stripping.</b> <see cref="RecordRow"/>, <see cref="RecordParameters"/> and <see cref="IdParameters"/> are built
    /// and read only by Dapper, through reflection; <c>Assets/link.xml</c> preserves them.
    /// </para>
    /// </remarks>
    public sealed class NpgsqlCharacterRecordStore : ICharacterRecordStore
    {
        private const int COMMAND_TIMEOUT_SECONDS = CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS;

        private const string SELECT_SQL =
            "SELECT character_id AS CharacterId, account_id AS AccountId, character_name AS CharacterName, "
            + "class_type AS ClassType, level AS Level, experience AS Experience, held_free_points AS HeldFreePoints, "
            + "str AS Str, dex AS Dex, vit AS Vit, int_stat AS IntStat, max_hp AS MaxHp, max_mp AS MaxMp, "
            + "attack_power AS AttackPower, defense AS Defense, magic_defense AS MagicDefense, "
            + "crit_chance AS CritChance, crit_multiplier AS CritMultiplier, attack_range AS AttackRange, "
            + "attack_speed_multiplier AS AttackSpeedMultiplier, movement_speed AS MovementSpeed, "
            + "current_hp AS CurrentHp, current_mp AS CurrentMp, gold_balance AS GoldBalance, "
            + "gold_version AS GoldVersion, last_zone_id AS LastZoneId, gear_slots::text AS GearSlotsJson, "
            + "inventory_slots::text AS InventorySlotsJson, save_version AS SaveVersion, "
            + "created_at_utc AS CreatedAtUtc, last_saved_at_utc AS LastSavedAtUtc "
            + "FROM character_records WHERE character_id = @CharacterId";

        private const string INSERT_SQL =
            "INSERT INTO character_records ("
            + "character_id, account_id, character_name, class_type, level, experience, held_free_points, "
            + "str, dex, vit, int_stat, max_hp, max_mp, attack_power, defense, magic_defense, "
            + "crit_chance, crit_multiplier, attack_range, attack_speed_multiplier, movement_speed, "
            + "current_hp, current_mp, gold_balance, gold_version, last_zone_id, gear_slots, inventory_slots, "
            + "save_version, created_at_utc, last_saved_at_utc) VALUES ("
            + "@CharacterId, @AccountId, @CharacterName, @ClassType, @Level, @Experience, @HeldFreePoints, "
            + "@Str, @Dex, @Vit, @IntStat, @MaxHp, @MaxMp, @AttackPower, @Defense, @MagicDefense, "
            + "@CritChance, @CritMultiplier, @AttackRange, @AttackSpeedMultiplier, @MovementSpeed, "
            + "@CurrentHp, @CurrentMp, @GoldBalance, @GoldVersion, @LastZoneId, "
            + "@GearSlotsJson::jsonb, @InventorySlotsJson::jsonb, @SaveVersion, NOW(), NOW()) "
            + "ON CONFLICT (character_id) DO NOTHING";

        // ADR-006 Decision 2 "Key Interfaces": the optimistic-concurrency UPDATE. character_name, account_id and
        // class_type are not changed by a save, as in the ADR's text.
        private const string UPDATE_SQL =
            "UPDATE character_records "
            + "SET level = @Level, experience = @Experience, held_free_points = @HeldFreePoints, "
            + "str = @Str, dex = @Dex, vit = @Vit, int_stat = @IntStat, "
            + "max_hp = @MaxHp, max_mp = @MaxMp, attack_power = @AttackPower, "
            + "defense = @Defense, magic_defense = @MagicDefense, "
            + "crit_chance = @CritChance, crit_multiplier = @CritMultiplier, "
            + "attack_range = @AttackRange, attack_speed_multiplier = @AttackSpeedMultiplier, "
            + "movement_speed = @MovementSpeed, current_hp = @CurrentHp, current_mp = @CurrentMp, "
            + "gold_balance = @GoldBalance, gold_version = @GoldVersion, last_zone_id = @LastZoneId, "
            + "gear_slots = @GearSlotsJson::jsonb, inventory_slots = @InventorySlotsJson::jsonb, "
            + "save_version = save_version + 1, last_saved_at_utc = NOW() "
            + "WHERE character_id = @CharacterId AND save_version = @ExpectedVersion";

        private readonly NpgsqlDataSource _dataSource;

        /// <summary>One row of <c>character_records</c> as Dapper reads it; the property names are the SELECT aliases.</summary>
        internal sealed class RecordRow
        {
            /// <summary>character_id.</summary>
            public long CharacterId { get; set; }

            /// <summary>account_id.</summary>
            public long AccountId { get; set; }

            /// <summary>character_name.</summary>
            public string CharacterName { get; set; }

            /// <summary>class_type (SMALLINT).</summary>
            public short ClassType { get; set; }

            /// <summary>level.</summary>
            public int Level { get; set; }

            /// <summary>experience.</summary>
            public int Experience { get; set; }

            /// <summary>held_free_points.</summary>
            public int HeldFreePoints { get; set; }

            /// <summary>str.</summary>
            public int Str { get; set; }

            /// <summary>dex.</summary>
            public int Dex { get; set; }

            /// <summary>vit.</summary>
            public int Vit { get; set; }

            /// <summary>int_stat.</summary>
            public int IntStat { get; set; }

            /// <summary>max_hp.</summary>
            public int MaxHp { get; set; }

            /// <summary>max_mp.</summary>
            public int MaxMp { get; set; }

            /// <summary>attack_power.</summary>
            public int AttackPower { get; set; }

            /// <summary>defense.</summary>
            public int Defense { get; set; }

            /// <summary>magic_defense.</summary>
            public int MagicDefense { get; set; }

            /// <summary>crit_chance (REAL).</summary>
            public float CritChance { get; set; }

            /// <summary>crit_multiplier (REAL).</summary>
            public float CritMultiplier { get; set; }

            /// <summary>attack_range (REAL).</summary>
            public float AttackRange { get; set; }

            /// <summary>attack_speed_multiplier (REAL).</summary>
            public float AttackSpeedMultiplier { get; set; }

            /// <summary>movement_speed (REAL).</summary>
            public float MovementSpeed { get; set; }

            /// <summary>current_hp (REAL).</summary>
            public float CurrentHp { get; set; }

            /// <summary>current_mp (REAL).</summary>
            public float CurrentMp { get; set; }

            /// <summary>gold_balance.</summary>
            public long GoldBalance { get; set; }

            /// <summary>gold_version.</summary>
            public long GoldVersion { get; set; }

            /// <summary>last_zone_id.</summary>
            public long LastZoneId { get; set; }

            /// <summary>gear_slots as JSON text.</summary>
            public string GearSlotsJson { get; set; }

            /// <summary>inventory_slots as JSON text.</summary>
            public string InventorySlotsJson { get; set; }

            /// <summary>save_version.</summary>
            public long SaveVersion { get; set; }

            /// <summary>created_at_utc.</summary>
            public DateTime CreatedAtUtc { get; set; }

            /// <summary>last_saved_at_utc.</summary>
            public DateTime LastSavedAtUtc { get; set; }
        }

        /// <summary>The parameter of the fetch statement (a named type, so <c>Assets/link.xml</c> can preserve it).</summary>
        internal sealed class IdParameters
        {
            /// <summary>character_id.</summary>
            public long CharacterId { get; set; }
        }

        /// <summary>The parameters of the insert and update statements; a copy of the record taken on the calling thread.</summary>
        internal sealed class RecordParameters
        {
            /// <summary>character_id.</summary>
            public long CharacterId { get; set; }

            /// <summary>account_id.</summary>
            public long AccountId { get; set; }

            /// <summary>character_name.</summary>
            public string CharacterName { get; set; }

            /// <summary>class_type (SMALLINT).</summary>
            public short ClassType { get; set; }

            /// <summary>level.</summary>
            public int Level { get; set; }

            /// <summary>experience.</summary>
            public int Experience { get; set; }

            /// <summary>held_free_points.</summary>
            public int HeldFreePoints { get; set; }

            /// <summary>str.</summary>
            public int Str { get; set; }

            /// <summary>dex.</summary>
            public int Dex { get; set; }

            /// <summary>vit.</summary>
            public int Vit { get; set; }

            /// <summary>int_stat.</summary>
            public int IntStat { get; set; }

            /// <summary>max_hp.</summary>
            public int MaxHp { get; set; }

            /// <summary>max_mp.</summary>
            public int MaxMp { get; set; }

            /// <summary>attack_power.</summary>
            public int AttackPower { get; set; }

            /// <summary>defense.</summary>
            public int Defense { get; set; }

            /// <summary>magic_defense.</summary>
            public int MagicDefense { get; set; }

            /// <summary>crit_chance (REAL).</summary>
            public float CritChance { get; set; }

            /// <summary>crit_multiplier (REAL).</summary>
            public float CritMultiplier { get; set; }

            /// <summary>attack_range (REAL).</summary>
            public float AttackRange { get; set; }

            /// <summary>attack_speed_multiplier (REAL).</summary>
            public float AttackSpeedMultiplier { get; set; }

            /// <summary>movement_speed (REAL).</summary>
            public float MovementSpeed { get; set; }

            /// <summary>current_hp (REAL).</summary>
            public float CurrentHp { get; set; }

            /// <summary>current_mp (REAL).</summary>
            public float CurrentMp { get; set; }

            /// <summary>gold_balance.</summary>
            public long GoldBalance { get; set; }

            /// <summary>gold_version.</summary>
            public long GoldVersion { get; set; }

            /// <summary>last_zone_id.</summary>
            public long LastZoneId { get; set; }

            /// <summary>save_version (the insert writes it; the update ignores it).</summary>
            public long SaveVersion { get; set; }

            /// <summary>The version the update's predicate compares to.</summary>
            public long ExpectedVersion { get; set; }

            /// <summary>gear_slots as JSON text.</summary>
            public string GearSlotsJson { get; set; }

            /// <summary>inventory_slots as JSON text.</summary>
            public string InventorySlotsJson { get; set; }
        }

        /// <summary>Creates the store over a pooled data source.</summary>
        /// <param name="dataSource">The pooled data source; not null; never disposed here.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public NpgsqlCharacterRecordStore(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <inheritdoc/>
        /// <exception cref="CharacterRecordCorruptException">A column or a slot document holds a value the record cannot take.</exception>
        public async Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            RecordRow row;
            try
            {
                var command = new CommandDefinition(
                    SELECT_SQL,
                    new IdParameters { CharacterId = id.RawValue },
                    commandTimeout: COMMAND_TIMEOUT_SECONDS,
                    cancellationToken: ct);
                row = await connection.QuerySingleOrDefaultAsync<RecordRow>(command).ConfigureAwait(false);
            }
            finally
            {
                await DisposeQuietlyAsync(connection, "connection").ConfigureAwait(false);
            }

            return row == null ? null : ToRecord(row);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="record"/> is null — thrown synchronously, as by
        /// <see cref="InMemoryCharacterRecordStore"/>; every other failure comes back as a faulted task.</exception>
        public Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            RecordParameters parameters;
            try
            {
                parameters = ToParameters(record, 0u);
            }
            catch (Exception e)
            {
                return Task.FromException<bool>(e);
            }

            return InsertCoreAsync(parameters, ct);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="record"/> is null — thrown synchronously, as by
        /// <see cref="InMemoryCharacterRecordStore"/>; every other failure comes back as a faulted task.</exception>
        public Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            RecordParameters parameters;
            try
            {
                parameters = ToParameters(record, expectedSaveVersion);
            }
            catch (Exception e)
            {
                return Task.FromException<int>(e);
            }

            return UpdateCoreAsync(parameters, ct);
        }

        private async Task<bool> InsertCoreAsync(RecordParameters parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            int rows;
            try
            {
                var command = new CommandDefinition(
                    INSERT_SQL,
                    parameters,
                    commandTimeout: COMMAND_TIMEOUT_SECONDS,
                    cancellationToken: ct);
                rows = await connection.ExecuteAsync(command).ConfigureAwait(false);
            }
            finally
            {
                await DisposeQuietlyAsync(connection, "connection").ConfigureAwait(false);
            }

            return rows == 1;
        }

        private async Task<int> UpdateCoreAsync(RecordParameters parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
            int rows;
            try
            {
                // One statement in one transaction (CR-CP-6). Disposing the transaction without a commit rolls it back.
                var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
                try
                {
                    var command = new CommandDefinition(
                        UPDATE_SQL,
                        parameters,
                        transaction,
                        COMMAND_TIMEOUT_SECONDS,
                        cancellationToken: ct);
                    rows = await connection.ExecuteAsync(command).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    await DisposeQuietlyAsync(transaction, "transaction").ConfigureAwait(false);
                }
            }
            finally
            {
                await DisposeQuietlyAsync(connection, "connection").ConfigureAwait(false);
            }

            // After a successful commit: a failure while disposing above cannot turn it into a fault.
            return rows;
        }

        // Disposes without letting a failure replace the call's own result or exception (after a cancel the connection
        // may be broken, and closing it is where that shows).
        private static async Task DisposeQuietlyAsync(IAsyncDisposable disposable, string what)
        {
            try
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[CharacterPersistence] Disposing the Npgsql " + what + " failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static RecordParameters ToParameters(CharacterRecord r, uint expectedSaveVersion)
        {
            // The JSON encoding is the copy of the slot arrays: it runs here, on the calling thread, before any await.
            return new RecordParameters
            {
                CharacterId = r.CharacterID.RawValue,
                AccountId = r.AccountID.RawValue,
                CharacterName = r.CharacterName,
                ClassType = r.ClassType,
                Level = r.Level,
                Experience = r.Experience,
                HeldFreePoints = r.HeldFreePoints,
                Str = r.STR,
                Dex = r.DEX,
                Vit = r.VIT,
                IntStat = r.INT,
                MaxHp = r.MaxHP,
                MaxMp = r.MaxMP,
                AttackPower = r.AttackPower,
                Defense = r.Defense,
                MagicDefense = r.MagicDefense,
                CritChance = r.CritChance,
                CritMultiplier = r.CritMultiplier,
                AttackRange = r.AttackRange,
                AttackSpeedMultiplier = r.AttackSpeedMultiplier,
                MovementSpeed = r.MovementSpeed,
                CurrentHp = r.CurrentHP,
                CurrentMp = r.CurrentMP,
                GoldBalance = r.GoldBalance,
                GoldVersion = r.GoldVersion,
                LastZoneId = r.LastZoneID.RawValue,
                SaveVersion = r.SaveVersion,
                ExpectedVersion = expectedSaveVersion,
                GearSlotsJson = CharacterSlotJsonCodec.EncodeGearSlots(r.GearSlots),
                InventorySlotsJson = CharacterSlotJsonCodec.EncodeInventorySlots(r.InventorySlots, r.InventoryItemCounts, r.InventoryEnhancementLevels),
            };
        }

        private static CharacterRecord ToRecord(RecordRow row)
        {
            var r = new CharacterRecord
            {
                CharacterID = new CharacterID(ToUInt(row.CharacterId, "character_id")),
                AccountID = new AccountID(ToUInt(row.AccountId, "account_id")),
                CharacterName = row.CharacterName,
                ClassType = ToByte(row.ClassType, "class_type"),
                Level = row.Level,
                Experience = row.Experience,
                HeldFreePoints = row.HeldFreePoints,
                STR = row.Str,
                DEX = row.Dex,
                VIT = row.Vit,
                INT = row.IntStat,
                MaxHP = row.MaxHp,
                MaxMP = row.MaxMp,
                AttackPower = row.AttackPower,
                Defense = row.Defense,
                MagicDefense = row.MagicDefense,
                CritChance = row.CritChance,
                CritMultiplier = row.CritMultiplier,
                AttackRange = row.AttackRange,
                AttackSpeedMultiplier = row.AttackSpeedMultiplier,
                MovementSpeed = row.MovementSpeed,
                CurrentHP = row.CurrentHp,
                CurrentMP = row.CurrentMp,
                GoldBalance = ToUInt(row.GoldBalance, "gold_balance"),
                GoldVersion = ToUInt(row.GoldVersion, "gold_version"),
                LastZoneID = new ZoneID(ToUInt(row.LastZoneId, "last_zone_id")),
                SaveVersion = ToUInt(row.SaveVersion, "save_version"),
                CreatedAtUtc = ToUtc(row.CreatedAtUtc),
                LastSavedAtUtc = ToUtc(row.LastSavedAtUtc),
            };
            CharacterSlotJsonCodec.DecodeGearSlots(row.GearSlotsJson, r.GearSlots);
            CharacterSlotJsonCodec.DecodeInventorySlots(row.InventorySlotsJson, r.InventorySlots, r.InventoryItemCounts, r.InventoryEnhancementLevels);
            return r;
        }

        private static uint ToUInt(long value, string column)
        {
            if (value < 0L || value > uint.MaxValue)
            {
                throw new CharacterRecordCorruptException("Column " + column + " holds " + value + ", outside the uint range.");
            }

            return (uint)value;
        }

        private static byte ToByte(short value, string column)
        {
            if (value < 0 || value > byte.MaxValue)
            {
                throw new CharacterRecordCorruptException("Column " + column + " holds " + value + ", outside the byte range.");
            }

            return (byte)value;
        }

        private static DateTime ToUtc(DateTime value)
        {
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    return value;
                case DateTimeKind.Local:
                    return value.ToUniversalTime();
                default:
                    return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }
    }
}
