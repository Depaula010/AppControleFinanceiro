using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Spec 002 §3.2: <c>POST /api/accounts</c> e <c>/api/contas</c>, corpo comparado como texto exato.</summary>
[Trait("Category", "Docker")]
public class AccountsPostEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string TypeMessage =
        "Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro";

    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Post(string? json) => _http.Post(_user, json);

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await Post(json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error(message));
    }

    private Task<int> CountAccounts() => _data.ScalarAsync<int>("SELECT count(*)::int FROM contas");

    // ---------- sucesso ----------

    [Fact]
    [Trait("AC", "002-10")]
    public async Task MinimalBody_Returns201WithTheExactBody_AndPersistsDefaults()
    {
        var (status, body) = await Post("""{"nome_conta":"Nubank","tipo_conta":"Conta Corrente"}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe(
            """{"status":"success","message":"Conta criada com sucesso","data":{"id":1,"nome_conta":"Nubank","tipo_conta":"Conta Corrente","saldo_inicial":0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}}""");
        var row = (await _data.QueryAsync<(int User, bool Ativa, bool Inclui, int? Ordem)>(
            "SELECT usuario_id, ativa, inclui_saldo_total, ordem FROM contas WHERE id = 1")).Single();
        row.ShouldBe((_user, true, true, null));
    }

    [Fact]
    [Trait("AC", "002-11")]
    public async Task Card_WithLimitAndDays_Returns201()
    {
        var (status, body) = await Post(
            """{"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","limite_credito":5000,"dia_fechamento":5,"dia_vencimento":10}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe(
            """{"status":"success","message":"Conta criada com sucesso","data":{"id":1,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":5000,"dia_fechamento":5,"dia_vencimento":10}}""");
    }

    [Fact]
    [Trait("AC", "002-11")]
    public async Task ZeroLimit_IsStoredAsZero_ButAnsweredAsNull()
    {
        var (status, body) = await Post(
            """{"nome_conta":"Zero","tipo_conta":"Cartão de Crédito","limite_credito":0,"dia_fechamento":5,"dia_vencimento":10}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"limite_credito\":null");
        (await _data.ScalarAsync<decimal>("SELECT limite_credito FROM contas WHERE id = 1")).ShouldBe(0.00m);
    }

    [Fact]
    [Trait("AC", "002-82")]
    public async Task Alias_Contas_BehavesTheSame()
    {
        var (status, body) = await _http.Post(_user, """{"nome_conta":"Via alias","tipo_conta":"Dinheiro"}""", "/api/contas");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"nome_conta\":\"Via alias\"");
        (await _http.Post(_user, """{"nome_conta":"Via alias","tipo_conta":"Dinheiro"}""", "/api/contas")).Body
            .ShouldBe(AccountsHttp.Error("Já existe uma conta com este nome"));
    }

    // ---------- corpo ----------

    [Theory]
    [Trait("AC", "002-12")]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[1]")]
    [InlineData("\"abc\"")]
    [InlineData("0")]
    public async Task InvalidOrEmptyBody_Returns400NotFiveHundred(string json)
    {
        await AssertBadRequest(json, "Dados não fornecidos");
        (await CountAccounts()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "002-12")]
    public async Task NoBodyAtAll_Returns400()
    {
        await AssertBadRequest(null, "Dados não fornecidos");
    }

    [Fact]
    [Trait("AC", "002-12")]
    public async Task JsonSentWithoutJsonContentType_IsStillRead()
    {
        var (status, _) = await _http.Post(_user, """{"nome_conta":"Texto","tipo_conta":"Dinheiro"}""", contentType: "text/plain");

        status.ShouldBe(HttpStatusCode.Created);
    }

    // ---------- nome e tipo ----------

    [Theory]
    [Trait("AC", "002-13")]
    [InlineData("""{"tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":"","tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":"   ","tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":null,"tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":123,"tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":["a"],"tipo_conta":"Dinheiro"}""")]
    [InlineData("""{"nome_conta":"  ","tipo_conta":"invalido"}""")]
    public async Task MissingOrBlankOrNonTextName_Returns400(string json)
    {
        await AssertBadRequest(json, "Nome da conta é obrigatório");
    }

    [Theory]
    [Trait("AC", "002-14")]
    [InlineData("""{"nome_conta":"A"}""")]
    [InlineData("""{"nome_conta":"A","tipo_conta":""}""")]
    [InlineData("""{"nome_conta":"A","tipo_conta":"  "}""")]
    [InlineData("""{"nome_conta":"A","tipo_conta":null}""")]
    [InlineData("""{"nome_conta":"A","tipo_conta":7}""")]
    public async Task MissingOrBlankOrNonTextType_Returns400(string json)
    {
        await AssertBadRequest(json, "Tipo da conta é obrigatório");
    }

    [Theory]
    [Trait("AC", "002-15")]
    [InlineData("corrente")]
    [InlineData("conta corrente")]
    [InlineData("Conta  Corrente")]
    [InlineData("Cartao de Credito")]
    public async Task InvalidType_Returns400WithTheSixValues(string type)
    {
        await AssertBadRequest($$"""{"nome_conta":"A","tipo_conta":"{{type}}"}""", TypeMessage);
    }

    [Fact]
    [Trait("AC", "002-15")]
    public async Task TypeWithSpacesAtTheEnds_IsAcceptedAndStoredTrimmed()
    {
        var (status, _) = await Post("""{"nome_conta":"A","tipo_conta":" Dinheiro "}""");

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<string>("SELECT tipo_conta FROM contas WHERE id = 1")).ShouldBe("Dinheiro");
    }

    [Fact]
    [Trait("AC", "002-16")]
    public async Task Name_IsTrimmed_AndCutAt100CodePoints()
    {
        await Post("""{"nome_conta":"  Nubank  ","tipo_conta":"Dinheiro"}""");
        var longName = new string('á', 80) + string.Concat(Enumerable.Repeat("😀", 40));
        var (status, body) = await Post($$"""{"nome_conta":"{{longName}}","tipo_conta":"Dinheiro"}""");

        status.ShouldBe(HttpStatusCode.Created);
        var expected = new string('á', 80) + string.Concat(Enumerable.Repeat("😀", 20));
        // O emoji sai escapado como par substituto (😀): compara o valor, não o texto.
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("nome_conta").GetString().ShouldBe(expected);
        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = 1")).ShouldBe("Nubank");
        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = 2")).ShouldBe(expected);
    }

    // ---------- números ----------

    [Theory]
    [Trait("AC", "002-17")]
    [InlineData("\"12.5\"", 12.5)]
    [InlineData("12", 12)]
    [InlineData("-3.5", -3.5)]
    [InlineData("true", 1)]
    [InlineData("\" 7 \"", 7)]
    [InlineData("null", 0)]
    public async Task Balance_AcceptsWhatPythonFloatAccepts(string value, double expected)
    {
        var (status, _) = await Post($$"""{"nome_conta":"S","tipo_conta":"Dinheiro","saldo_inicial":{{value}}}""");

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<decimal>("SELECT saldo_inicial FROM contas WHERE id = 1")).ShouldBe((decimal)expected);
    }

    [Theory]
    [Trait("AC", "002-17")]
    [Trait("AC", "002-28")]
    [InlineData("\"abc\"")]
    [InlineData("\"1,5\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("1e30")]
    [InlineData("\"inf\"")]
    [InlineData("\"nan\"")]
    [InlineData("10000000000000")]
    public async Task InvalidOrExtremeBalance_Returns400(string value)
    {
        await AssertBadRequest($$"""{"nome_conta":"S","tipo_conta":"Dinheiro","saldo_inicial":{{value}}}""", "Saldo inicial deve ser um número válido");
        (await CountAccounts()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "002-28")]
    public async Task LargestValueThatFitsNumeric15x2_IsAccepted()
    {
        var (status, _) = await Post("""{"nome_conta":"S","tipo_conta":"Dinheiro","saldo_inicial":9999999999999.99}""");

        status.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("AC", "002-28")]
    [InlineData("\"abc\"")]
    [InlineData("1e30")]
    [InlineData("\"nan\"")]
    public async Task InvalidOrExtremeLimit_Returns400_InAnyType(string value)
    {
        await AssertBadRequest($$"""{"nome_conta":"L","tipo_conta":"Dinheiro","limite_credito":{{value}}}""", "Limite de crédito deve ser um número válido");
    }

    [Theory]
    [Trait("AC", "002-18")]
    [InlineData("""{"limite_credito":"abc","dia_fechamento":5,"dia_vencimento":10}""", "Limite de crédito deve ser um número válido")]
    [InlineData("""{"dia_fechamento":0,"dia_vencimento":10}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":32,"dia_vencimento":10}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":"abc","dia_vencimento":10}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":[],"dia_vencimento":10}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":5,"dia_vencimento":0}""", "Dia de vencimento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":5,"dia_vencimento":"x"}""", "Dia de vencimento deve ser entre 1 e 31")]
    [InlineData("""{"limite_credito":"abc","dia_fechamento":0,"dia_vencimento":0}""", "Limite de crédito deve ser um número válido")]
    [InlineData("""{"dia_fechamento":0,"dia_vencimento":0}""", "Dia de fechamento deve ser entre 1 e 31")]
    public async Task CardValidation_FollowsTheSpecOrder(string extra, string message)
    {
        await AssertBadRequest($$"""{"nome_conta":"Cartão","tipo_conta":"Cartão de Crédito",{{extra[1..]}}""", message);
        (await CountAccounts()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public async Task Days_AreConvertedLikePythonInt()
    {
        var (status, body) = await Post(
            """{"nome_conta":"C","tipo_conta":"Cartão de Crédito","dia_fechamento":10.9,"dia_vencimento":"12"}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"dia_fechamento\":10,\"dia_vencimento\":12");

        var (_, bool1) = await Post("""{"nome_conta":"C2","tipo_conta":"Cartão de Crédito","dia_fechamento":true,"dia_vencimento":true}""");
        bool1.ShouldContain("\"dia_fechamento\":1,\"dia_vencimento\":1");
    }

    [Fact]
    [Trait("AC", "002-19")]
    public async Task NonCardAccount_AcceptsALimit()
    {
        var (status, body) = await Post("""{"nome_conta":"D","tipo_conta":"Dinheiro","limite_credito":500}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"limite_credito\":500");
    }

    [Theory]
    [Trait("AC", "002-19")]
    [InlineData("Dinheiro", "\"dia_fechamento\":5")]
    [InlineData("Conta Corrente", "\"dia_vencimento\":10")]
    [InlineData("Conta Corrente", "\"dia_fechamento\":\"abc\"")]
    public async Task DaysOutsideACard_Return400AndSaveNothing(string type, string days)
    {
        await AssertBadRequest(
            $$"""{"nome_conta":"X","tipo_conta":"{{type}}",{{days}}}""",
            "Dia de fechamento e vencimento só se aplicam a Cartão de Crédito");
        (await CountAccounts()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "002-19")]
    public async Task NullDaysOutsideACard_AreAccepted()
    {
        var (status, _) = await Post("""{"nome_conta":"X","tipo_conta":"Dinheiro","dia_fechamento":null,"dia_vencimento":null}""");

        status.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("AC", "002-29")]
    [InlineData("")]
    [InlineData(""","dia_fechamento":5""")]
    [InlineData(""","dia_vencimento":10""")]
    [InlineData(""","dia_fechamento":null,"dia_vencimento":10""")]
    [InlineData(""","dia_fechamento":5,"dia_vencimento":null""")]
    public async Task CardWithoutBothDays_Returns400(string days)
    {
        await AssertBadRequest(
            $$"""{"nome_conta":"Cartão","tipo_conta":"Cartão de Crédito"{{days}}}""",
            "Cartão de Crédito exige dia de fechamento e dia de vencimento");
        (await CountAccounts()).ShouldBe(0);
    }

    // ---------- nome duplicado e reativação ----------

    [Fact]
    [Trait("AC", "002-20")]
    public async Task DuplicateActiveName_Returns400_ButOtherCaseIsAnotherName()
    {
        await Post("""{"nome_conta":"Nubank","tipo_conta":"Dinheiro"}""");

        await AssertBadRequest("""{"nome_conta":"Nubank","tipo_conta":"Conta Corrente"}""", "Já existe uma conta com este nome");
        var (status, _) = await Post("""{"nome_conta":"nubank","tipo_conta":"Dinheiro"}""");

        status.ShouldBe(HttpStatusCode.Created);
        (await CountAccounts()).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "002-21")]
    public async Task NameOfAnInactiveAccount_ReactivatesItWithTheBodyValues()
    {
        var old = await _data.InsertAccountAsync(
            _user, "Nubank", "Cartão de Crédito", 99m, ativa: false, fechamento: 5, vencimento: 10, cor: "#000000", icone: "old", limite: 1m);

        var (status, body) = await Post("""{"nome_conta":"Nubank","tipo_conta":"Dinheiro","saldo_inicial":7}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe(
            $$$"""{"status":"success","message":"Conta criada com sucesso","data":{"id":{{{old}}},"nome_conta":"Nubank","tipo_conta":"Dinheiro","saldo_inicial":7,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}}""");
        (await CountAccounts()).ShouldBe(1);
        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @old", new { old })).ShouldBeTrue();
        (await _http.Get(_user)).Body.ShouldContain($"\"id\":{old},\"nome\":\"Nubank\"");
    }

    [Fact]
    [Trait("AC", "002-22")]
    public async Task SameNameInAnotherUser_IsIndependent()
    {
        var other = await _data.InsertUserAsync("5511900000002");
        await _data.InsertAccountAsync(other, "Nubank");
        await _data.InsertAccountAsync(other, "Antiga", ativa: false);

        var (a, _) = await Post("""{"nome_conta":"Nubank","tipo_conta":"Dinheiro"}""");
        var (b, _) = await Post("""{"nome_conta":"Antiga","tipo_conta":"Dinheiro"}""");

        a.ShouldBe(HttpStatusCode.Created);
        b.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE usuario_id = @other AND nome_conta = 'Antiga'", new { other })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "002-23")]
    public async Task TwoSimultaneousPostsOfTheSameName_OneCreatesAndTheOtherGets400()
    {
        const string json = """{"nome_conta":"Concorrente","tipo_conta":"Dinheiro"}""";

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => Post(json))));

        results.Select(r => r.Status).Order().ShouldBe([HttpStatusCode.Created, HttpStatusCode.BadRequest]);
        results.Single(r => r.Status == HttpStatusCode.BadRequest).Body.ShouldBe(AccountsHttp.Error("Já existe uma conta com este nome"));
        (await CountAccounts()).ShouldBe(1);
    }

    // ---------- cor, ícone, campos ignorados ----------

    [Fact]
    [Trait("AC", "002-24")]
    public async Task ColorAndIcon_DefaultWhenAbsent_NullWhenExplicitNull()
    {
        var (_, withNulls) = await Post("""{"nome_conta":"N","tipo_conta":"Dinheiro","cor_hex":null,"icone":null}""");

        withNulls.ShouldContain("\"cor_hex\":null,\"icone\":null");
        var row = (await _data.QueryAsync<(string? Cor, string? Icone)>("SELECT cor_hex, icone FROM contas WHERE id = 1")).Single();
        row.ShouldBe((null, null));
    }

    [Theory]
    [Trait("AC", "002-24")]
    [InlineData("\"cor_hex\":\"azul\"")]
    [InlineData("\"cor_hex\":\"#12345\"")]
    [InlineData("\"cor_hex\":\"#1234567\"")]
    [InlineData("\"cor_hex\":\"3B82F6\"")]
    [InlineData("\"cor_hex\":123")]
    public async Task InvalidColor_Returns400(string field)
    {
        await AssertBadRequest($$"""{"nome_conta":"C","tipo_conta":"Dinheiro",{{field}}}""", "Cor inválida. Use o formato #RRGGBB");
    }

    [Fact]
    [Trait("AC", "002-24")]
    public async Task Color_AcceptsAnyHexCase()
    {
        var (status, body) = await Post("""{"nome_conta":"C","tipo_conta":"Dinheiro","cor_hex":"#aBc123"}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"cor_hex\":\"#aBc123\"");
    }

    [Fact]
    [Trait("AC", "002-24")]
    public async Task Icon_Allows50Characters_Refuses51AndNonText()
    {
        (await Post($$"""{"nome_conta":"I50","tipo_conta":"Dinheiro","icone":"{{new string('i', 50)}}"}""")).Status.ShouldBe(HttpStatusCode.Created);
        await AssertBadRequest($$"""{"nome_conta":"I51","tipo_conta":"Dinheiro","icone":"{{new string('i', 51)}}"}""", "Ícone inválido");
        await AssertBadRequest("""{"nome_conta":"I1","tipo_conta":"Dinheiro","icone":1}""", "Ícone inválido");
    }

    [Fact]
    [Trait("AC", "002-25")]
    public async Task UnknownKeys_AreIgnored_AndTheOwnerIsTheTokenUser()
    {
        var other = await _data.InsertUserAsync("5511900000002");

        var (status, _) = await Post($$"""{"nome_conta":"Dele","tipo_conta":"Dinheiro","usuario_id":{{other}},"ativa":false,"banco":"x"}""");

        status.ShouldBe(HttpStatusCode.Created);
        var row = (await _data.QueryAsync<(int User, bool Ativa)>("SELECT usuario_id, ativa FROM contas WHERE id = 1")).Single();
        row.ShouldBe((_user, true));
    }
}
