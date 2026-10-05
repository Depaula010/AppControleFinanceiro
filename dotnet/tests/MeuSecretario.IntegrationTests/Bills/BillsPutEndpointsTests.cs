using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary><c>PUT /api/bills/{id}</c> e <c>/api/contas-mensais/{id}</c> (spec 005 §3.3), corpo comparado como texto exato.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class BillsPutEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BillsHttp _http = new(postgres);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;
    private int _bill;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _account = await _data.InsertAccountAsync(_user);
        _bill = await _data.InsertScheduleAsync(_user, _account, "Aluguel", valor: 1500m, dia: 5, incluirNaReserva: true);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Put(string? json, long? id = null) => _http.Put(_user, id ?? _bill, json);

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await Put(json);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldBe(BillsHttp.Error(message));
    }

    private async Task AssertUntouched()
    {
        var row = (await _data.QueryAsync<(string Descricao, decimal Valor, int Dia)>(
            "SELECT descricao, valor_previsto, dia_execucao FROM agendamentos WHERE id = @_bill", new { _bill })).Single();
        row.ShouldBe(("Aluguel", 1500m, 5));
    }

    private static string Id(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Task<T> Stored<T>(string column) =>
        _data.ScalarAsync<T>($"SELECT {column} FROM agendamentos WHERE id = @_bill", new { _bill });

    // ============================== sucesso ==============================

    [Fact]
    [Trait("AC", "005-42")]
    public async Task OnlyTheDescription_ChangesOnlyIt_Returns200WithTheExactBody()
    {
        var (status, body) = await Put("""{"descricao":"Nova"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$$"""{"status":"success","data":{"id":{{{_bill}}},"descricao":"Nova","valor_previsto":1500,"tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":5,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":59,"subcategoria_nome":"Outros","conta_id":{{{_account}}},"conta_nome":"Mercado pago","data_inicio":"2026-01-05","incluir_na_reserva":true,"total_parcelas":null,"parcelas_executadas":0}}""");
    }

    [Fact]
    [Trait("AC", "005-42")]
    public async Task UpdatedAt_Advances()
    {
        await _data.ExecuteAsync("UPDATE agendamentos SET updated_at = '2020-01-01' WHERE id = @_bill", new { _bill });

        await Put("""{"descricao":"Nova"}""");

        (await Stored<bool>("updated_at > '2020-01-01'")).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "005-81")]
    public async Task Alias_ContasMensais_BehavesTheSame()
    {
        var (status, body) = await _http.Put(_user, _bill, """{"descricao":"Nova"}""", BillsHttp.Alias);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"descricao\":\"Nova\"");
    }

    // ============================== validações ==============================

    [Theory]
    [Trait("AC", "005-40")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"abc\"")]
    public async Task InvalidOrEmptyBody_Is400DataMissing_EvenWhenTheBillDoesNotExist(string? json)
    {
        await AssertBadRequest(json, "Dados não fornecidos");

        var (status, body) = await Put(json, id: 99999);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(BillsHttp.Error("Dados não fornecidos"));
    }

    [Fact]
    [Trait("AC", "005-41")]
    public async Task MissingInactiveOrForeignBill_Is404TheSame_AndTheOthersStayIntact()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var foreign = await _data.InsertScheduleAsync(other, otherAccount, "alheio");
        var inactive = await _data.InsertScheduleAsync(_user, _account, "x", ativo: false);

        foreach (var id in new long[] { 99999, foreign, inactive })
        {
            var (status, body) = await Put("""{"descricao":"Y"}""", id);

            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
        }

        (await _data.ScalarAsync<string>("SELECT descricao FROM agendamentos WHERE id = @foreign", new { foreign })).ShouldBe("alheio");
    }

    [Theory]
    [Trait("AC", "005-42")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    [InlineData("5")]
    public async Task BlankNullOrNonTextDescription_Is400_NeverStoresNone(string value)
    {
        await AssertBadRequest($$"""{"descricao":{{value}}}""", "Descrição não pode ser vazia");
        await AssertUntouched();
    }

    [Fact]
    [Trait("AC", "005-42")]
    public async Task Description_IsTrimmed_AndOver255IsRefused()
    {
        await AssertBadRequest($$"""{"descricao":"{{new string('a', 256)}}"}""", "Descrição deve ter no máximo 255 caracteres");

        var (status, _) = await Put("""{"descricao":"  Luz  "}""");
        status.ShouldBe(HttpStatusCode.OK);
        (await Stored<string>("descricao")).ShouldBe("Luz");
    }

    [Theory]
    [Trait("AC", "005-43")]
    [InlineData("\"tipo_agendamento\":\"X\"")]
    [InlineData("\"tipo_agendamento\":\"fixo\"")]
    [InlineData("\"tipo_agendamento\":\" FIXO \"")]
    [InlineData("\"tipo_agendamento\":null")]
    public async Task InvalidType_Is400_WithoutTrim(string field)
    {
        await AssertBadRequest("{" + field + "}", "Tipo inválido. Valores: FIXO, LEMBRETE_VARIAVEL, PARCELADO");
    }

    [Theory]
    [Trait("AC", "005-43")]
    [InlineData("\"periodicidade\":\"mensal\"")]
    [InlineData("\"periodicidade\":\" MENSAL \"")]
    [InlineData("\"periodicidade\":\"BIMESTRAL\"")]
    public async Task InvalidPeriodicity_Is400(string field)
    {
        await AssertBadRequest("{" + field + "}", "Periodicidade inválida. Valores: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL");
    }

    [Theory]
    [Trait("AC", "005-44")]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    public async Task InvalidDay_Is400(string day)
    {
        await AssertBadRequest($$"""{"dia_execucao":{{day}}}""", "Dia de execução deve ser entre 1 e 31");
    }

    [Fact]
    [Trait("AC", "005-44")]
    public async Task DayAsText_IsAccepted()
    {
        var (status, body) = await Put("""{"dia_execucao":"20"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"dia_execucao\":20,");
    }

    [Theory]
    [Trait("AC", "005-45")]
    [InlineData("13")]
    [InlineData("\"x\"")]
    public async Task InvalidMonth_Is400(string month)
    {
        await AssertBadRequest($$"""{"mes_execucao":{{month}}}""", "Mês deve ser entre 1 e 12");
    }

    [Fact]
    [Trait("AC", "005-45")]
    public async Task Month_OnAnAnnualBill_IsStored_AndNullClearsOnlyWhenNoLongerAnnual()
    {
        var annual = await _data.InsertScheduleAsync(_user, _account, "IPTU", periodicidade: "ANUAL", mes: 3);

        var (status, body) = await _http.Put(_user, annual, """{"mes_execucao":6}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"mes_execucao\":6,");
    }

    [Theory]
    [Trait("AC", "005-45")]
    [InlineData("6")]
    [InlineData("null")]
    public async Task Month_OnANonAnnualBill_IsClearedAutomatically(string month)
    {
        var (status, body) = await Put($$"""{"mes_execucao":{{month}}}""");

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"mes_execucao\":null,");
    }

    [Theory]
    [Trait("AC", "005-46")]
    [InlineData("\"abc\"")]
    [InlineData("[]")]
    public async Task UnreadableAmount_Is400(string amount)
    {
        await AssertBadRequest($$"""{"valor_previsto":{{amount}}}""", "Valor previsto inválido");
    }

    [Theory]
    [Trait("AC", "005-46")]
    [InlineData("250.75", "250.75")]
    [InlineData("\"99.9\"", "99.9")]
    public async Task Amount_IsStored(string amount, string stored)
    {
        var (status, body) = await Put($$"""{"valor_previsto":{{amount}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"valor_previsto\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-46")]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("null")]
    public async Task VariableReminder_AcceptsZeroNegativeAndNullAmount(string amount)
    {
        var variable = await _data.InsertScheduleAsync(_user, _account, "Luz", tipo: "LEMBRETE_VARIAVEL", valor: 100m);

        var (status, body) = await _http.Put(_user, variable, $$"""{"valor_previsto":{{amount}}}""");

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain($"\"valor_previsto\":{amount},");
    }

    [Theory]
    [Trait("AC", "005-46")]
    [InlineData("0", "Valor previsto deve ser um número positivo")]
    [InlineData("-10", "Valor previsto deve ser um número positivo")]
    [InlineData("null", "Valor previsto é obrigatório para contas fixas ou parceladas")]
    public async Task FixedBill_ResultingWithoutAPositiveAmount_Is400(string amount, string message)
    {
        await AssertBadRequest($$"""{"valor_previsto":{{amount}}}""", message);
        await AssertUntouched();
    }

    [Fact]
    [Trait("AC", "005-47")]
    public async Task Account_OwnedAndActive_Updates_AndTheNameFollows()
    {
        var other = await _data.InsertAccountAsync(_user, "Nubank");

        var (status, body) = await Put($$"""{"conta_id":{{other}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"conta_id\":{other},\"conta_nome\":\"Nubank\",");
    }

    [Fact]
    [Trait("AC", "005-47")]
    public async Task Account_OfAnotherUserMissingInactiveOrNull_Is404()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Dele");
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);

        foreach (var account in new[] { Id(foreign), Id(inactive), "99999", "null" })
        {
            var (status, body) = await Put($$"""{"conta_id":{{account}}}""");

            status.ShouldBe(HttpStatusCode.NotFound, account);
            body.ShouldBe(BillsHttp.Error("Conta bancária não encontrada"));
        }

        await AssertUntouched();
    }

    [Fact]
    [Trait("AC", "005-48")]
    public async Task Subcategory_Existing_Updates_AndTheNameFollows()
    {
        var own = await _data.InsertUserSubcategoryAsync(_user, "Minha");

        var (status, body) = await Put($$"""{"subcategoria_id":{{own}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"subcategoria_id\":{own},\"subcategoria_nome\":\"Minha\",");
    }

    [Fact]
    [Trait("AC", "005-48")]
    public async Task Subcategory_MissingNullOrOfAnotherUser_Is404()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertUserSubcategoryAsync(other, "Dele");

        foreach (var subcategory in new[] { "999999", "null", Id(foreign) })
        {
            var (status, body) = await Put($$"""{"subcategoria_id":{{subcategory}}}""");

            status.ShouldBe(HttpStatusCode.NotFound, subcategory);
            body.ShouldBe(BillsHttp.Error("Subcategoria não encontrada"));
        }

        await AssertUntouched();
    }

    [Theory]
    [Trait("AC", "005-49")]
    [InlineData("\"7\"", 7)]
    [InlineData("0", 0)]
    public async Task NotifyDays_Readable_IsStored(string value, int stored)
    {
        var (status, body) = await Put($$"""{"notificar_antes_dias":{{value}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"notificar_antes_dias\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-49")]
    [InlineData("\"abc\"")]
    [InlineData("null")]
    public async Task NotifyDays_Unreadable_IsIgnored_NoMoreA500(string value)
    {
        var (status, body) = await Put($$"""{"notificar_antes_dias":{{value}},"descricao":"Nova"}""");

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"descricao\":\"Nova\"");
        body.ShouldContain("\"notificar_antes_dias\":3,");
    }

    [Fact]
    [Trait("AC", "005-49")]
    public async Task NotifyDays_Unreadable_Alone_IsNothingToUpdate()
    {
        await AssertBadRequest("""{"notificar_antes_dias":"abc"}""", "Nenhum campo para atualizar");
    }

    [Theory]
    [Trait("AC", "005-50")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public async Task IncludeInReserve_Boolean_IsStored(string value, string stored)
    {
        var (status, body) = await Put($$"""{"incluir_na_reserva":{{value}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"incluir_na_reserva\":{stored},");
    }

    [Theory]
    [Trait("AC", "005-50")]
    [InlineData("\"false\"")]
    [InlineData("null")]
    [InlineData("1")]
    public async Task IncludeInReserve_NotABoolean_Is400(string value)
    {
        await AssertBadRequest($$"""{"incluir_na_reserva":{{value}}}""", "Campo 'incluir_na_reserva' deve ser verdadeiro ou falso");
    }

    [Theory]
    [Trait("AC", "005-51")]
    [InlineData("\"amanhã\"")]
    [InlineData("null")]
    [InlineData("\"2026-02-30\"")]
    public async Task StartDate_NotInIsoFormat_OrNull_Is400(string value)
    {
        await AssertBadRequest($$"""{"data_inicio":{{value}}}""", "Data de início deve estar no formato YYYY-MM-DD");
    }

    [Fact]
    [Trait("AC", "005-51")]
    public async Task StartDate_Iso_IsStored()
    {
        var (status, body) = await Put("""{"data_inicio":"2026-03-01"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"data_inicio\":\"2026-03-01\"");
    }

    [Theory]
    [Trait("AC", "005-52")]
    [InlineData("1")]
    [InlineData("\"x\"")]
    public async Task InvalidInstallments_Is400(string value)
    {
        await AssertBadRequest($$"""{"total_parcelas":{{value}}}""", "Número de parcelas deve ser um inteiro maior ou igual a 2");
    }

    [Fact]
    [Trait("AC", "005-52")]
    public async Task Installments_OnAnInstallmentBill_AreStored()
    {
        var installment = await _data.InsertScheduleAsync(_user, _account, "Sofá", tipo: "PARCELADO", totalParcelas: 6, parcelasExecutadas: 2);

        var (status, body) = await _http.Put(_user, installment, """{"total_parcelas":8}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"total_parcelas\":8,\"parcelas_executadas\":2}");
    }

    [Fact]
    [Trait("AC", "005-52")]
    public async Task InstallmentTotal_BelowTheExecuted_Is400()
    {
        var installment = await _data.InsertScheduleAsync(_user, _account, "Sofá", tipo: "PARCELADO", totalParcelas: 6, parcelasExecutadas: 4);

        var (status, body) = await _http.Put(_user, installment, """{"total_parcelas":3}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(BillsHttp.Error("Número de parcelas não pode ser menor que as parcelas já executadas"));
    }

    [Fact]
    [Trait("AC", "005-52")]
    public async Task Installments_OnANonInstallmentBill_AreClearedAutomatically()
    {
        var (status, body) = await Put("""{"total_parcelas":3}""");

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"total_parcelas\":null,");
    }

    [Fact]
    [Trait("AC", "005-53")]
    public async Task SwitchingToInstallment_NeedsTheTotalInTheSameBody()
    {
        await AssertBadRequest("""{"tipo_agendamento":"PARCELADO"}""", "Número de parcelas é obrigatório para contas parceladas");

        var (status, body) = await Put("""{"tipo_agendamento":"PARCELADO","total_parcelas":6}""");
        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"tipo_agendamento\":\"PARCELADO\"");
        body.ShouldContain("\"total_parcelas\":6,\"parcelas_executadas\":0}");
    }

    [Fact]
    [Trait("AC", "005-53")]
    public async Task OnAnAlreadyInstallmentBill_OtherFieldsDoNotNeedTheTotal()
    {
        var installment = await _data.InsertScheduleAsync(_user, _account, "Sofá", tipo: "PARCELADO", totalParcelas: 6);

        var (status, _) = await _http.Put(_user, installment, """{"descricao":"x"}""");

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("AC", "005-54")]
    [InlineData("""{"foo":1}""")]
    [InlineData("""{"parcelas_executadas":3,"ativo":false}""")]
    [InlineData("""{"usuario_id":99}""")]
    public async Task OnlyUnrecognizedKeys_Is400NothingToUpdate(string json)
    {
        await AssertBadRequest(json, "Nenhum campo para atualizar");
    }

    [Fact]
    [Trait("AC", "005-55")]
    public async Task WithTwoErrors_TheFirstInTheSpecOrderAnswers_AndNothingIsWritten()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Dele");

        await AssertBadRequest($$"""{"dia_execucao":0,"conta_id":{{foreign}},"descricao":"Mudou"}""", "Dia de execução deve ser entre 1 e 31");

        await AssertUntouched();
    }

    [Fact]
    [Trait("AC", "005-57")]
    public async Task DescriptionIsCheckedBeforeTheType()
    {
        await AssertBadRequest("""{"tipo_agendamento":"X","descricao":""}""", "Descrição não pode ser vazia");
    }

    // ============================== estado final (desvio 074) ==============================

    [Fact]
    [Trait("AC", "005-56")]
    public async Task AngularEdit_OfAFixedMonthlyBill_Is200()
    {
        var angular = $$"""{"descricao":"Aluguel novo","tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":6,"data_inicio":"2026-01-05","subcategoria_id":59,"conta_id":{{_account}},"notificar_antes_dias":3,"incluir_na_reserva":true,"total_parcelas":null,"valor_previsto":1600}""";

        var (status, body) = await Put(angular);

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"descricao\":\"Aluguel novo\"");
        body.ShouldContain("\"valor_previsto\":1600,");
    }

    [Fact]
    [Trait("AC", "005-56")]
    public async Task AngularEdit_AnnualToMonthly_NoLongerFails_AndClearsTheMonth()
    {
        var annual = await _data.InsertScheduleAsync(_user, _account, "IPTU", periodicidade: "ANUAL", mes: 3);
        var angular = $$"""{"descricao":"IPTU","tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":5,"data_inicio":"2026-01-05","subcategoria_id":59,"conta_id":{{_account}},"notificar_antes_dias":3,"incluir_na_reserva":false,"total_parcelas":null,"valor_previsto":100}""";

        var (status, body) = await _http.Put(_user, annual, angular);

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"periodicidade\":\"MENSAL\",\"dia_execucao\":5,\"mes_execucao\":null,");
    }

    [Fact]
    [Trait("AC", "005-56")]
    public async Task AngularEdit_LeavingInstallmentWithExecuted_NoLongerFails_AndZeroesTheHistory()
    {
        var installment = await _data.InsertScheduleAsync(_user, _account, "Sofá", tipo: "PARCELADO", totalParcelas: 6, parcelasExecutadas: 2);
        var angular = $$"""{"descricao":"Sofá","tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":5,"data_inicio":"2026-01-05","subcategoria_id":59,"conta_id":{{_account}},"notificar_antes_dias":3,"incluir_na_reserva":false,"total_parcelas":null,"valor_previsto":100}""";

        var (status, body) = await _http.Put(_user, installment, angular);

        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"tipo_agendamento\":\"FIXO\"");
        body.ShouldContain("\"total_parcelas\":null,\"parcelas_executadas\":0}");
    }

    [Fact]
    [Trait("AC", "005-58")]
    public async Task SwitchingToAnnual_WithoutMonth_Is400_WithMonth_Is200()
    {
        await AssertBadRequest("""{"periodicidade":"ANUAL"}""", "Mês de execução é obrigatório para periodicidade anual");
        await AssertUntouched();

        var (status, body) = await Put("""{"periodicidade":"ANUAL","mes_execucao":3}""");
        status.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldContain("\"periodicidade\":\"ANUAL\",\"dia_execucao\":5,\"mes_execucao\":3,");
    }

    [Fact]
    [Trait("AC", "005-58")]
    public async Task ClearingTheMonthOfAnAnnualBill_Is400()
    {
        var annual = await _data.InsertScheduleAsync(_user, _account, "IPTU", periodicidade: "ANUAL", mes: 3);

        var (status, body) = await _http.Put(_user, annual, """{"mes_execucao":null}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(BillsHttp.Error("Mês de execução é obrigatório para periodicidade anual"));
    }

    [Fact]
    [Trait("AC", "005-46")]
    public async Task SwitchingAVariableReminderWithoutAmountToFixed_Is400()
    {
        var variable = await _data.InsertScheduleAsync(_user, _account, "Luz", tipo: "LEMBRETE_VARIAVEL", valor: null);

        var (status, body) = await _http.Put(_user, variable, """{"tipo_agendamento":"FIXO"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(BillsHttp.Error("Valor previsto é obrigatório para contas fixas ou parceladas"));
    }
}
