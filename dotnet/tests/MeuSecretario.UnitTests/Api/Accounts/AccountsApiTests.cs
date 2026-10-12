using System.Numerics;
using System.Text;
using MeuSecretario.Api.Accounts;
using MeuSecretario.Application.Accounts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace MeuSecretario.UnitTests.Api.Accounts;

public class AccountsBodyReaderTests
{
    private static Task<RawBody?> Read(string? json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty));
        return AccountsBodyReader.ReadAsync(context.Request, CancellationToken.None);
    }

    [Theory]
    [Trait("AC", "002-12")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[1]")]
    [InlineData("\"abc\"")]
    [InlineData("0")]
    [InlineData("{\"a\":NaN}")]
    [InlineData("{\"a\":")]
    public async Task NotAJsonObject_IsNull(string? json)
    {
        (await Read(json)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-12")]
    public async Task EmptyObject_IsAnEmptyBody()
    {
        (await Read("{}"))!.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-24")]
    public async Task DistinguishesAbsentFromNull_AndKeepsEachJsonKind()
    {
        var body = (await Read(
            """{"n":null,"t":"x","i":12,"big":123456789012345678901234567890,"d":1.5,"e":1e2,"b":true,"f":false,"o":{"a":1},"l":[1]}"""))!;

        body.Get("ausente").ShouldBe(RawValue.Absent);
        body.Get("n").ShouldBe(RawValue.Null);
        body.Get("t").ShouldBe(RawValue.Text("x"));
        body.Get("i").ShouldBe(RawValue.Whole(12));
        body.Get("big").ShouldBe(RawValue.Whole(BigInteger.Parse("123456789012345678901234567890", System.Globalization.CultureInfo.InvariantCulture)));
        body.Get("d").ShouldBe(RawValue.Fractional(1.5));
        body.Get("e").ShouldBe(RawValue.Fractional(100));
        body.Get("b").ShouldBe(RawValue.Bool(true));
        body.Get("f").ShouldBe(RawValue.Bool(false));
        body.Get("o").ShouldBe(RawValue.Other);
        body.Get("l").ShouldBe(RawValue.Other);
    }

    [Fact]
    public async Task DuplicateKeys_LastOneWins_LikePython()
    {
        (await Read("""{"a":1,"a":2}"""))!.Get("a").ShouldBe(RawValue.Whole(2));
    }
}

public class AccountsMessagesTests
{
    [Theory]
    [Trait("AC", "002-12")]
    [InlineData(AccountError.DataMissing, "Dados não fornecidos")]
    [InlineData(AccountError.NameRequired, "Nome da conta é obrigatório")]
    [InlineData(AccountError.TypeRequired, "Tipo da conta é obrigatório")]
    [InlineData(AccountError.TypeInvalid, "Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro")]
    [InlineData(AccountError.BalanceInvalid, "Saldo inicial deve ser um número válido")]
    [InlineData(AccountError.ColorInvalid, "Cor inválida. Use o formato #RRGGBB")]
    [InlineData(AccountError.IconInvalid, "Ícone inválido")]
    [InlineData(AccountError.LimitInvalid, "Limite de crédito deve ser um número válido")]
    [InlineData(AccountError.DaysOnlyForCard, "Dia de fechamento e vencimento só se aplicam a Cartão de Crédito")]
    [InlineData(AccountError.ClosingDayInvalid, "Dia de fechamento deve ser entre 1 e 31")]
    [InlineData(AccountError.DueDayInvalid, "Dia de vencimento deve ser entre 1 e 31")]
    [InlineData(AccountError.CardNeedsDays, "Cartão de Crédito exige dia de fechamento e dia de vencimento")]
    [InlineData(AccountError.NameEmpty, "Nome da conta não pode ser vazio")]
    [InlineData(AccountError.NothingToUpdate, "Nenhum campo para atualizar")]
    public void EveryValidationError_HasItsExactFlaskText(AccountError error, string expected)
    {
        AccountsMessages.For(error).ShouldBe(expected);
    }

    [Fact]
    public void EveryAccountError_HasAMessage()
    {
        foreach (var error in Enum.GetValues<AccountError>())
        {
            AccountsMessages.For(error).ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    [Trait("AC", "002-71")]
    public void DeleteBlockMessages_CarryTheExactCount()
    {
        AccountsMessages.HasTransactions(3).ShouldBe("Não é possível excluir esta conta. Existem 3 transações vinculadas.");
        AccountsMessages.HasActiveSchedules(2).ShouldBe("Não é possível excluir esta conta. Existem 2 agendamentos ativos vinculados.");
    }
}

public class AccountsResponsesTests
{
    private static readonly AccountDetails Card = new(4, "Nubank", AccountTypes.Card, 0m, "#3B82F6", "wallet", 5000m, 5, 10);

    [Fact]
    [Trait("AC", "002-01")]
    public async Task List_HasTheOriginalFiveKeysThenTheExtras_InOrder()
    {
        var items = new List<AccountListItem> { new(1, "Carteira", "Dinheiro", 100m, "#3B82F6", "wallet", null, null, null, 150.5m) };

        var (status, json) = await ResultRunner.Run(AccountsResponses.List(items));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","data":[{"id":1,"nome":"Carteira","banco":"Dinheiro","tipo":"Dinheiro","saldo":150.5,"nome_conta":"Carteira","tipo_conta":"Dinheiro","saldo_inicial":100,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}]}""");
    }

    [Fact]
    [Trait("AC", "002-05")]
    public async Task List_Empty_StillSendsData()
    {
        var (status, json) = await ResultRunner.Run(AccountsResponses.List([]));

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "002-06")]
    public async Task List_BalanceIsANumberWithCents()
    {
        var items = new List<AccountListItem> { new(1, "A", "Outro", 0m, null, null, 0m, null, null, 70.55m) };

        var (_, json) = await ResultRunner.Run(AccountsResponses.List(items));

        json.ShouldContain("\"saldo\":70.55");
        json.ShouldContain("\"limite_credito\":null");   // zero sai null (RN-002-11)
    }

    [Fact]
    [Trait("AC", "002-11")]
    public async Task Created_HasTheExactSpecBody()
    {
        var (status, json) = await ResultRunner.Run(AccountsResponses.Created(Card));

        status.ShouldBe(201);
        json.ShouldBe(
            """{"status":"success","message":"Conta criada com sucesso","data":{"id":4,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":5000,"dia_fechamento":5,"dia_vencimento":10}}""");
    }

    [Fact]
    [Trait("AC", "002-48")]
    public async Task Updated_ZeroLimitIsNull()
    {
        var (status, json) = await ResultRunner.Run(AccountsResponses.Updated(Card with { LimiteCredito = 0m }));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","message":"Conta atualizada com sucesso","data":{"id":4,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":5,"dia_vencimento":10}}""");
    }

    [Fact]
    [Trait("AC", "002-70")]
    public async Task Deleted_HasNoData()
    {
        var (status, json) = await ResultRunner.Run(AccountsResponses.Deleted());

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","message":"Conta removida com sucesso"}""");
    }

    [Fact]
    [Trait("AC", "002-41")]
    public async Task Error_UsesEnvelopeA()
    {
        var (status, json) = await ResultRunner.Run(AccountsResponses.Error(AccountsMessages.NotFound, 404));

        status.ShouldBe(404);
        json.ShouldBe("""{"status":"error","message":"Conta não encontrada"}""");
    }
}
