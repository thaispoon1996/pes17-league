using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Pes17League.Clubs.Application;
using Pes17League.Clubs.Infrastructure;

namespace Pes17League.Clubs.Tests;

/// <summary>
/// Test tích hợp cần Postgres thật (PES17_TEST_DB, user có quyền CREATE DATABASE) và OF gốc (PES_OF_FIXTURES/orig.OPT).
/// Mỗi test tạo 1 database riêng, chạy migration, xoá khi xong. Thiếu biến → skip.
/// </summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (Env.Db is null || Env.Fixtures is null) Skip = "Không có PES17_TEST_DB / PES_OF_FIXTURES";
    }
}

public static class Env
{
    public static string? Db => Environment.GetEnvironmentVariable("PES17_TEST_DB");
    public static string? Fixtures => Environment.GetEnvironmentVariable("PES_OF_FIXTURES") is { } d && Directory.Exists(d) ? d : null;
    public static byte[] OrigOptionFile => File.ReadAllBytes(Path.Combine(Fixtures!, "orig.OPT"));
}

public sealed class TestDb : IAsyncDisposable
{
    private readonly string _admin;
    private readonly string _name = "pesleague_test_" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _storeDir = Path.Combine(Path.GetTempPath(), "pes17league-store-" + Guid.NewGuid().ToString("N")[..8]);
    public string ConnectionString { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    public IOptionFileStore Store { get; }

    private TestDb()
    {
        _admin = Env.Db!;
        ConnectionString = new NpgsqlConnectionStringBuilder(_admin) { Database = _name }.ConnectionString;
        Store = new FileSystemOptionFileStore(_storeDir);
    }

    public static async Task<TestDb> CreateAsync()
    {
        var t = new TestDb();
        await using var db = t.Context();
        await db.Database.MigrateAsync();   // tạo DB + chạy migration InitialClubs
        return t;
    }

    public ClubsDbContext Context() => new(new DbContextOptionsBuilder<ClubsDbContext>()
        .UseNpgsql(ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", ClubsDbContext.Schema)).Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(_admin);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
        if (Directory.Exists(_storeDir)) Directory.Delete(_storeDir, true);
    }
}
