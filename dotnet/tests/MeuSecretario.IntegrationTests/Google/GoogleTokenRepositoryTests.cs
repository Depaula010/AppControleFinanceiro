using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Google;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Google;

/// <summary>Tabela <c>googlecalendartokens</c> em PostgreSQL real (spec 010d, T23).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class GoogleTokenRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Calendar = "https://www.googleapis.com/auth/calendar";
    private const string Drive = "https://www.googleapis.com/auth/drive.file";

    private GoogleTokenRepository Repository => new(postgres.Connections);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedUser(string name = "Maria", string? whatsapp = "5511999990001")
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp) VALUES (@name, @whatsapp) RETURNING id", new { name, whatsapp });
    }

    private async Task<T> Scalar<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    private static GoogleTokenSet Tokens(string access, string? refresh, DateTime? expiry = null, params string[] scopes) =>
        new(access, refresh, expiry is null ? null : new DateTimeOffset(DateTime.SpecifyKind(expiry.Value, DateTimeKind.Utc)), scopes.Length == 0 ? [Calendar, Drive] : scopes);

    [Fact]
    [Trait("AC", "010-77")]
    public async Task Save_InsertsRowWithAllColumns()
    {
        var uid = await SeedUser();
        var expiry = new DateTime(2026, 10, 2, 15, 0, 0, DateTimeKind.Utc);

        await Repository.SaveAsync(uid, Tokens("acc-1", "ref-1", expiry), CancellationToken.None);

        var row = await Repository.FindAsync(uid, CancellationToken.None);
        row.ShouldNotBeNull();
        row.AccessToken.ShouldBe("acc-1");
        row.RefreshToken.ShouldBe("ref-1");
        row.TokenExpiry.ShouldBe(expiry);
        row.NeedsReconnect.ShouldBeFalse();
        row.Scopes.ShouldBe($"[\"{Calendar}\", \"{Drive}\"]");
        System.Text.Json.JsonSerializer.Deserialize<string[]>(row.Scopes!).ShouldBe([Calendar, Drive]);
    }

    [Fact]
    [Trait("AC", "010-78")]
    public async Task Save_WithoutRefreshToken_KeepsTheOldOne_AndWithOneReplacesIt()
    {
        var uid = await SeedUser();
        await Repository.SaveAsync(uid, Tokens("acc-1", "ref-1"), CancellationToken.None);

        await Repository.SaveAsync(uid, Tokens("acc-2", null), CancellationToken.None);
        var kept = await Repository.FindAsync(uid, CancellationToken.None);
        kept!.AccessToken.ShouldBe("acc-2");
        kept.RefreshToken.ShouldBe("ref-1");

        await Repository.SaveAsync(uid, Tokens("acc-3", "ref-3"), CancellationToken.None);
        (await Repository.FindAsync(uid, CancellationToken.None))!.RefreshToken.ShouldBe("ref-3");
        (await Scalar<int>("SELECT count(*) FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-78")]
    public async Task Save_ClearsNeedsReconnect_AndTouchesUpdatedAt()
    {
        var uid = await SeedUser();
        await Repository.SaveAsync(uid, Tokens("acc-1", "ref-1"), CancellationToken.None);
        await Repository.MarkNeedsReconnectAsync(uid, CancellationToken.None);
        (await Repository.FindAsync(uid, CancellationToken.None))!.NeedsReconnect.ShouldBeTrue();
        await Scalar<int>("UPDATE googlecalendartokens SET updated_at = '2020-01-01' WHERE usuario_id = @uid RETURNING 1", new { uid });

        await Repository.SaveAsync(uid, Tokens("acc-2", null), CancellationToken.None);

        (await Repository.FindAsync(uid, CancellationToken.None))!.NeedsReconnect.ShouldBeFalse();
        (await Scalar<DateTime>("SELECT updated_at FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).Year.ShouldBeGreaterThan(2020);
    }

    [Fact]
    [Trait("AC", "010-85")]
    public async Task MarkNeedsReconnect_SetsTheFlagOnlyForThatUser()
    {
        var one = await SeedUser("Um", "5511999990001");
        var two = await SeedUser("Dois", "5511999990002");
        await Repository.SaveAsync(one, Tokens("a", "r"), CancellationToken.None);
        await Repository.SaveAsync(two, Tokens("b", "r"), CancellationToken.None);

        await Repository.MarkNeedsReconnectAsync(one, CancellationToken.None);

        (await Repository.FindAsync(one, CancellationToken.None))!.NeedsReconnect.ShouldBeTrue();
        (await Repository.FindAsync(two, CancellationToken.None))!.NeedsReconnect.ShouldBeFalse();
    }

    [Fact]
    public async Task Find_WithoutRow_ReturnsNull_AndNullExpiryIsPreserved()
    {
        var uid = await SeedUser();
        (await Repository.FindAsync(uid, CancellationToken.None)).ShouldBeNull();

        await Repository.SaveAsync(uid, Tokens("a", null, expiry: null), CancellationToken.None);

        (await Repository.FindAsync(uid, CancellationToken.None))!.TokenExpiry.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-77")]
    public async Task Save_ForMissingUser_ViolatesTheForeignKey()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() =>
            Repository.SaveAsync(9999, Tokens("a", "r"), CancellationToken.None));

        ex.SqlState.ShouldBe("23503");
    }

    [Fact]
    [Trait("AC", "010-87")]
    public async Task GetScopes_ReturnsTheStoredText_OrNull()
    {
        var uid = await SeedUser();
        (await Repository.GetScopesAsync(uid, CancellationToken.None)).ShouldBeNull();

        await Repository.SaveAsync(uid, Tokens("a", "r", scopes: [Calendar]), CancellationToken.None);
        (await Repository.GetScopesAsync(uid, CancellationToken.None)).ShouldBe($"[\"{Calendar}\"]");

        await Scalar<int>("UPDATE googlecalendartokens SET scopes = NULL WHERE usuario_id = @uid RETURNING 1", new { uid });
        (await Repository.GetScopesAsync(uid, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Delete_RemovesTheRow_AndReportsWhetherThereWasOne()
    {
        var uid = await SeedUser();
        await Repository.SaveAsync(uid, Tokens("a", "r"), CancellationToken.None);

        (await Repository.DeleteAsync(uid, CancellationToken.None)).ShouldBeTrue();
        (await Repository.DeleteAsync(uid, CancellationToken.None)).ShouldBeFalse();
        (await Repository.FindAsync(uid, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task UserLookup_FindsNameAndWhatsapp_OrNull()
    {
        var uid = await SeedUser("Maria", "5511999990001");
        var lookup = new GoogleUserLookup(postgres.Connections);

        (await lookup.FindAsync(uid, CancellationToken.None)).ShouldBe(new GoogleUserInfo("Maria", "5511999990001"));
        (await lookup.FindNameAsync(uid, CancellationToken.None)).ShouldBe("Maria");
        (await lookup.FindAsync(uid + 100, CancellationToken.None)).ShouldBeNull();
        (await lookup.FindNameAsync(uid + 100, CancellationToken.None)).ShouldBeNull();
    }
}
