using MeuSecretario.Application.Accounts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.UnitTests.Domain.Accounts;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Accounts;

internal static class Samples
{
    public static AccountDetails Details(int id = 7, string tipo = "Dinheiro", int? closing = null, int? due = null) =>
        new(id, "Carteira", tipo, 0m, "#3B82F6", "wallet", null, closing, due);
}

public class ListAccountsTests
{
    [Fact]
    [Trait("AC", "002-01")]
    public async Task ReturnsWhatTheRepositoryListsForTheUser()
    {
        var repository = Substitute.For<IAccountRepository>();
        var items = new List<AccountListItem> { new(1, "A", "Dinheiro", 10m, "#3B82F6", "wallet", null, null, null, 15m) };
        repository.ListActiveAsync(42, Arg.Any<CancellationToken>()).Returns(items);

        var result = await new ListAccounts(repository).ExecuteAsync(42, CancellationToken.None);

        result.ShouldBe(items);
    }
}

public class DeleteAccountTests
{
    private readonly IAccountRepository _repository = Substitute.For<IAccountRepository>();

    [Theory]
    [Trait("AC", "002-70")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DelegatesToTheRepository_WithTheTokenUser(int scenario)
    {
        DeleteAccountResult expected = scenario switch
        {
            0 => new DeleteAccountResult.Deleted(),
            1 => new DeleteAccountResult.HasTransactions(3),
            _ => new DeleteAccountResult.HasActiveSchedules(2),
        };
        _repository.SoftDeleteAsync(42, 9, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new DeleteAccount(_repository).ExecuteAsync(42, 9, CancellationToken.None);

        result.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "002-72")]
    public async Task NotFound_IsPassedThrough()
    {
        _repository.SoftDeleteAsync(42, 9, Arg.Any<CancellationToken>()).Returns(new DeleteAccountResult.NotFound());

        (await new DeleteAccount(_repository).ExecuteAsync(42, 9, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.NotFound>();
    }
}

public class CreateAccountTests
{
    private readonly IAccountRepository _repository = Substitute.For<IAccountRepository>();

    private Task<CreateAccountResult> Run(RawBody? body) => new CreateAccount(_repository).ExecuteAsync(42, body, CancellationToken.None);

    [Fact]
    [Trait("AC", "002-12")]
    public async Task NullBody_IsDataMissing_AndTheRepositoryIsUntouched()
    {
        (await Run(null)).ShouldBe(new CreateAccountResult.Invalid(AccountError.DataMissing));

        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "002-13")]
    public async Task ValidationError_NeverReachesTheRepository()
    {
        (await Run(Raw.Body(("tipo_conta", Raw.T("Dinheiro"))))).ShouldBe(new CreateAccountResult.Invalid(AccountError.NameRequired));

        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "002-10")]
    public async Task ValidBody_CreatesForTheTokenUser()
    {
        var created = Samples.Details();
        _repository.CreateAsync(42, Arg.Any<NewAccountData>(), Arg.Any<CancellationToken>()).Returns(new CreateAccountResult.Created(created));

        var result = await Run(Raw.Simple());

        result.ShouldBe(new CreateAccountResult.Created(created));
        await _repository.Received(1).CreateAsync(
            42,
            Arg.Is<NewAccountData>(d => d.NomeConta == "Carteira" && d.TipoConta == "Dinheiro" && d.CorHex == "#3B82F6"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "002-20")]
    public async Task NameTakenFromTheRepository_IsPassedThrough()
    {
        _repository.CreateAsync(42, Arg.Any<NewAccountData>(), Arg.Any<CancellationToken>()).Returns(new CreateAccountResult.NameTaken());

        (await Run(Raw.Simple())).ShouldBeOfType<CreateAccountResult.NameTaken>();
    }
}

public class UpdateAccountTests
{
    private readonly IAccountRepository _repository = Substitute.For<IAccountRepository>();

    public UpdateAccountTests()
    {
        _repository.GetActiveAsync(42, 7, Arg.Any<CancellationToken>()).Returns(Samples.Details());
        _repository.NameTakenAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        _repository.UpdateAsync(42, 7, Arg.Any<AccountChanges>(), Arg.Any<CancellationToken>())
            .Returns(new UpdateAccountResult.Updated(Samples.Details()));
    }

    private Task<UpdateAccountResult> Run(RawBody? body) => new UpdateAccount(_repository).ExecuteAsync(42, 7, body, CancellationToken.None);

    [Fact]
    [Trait("AC", "002-40")]
    public async Task EmptyBody_IsDataMissing_BeforeLookingTheAccountUp()
    {
        (await Run(Raw.Body())).ShouldBe(new UpdateAccountResult.Invalid(AccountError.DataMissing));
        (await Run(null)).ShouldBe(new UpdateAccountResult.Invalid(AccountError.DataMissing));

        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "002-41")]
    public async Task UnknownAccount_IsNotFound_BeforeFieldValidation()
    {
        _repository.GetActiveAsync(42, 7, Arg.Any<CancellationToken>()).Returns((AccountDetails?)null);

        var result = await Run(Raw.Body(("tipo_conta", Raw.T("invalido"))));

        result.ShouldBeOfType<UpdateAccountResult.NotFound>();
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "002-43")]
    public async Task BlankName_IsNameEmpty_WithoutAskingTheBankAboutDuplicates()
    {
        (await Run(Raw.Body(("nome_conta", Raw.T("  "))))).ShouldBe(new UpdateAccountResult.Invalid(AccountError.NameEmpty));

        await _repository.DidNotReceiveWithAnyArgs().NameTakenAsync(default, default!, default, default);
    }

