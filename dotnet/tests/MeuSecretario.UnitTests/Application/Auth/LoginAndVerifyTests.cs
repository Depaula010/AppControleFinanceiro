using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Auth;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Auth;

public class LoginUserTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly LoginUser _sut;

    public LoginUserTests()
    {
        _jwt.Issue(Arg.Any<int>()).Returns("jwt-emitido");
        _sut = new LoginUser(_users, _hasher, _jwt);
    }

    private Task<LoginResult> Run(string? whatsapp, string? password) =>
        _sut.ExecuteAsync(new LoginInput(whatsapp, password), CancellationToken.None);

    private void GivenUser(string? hash) =>
        _users.FindByWhatsappAsync("5511999999999", Arg.Any<CancellationToken>())
            .Returns(new UserRecord(42, "Ana", "5511999999999", hash));

    [Fact]
    [Trait("AC", "001-20")]
    public async Task CorrectCredentials_ReturnsTokenAndUserFromTheDatabase()
    {
        GivenUser("hash-do-banco");
        _hasher.Verify("hash-do-banco", "SenhaSegura123").Returns(true);

        var result = await Run("5511999999999", "  SenhaSegura123 ");

        result.ShouldBe(new LoginResult.Success("jwt-emitido", new LoggedUser(42, "Ana", "5511999999999")));
        _jwt.Received(1).Issue(42);
    }

    [Fact]
    [Trait("AC", "001-21")]
    public async Task FormattedNumber_IsLookedUpByDigits()
    {
        GivenUser("hash");
        _hasher.Verify("hash", "SenhaSegura123").Returns(true);

        var result = await Run("+55 (11) 99999-9999", "SenhaSegura123");

        result.ShouldBeOfType<LoginResult.Success>();
    }

    [Theory]
    [Trait("AC", "001-22")]
    [InlineData(null, "senha")]
    [InlineData("", "senha")]
    [InlineData("   ", "senha")]
    [InlineData("5511999999999", null)]
    [InlineData("5511999999999", "   ")]
    public async Task BlankFields_ReturnMissingFields_WithoutQueryingTheDatabase(string? whatsapp, string? password)
    {
        var result = await Run(whatsapp, password);

        result.ShouldBe(new LoginResult.MissingFields());
        await _users.DidNotReceiveWithAnyArgs().FindByWhatsappAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "001-23")]
    public async Task UnknownNumber_ReturnsNotRegistered()
    {
        (await Run("5511999999999", "senha")).ShouldBe(new LoginResult.NotRegistered());
    }

    [Fact]
    [Trait("AC", "001-23")]
    public async Task NumberWithoutDigits_IsLookedUpAsEmpty_AndIsNotRegistered()
    {
        // Igual ao Flask: "abc" vira "" e simplesmente não encontra ninguém.
        (await Run("abc", "senha")).ShouldBe(new LoginResult.NotRegistered());
        await _users.Received(1).FindByWhatsappAsync(string.Empty, Arg.Any<CancellationToken>());
    }

    [Theory]
    [Trait("AC", "001-24")]
    [InlineData(null)]
    [InlineData("")]
    public async Task UserWithoutPasswordHash_ReturnsNoPassword(string? hash)
    {
        GivenUser(hash);

        (await Run("5511999999999", "senha")).ShouldBe(new LoginResult.NoPassword());
        _hasher.DidNotReceiveWithAnyArgs().Verify(default, default!);
    }

    [Fact]
    [Trait("AC", "001-25")]
    public async Task WrongPassword_ReturnsWrongPassword_AndIssuesNoToken()
    {
        GivenUser("hash");
        _hasher.Verify("hash", "errada").Returns(false);

        (await Run("5511999999999", "errada")).ShouldBe(new LoginResult.WrongPassword());
        _jwt.DidNotReceiveWithAnyArgs().Issue(default);
    }
}

public class VerifyTokenTests
{
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();

    private VerifyToken Sut() => new(_jwt);

    [Theory]
    [Trait("AC", "001-31")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankToken_ReturnsMissingToken(string? token)
    {
        Sut().Execute(token).ShouldBe(new VerifyResult.MissingToken());
        _jwt.DidNotReceiveWithAnyArgs().Validate(default!);
    }

    [Fact]
    [Trait("AC", "001-32")]
    public void InvalidToken_ReturnsInvalid()
    {
        _jwt.Validate("ruim").Returns(JwtValidation.Invalid);

        Sut().Execute("ruim").ShouldBe(new VerifyResult.Invalid());
    }

    [Fact]
    [Trait("AC", "001-33")]
    public void ValidTokenWithoutUserId_ReturnsNoUserId()
    {
        _jwt.Validate("t").Returns(new JwtValidation(true, null));

        Sut().Execute("t").ShouldBe(new VerifyResult.NoUserId());
    }

    [Fact]
    [Trait("AC", "001-30")]
    public void ValidToken_ReturnsTheUserId_AfterTrimming()
    {
        _jwt.Validate("t").Returns(new JwtValidation(true, 42));

        Sut().Execute("  t  ").ShouldBe(new VerifyResult.Valid(42));
    }

    [Fact]
    public void ValidTokenWithUserIdZero_IsStillValid_LikeTheFlask()
    {
        // O /auth/verify devolve payload['user_id'] sem checar se é zero.
        _jwt.Validate("t").Returns(new JwtValidation(true, 0));

        Sut().Execute("t").ShouldBe(new VerifyResult.Valid(0));
    }
}
