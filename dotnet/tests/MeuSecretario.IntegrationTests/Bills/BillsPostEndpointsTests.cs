using System.Globalization;
using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary><c>POST /api/bills</c> e <c>/api/contas-mensais</c> (spec 005 §3.2), corpo comparado como texto exato.</summary>
[Trait("Category", "Docker")]
public class BillsPostEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string TypeMessage = "Tipo de agendamento inválido. Valores permitidos: FIXO, LEMBRETE_VARIAVEL, PARCELADO";
    private const string PeriodMessage = "Periodicidade inválida. Valores permitidos: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL";

    private readonly BillsHttp _http = new(postgres);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _account = await _data.InsertAccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Body(string extra = "", string type = "FIXO", string periodicity = "MENSAL", string day = "10", string account = "3", string amount = "120") =>
        $$"""{"descricao":"Internet","tipo_agendamento":"{{type}}","periodicidade":"{{periodicity}}","subcategoria_id":59,"conta_id":{{account}},"dia_execucao":{{day}},"valor_previsto":{{amount}}{{extra}}}""";

    private string Mine(string extra = "", string type = "FIXO", string periodicity = "MENSAL", string day = "10", string amount = "120") =>
        Body(extra, type, periodicity, day, _account.ToString(CultureInfo.InvariantCulture), amount);

    private Task<(HttpStatusCode Status, string Body)> Post(string? json) => _http.Post(_user, json);

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await Post(json);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldBe(BillsHttp.Error(message));
        (await Count()).ShouldBe(0);
    }

    private Task<int> Count() => _data.ScalarAsync<int>("SELECT count(*)::int FROM agendamentos");

    private async Task<(HttpStatusCode Status, string Body)> PostWithClock(string json, DateOnly today)
    {
        await using var factory = _http.WithClock(today);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bills")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", MeuSecretario.IntegrationTests.Accounts.AccountsHttp.TokenFor(_user));
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---------- sucesso ----------

    [Fact]
    [Trait("AC", "005-10")]
    public async Task MinimalFixedBody_Returns201WithTheExactBody_AndPersistsOwnership()
    {
        var (status, body) = await Post(Mine());

        status.ShouldBe(HttpStatusCode.Created);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);   // só confere o formato; o dia exato tem teste com relógio falso
        body.ShouldStartWith(
            $$"""{"status":"success","data":{"id":1,"descricao":"Internet","valor_previsto":120,"tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":10,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":59,"subcategoria_nome":"Outros","conta_id":{{_account}},"conta_nome":"Mercado pago","data_inicio":"20""");
        body.ShouldEndWith(""","incluir_na_reserva":false,"total_parcelas":null,"parcelas_executadas":0}}""");
        today.Year.ShouldBeGreaterThan(2000);
        var row = (await _data.QueryAsync<(int User, bool Ativo)>("SELECT usuario_id, ativo FROM agendamentos WHERE id = 1")).Single();
        row.ShouldBe((_user, true));
    }

    [Fact]
    [Trait("AC", "005-22")]
    public async Task StartDate_Absent_IsTodayInSaoPaulo_NotInUtc()
    {
        var (status, body) = await PostWithClock(Mine(), new DateOnly(2026, 10, 4));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"data_inicio\":\"2026-10-04\"");
    }

    [Theory]
    [Trait("AC", "005-22")]
    [InlineData(",\"data_inicio\":null")]
    [InlineData(",\"data_inicio\":\"\"")]
    public async Task StartDate_NullOrEmpty_IsToday(string extra)
    {
        var (status, body) = await PostWithClock(Mine(extra), new DateOnly(2026, 10, 4));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"data_inicio\":\"2026-10-04\"");
    }

    [Fact]
    [Trait("AC", "005-22")]
    public async Task StartDate_Given_IsStored()
    {
        var (status, body) = await Post(Mine(",\"data_inicio\":\"2026-02-01\""));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"data_inicio\":\"2026-02-01\"");
    }

    [Theory]
    [Trait("AC", "005-22")]
    [InlineData("\"amanhã\"")]
    [InlineData("\"2026-02-30\"")]
    [InlineData("123")]
    public async Task StartDate_NotInIsoFormat_Is400(string value)
    {
        await AssertBadRequest(Mine($",\"data_inicio\":{value}"), "Data de início deve estar no formato YYYY-MM-DD");
    }

    [Fact]
    [Trait("AC", "005-81")]
    public async Task Alias_ContasMensais_BehavesTheSame()
    {
        var (status, body) = await _http.Post(_user, Mine(), BillsHttp.Alias);

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"id\":1,");
    }

    [Fact]
    [Trait("AC", "005-27")]
    public async Task AngularCreateBody_Returns201()
    {
        var angular = $$"""{"descricao":"Aluguel","tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":5,"data_inicio":"2026-10-04","subcategoria_id":59,"conta_id":{{_account}},"notificar_antes_dias":3,"incluir_na_reserva":true,"total_parcelas":null,"valor_previsto":1500.5}""";

        var (status, body) = await Post(angular);

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"valor_previsto\":1500.5,");
        body.ShouldContain("\"incluir_na_reserva\":true,");
        body.ShouldContain("\"total_parcelas\":null,");
    }

    [Fact]
    [Trait("AC", "005-28")]
    public async Task CreatedBill_ShowsUpInTheGetAndTheSummary()
    {
        await Post(Mine());

        var (_, list) = await _http.Get(_user);
        var (_, summary) = await _http.Get(_user, "/api/bills/summary");

        list.ShouldContain("\"descricao\":\"Internet\"");
        list.ShouldContain("\"tipo_tran");
        summary.ShouldContain("\"total_despesas_mensais\":120,");
    }

    // ---------- validações ----------

    [Theory]
    [Trait("AC", "005-11")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"abc\"")]
    public async Task InvalidOrEmptyBody_Is400DataMissing_Never500(string? json)
    {
        await AssertBadRequest(json, "Dados não fornecidos");
    }

    [Fact]
    [Trait("AC", "005-11")]
    public async Task BodyWithoutContentType_IsRead()
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Post, "/api/bills", Mine(), _user);

        status.ShouldBe(HttpStatusCode.Created, body);
    }

    [Theory]
    [Trait("AC", "005-13")]
    [InlineData("\"descricao\":\"\"")]
    [InlineData("\"descricao\":\"   \"")]
    [InlineData("\"descricao\":null")]
    [InlineData("\"descricao\":5")]
    public async Task BlankOrNonTextDescription_Is400(string field)
    {
        var json = Mine().Replace("\"descricao\":\"Internet\"", field);
        await AssertBadRequest(json, "Descrição é obrigatória");
    }

    [Fact]
    [Trait("AC", "005-13")]
    public async Task Description_IsTrimmed_AndOver255IsRefused()
    {
        await AssertBadRequest(Mine().Replace("Internet", new string('a', 256)), "Descrição deve ter no máximo 255 caracteres");

        var (status, body) = await Post(Mine().Replace("Internet", "  Luz  "));
        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"descricao\":\"Luz\"");
    }

    [Fact]
    [Trait("AC", "005-13")]
    public async Task Description_Of255CodePointsWithEmoji_IsStoredWhole()
    {
        var text = string.Concat(Enumerable.Repeat("é😀", 127)) + "é";   // 255 pontos de código
        var (status, _) = await Post(Mine().Replace("Internet", text));

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<string>("SELECT descricao FROM agendamentos WHERE id = 1")).ShouldBe(text);
    }

    [Theory]
    [Trait("AC", "005-14")]
    [InlineData("fixo")]
    [InlineData("")]
    [InlineData("MENSAL")]
    public async Task InvalidType_Is400(string type)
    {
        await AssertBadRequest(Mine(type: type), TypeMessage);
    }

    [Fact]
    [Trait("AC", "005-14")]
    public async Task MissingType_Is400_AndPaddedTypeIsAccepted()
    {
        await AssertBadRequest(Mine().Replace("\"tipo_agendamento\":\"FIXO\",", string.Empty), TypeMessage);

        var (status, body) = await Post(Mine(type: " FIXO "));
        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"tipo_agendamento\":\"FIXO\"");
    }

    [Theory]
    [Trait("AC", "005-15")]
    [InlineData("mensal")]
    [InlineData("")]
    [InlineData("BIMESTRAL")]
    public async Task InvalidPeriodicity_Is400(string periodicity)
    {
        await AssertBadRequest(Mine(periodicity: periodicity), PeriodMessage);
    }

    [Fact]
    [Trait("AC", "005-15")]
    public async Task PaddedPeriodicity_IsAccepted()
    {
        var (status, _) = await Post(Mine(periodicity: " MENSAL "));

        status.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("AC", "005-16")]
    [InlineData("\"subcategoria_id\":null", "Subcategoria é obrigatória")]
    [InlineData("\"subcategoria_id\":0", "Subcategoria é obrigatória")]
    [InlineData("\"subcategoria_id\":\"\"", "Subcategoria é obrigatória")]
    [InlineData("\"subcategoria_id\":false", "Subcategoria é obrigatória")]
    [InlineData("\"conta_id\":null", "Conta bancária é obrigatória")]
    [InlineData("\"conta_id\":0", "Conta bancária é obrigatória")]
    public async Task FalsyReferences_Are400(string replacement, string message)
    {
        var key = replacement[..replacement.IndexOf(':')];
        var json = System.Text.RegularExpressions.Regex.Replace(Mine(), key + ":[^,}]+", replacement);

        await AssertBadRequest(json, message);
    }

    [Theory]
    [Trait("AC", "005-17")]
    [InlineData("\"abc\"")]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("-1")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task InvalidDay_Is400(string day)
    {
        await AssertBadRequest(Mine(day: day), "Dia de execução deve ser entre 1 e 31");
    }

    [Theory]
    [Trait("AC", "005-17")]
    [InlineData("\"15\"", 15)]
    [InlineData("15.9", 15)]
    [InlineData("true", 1)]
    [InlineData("31", 31)]
    public async Task DayThatPythonIntAccepts_IsStored(string day, int expected)
    {
        var (status, body) = await Post(Mine(day: day));

        status.ShouldBe(HttpStatusCode.Created, body);
        body.ShouldContain($"\"dia_execucao\":{expected},");
    }

    [Theory]
    [Trait("AC", "005-18")]
    [InlineData("FIXO")]
    [InlineData("PARCELADO")]
    public async Task FixedOrInstallmentWithoutAmount_Is400(string type)
    {
        var json = Mine(",\"total_parcelas\":3", type).Replace(",\"valor_previsto\":120", string.Empty);

        await AssertBadRequest(json, "Valor previsto é obrigatório para contas fixas ou parceladas");
        await AssertBadRequest(Mine(",\"total_parcelas\":3", type, amount: "null"), "Valor previsto é obrigatório para contas fixas ou parceladas");
    }

    [Theory]
    [Trait("AC", "005-18")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("\"abc\"")]
    [InlineData("[]")]
    public async Task FixedAmountNotPositive_Is400(string amount)
    {
        await AssertBadRequest(Mine(amount: amount), "Valor previsto deve ser um número positivo");
    }

    [Theory]
    [Trait("AC", "005-18")]
    [InlineData("\"120.5\"", "120.5")]
    [InlineData("true", "1")]
    public async Task FixedAmountPythonFloatAccepts_IsStored(string amount, string stored)
    {
        var (status, body) = await Post(Mine(amount: amount));

        status.ShouldBe(HttpStatusCode.Created, body);
        body.ShouldContain($"\"valor_previsto\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-18")]
    [InlineData(null, "null")]
    [InlineData("\"abc\"", "null")]
    [InlineData("0", "0")]
    [InlineData("-5", "-5")]
    public async Task VariableReminder_AmountIsOptional(string? amount, string stored)
    {
        var json = amount is null
            ? Mine(type: "LEMBRETE_VARIAVEL").Replace(",\"valor_previsto\":120", string.Empty)
            : Mine(type: "LEMBRETE_VARIAVEL", amount: amount);

        var (status, body) = await Post(json);

        status.ShouldBe(HttpStatusCode.Created, body);
        body.ShouldContain($"\"valor_previsto\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-19")]
    [InlineData("")]
    [InlineData(",\"total_parcelas\":null")]
    [InlineData(",\"total_parcelas\":1")]
    [InlineData(",\"total_parcelas\":0")]
    [InlineData(",\"total_parcelas\":\"x\"")]
    public async Task InstallmentWithoutValidTotal_Is400(string extra)
    {
        await AssertBadRequest(Mine(extra, "PARCELADO"), "Número de parcelas deve ser um inteiro maior ou igual a 2");
    }

    [Theory]
    [Trait("AC", "005-19")]
    [InlineData("2")]
    [InlineData("\"12\"")]
    public async Task Installment_StoresTotal_WithZeroExecuted(string total)
    {
        var (status, body) = await Post(Mine($",\"total_parcelas\":{total}", "PARCELADO"));

        status.ShouldBe(HttpStatusCode.Created, body);
        body.ShouldContain($"\"total_parcelas\":{total.Trim('"')},\"parcelas_executadas\":0}}");
    }

    [Fact]
    [Trait("AC", "005-19")]
    public async Task InstallmentsOfAFixedBill_AreIgnored()
    {
        var (status, body) = await Post(Mine(",\"total_parcelas\":5"));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"total_parcelas\":null,");
    }

    [Theory]
    [Trait("AC", "005-20")]
    [InlineData("13")]
    [InlineData("0")]
    [InlineData("\"x\"")]
    public async Task AnnualWithInvalidMonth_Is400(string month)
    {
        await AssertBadRequest(Mine($",\"mes_execucao\":{month}", periodicity: "ANUAL"), "Mês de execução deve ser entre 1 e 12");
    }

    [Fact]
    [Trait("AC", "005-20")]
    public async Task Annual_WithMonth_IsStored_OtherPeriodicitiesIgnoreIt()
    {
        var (annualStatus, annual) = await Post(Mine(",\"mes_execucao\":3", periodicity: "ANUAL"));
        var (monthlyStatus, monthly) = await Post(Mine(",\"mes_execucao\":5"));

        annualStatus.ShouldBe(HttpStatusCode.Created);
        annual.ShouldContain("\"periodicidade\":\"ANUAL\",\"dia_execucao\":10,\"mes_execucao\":3,");
        monthlyStatus.ShouldBe(HttpStatusCode.Created);
        monthly.ShouldContain("\"mes_execucao\":null,");
    }

    [Theory]
    [Trait("AC", "005-20")]
    [InlineData("")]
    [InlineData(",\"mes_execucao\":null")]
    public async Task AnnualWithoutMonth_Is400_NoLongerAn500(string extra)
    {
        await AssertBadRequest(Mine(extra, periodicity: "ANUAL"), "Mês de execução é obrigatório para periodicidade anual");
    }

    [Theory]
    [Trait("AC", "005-21")]
    [InlineData("", 3)]
    [InlineData(",\"notificar_antes_dias\":\"abc\"", 3)]
    [InlineData(",\"notificar_antes_dias\":null", 3)]
    [InlineData(",\"notificar_antes_dias\":0", 0)]
    [InlineData(",\"notificar_antes_dias\":-2", -2)]
    [InlineData(",\"notificar_antes_dias\":\"7\"", 7)]
    public async Task NotifyDays_DefaultsToThree(string extra, int stored)
    {
        var (status, body) = await Post(Mine(extra));

        status.ShouldBe(HttpStatusCode.Created, body);
        body.ShouldContain($"\"notificar_antes_dias\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-23")]
    [InlineData("", "false")]
    [InlineData(",\"incluir_na_reserva\":true", "true")]
    [InlineData(",\"incluir_na_reserva\":null", "false")]
    public async Task IncludeInReserve_DefaultsToFalse(string extra, string stored)
    {
        var (status, body) = await Post(Mine(extra));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain($"\"incluir_na_reserva\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-23")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public async Task IncludeInReserve_NotABoolean_Is400(string value)
    {
        await AssertBadRequest(Mine($",\"incluir_na_reserva\":{value}"), "Campo 'incluir_na_reserva' deve ser verdadeiro ou falso");
    }

    [Fact]
    [Trait("AC", "005-12")]
    public async Task WithTwoErrors_TheFirstInTheSpecOrderAnswers()
    {
        await AssertBadRequest(
            Mine().Replace("\"descricao\":\"Internet\"", "\"descricao\":\"\"").Replace("FIXO", "X"), "Descrição é obrigatória");
        await AssertBadRequest(Mine(type: "X", periodicity: "X"), TypeMessage);
        await AssertBadRequest(Mine(day: "0").Replace(",\"valor_previsto\":120", string.Empty), "Dia de execução deve ser entre 1 e 31");
    }

    // ---------- banco ----------

    [Fact]
    [Trait("AC", "005-12")]
    [Trait("AC", "005-24")]
    public async Task AccountOfAnotherUserMissingOrInactive_Is404_AfterAllValidations_AndNothingIsWritten()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Dele");
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);

        foreach (var account in new[] { foreign, inactive, 99999 })
        {
            var (status, body) = await Post(Body(account: account.ToString(CultureInfo.InvariantCulture)));

            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(BillsHttp.Error("Conta bancária não encontrada"));
        }

        (await Count()).ShouldBe(0);
        // 400 antes do 404: o dia inválido responde primeiro mesmo com a conta alheia
        var (invalidStatus, invalid) = await Post(Body(day: "0", account: foreign.ToString(CultureInfo.InvariantCulture)));
        invalidStatus.ShouldBe(HttpStatusCode.BadRequest);
        invalid.ShouldBe(BillsHttp.Error("Dia de execução deve ser entre 1 e 31"));
    }

    [Fact]
    [Trait("AC", "005-25")]
    public async Task OwnerActiveAndExecutedInTheBody_AreIgnored()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");

        var (status, body) = await Post(Mine($",\"usuario_id\":{other},\"ativo\":false,\"parcelas_executadas\":9,\"id\":1"));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"parcelas_executadas\":0}");
        var row = (await _data.QueryAsync<(int User, bool Ativo, int Executed)>("SELECT usuario_id, ativo, parcelas_executadas FROM agendamentos")).Single();
        row.ShouldBe((_user, true, 0));
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task Subcategory_Missing_OrOfAnotherUser_Is404()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertUserSubcategoryAsync(other, "Dele");

        foreach (var subcategory in new[] { 999999, foreign })
        {
            var (status, body) = await Post(Mine().Replace("\"subcategoria_id\":59", $"\"subcategoria_id\":{subcategory}"));

            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(BillsHttp.Error("Subcategoria não encontrada"));
        }

        (await Count()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task Subcategory_OwnedByTheUser_IsAccepted()
    {
        var own = await _data.InsertUserSubcategoryAsync(_user, "Minha");

        var (status, body) = await Post(Mine().Replace("\"subcategoria_id\":59", $"\"subcategoria_id\":{own}"));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"subcategoria_nome\":\"Minha\"");
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task AccountIsCheckedBeforeTheSubcategory()
    {
        var (status, body) = await Post(Body(account: "99999").Replace("\"subcategoria_id\":59", "\"subcategoria_id\":999999"));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta bancária não encontrada"));
    }
}
