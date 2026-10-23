using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Settings;

[Trait("Category", "Docker")]
public class NotificationsEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Path = "/api/user/notifications";
    private const string Defaults =
        """{"morningBriefing":{"enabled":true,"time":"07:00"},"eveningCheckIn":{"enabled":true,"time":"19:00"},"financialAlerts":{"enabled":true,"daysBeforeDue":3}}""";

    private readonly SettingsEnv _env = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<int> Rows() => _env.ScalarAsync<int>("SELECT count(*)::int FROM notificationconfigs WHERE usuario_id = @_user", new { _user });

    [Fact]
    [Trait("AC", "007-40")]
    public async Task Get_WithoutRow_ReturnsTheDefaults_AndCreatesNothing()
    {
        var (status, body) = await _env.Get(_user, Path);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Data(Defaults));
        (await Rows()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "007-41")]
    public async Task Get_WithRow_UsesColumns_NullFinancialIsFalse_ZeroThresholdIsThree()
    {
        await _env.ExecuteAsync(
            """
            INSERT INTO notificationconfigs (usuario_id, resumo_matinal_hora, checkin_noturno_hora, alertas_financeiros_ativos, dias_antes_vencimento)
            VALUES (@_user, '08:30', '21:15:00', NULL, 0)
            """, new { _user });

        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data(
            """{"morningBriefing":{"enabled":true,"time":"08:30"},"eveningCheckIn":{"enabled":true,"time":"21:15"},"financialAlerts":{"enabled":false,"daysBeforeDue":3}}"""));

        await _env.ExecuteAsync("UPDATE notificationconfigs SET dias_antes_vencimento = 7, resumo_matinal_hora = '07:00:30.5' WHERE usuario_id = @_user", new { _user });
        var body = (await _env.Get(_user, Path)).Body;
        body.ShouldContain("\"daysBeforeDue\":7");
        body.ShouldContain("\"time\":\"07:00\"");
    }

    private const string AngularBody =
        """{"morningBriefing":{"enabled":false,"time":"08:30"},"eveningCheckIn":{"enabled":true,"time":"21:15"},"financialAlerts":{"enabled":false,"daysBeforeDue":7}}""";

    [Fact]
    [Trait("AC", "007-42")]
    public async Task Put_WritesSixValues_GetReadsThemBack_AndDoesNotDuplicate()
    {
        var (status, body) = await _env.Put(_user, Path, AngularBody);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Success("Notificações atualizadas com sucesso"));
        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data(AngularBody));

        var first = await _env.ScalarAsync<DateTime>("SELECT updated_at FROM notificationconfigs WHERE usuario_id = @_user", new { _user });
        await Task.Delay(20);
        (await _env.Put(_user, Path, AngularBody)).Status.ShouldBe(HttpStatusCode.OK);

        (await Rows()).ShouldBe(1);
        (await _env.ScalarAsync<DateTime>("SELECT updated_at FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeGreaterThan(first);
    }

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("17:59")]
    [InlineData("24:00")]
    [InlineData("7:00")]
    public async Task Put_EveningHourOutOfRange_Returns400(string time)
    {
        var (status, body) = await _env.Put(_user, Path, $$$"""{"eveningCheckIn":{"time":"{{{time}}}"}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Horário do check-in noturno deve ser entre 18:00 e 23:00"));
    }

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("\"abc\"")]
    [InlineData("\"19\"")]
    [InlineData("\"19:00:00\"")]
    [InlineData("null")]
    [InlineData("1900")]
    [InlineData("\"18:75\"")]
    public async Task Put_EveningFormatInvalid_Returns400(string time)
    {
        var (status, body) = await _env.Put(_user, Path, $$$"""{"eveningCheckIn":{"time":{{{time}}}}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Formato de horário inválido (use HH:MM)"));
        (await Rows()).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("18:00")]
    [InlineData("23:30")]
    public async Task Put_EveningEdgeTimes_AreStored(string time)
    {
        (await _env.Put(_user, Path, $$$"""{"eveningCheckIn":{"time":"{{{time}}}"}}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<string>("SELECT checkin_noturno_hora::text FROM notificationconfigs WHERE usuario_id = @_user", new { _user }))
            .ShouldBe(time + ":00");
    }

    [Fact]
    [Trait("AC", "007-44")]
    public async Task Put_EmptyBody_WritesTheDefaults_NotThePreviousValues()
    {
        await _env.Put(_user, Path, AngularBody);

        (await _env.Put(_user, Path, "{}")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data(Defaults));
        (await Rows()).ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "007-45")]
    [InlineData("\"25:00\"")]
    [InlineData("\"abc\"")]
    [InlineData("null")]
    public async Task Put_MorningTimeInvalid_Returns400_Desvio085(string time)
    {
        var (status, body) = await _env.Put(_user, Path, $$$"""{"morningBriefing":{"time":{{{time}}}}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Formato de horário inválido (use HH:MM)"));
        (await Rows()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "007-45")]
    public async Task Put_MorningTimeWithOneDigitHour_IsStoredCanonical()
    {
        (await _env.Put(_user, Path, """{"morningBriefing":{"time":"7:30"}}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.Get(_user, Path)).Body.ShouldContain("\"morningBriefing\":{\"enabled\":true,\"time\":\"07:30\"}");
    }

    [Theory]
    [Trait("AC", "007-46")]
    [InlineData(0, 3)]
    [InlineData(-5, -5)]
    [InlineData(45, 45)]
    public async Task Put_DaysBeforeDue_HasNoRange(int sent, int read)
    {
        (await _env.Put(_user, Path, $$$"""{"financialAlerts":{"daysBeforeDue":{{{sent}}}}}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<int>("SELECT dias_antes_vencimento FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(sent);
        (await _env.Get(_user, Path)).Body.ShouldContain($"\"daysBeforeDue\":{read}");
    }

    [Fact]
    [Trait("AC", "007-48")]
    public async Task Put_DaysBeforeDue_NeverTouchesThePotThreshold_Desvio250()
    {
        await _env.ExecuteAsync(
            "INSERT INTO notificationconfigs (usuario_id, alerta_potes_threshold) VALUES (@_user, 70)", new { _user });

        (await _env.Put(_user, Path, AngularBody)).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<int>("SELECT alerta_potes_threshold FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(70);
        (await _env.ScalarAsync<int>("SELECT dias_antes_vencimento FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(7);
        (await _env.Get(_user, Path)).Body.ShouldContain("\"daysBeforeDue\":7");
    }

    [Fact]
    [Trait("AC", "007-48")]
    public async Task Get_ExistingRowWithoutTheNewValue_ReadsTheColumnDefaultNotThePotThreshold_Desvio250()
    {
        // Linha antiga (como a produção, com o 1 do bug em alerta_potes_threshold): a migration não copia o valor.
        await _env.ExecuteAsync(
            "INSERT INTO notificationconfigs (usuario_id, alerta_potes_threshold) VALUES (@_user, 1)", new { _user });

        (await _env.Get(_user, Path)).Body.ShouldContain("\"daysBeforeDue\":3");
    }

    [Theory]
    [Trait("AC", "007-47")]
    [InlineData("""{"morningBriefing":{"enabled":"x"}}""")]
    [InlineData("""{"financialAlerts":null}""")]
    [InlineData("""{"financialAlerts":{"daysBeforeDue":null}}""")]
    [InlineData("""{"eveningCheckIn":[]}""")]
    public async Task Put_WrongTypes_Return400_Desvio084(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Dados inválidos"));
        (await Rows()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "007-47")]
    public async Task Put_DaysBeforeDueBeyondInt32_Returns400()
    {
        var (status, body) = await _env.Put(_user, Path, """{"financialAlerts":{"daysBeforeDue":99999999999}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Dados inválidos"));
    }
}
