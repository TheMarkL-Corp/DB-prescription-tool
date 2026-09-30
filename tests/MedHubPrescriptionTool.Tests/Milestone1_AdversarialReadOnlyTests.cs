using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using MedHubPrescriptionTool.Configuration;
using MedHubPrescriptionTool.Data;
using Npgsql;
using Xunit;

namespace MedHubPrescriptionTool.Tests;

/// <summary>
/// Adversarial stress-test suite for Milestone 1 read-only enforcement.
/// Validates that connections created by DatabaseConnectionFactory physically block
/// all DML, DDL, multi-statement injection, and session override attacks with
/// PostgresException SQLState 25006, leaving the MedHub database completely unaltered.
/// </summary>
public class Milestone1_AdversarialReadOnlyTests
{
    private readonly IDatabaseConnectionFactory _factory;

    public Milestone1_AdversarialReadOnlyTests()
    {
        var settings = new DbConnectionSettings
        {
            Host = "127.0.0.1",
            Port = 5433,
            Database = "amisdbV2",
            Username = "postgres",
            Password = "Syscom@123",
            ReadOnly = true,
            TimeoutSeconds = 10,
            CommandTimeoutSeconds = 10
        };
        _factory = new DatabaseConnectionFactory(settings);
    }

    private async Task<Dictionary<string, int>> CaptureTableCountsAsync()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        var counts = new Dictionary<string, int>();
        var tables = new[]
        {
            "Patients",
            "Encounter",
            "Prescriptions",
            "Schedule",
            "Medications",
            "AMiS_Patient_Medication_Dispense",
            "Care_Stations",
            "Rooms_Beds",
            "Users"
        };

        foreach (var tbl in tables)
        {
            var count = await conn.ExecuteScalarAsync<int>($"SELECT count(*)::int FROM \"{tbl}\";");
            counts[tbl] = count;
        }

