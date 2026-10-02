using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.UnitTests.Fixtures;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Infrastructure;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private JwtTokenService Create(string? secret = null) => new(secret ?? AuthFixtures.JwtSecret, _time);

    // ---- Tokens emitidos pelo PyJWT (Flask) ------------------------------------------------

    [Fact]
    [Trait("AC", "001-34")]
    public void Validate_AcceptsATokenIssuedByThePyJwtOfTheFlask()
    {
        var result = Create().Validate(AuthFixtures.JwtToken("valid"));

        result.IsValid.ShouldBeTrue();
        result.UserId.ShouldBe(AuthFixtures.JwtUserId);
    }

    [Theory]
    [Trait("AC", "001-32")]
    [InlineData("expired")]
    [InlineData("alg_none")]
    [InlineData("hs512")]
    [InlineData("wrong_secret")]
    [InlineData("tampered_payload")]
    [InlineData("no_exp")]
    public void Validate_RejectsInvalidTokens(string fixtureName)
    {
        Create().Validate(AuthFixtures.JwtToken(fixtureName)).IsValid.ShouldBeFalse(fixtureName);
    }

    [Theory]
    [Trait("AC", "001-33")]
    [InlineData("no_user_id")]
    [InlineData("sub_only")]
    public void Validate_ValidTokenWithoutUserIdClaim_HasNullUserId(string fixtureName)
    {
        // O claim lido é "user_id", não "sub".
        var result = Create().Validate(AuthFixtures.JwtToken(fixtureName));

        result.IsValid.ShouldBeTrue();
        result.UserId.ShouldBeNull();
    }

    [Fact]
    public void Validate_UserIdZero_IsReturnedAsZero_AndTheFilterTreatsItAsMissing()
    {
        var result = Create().Validate(AuthFixtures.JwtToken("user_id_zero"));

        result.IsValid.ShouldBeTrue();
        result.UserId.ShouldBe(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("a.b.c")]
    [InlineData("a.b.c.d")]
    [InlineData("não-é-jwt")]
    public void Validate_NeverThrowsOnGarbage(string token)
    {
        Create().Validate(token).IsValid.ShouldBeFalse();
    }

    // ---- Tokens emitidos pelo .NET ---------------------------------------------------------

    [Fact]
    [Trait("AC", "001-28")]
    public void Issue_ProducesHs256TokenWithIntegerUserIdAndIatExp24HoursApart()
    {
        var token = Create().Issue(7);

        var parts = token.Split('.');
        parts.Length.ShouldBe(3);
        var header = JsonDocument.Parse(Decode(parts[0])).RootElement;
        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;

        header.GetProperty("alg").GetString().ShouldBe("HS256");
        header.GetProperty("typ").GetString().ShouldBe("JWT");
        payload.GetProperty("user_id").ValueKind.ShouldBe(JsonValueKind.Number); // inteiro, não texto
        payload.GetProperty("user_id").GetInt32().ShouldBe(7);
        payload.GetProperty("iat").GetInt64().ShouldBe(Now.ToUnixTimeSeconds());
        payload.GetProperty("exp").GetInt64().ShouldBe(Now.ToUnixTimeSeconds() + 86_400);
        payload.TryGetProperty("nbf", out _).ShouldBeFalse();
        payload.TryGetProperty("sub", out _).ShouldBeFalse();
        payload.TryGetProperty("iss", out _).ShouldBeFalse();
        payload.TryGetProperty("aud", out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "001-28")]
    public void Issue_SignatureMatchesAnIndependentHmacSha256()
    {
        // Verificação independente da biblioteca: é exatamente o que o PyJWT calcula.
        var token = Create().Issue(7);
        var parts = token.Split('.');

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(AuthFixtures.JwtSecret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));

        parts[2].ShouldBe(Convert.ToBase64String(expected).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    [Fact]
    public void Issue_RoundTripsThroughValidate()
    {
        var service = Create();

        var result = service.Validate(service.Issue(123));

        result.IsValid.ShouldBeTrue();
        result.UserId.ShouldBe(123);
    }

    [Fact]
    public void Validate_ExpiresExactlyAt24Hours_WithNoClockSkew()
    {
        var service = Create();
        var token = service.Issue(7);

        _time.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        service.Validate(token).IsValid.ShouldBeTrue();

        _time.Advance(TimeSpan.FromSeconds(1)); // exp == agora: o PyJWT já considera expirado
        service.Validate(token).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RejectsATokenSignedWithAnotherSecret()
    {
        var token = Create("outra-chave-de-teste-diferente-0123456789ab").Issue(7);

        Create().Validate(token).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_RejectsShortSecretWithoutLeakingIt()
    {
        var ex = Should.Throw<ArgumentException>(() => Create("curta-secreta"));

        ex.Message.ShouldNotContain("curta-secreta");
    }

    private static string Decode(string base64Url)
    {
        var text = base64Url.Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }
}
