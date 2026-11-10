#pragma warning disable EF1002 // identifiers below come from the EF model, never from user input
﻿using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NJTruckCharging.Infrastructure;
using NJTruckCharging.Infrastructure.Persistence;

// C3 Site Finder -> Supabase PostgreSQL copy tool (run by an operator from a trusted machine; never deployed).
// Everything it creates lives in schema c3, owned by role c3_migrator (the dedicated C3-Site-Finder Supabase project).
//
// Secrets come from environment variables only (never command-line arguments or files):
//   C3_SOURCE              SQL Server connection string of the database to copy FROM (only read; never the live DB -
//                          use a restored, verified backup).
//   C3_TARGET              PostgreSQL connection string AS c3_migrator (Supabase pooler user "c3_migrator.<project-ref>").
//   C3_MIGRATOR_PASSWORD   } only for "bootstrap-sql": turned into one-way SCRAM verifiers; the plain passwords never
//   C3_READONLY_PASSWORD   } leave this machine.
//
// Commands, in order:
//   inventory-sql   print sql/00-inventory.sql (read-only; run in the Supabase SQL Editor)
//   bootstrap-sql   print sql/01-roles-and-schema.sql with SCRAM verifiers (run once in the SQL Editor as postgres)
//   script          print the CREATE TABLE script for schema c3 (= sql/02-c3-tables.sql; no database needed)
//   create-schema   as c3_migrator: create schema c3 and its tables (refuses if c3 already has tables)
//   copy            as c3_migrator: copy every table C3_SOURCE -> c3 (refuses unless every c3 table is empty)
//   verify          row counts + content hash of every table, source vs target
//   check-login     READ-ONLY: sign in with C3_TARGET and report who you are and what you may do in schema c3
//   secure          as c3_migrator: run sql/03-runtime-grants.sql (SELECT-only runtime role, RLS, no API-role access)
//   lockdown-sql    print sql/04-lockdown.sql (run in the SQL Editor as postgres)
//   check-sql       print sql/05-check-isolation.sql (read-only; run in the SQL Editor as postgres; all rows OK)

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var command = args.FirstOrDefault() ?? "";
try
{
    switch (command)
    {
        case "script":
            await using (var t = Db.Target(requireConnection: false)) Console.WriteLine(t.Database.GenerateCreateScript());
            return 0;
        case "create-schema": return await CreateSchemaAsync();
        case "copy": return await CopyAsync();
        case "verify": return await VerifyAsync();
        case "secure": return await SecureAsync();
        case "check-login": return await CheckLoginAsync();
        case "inventory-sql": Console.WriteLine(Sql.Read("00-inventory.sql")); return 0;
        case "bootstrap-sql": Console.WriteLine(Sql.Bootstrap()); return 0;
        case "lockdown-sql": Console.WriteLine(Sql.Read("04-lockdown.sql")); return 0;
        case "check-sql": Console.WriteLine(Sql.Read("05-check-isolation.sql")); return 0;
        default:
            Console.Error.WriteLine("Usage: NJTruckCharging.CloudCopy inventory-sql | bootstrap-sql | script | check-login | create-schema | copy | verify | secure | lockdown-sql | check-sql");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

/// <summary>Tables in foreign-key order (principals first). Owned types travel with their owner.</summary>
static List<IEntityType> TablesInOrder(AppDbContext db)
{
    var all = db.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList();
    var ordered = new List<IEntityType>();
    while (ordered.Count < all.Count)
    {
        var next = all.Where(e => !ordered.Contains(e) && e.GetForeignKeys()
                .All(fk => fk.PrincipalEntityType == e || ordered.Contains(fk.PrincipalEntityType.IsOwned() ? fk.PrincipalEntityType.FindOwnership()!.PrincipalEntityType : fk.PrincipalEntityType)))
            .ToList();
        if (next.Count == 0) throw new InvalidOperationException("Circular foreign keys between tables.");
        ordered.AddRange(next.OrderBy(e => e.GetTableName()));
    }
    return ordered;
}

static async Task<int> CreateSchemaAsync()
{
    await using var t = Db.Target();
    var existing = await t.Database.SqlQueryRaw<int>(
        "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = {0}", DatabaseProvider.PostgresSchema).SingleAsync();
    if (existing > 0) throw new InvalidOperationException($"Schema '{DatabaseProvider.PostgresSchema}' already has {existing} tables; nothing was changed.");
    await t.Database.ExecuteSqlRawAsync(t.Database.GenerateCreateScript());
    Console.WriteLine($"Created schema '{DatabaseProvider.PostgresSchema}' with {TablesInOrder(t).Count} tables.");
    return 0;
}

static async Task<int> CopyAsync()
{
    await using var s = Db.Source();
    await using var probe = Db.Target();
    var tables = TablesInOrder(probe);
    foreach (var e in tables)
        if (await CountAsync(probe, e) > 0) throw new InvalidOperationException($"Target table {e.GetTableName()} is not empty; nothing was copied.");

    foreach (var e in tables)
    {
        var started = DateTime.UtcNow;
        var n = await (Task<int>)typeof(Tables).GetMethod(nameof(Tables.CopyTableAsync))!
            .MakeGenericMethod(e.ClrType).Invoke(null, [s])!;
        Console.WriteLine($"  {e.GetTableName(),-28} {n,8:N0} rows  {(DateTime.UtcNow - started).TotalSeconds,6:N1}s");
    }

    // Identity sequences continue after the copied keys.
    await using var t = Db.Target();
    foreach (var e in tables)
        foreach (var p in e.GetProperties().Where(p => p.ValueGenerated == ValueGenerated.OnAdd && p.IsPrimaryKey() && p.ClrType == typeof(int)))
        {
            var table = $"\"{DatabaseProvider.PostgresSchema}\".\"{e.GetTableName()}\"";
            var col = p.GetColumnName();
            await t.Database.ExecuteSqlRawAsync(
                $"SELECT setval(pg_get_serial_sequence('{table}', '{col}'), GREATEST(COALESCE(MAX(\"{col}\"), 0), 1), MAX(\"{col}\") IS NOT NULL) FROM {table}");
        }
    Console.WriteLine("Copy finished. Run 'verify' next.");
    return 0;
}

static async Task<int> CountAsync(AppDbContext db, IEntityType e) =>
    await (Task<int>)typeof(Tables).GetMethod(nameof(Tables.CountTableAsync))!
        .MakeGenericMethod(e.ClrType).Invoke(null, [db])!;

static async Task<int> VerifyAsync()
{
    await using var s = Db.Source();
    await using var t = Db.Target();
    var ok = true;
    Console.WriteLine($"  {"table",-28} {"source",8} {"target",8}  content");
    foreach (var e in TablesInOrder(t))
    {
        var hash = typeof(Tables).GetMethod(nameof(Tables.HashTableAsync))!
            .MakeGenericMethod(e.ClrType);
        var (sn, sh) = await (Task<(int, string)>)hash.Invoke(null, [s])!;
        var (tn, th) = await (Task<(int, string)>)hash.Invoke(null, [t])!;
        var same = sn == tn && sh == th;
        ok &= same;
        Console.WriteLine($"  {e.GetTableName(),-28} {sn,8:N0} {tn,8:N0}  {(same ? "identical" : "DIFFERENT")}");
    }
    Console.WriteLine(ok ? "VERIFIED: every table is identical." : "MISMATCH: see DIFFERENT rows above.");
    return ok ? 0 : 1;
}

/// <summary>Read-only sign-in test: proves the login, password and pooler work, and shows the role's rights in c3.</summary>
static async Task<int> CheckLoginAsync()
{
    await using var t = Db.Target();
    var info = await t.Database.SqlQueryRaw<string>("""
        SELECT current_user::text || ' on ' || current_database()
            || ' | schema c3: ' || CASE WHEN to_regnamespace('c3') IS NULL THEN 'missing'
                 ELSE 'usage=' || has_schema_privilege('c3', 'USAGE') || ', create=' || has_schema_privilege('c3', 'CREATE')
                      || ', tables=' || (SELECT count(*) FROM pg_tables WHERE schemaname = 'c3') END
            || ' | read-only transactions=' || current_setting('default_transaction_read_only')
            || ' | ' || split_part(version(), ' ', 2) AS "Value"
        """).SingleAsync();
    Console.WriteLine("Signed in: " + info);

    // Exact row counts (still read-only) when this login may read the c3 tables, e.g. c3_runtime_readonly after Step 3.
    var tables = await t.Database.SqlQueryRaw<string>("""
        SELECT c.relname::text AS "Value" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'c3' AND c.relkind IN ('r', 'p') AND has_table_privilege(c.oid, 'SELECT') ORDER BY c.relname
        """).ToListAsync();
    if (tables.Count > 0)
    {
        var counts = new List<(string Table, long Rows)>();
        foreach (var table in tables)
            counts.Add((table, await t.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM c3.\"{table.Replace("\"", "\"\"")}\"").SingleAsync()));
        Console.WriteLine($"Rows (exact): {string.Join(", ", counts.Select(c => $"{c.Table}={c.Rows:N0}"))}");
        Console.WriteLine($"Rows (exact): total {counts.Sum(c => c.Rows):N0} in {counts.Count} readable c3 tables");
    }
    return 0;
}

