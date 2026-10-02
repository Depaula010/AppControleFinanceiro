using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Spec 002 §3.3: <c>PUT /api/accounts/{id}</c> e <c>/api/contas/{id}</c>, corpo comparado como texto exato.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class AccountsPutEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string TypeMessage =
        "Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro";

    private const string DaysOnlyForCard = "Dia de fechamento e vencimento só se aplicam a Cartão de Crédito";
    private const string CardNeedsDays = "Cartão de Crédito exige dia de fechamento e dia de vencimento";

    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _id;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _id = await _data.InsertAccountAsync(_user, "Original", "Dinheiro", 10m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Put(string? json, int? id = null) => _http.Put(_user, id ?? _id, json);

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await Put(json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error(message));
    }

    private async Task<(string Nome, string Tipo, decimal Saldo, string? Cor, string? Icone, decimal? Limite, int? Fech, int? Venc)> Row(int? id = null) =>
        (await _data.QueryAsync<(string, string, decimal, string?, string?, decimal?, int?, int?)>(
            "SELECT nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento FROM contas WHERE id = @id",
            new { id = id ?? _id })).Single();

    // ---------- corpo e posse ----------

    [Theory]
    [Trait("AC", "002-40")]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("\"abc\"")]
    public async Task InvalidBody_Returns400_EvenWhenTheAccountDoesNotExist(string json)
    {
        await AssertBadRequest(json, "Dados não fornecidos");

        var (status, body) = await Put(json, id: 99999);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error("Dados não fornecidos"));
    }

    [Fact]
    [Trait("AC", "002-40")]
    public async Task NoBody_Returns400()
    {
        await AssertBadRequest(null, "Dados não fornecidos");
    }

    [Fact]
    [Trait("AC", "002-41")]
    public async Task UnknownInactiveAndForeignAccounts_Return404WithTheSameBody()
    {
        var other = await _data.InsertUserAsync("5511900000002");
        var foreign = await _data.InsertAccountAsync(other, "DoOutro");
        var inactive = await _data.InsertAccountAsync(_user, "Inativa", ativa: false);

        foreach (var id in new[] { 99999, inactive, foreign })
        {
            var (status, body) = await Put("""{"nome_conta":"Invasor"}""", id);

            status.ShouldBe(HttpStatusCode.NotFound, $"id {id}");
            body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
        }

        (await Row(foreign)).Nome.ShouldBe("DoOutro");
        (await Row(inactive)).Nome.ShouldBe("Inativa");
    }

    [Fact]
    [Trait("AC", "002-86")]
    public async Task IdThatDoesNotFitAnInt_IsAnUnknownAccount_AfterTheBodyCheck()
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Put, "/api/accounts/99999999999999999999", """{"nome_conta":"X"}""", _user);
        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));

        var (badStatus, badBody) = await _http.SendAsync(HttpMethod.Put, "/api/accounts/99999999999999999999", "{}", _user);
        badStatus.ShouldBe(HttpStatusCode.BadRequest);
        badBody.ShouldBe(AccountsHttp.Error("Dados não fornecidos"));
    }

    [Fact]
    [Trait("AC", "002-82")]
    public async Task Alias_Contas_BehavesTheSame()
    {
        var (status, body) = await _http.Put(_user, _id, """{"nome_conta":"Pelo alias"}""", "/api/contas");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"nome_conta\":\"Pelo alias\"");
        (await _http.Put(_user, 99999, """{"nome_conta":"X"}""", "/api/contas")).Body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
    }

    // ---------- nome ----------

    [Fact]
    [Trait("AC", "002-42")]
    public async Task NameOnly_Returns200WithTheExactBody_AndChangesNothingElse()
    {
        var before = await _data.ScalarAsync<DateTime>("SELECT updated_at FROM contas WHERE id = @_id", new { _id });
        await Task.Delay(20);

        var (status, body) = await Put("""{"nome_conta":"Novo"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$$"""{"status":"success","message":"Conta atualizada com sucesso","data":{"id":{{{_id}}},"nome_conta":"Novo","tipo_conta":"Dinheiro","saldo_inicial":10,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}}""");
        (await Row()).ShouldBe(("Novo", "Dinheiro", 10m, "#3B82F6", "wallet", null, null, null));
        (await _data.ScalarAsync<DateTime>("SELECT updated_at FROM contas WHERE id = @_id", new { _id })).ShouldBeGreaterThan(before);
    }

    [Theory]
    [Trait("AC", "002-43")]
    [Trait("AC", "002-56")]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("[\"a\"]")]
    public async Task BlankOrNonTextName_Returns400(string value)
    {
        await AssertBadRequest($$"""{"nome_conta":{{value}}}""", "Nome da conta não pode ser vazio");
        (await Row()).Nome.ShouldBe("Original");
    }

    [Fact]
    [Trait("AC", "002-43")]
    public async Task LongName_IsTrimmedAndCutAt100()
    {
        var (status, _) = await Put($$"""{"nome_conta":"  {{new string('x', 150)}}"}""");

        status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Nome.ShouldBe(new string('x', 100));
    }

    [Fact]
    [Trait("AC", "002-44")]
    public async Task DuplicateName_Returns400_OwnNameIsFine()
    {
        await _data.InsertAccountAsync(_user, "Ativa");
        await _data.InsertAccountAsync(_user, "Antiga", ativa: false);

        await AssertBadRequest("""{"nome_conta":"Ativa"}""", "Já existe outra conta com este nome");
        await AssertBadRequest("""{"nome_conta":"Antiga"}""", "Já existe outra conta com este nome");
        var (status, _) = await Put("""{"nome_conta":"Original"}""");

        status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Nome.ShouldBe("Original");
    }

    [Fact]
    [Trait("AC", "002-44")]
    public async Task SameNameInAnotherUser_IsNotADuplicate()
    {
        var other = await _data.InsertUserAsync("5511900000002");
        await _data.InsertAccountAsync(other, "Compartilhado");

        (await Put("""{"nome_conta":"Compartilhado"}""")).Status.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- tipo, saldo, cor, ícone, limite, dias ----------

    [Theory]
    [Trait("AC", "002-45")]
    [InlineData("\"\"")]
    [InlineData("\"corrente\"")]
    [InlineData("null")]
    [InlineData("7")]
    public async Task InvalidType_Returns400(string value)
    {
        await AssertBadRequest($$"""{"tipo_conta":{{value}}}""", TypeMessage);
        (await Row()).Tipo.ShouldBe("Dinheiro");
    }

    [Fact]
    [Trait("AC", "002-45")]
    public async Task ValidType_OnAnAccountWithoutDays_Updates()
    {
        var (status, _) = await Put("""{"tipo_conta":" Investimento "}""");

        status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Tipo.ShouldBe("Investimento");
    }

    [Theory]
    [Trait("AC", "002-46")]
    [InlineData("\"abc\"")]
    [InlineData("null")]
    [InlineData("1e30")]
    [InlineData("\"nan\"")]
    public async Task InvalidBalance_Returns400(string value)
    {
        await AssertBadRequest($$"""{"saldo_inicial":{{value}}}""", "Saldo inicial deve ser um número válido");
        (await Row()).Saldo.ShouldBe(10m);
    }

    [Fact]
    [Trait("AC", "002-46")]
    public async Task ValidBalance_Updates()
    {
        var (status, body) = await Put("""{"saldo_inicial":250.75}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"saldo_inicial\":250.75");
        (await Row()).Saldo.ShouldBe(250.75m);
    }

    [Fact]
    [Trait("AC", "002-47")]
    public async Task ColorAndIcon_UpdateOrClearToNull()
    {
        (await Put("""{"cor_hex":"#FFFFFF","icone":"star"}""")).Status.ShouldBe(HttpStatusCode.OK);
        var row = await Row();
        row.Cor.ShouldBe("#FFFFFF");
        row.Icone.ShouldBe("star");

        var (status, body) = await Put("""{"cor_hex":null,"icone":null}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"cor_hex\":null,\"icone\":null");
        var cleared = await Row();
        cleared.Cor.ShouldBeNull();
        cleared.Icone.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-47")]
    public async Task InvalidColorOrIcon_Returns400()
    {
        await AssertBadRequest("""{"cor_hex":"branco"}""", "Cor inválida. Use o formato #RRGGBB");
        await AssertBadRequest("""{"cor_hex":5}""", "Cor inválida. Use o formato #RRGGBB");
        await AssertBadRequest($$"""{"icone":"{{new string('i', 51)}}"}""", "Ícone inválido");
    }

    [Fact]
    [Trait("AC", "002-48")]
    public async Task Limit_NullClears_ZeroIsStoredButAnsweredNull_InvalidIs400_InAnyType()
    {
        var (status, body) = await Put("""{"limite_credito":0}""");
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"limite_credito\":null");
        (await Row()).Limite.ShouldBe(0.00m);

        (await Put("""{"limite_credito":300.5}""")).Body.ShouldContain("\"limite_credito\":300.5");
        (await Put("""{"limite_credito":null}""")).Status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Limite.ShouldBeNull();
        await AssertBadRequest("""{"limite_credito":"abc"}""", "Limite de crédito deve ser um número válido");
    }

    [Theory]
    [Trait("AC", "002-49")]
    [InlineData("""{"dia_fechamento":0}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":32}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_fechamento":"x"}""", "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData("""{"dia_vencimento":0}""", "Dia de vencimento deve ser entre 1 e 31")]
    [InlineData("""{"dia_vencimento":"x"}""", "Dia de vencimento deve ser entre 1 e 31")]
    [InlineData("""{"limite_credito":"x","dia_fechamento":0,"dia_vencimento":0}""", "Limite de crédito deve ser um número válido")]
    [InlineData("""{"dia_fechamento":0,"dia_vencimento":0}""", "Dia de fechamento deve ser entre 1 e 31")]
    public async Task InvalidDays_FollowTheSpecOrder(string json, string message)
    {
        await AssertBadRequest(json, message);
    }

    [Fact]
    [Trait("AC", "002-49")]
    public async Task NullDays_OnANonCardAccount_Return200()
    {
        var (status, _) = await Put("""{"dia_fechamento":null,"dia_vencimento":null}""");

        status.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- ordem, nada para atualizar, consistência ----------

    [Fact]
    [Trait("AC", "002-50")]
    public async Task TwoErrors_AnswerTheFirstInSpecOrder_AndNothingIsSaved()
    {
        await _data.InsertAccountAsync(_user, "Existente");

        await AssertBadRequest("""{"tipo_conta":"invalido","nome_conta":"Existente"}""", "Já existe outra conta com este nome");
        await AssertBadRequest("""{"tipo_conta":"invalido","saldo_inicial":"x"}""", TypeMessage);

        (await Row()).ShouldBe(("Original", "Dinheiro", 10m, "#3B82F6", "wallet", null, null, null));
    }

    [Fact]
    [Trait("AC", "002-50")]
    public async Task NameIsNotSaved_WhenAnotherFieldIsInvalid()
    {
        await AssertBadRequest("""{"nome_conta":"Novo","saldo_inicial":"abc"}""", "Saldo inicial deve ser um número válido");

        (await Row()).Nome.ShouldBe("Original");
    }

    [Fact]
    [Trait("AC", "002-51")]
    public async Task NoRecognizedKey_Returns400()
    {
        await AssertBadRequest("""{"foo":1}""", "Nenhum campo para atualizar");
    }

    [Theory]
    [Trait("AC", "002-52")]
    [InlineData("""{"dia_fechamento":5}""")]
    [InlineData("""{"dia_vencimento":10}""")]
    public async Task DaysOnANonCardAccount_Return400_AndNothingChanges(string json)
    {
        await AssertBadRequest(json, DaysOnlyForCard);

        (await Row()).ShouldBe(("Original", "Dinheiro", 10m, "#3B82F6", "wallet", null, null, null));
    }

    [Fact]
    [Trait("AC", "002-52")]
    public async Task ChangingACardWithDaysToAnotherType_IsRefused_UnlessDaysAreClearedTogether()
    {
        var card = await _data.InsertAccountAsync(_user, "Nubank", "Cartão de Crédito", fechamento: 5, vencimento: 10);

        var (status, body) = await _http.Put(_user, card, """{"tipo_conta":"Dinheiro"}""");
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error(DaysOnlyForCard));
        (await Row(card)).Tipo.ShouldBe("Cartão de Crédito");

        var (ok, _) = await _http.Put(_user, card, """{"tipo_conta":"Dinheiro","dia_fechamento":null,"dia_vencimento":null}""");
        ok.ShouldBe(HttpStatusCode.OK);
        var row = await Row(card);
        row.Tipo.ShouldBe("Dinheiro");
        row.Fech.ShouldBeNull();
        row.Venc.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-54")]
    public async Task AngularBody_OnANonCardAccount_Returns200AndChangesNothing()
    {
        var (status, _) = await Put(
            """{"nome":"Novo","tipo":"corrente","banco":"X","saldo":5,"cor":"#000000","limite":10,"dia_vencimento":null,"dia_fechamento":null}""");

        status.ShouldBe(HttpStatusCode.OK);
        (await Row()).ShouldBe(("Original", "Dinheiro", 10m, "#3B82F6", "wallet", null, null, null));
    }

    [Fact]
    [Trait("AC", "002-54")]
    public async Task AngularBody_OnACard_IsRefusedInsteadOfClearingTheDaysSilently()
    {
        var card = await _data.InsertAccountAsync(_user, "Nubank", "Cartão de Crédito", fechamento: 5, vencimento: 10);

        var (status, body) = await _http.Put(_user, card,
            """{"nome":"Novo","tipo":"cartao_credito","saldo":5,"dia_vencimento":null,"dia_fechamento":null}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error(CardNeedsDays));
        var row = await Row(card);
        row.Fech.ShouldBe(5);
        row.Venc.ShouldBe(10);
    }

    [Fact]
    [Trait("AC", "002-55")]
    public async Task ChangingToCard_NeedsBothDays()
    {
        await AssertBadRequest("""{"tipo_conta":"Cartão de Crédito"}""", CardNeedsDays);
        await AssertBadRequest("""{"tipo_conta":"Cartão de Crédito","dia_fechamento":5}""", CardNeedsDays);

        var (status, body) = await Put("""{"tipo_conta":"Cartão de Crédito","dia_fechamento":5,"dia_vencimento":9}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"tipo_conta\":\"Cartão de Crédito\"");
        body.ShouldContain("\"dia_fechamento\":5,\"dia_vencimento\":9");
    }

    [Fact]
    [Trait("AC", "002-55")]
    public async Task RenamingALegacyCardWithoutDays_StillWorks()
    {
        var legacy = await _data.InsertAccountAsync(_user, "Antigo", "Cartão de Crédito");

        var (status, _) = await _http.Put(_user, legacy, """{"nome_conta":"Renomeado"}""");

        status.ShouldBe(HttpStatusCode.OK);
        (await Row(legacy)).Nome.ShouldBe("Renomeado");
    }
}
