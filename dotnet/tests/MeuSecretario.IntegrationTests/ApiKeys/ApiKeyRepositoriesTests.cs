using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Infrastructure.ApiKeys;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.ApiKeys;

/// <summary>Repositórios Dapper da 008a contra PostgreSQL 15 real: UNIQUE, FK, dono, upsert, agregação, plano.</summary>
[Trait("Category", "Docker")]
public class ApiKeyRepositoriesTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        await ApiKeysTestSupport.SeedPlansAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApiKeyRepository Keys() => new(postgres.Connections);

    // ---------------------------------------------------------------- chaves

    [Fact]
    [Trait("AC", "008-10")]
    public async Task Insert_Persists_ActiveWithTimestamps()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var result = await Keys().InsertAsync(user, "gemini", "token", default);

        var created = result.ShouldBeOfType<InsertKeyResult.Created>();
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = @id", new { id = created.Id })).ShouldBeTrue();
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = @id", new { id = created.Id })).ShouldBe("token");
    }

    [Fact]
    [Trait("AC", "008-13")]
    public async Task Insert_UnknownUser_IsUserNotFound_AndDuplicateIsAlreadyExists_EvenWhenInactive()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);

        (await Keys().InsertAsync(999, "gemini", "t", default)).ShouldBeOfType<InsertKeyResult.UserNotFound>();

        await Keys().InsertAsync(user, "gemini", "t", default);
        (await Keys().InsertAsync(user, "gemini", "t2", default)).ShouldBeOfType<InsertKeyResult.AlreadyExists>();

        await ApiKeysTestSupport.Execute(postgres, "UPDATE chavesapiusuario SET ativo = FALSE");
        (await Keys().InsertAsync(user, "gemini", "t3", default)).ShouldBeOfType<InsertKeyResult.AlreadyExists>();
    }

    [Fact]
    [Trait("AC", "008-14")]
    public async Task List_IsOrderedByProvider_AndNeverCarriesTheKey()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await Keys().InsertAsync(user, "weather", "t1", default);
        await Keys().InsertAsync(user, "gemini", "t2", default);

        var rows = await Keys().ListByUserAsync(user, default);

        rows.Select(r => r.Provider).ShouldBe(["gemini", "weather"]);
        rows.ShouldAllBe(r => r.Active == true && r.CreatedAt != null && r.LastUsedAt == null);
        (await Keys().ListByUserAsync(user + 1, default)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Update_AndDeactivate_OnlyActForTheOwner()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990001");
        var b = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990002");
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", "original");

        (await Keys().UpdateAsync(keyId, b, "invasor", false, default)).ShouldBeNull();
        (await Keys().DeactivateAsync(keyId, b, default)).ShouldBeNull();
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBe("original");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBeTrue();

        (await Keys().UpdateAsync(keyId, a, "nova", null, default)).ShouldBe(new KeyRef("gemini", a));
        (await Keys().DeactivateAsync(keyId, a, default)).ShouldBe(new KeyRef("gemini", a));
        (await Keys().DeactivateAsync(keyId, a, default)).ShouldBe(new KeyRef("gemini", a)); // repetir também acha a linha
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "008-15")]
    public async Task Update_OnlyActive_KeepsTheKey_AndOnlyKey_KeepsActive()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, user, "gemini", "original");

        await Keys().UpdateAsync(keyId, user, null, false, default);
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBe("original");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBeFalse();

        await Keys().UpdateAsync(keyId, user, "nova", null, default);
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBe("nova");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = @keyId", new { keyId })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "008-62")]
    public async Task FindActive_IgnoresInactiveKeys_AndTouchSetsLastUse()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, user, "gemini", "token", active: false);
        (await Keys().FindActiveEncryptedAsync(user, "gemini", default)).ShouldBeNull();

        await ApiKeysTestSupport.Execute(postgres, "UPDATE chavesapiusuario SET ativo = TRUE");
        (await Keys().FindActiveEncryptedAsync(user, "gemini", default)).ShouldBe("token");

        await Keys().TouchLastUseAsync(user, "gemini", default);
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ultimo_uso_em IS NOT NULL FROM chavesapiusuario WHERE usuario_id = @user", new { user })).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- preferências

    [Fact]
    [Trait("AC", "008-20")]
    public async Task Preference_Upsert_KeepsTheSameRow()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new PreferenceRepository(postgres.Connections);

        var first = (await repo.UpsertAsync(user, "gemini", true, default)).ShouldBeOfType<UpsertPreferenceResult.Saved>();
        var second = (await repo.UpsertAsync(user, "gemini", false, default)).ShouldBeOfType<UpsertPreferenceResult.Saved>();

        second.Id.ShouldBe(first.Id);
        (await repo.FindAsync(user, "gemini", default)).ShouldBe(false);
        (await repo.FindAsync(user, "weather", default)).ShouldBeNull();
        (await repo.UpsertAsync(12345, "gemini", true, default)).ShouldBeOfType<UpsertPreferenceResult.UserNotFound>();
    }

    [Fact]
    [Trait("AC", "008-22")]
    public async Task Preference_ListIsOrdered_AndDeleteReportsWhetherARowExisted()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new PreferenceRepository(postgres.Connections);
        await repo.UpsertAsync(user, "weather", true, default);
        await repo.UpsertAsync(user, "gemini", false, default);

        (await repo.ListAsync(user, default)).Select(p => p.Provider).ShouldBe(["gemini", "weather"]);
        (await repo.DeleteAsync(user, "gemini", default)).ShouldBeTrue();
        (await repo.DeleteAsync(user, "gemini", default)).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- uso

    [Fact]
    [Trait("AC", "008-65")]
    public async Task Usage_IncrementCreatesThenIncrements_AndQueryAggregates()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new UsageRepository(postgres.Connections);

        await repo.IncrementAsync(user, "gemini", "sistema", "2026-10", default);
        await repo.IncrementAsync(user, "gemini", "sistema", "2026-10", default);
        await repo.IncrementAsync(user, "gemini", "propria", "2026-10", default);
        await repo.IncrementAsync(user, "weather", "sistema", "2026-10", default);
        await repo.IncrementAsync(user, "gemini", "sistema", "2026-09", default);

        var rows = await repo.QueryMonthAsync(user, "2026-10", null, default);
        rows.Select(r => (r.Provider, r.KeyType, r.TotalCalls)).ShouldBe(
            [("gemini", "propria", 1L), ("gemini", "sistema", 2L), ("weather", "sistema", 1L)]);
        (await repo.QueryMonthAsync(user, "2026-10", "weather", default)).Count.ShouldBe(1);
        (await repo.QueryMonthAsync(user, "qualquer", null, default)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "008-66")]
    public async Task Usage_SumSystemMonth_OnlyCountsSystemKeysOfTheMonthAndProvider()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new UsageRepository(postgres.Connections);
        await repo.IncrementAsync(user, "gemini", "sistema", "2026-10", default);
        await repo.IncrementAsync(user, "gemini", "propria", "2026-10", default);
        await repo.IncrementAsync(user, "gemini", "sistema", "2026-09", default);
        await repo.IncrementAsync(user, "weather", "sistema", "2026-10", default);

        (await repo.SumSystemMonthAsync(user, "gemini", "2026-10", default)).ShouldBe(1);
        (await repo.SumSystemMonthAsync(user, "openroute", "2026-10", default)).ShouldBe(0);
    }

    // ---------------------------------------------------------------- logs

    [Fact]
    [Trait("AC", "008-31")]
    public async Task AccessLog_InsertThenListFiltersAndOrdersByCreatedAtDesc()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new AccessLogRepository(postgres.Connections);
        await repo.InsertAsync(user, "gemini", "propria", "obter_chave", true, null, default);
        await repo.InsertAsync(user, "weather", "n/a", "obter_chave", false, "NoPreference", default);
        await repo.InsertAsync(user, "gemini", "sistema", "obter_chave", true, null, default);

        var all = await repo.ListAsync(user, null, null, 50, default);
        all.Count.ShouldBe(3);
        all.Select(l => l.Id).ShouldBe(all.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).Select(l => l.Id).ToList(), ignoreOrder: true);
        (await repo.ListAsync(user, "gemini", null, 50, default)).Count.ShouldBe(2);
        (await repo.ListAsync(user, null, false, 50, default)).Single().ErrorMessage.ShouldBe("NoPreference");
        (await repo.ListAsync(user, null, null, 0, default)).ShouldBeEmpty();
        (await repo.ListAsync(user, null, null, 1, default)).Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------- plano

    [Fact]
    [Trait("AC", "008-66")]
    public async Task Plan_OnlyActiveAndNotExpiredSubscriptionsCount()
    {
        var user = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var repo = new PlanRepository(postgres.Connections);
        (await repo.FindActivePlanAsync(user, default)).ShouldBeNull();

        var prata = await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT id FROM planos WHERE nome_plano = 'Prata'");
        var bronze = await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT id FROM planos WHERE nome_plano = 'Bronze'");

        // expirada e cancelada não contam
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status, data_fim) VALUES (@user, @prata, 'ativo', now() - interval '1 day')",
            new { user, prata });
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status) VALUES (@user, @prata, 'cancelado')", new { user, prata });
        (await repo.FindActivePlanAsync(user, default)).ShouldBeNull();

        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status, data_fim) VALUES (@user, @bronze, 'ativo', now() + interval '10 days')",
            new { user, bronze });

        var plan = await repo.FindActivePlanAsync(user, default);
        plan.ShouldBe(new ActivePlan("Bronze", 100, 50, 10));
        plan!.LimitFor("openroute").ShouldBe(10);
    }
}