static async Task<int> SecureAsync()
{
    await using var t = Db.Target();
    var who = await t.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync();
    if (who != "c3_migrator") throw new InvalidOperationException($"Run 'secure' as c3_migrator (connected as {who}).");
    var runtime = await t.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_roles WHERE rolname = 'c3_runtime_readonly'").SingleAsync();
    if (runtime != 1) throw new InvalidOperationException("Role c3_runtime_readonly does not exist: run the bootstrap SQL first.");
    await t.Database.ExecuteSqlRawAsync(Sql.Read("03-runtime-grants.sql").Replace("{", "{{").Replace("}", "}}"));
    Console.WriteLine("c3_runtime_readonly: SELECT only on every c3 table; row-level security on; PUBLIC and Supabase API roles revoked.");
    return 0;
}

static class Sql
{
    public static string Read(string name)
    {
        using var s = typeof(Sql).Assembly.GetManifestResourceStream("sql." + name)
            ?? throw new InvalidOperationException($"Embedded SQL {name} not found.");
        return new StreamReader(s).ReadToEnd();
    }

    public static string Bootstrap()
    {
        var sql = Read("01-roles-and-schema.sql")
            .Replace("{{MIGRATOR_VERIFIER}}", Scram.Verifier(Password("C3_MIGRATOR_PASSWORD")))
            .Replace("{{READONLY_VERIFIER}}", Scram.Verifier(Password("C3_READONLY_PASSWORD")));
        return sql;
    }

