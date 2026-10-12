using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Auth;
using MeuSecretario.Domain.Auth;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.Auth;

public class RegisterUserTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly RegisterUser _sut;

    public RegisterUserTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hash-gerado");
        _encryption.Encrypt(Arg.Any<string>()).Returns("chave-cifrada");
        _users.CreateWithDefaultAccountsAsync(default!, default!, default).ReturnsForAnyArgs(CreateUserResult.Created(7));
        _sut = new RegisterUser(_users, _hasher, _encryption);
    }

    private static RegisterInput Valid() => new("  Ana Souza ", "+55 (11) 99999-9999", "  SenhaSegura123 ", 10, 5);

    private Task<RegisterResult> Run(RegisterInput input) => _sut.ExecuteAsync(input, CancellationToken.None);

    [Fact]
    [Trait("AC", "001-01")]
    public async Task ValidInput_CreatesTheUserWithTheThreeDefaultAccounts()
    {
        var result = await Run(Valid());

        result.ShouldBe(new RegisterResult.Success(7));
        await _users.Received(1).CreateWithDefaultAccountsAsync(
            Arg.Is<NewUser>(u =>
                u.Nome == "Ana Souza"                    // trim
                && u.NumeroWhatsapp == "5511999999999"   // só dígitos
                && u.SenhaHash == "hash-gerado"
                && u.ApiKeyAutomate == "chave-cifrada"),
            Arg.Is<IReadOnlyList<NewAccount>>(a =>
                a.Count == 3 && a[2].NomeConta == "Cartão de Crédito" && a[2].DiaVencimento == 10 && a[2].DiaFechamento == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HashesTheTrimmedPassword()
    {
        await Run(Valid());

        _hasher.Received(1).Hash("SenhaSegura123");
    }

    [Fact]
    [Trait("AC", "001-13")]
    public async Task ApiKeyIsA43CharUrlSafeTokenEncryptedWithFernet()
    {
        string? plain = null;
        _encryption.Encrypt(Arg.Do<string>(p => plain = p)).Returns("chave-cifrada");

        await Run(Valid());

        plain.ShouldNotBeNull();
        Regex.IsMatch(plain, "^[A-Za-z0-9_-]{43}$").ShouldBeTrue(plain); // equivale a secrets.token_urlsafe(32)
    }

    [Fact]
    [Trait("AC", "001-13")]
    public async Task WhenEncryptionFails_RegistrationFails_InsteadOfStoringPlainText()
    {
        // Desvio 017: o Flask gravava a chave em texto puro se a cifra falhasse.
        _encryption.Encrypt(Arg.Any<string>()).Throws(new InvalidOperationException("falha"));

        await Should.ThrowAsync<InvalidOperationException>(() => Run(Valid()));

        await _users.DidNotReceiveWithAnyArgs().CreateWithDefaultAccountsAsync(default!, default!, default);
    }

    [Theory]
    [Trait("AC", "001-03")]
    [InlineData(null, "5511999999999", "SenhaSegura123")]
    [InlineData("", "5511999999999", "SenhaSegura123")]
    [InlineData("   ", "5511999999999", "SenhaSegura123")]
    [InlineData("Ana", null, "SenhaSegura123")]
    [InlineData("Ana", "  ", "SenhaSegura123")]
    [InlineData("Ana", "5511999999999", null)]
    [InlineData("Ana", "5511999999999", "      ")]
    public async Task MissingOrBlankRequiredText_ReturnsMissingFields(string? nome, string? whatsapp, string? password)
    {
        var result = await Run(new RegisterInput(nome, whatsapp, password, 10, 5));

        result.ShouldBe(new RegisterResult.MissingFields());
    }

    [Fact]
    [Trait("AC", "001-09")]
    public async Task WhatsappWithoutDigits_ReturnsMissingFields_AndStoresNothing()
    {
        // Desvio 031
        var result = await Run(Valid() with { Whatsapp = "abc" });

        result.ShouldBe(new RegisterResult.MissingFields());
        await _users.DidNotReceiveWithAnyArgs().CreateWithDefaultAccountsAsync(default!, default!, default);
    }

    [Theory]
    [Trait("AC", "001-04")]
    [InlineData("12345")]
    [InlineData("  abc  ")]   // 3 caracteres depois do trim
    public async Task ShortPassword_ReturnsPasswordTooShort(string password)
    {
        var result = await Run(Valid() with { Password = password });

        result.ShouldBe(new RegisterResult.PasswordTooShort());
    }

    [Fact]
    [Trait("AC", "001-04")]
    public async Task PasswordOfSixCharacters_IsAccepted()
    {
        (await Run(Valid() with { Password = "123456" })).ShouldBeOfType<RegisterResult.Success>();
    }

    [Theory]
    [Trait("AC", "001-05")]
    [InlineData(null, 5)]
    [InlineData(10, null)]
    [InlineData(null, null)]
    public async Task MissingDays_ReturnsMissingDays(object? venc, object? fech)
    {
        var result = await Run(Valid() with { DiaVencimento = venc, DiaFechamento = fech });

        result.ShouldBe(new RegisterResult.MissingDays());
    }

    [Theory]
    [Trait("AC", "001-06")]
    [InlineData(0, 5, "dia_vencimento")]
    [InlineData(32, 5, "dia_vencimento")]
    [InlineData(10, 0, "dia_fechamento")]
    [InlineData(10, 32, "dia_fechamento")]
    [InlineData(0, 40, "dia_vencimento")]
    public async Task DayOutOfRange_ReturnsTheFirstInvalidField(int venc, int fech, string field)
    {
        var result = await Run(Valid() with { DiaVencimento = venc, DiaFechamento = fech });

        result.ShouldBe(new RegisterResult.DayOutOfRange(field));
    }

    [Theory]
    [Trait("AC", "001-07")]
    [InlineData("abc")]
    [InlineData(true)]
    public async Task NonIntegerDay_ReturnsDaysNotInteger(object bad)
    {
        var result = await Run(Valid() with { DiaVencimento = bad });

        result.ShouldBe(new RegisterResult.DaysNotInteger());
    }

    [Theory]
    [Trait("AC", "001-07")]
    [InlineData("10", "5")]
    [InlineData(10.9, 5.1)]
    public async Task NumericStringAndDecimalDays_AreAccepted(object venc, object fech)
    {
        (await Run(Valid() with { DiaVencimento = venc, DiaFechamento = fech })).ShouldBeOfType<RegisterResult.Success>();
    }

    [Fact]
    public async Task ValidationOrder_RequiredFieldsBeforePasswordBeforeDays()
    {
        // Tudo errado ao mesmo tempo: vale a primeira regra da sequência do Flask.
        (await Run(new RegisterInput("", "x", "1", null, null))).ShouldBe(new RegisterResult.MissingFields());
        (await Run(new RegisterInput("Ana", "x", "1", null, null))).ShouldBe(new RegisterResult.PasswordTooShort());
        (await Run(new RegisterInput("Ana", "5511999999999", "123456", null, null))).ShouldBe(new RegisterResult.MissingDays());
    }

    [Fact]
    [Trait("AC", "001-08")]
    public async Task ExistingWhatsapp_ReturnsWhatsappTaken_WithoutHashingOrWriting()
    {
        _users.FindByWhatsappAsync("5511999999999", Arg.Any<CancellationToken>())
            .Returns(new UserRecord(1, "Outra", "5511999999999", "hash"));

        var result = await Run(Valid());

        result.ShouldBe(new RegisterResult.WhatsappTaken());
        _hasher.DidNotReceiveWithAnyArgs().Hash(default!);
        await _users.DidNotReceiveWithAnyArgs().CreateWithDefaultAccountsAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "001-08")]
    public async Task LooksUpTheNumberWithDigitsOnly_SoDifferentFormattingStillConflicts()
    {
        await Run(Valid() with { Whatsapp = "(11) 99999-9999" });

        await _users.Received(1).FindByWhatsappAsync("11999999999", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "001-12")]
    public async Task ConcurrentRegistration_UniqueViolationFromRepository_ReturnsWhatsappTaken()
    {
        // Desvio 031: o segundo cadastro simultâneo passa pela checagem e falha na UNIQUE.
        _users.CreateWithDefaultAccountsAsync(default!, default!, default).ReturnsForAnyArgs(CreateUserResult.Taken);

        var result = await Run(Valid());

        result.ShouldBe(new RegisterResult.WhatsappTaken());
    }
}
