using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Infrastructure.CalendarAlerts;
using MeuSecretario.IntegrationTests.Settings;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.CalendarAlerts;

[Trait("Category", "Docker")]
public sealed class CalendarAlertConfigRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SettingsEnv _env = new(postgres);
    private CalendarAlertConfigRepository _repository = default!;
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _repository = new CalendarAlertConfigRepository(postgres.Connections);
        _user = await _env.InsertUserAsync("5511900000001");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task AddTokenAsync(int userId, bool needsReconnect) =>
        _env.ExecuteAsync(
            "INSERT INTO googlecalendartokens (usuario_id, access_token, needs_reconnect) VALUES (@userId, 'x', @needsReconnect)",
            new { userId, needsReconnect });

    [Fact]
    [Trait("AC", "014-10")]
    public async Task GetOrCreate_InsertsTheDefaults_AndIsIdempotent()
    {
        var first = await _repository.GetOrCreateAsync(_user, CancellationToken.None);
        await _repository.UpdateAsync(_user, true, 9, CancellationToken.None);
        var second = await _repository.GetOrCreateAsync(_user, CancellationToken.None);

        first.ShouldBe(new CalendarAlertConfig(false, 1));
        second.ShouldBe(new CalendarAlertConfig(true, 9));
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM calendaralertconfigs")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "014-50")]
    public async Task GetOrCreate_Concurrent_KeepsOneRowPerUser()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => _repository.GetOrCreateAsync(_user, CancellationToken.None))));

        results.ShouldAllBe(c => c == new CalendarAlertConfig(false, 1));
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM calendaralertconfigs")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "014-22")]
    public async Task Update_OnlyTouchesWhatWasSent_AndRenewsUpdatedAt()
    {
        await _repository.GetOrCreateAsync(_user, CancellationToken.None);
        await _repository.UpdateAsync(_user, true, 20, CancellationToken.None);
        await _env.ExecuteAsync("UPDATE calendaralertconfigs SET updated_at = '2020-01-01'");

        var onlyMinutes = await _repository.UpdateAsync(_user, null, 3, CancellationToken.None);
        var renewed = await _env.ScalarAsync<bool>("SELECT updated_at > now() - interval '1 minute' FROM calendaralertconfigs");
        var onlyActive = await _repository.UpdateAsync(_user, false, null, CancellationToken.None);

        onlyMinutes.ShouldBe(new CalendarAlertConfig(true, 3));
        renewed.ShouldBeTrue();
        onlyActive.ShouldBe(new CalendarAlertConfig(false, 3));
    }

    [Fact]
    [Trait("AC", "014-26")]
    public async Task Update_OutOfRangeMinutes_ViolatesTheCheckConstraint()
    {
        await _repository.GetOrCreateAsync(_user, CancellationToken.None);

        var ex = await Should.ThrowAsync<PostgresException>(() => _repository.UpdateAsync(_user, null, 61, CancellationToken.None));

        ex.ConstraintName.ShouldBe("chk_minutos_antes");
    }

    [Fact]
    [Trait("AC", "014-47")]
    public async Task ActiveUsers_IncludesOnlyActiveConfigsWithAUsableToken()
    {
        var withToken = _user;
        var needsReconnect = await _env.InsertUserAsync("5511900000002", "Bia");
        var noToken = await _env.InsertUserAsync("5511900000003", "Caio");
        var inactiveConfig = await _env.InsertUserAsync("5511900000004", "Duda");
        var inactiveUser = await _env.InsertUserAsync("5511900000005", "Edu");
        foreach (var id in new[] { withToken, needsReconnect, noToken, inactiveUser })
        {
            await _repository.GetOrCreateAsync(id, CancellationToken.None);
            await _repository.UpdateAsync(id, true, id == withToken ? 5 : 1, CancellationToken.None);
        }

        await _repository.GetOrCreateAsync(inactiveConfig, CancellationToken.None);
        await AddTokenAsync(withToken, needsReconnect: false);
        await AddTokenAsync(needsReconnect, needsReconnect: true);
        await AddTokenAsync(inactiveConfig, needsReconnect: false);
        await AddTokenAsync(inactiveUser, needsReconnect: false);
        // Desvio 122: usuário desativado com alerta ligado e Google conectado não recebe mais alertas.
        await _env.ExecuteAsync("UPDATE usuarios SET ativo = false WHERE id = @inactiveUser", new { inactiveUser });

        var users = await _repository.ListUsersWithActiveAlertsAsync(CancellationToken.None);

        users.Count.ShouldBe(1);
        users[0].UserId.ShouldBe(withToken);
        users[0].WhatsappNumber.ShouldBe("5511900000001");
        users[0].MinutesBefore.ShouldBe(5);
    }
}