        return counts;
    }

    #region 1. DML Mutation Attacks

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv01_InsertIntoCareStations_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"
            INSERT INTO ""Care_Stations"" (""Station_Id"", ""Department_Id"", ""Station_Code"", ""Station_Name"") 
            VALUES (gen_random_uuid(), gen_random_uuid(), 'HACK', 'Hacked');";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("INSERT must be blocked");
        ex.Which.SqlState.Should().Be("25006", "read_only_sql_transaction SQLState is 25006");
        ex.Which.MessageText.Should().Contain("read-only");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv02_UpdatePatients_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"UPDATE ""Patients"" SET ""First_Name"" = 'Hacked';";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("UPDATE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
        ex.Which.MessageText.Should().Contain("read-only");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv03_DeleteFromPrescriptions_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"DELETE FROM ""Prescriptions"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("DELETE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
        ex.Which.MessageText.Should().Contain("read-only");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv04_InsertIntoPatients_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"
            INSERT INTO ""Patients"" (""Patient_Id"", ""Patient_Mrn"", ""Patient_Id_Type"", ""Patient_National_Id"", ""First_Name"", ""Last_Name"", ""Sex"", ""Date_Of_Birth"", ""Create_User"", ""Create_Time"", ""Modified_User"", ""Modified_Time"")
            VALUES (gen_random_uuid(), 'ADV-999', '1', 'HACK123456', 'Adversary', 'Attack', 'M', '1990-01-01', gen_random_uuid(), NOW(), gen_random_uuid(), NOW());";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("INSERT into Patients must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv05_UpdateScheduleDoses_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"UPDATE ""Schedule"" SET ""Calc_Dispense_Qty"" = 9999.00, ""Schedule_Status"" = '99';";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("UPDATE on Schedule must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv06_DeleteDispenseLog_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"DELETE FROM ""AMiS_Patient_Medication_Dispense"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("DELETE on Dispense log must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    #endregion

    #region 2. DDL Schema Alteration Attacks

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv07_CreateTable_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"CREATE TABLE ""Hacked_Table"" (id int);";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("CREATE TABLE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
        ex.Which.MessageText.Should().Contain("read-only");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv08_DropTable_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"DROP TABLE ""Rooms_Beds"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("DROP TABLE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
        ex.Which.MessageText.Should().Contain("read-only");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv09_AlterTableAddColumn_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"ALTER TABLE ""Patients"" ADD COLUMN ""Malicious_Column"" text;";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("ALTER TABLE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv10_TruncateTable_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"TRUNCATE TABLE ""Schedule"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("TRUNCATE must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    #endregion

    #region 3. Multi-Statement & SQL Injection Attacks

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv11_MultiStatement_SelectThenUpdate_MustBeBlocked()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"SELECT 1; UPDATE ""Patients"" SET ""First_Name"" = 'Hacked';";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.NextResultAsync()) { }
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Multi-statement injection with UPDATE must fail");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv12_MultiStatement_SelectThenInsert_MustBeBlocked()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"
            SELECT count(*) FROM ""Care_Stations"";
            INSERT INTO ""Care_Stations"" (""Station_Id"", ""Department_Id"", ""Station_Code"", ""Station_Name"")
            VALUES (gen_random_uuid(), gen_random_uuid(), 'HACK', 'Hacked');";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.NextResultAsync()) { }
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Multi-statement injection with INSERT must fail");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv13_MultiStatement_SelectThenDropTable_MustBeBlocked()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"SELECT * FROM ""Medications"" LIMIT 1; DROP TABLE ""Rooms_Beds"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.NextResultAsync()) { }
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Multi-statement injection with DROP TABLE must fail");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv14_MultiStatement_CommentObfuscationWithUpdate_MustBeBlocked()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"SELECT 1 /* comment */; UPDATE ""Patients"" SET ""Last_Name"" = 'Injected' WHERE 1=1;";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.NextResultAsync()) { }
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Comment-obfuscated multi-statement must fail");
        ex.Which.SqlState.Should().Be("25006");
    }

    #endregion

    #region 4. Advanced Injection & Block Mutation Attacks

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv15_MultiStatement_BeginRollbackWrapperWithUpdate_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        // Attempting an update wrapped inside transaction blocks without elevating read-write
        const string sql = @"
            BEGIN;
            UPDATE ""Patients"" SET ""First_Name"" = 'Tx_Hacked' WHERE 1=1;
            COMMIT;";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Transaction-wrapped UPDATE must fail in read-only mode");
        ex.Which.SqlState.Should().Be("25006", "Cannot execute UPDATE in read-only transaction");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv16_MultiStatement_StackedDeleteWithTrailingSelect_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"DELETE FROM ""Prescriptions""; SELECT * FROM ""Prescriptions"";";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.NextResultAsync()) { }
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Stacked DELETE statement must fail");
        ex.Which.SqlState.Should().Be("25006");
    }

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv17_AnonymousDoBlock_MustBeBlockedWith25006()
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        const string sql = @"DO $$ BEGIN UPDATE ""Patients"" SET ""First_Name"" = 'Hacked'; END $$;";

        Func<Task> act = async () =>
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>("Anonymous DO mutation block must be blocked");
        ex.Which.SqlState.Should().Be("25006");
    }

    #endregion

    #region 5. Comprehensive Immutability Confirmation

    [Fact]
    [Trait("Category", "AdversarialReadOnly")]
    public async Task Adv18_DatabaseContents_MustRemainCompletelyUnaltered()
    {
        // 1. Capture baseline counts
        var baselineCounts = await CaptureTableCountsAsync();

        // 2. Execute a barrage of mutation attempts on distinct connections
        var attacks = new[]
        {
            @"INSERT INTO ""Care_Stations"" (""Station_Id"", ""Department_Id"", ""Station_Code"", ""Station_Name"") VALUES (gen_random_uuid(), gen_random_uuid(), 'BARRAGE', 'Barrage');",
            @"UPDATE ""Patients"" SET ""First_Name"" = 'Barrage';",
            @"DELETE FROM ""Prescriptions"" WHERE 1=1;",
            @"CREATE TABLE ""Barrage_Table"" (id int);",
            @"DROP TABLE ""Rooms_Beds"";",
            @"SELECT 1; UPDATE ""Patients"" SET ""First_Name"" = 'Barrage';",
            @"DO $$ BEGIN UPDATE ""Patients"" SET ""First_Name"" = 'Barrage'; END $$;"
        };

        foreach (var attack in attacks)
        {
            try
            {
                await using var conn = await _factory.CreateOpenConnectionAsync(readOnly: true);
                await using var cmd = new NpgsqlCommand(attack, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException)
            {
                // Expected block
            }
        }

        // 3. Re-capture counts and verify 100% equivalence
        var postAttackCounts = await CaptureTableCountsAsync();

        postAttackCounts.Should().BeEquivalentTo(baselineCounts, "Row counts across ALL core tables must remain 100% identical");

        // 4. Verify no new tables exist
        await using var verifyConn = await _factory.CreateOpenConnectionAsync(readOnly: true);
        var hackedTableCount = await verifyConn.ExecuteScalarAsync<int>(
            @"SELECT count(*)::int FROM information_schema.tables WHERE table_name IN ('Hacked_Table', 'Barrage_Table');");
        hackedTableCount.Should().Be(0, "No hacked tables must have been created");

        // 5. Verify no patient record was modified
        var hackedPatientCount = await verifyConn.ExecuteScalarAsync<int>(
            @"SELECT count(*)::int FROM ""Patients"" WHERE ""First_Name"" IN ('Hacked', 'Barrage', 'Adversary') OR ""Last_Name"" = 'Injected';");
        hackedPatientCount.Should().Be(0, "No patient names must have been altered");

        // 6. Verify specific core counts match MedHub ground truth
        baselineCounts["Patients"].Should().Be(15);
        baselineCounts["Encounter"].Should().Be(16);
        baselineCounts["Prescriptions"].Should().Be(31);
        baselineCounts["Schedule"].Should().Be(385);
        baselineCounts["Medications"].Should().Be(114);
        baselineCounts["AMiS_Patient_Medication_Dispense"].Should().Be(2);
        baselineCounts["Care_Stations"].Should().Be(4);
        baselineCounts["Rooms_Beds"].Should().Be(19);
        baselineCounts["Users"].Should().Be(13);
    }

    #endregion
}