    private static string Password(string name)
    {
        var p = Db.Env(name);
        if (p.Length < 24) throw new InvalidOperationException($"{name} must be at least 24 characters.");
        if (p.Any(c => c < 33 || c > 126) || p.Contains('\'') || p.Contains('"') || p.Contains(';'))
            throw new InvalidOperationException($"{name}: use printable ASCII without spaces, quotes or semicolons (it goes into a connection string).");
        return p;
    }
}

/// <summary>PostgreSQL SCRAM-SHA-256 password verifier (RFC 5802/7677), the format pg_authid stores. One-way.</summary>
static class Scram
{
    public static string Verifier(string password, int iterations = 4096)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        var clientKey = HMACSHA256.HashData(salted, Encoding.ASCII.GetBytes("Client Key"));
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, Encoding.ASCII.GetBytes("Server Key"));
        return $"SCRAM-SHA-256${iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }
}

sealed class MicrosecondDateTime : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.GetDateTime();
    public override void Write(Utf8JsonWriter w, DateTime v, JsonSerializerOptions o) => w.WriteNumberValue(v.Ticks / 10);
}

sealed class MicrosecondDateTimeOffset : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.GetDateTimeOffset();
    public override void Write(Utf8JsonWriter w, DateTimeOffset v, JsonSerializerOptions o) => w.WriteNumberValue(v.UtcTicks / 10);
}

static class Db
{
    public static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : throw new InvalidOperationException($"Set the {name} environment variable.");
    
    public static AppDbContext Source() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(Env("C3_SOURCE"), o => o.CommandTimeout(600))
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
    
    public static AppDbContext Target(bool requireConnection = true) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(requireConnection ? Env("C3_TARGET") : "Host=localhost", o => o.CommandTimeout(600))
        .Options);
}

static class Tables
{
    public static async Task<int> CopyTableAsync<T>(AppDbContext source) where T : class
    {
        const int batch = 2000;
        var total = 0;
        var keyProps = source.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties;
        IQueryable<T> q = source.Set<T>();
        q = keyProps.Aggregate((IOrderedQueryable<T>?)null, (o, p) => o is null
            ? q.OrderBy(x => EF.Property<object>(x, p.Name))
            : o.ThenBy(x => EF.Property<object>(x, p.Name)))!;
        for (var skip = 0; ; skip += batch)
        {
            var rows = await q.Skip(skip).Take(batch).ToListAsync();
            if (rows.Count == 0) break;
            await using var t = Db.Target();
            t.ChangeTracker.AutoDetectChangesEnabled = false;
            t.Set<T>().AddRange(rows);
            await t.SaveChangesAsync();
            total += rows.Count;
            if (rows.Count < batch) break;
        }
        return total;
    }
    

    public static Task<int> CountTableAsync<T>(AppDbContext db) where T : class => db.Set<T>().CountAsync();
    

    /// <summary>Row count + SHA-256 of every row (all columns incl. owned values), ordered by primary key.
    /// Timestamps are compared at microsecond precision (PostgreSQL's resolution) and in UTC.</summary>
    public static async Task<(int, string)> HashTableAsync<T>(AppDbContext db) where T : class
    {
        var e = db.Model.FindEntityType(typeof(T))!;
        var rows = await db.Set<T>().AsNoTracking().ToListAsync();
        var keyProps = e.FindPrimaryKey()!.Properties.Select(p => p.PropertyInfo!).ToArray();
        var ordered = rows.OrderBy(r => string.Join("\u001f", keyProps.Select(p => Convert.ToString(p.GetValue(r), System.Globalization.CultureInfo.InvariantCulture))), StringComparer.Ordinal);
        var options = new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles, Converters = { new MicrosecondDateTime(), new MicrosecondDateTimeOffset() } };
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var r in ordered) sha.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r, options) + "\n"));
        return (rows.Count, Convert.ToHexString(sha.GetHashAndReset()));
    }
    
}

public partial class Program;