    [Fact]
    [Trait("AC", "002-44")]
    public async Task DuplicateName_ChecksAllOtherAccountsOfTheUser()
    {
        _repository.NameTakenAsync(42, "Outra", 7, Arg.Any<CancellationToken>()).Returns(true);

        (await Run(Raw.Body(("nome_conta", Raw.T(" Outra "))))).ShouldBeOfType<UpdateAccountResult.NameTaken>();

        await _repository.Received(1).NameTakenAsync(42, "Outra", 7, Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "002-50")]
    public async Task DuplicateName_WinsOverAnInvalidType()
    {
        _repository.NameTakenAsync(42, "Outra", 7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Run(Raw.Body(("nome_conta", Raw.T("Outra")), ("tipo_conta", Raw.T("invalido"))));

        result.ShouldBeOfType<UpdateAccountResult.NameTaken>();
    }

    [Fact]
    [Trait("AC", "002-50")]
    public async Task InvalidField_NeverWrites()
    {
        (await Run(Raw.Body(("nome_conta", Raw.T("Novo")), ("saldo_inicial", Raw.T("abc")))))
            .ShouldBe(new UpdateAccountResult.Invalid(AccountError.BalanceInvalid));

        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "002-51")]
    public async Task NoRecognizedKey_IsNothingToUpdate()
    {
        (await Run(Raw.Body(("foo", Raw.N(1))))).ShouldBe(new UpdateAccountResult.Invalid(AccountError.NothingToUpdate));
    }

    [Fact]
    [Trait("AC", "002-42")]
    public async Task OnlyTheNameChange_WritesJustTheName()
    {
        var result = await Run(Raw.Body(("nome_conta", Raw.T("Novo"))));

        result.ShouldBeOfType<UpdateAccountResult.Updated>();
        await _repository.Received(1).UpdateAsync(
            42, 7,
            Arg.Is<AccountChanges>(c => c.Nome.IsSet && c.Nome.Value == "Novo" && !c.Tipo.IsSet && !c.Saldo.IsSet && !c.Limite.IsSet),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "002-52")]
    public async Task ConsistencyIsCheckedAgainstTheCurrentAccount()
    {
        _repository.GetActiveAsync(42, 7, Arg.Any<CancellationToken>()).Returns(Samples.Details(tipo: AccountTypes.Card, closing: 5, due: 10));

        var result = await Run(Raw.Body(("tipo_conta", Raw.T("Dinheiro"))));

        result.ShouldBe(new UpdateAccountResult.Invalid(AccountError.DaysOnlyForCard));
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Theory]
    [Trait("AC", "002-44")]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RepositoryOutcomesRaceLost_ArePassedThrough(int scenario)
    {
        UpdateAccountResult lost = scenario == 0 ? new UpdateAccountResult.NameTaken() : new UpdateAccountResult.NotFound();
        _repository.UpdateAsync(42, 7, Arg.Any<AccountChanges>(), Arg.Any<CancellationToken>()).Returns(lost);

        (await Run(Raw.Body(("saldo_inicial", Raw.N(1))))).ShouldBe(lost);
    }
}
