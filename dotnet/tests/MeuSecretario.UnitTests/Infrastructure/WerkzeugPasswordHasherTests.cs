using System.Text.RegularExpressions;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.UnitTests.Infrastructure;

public class WerkzeugPasswordHasherTests
{
    // Iterações baixas só para o teste de geração ficar rápido; a verificação lê as iterações do hash.
    private readonly WerkzeugPasswordHasher _hasher = new(iterations: 1000);

    [Fact]
    [Trait("AC", "001-26")]
    [Trait("AC", "001-27")]
    [Trait("AC", "001-50")]
    public void Verify_AcceptsEveryHashGeneratedByWerkzeug()
    {
        AuthFixtures.Passwords.Count.ShouldBeGreaterThan(0);

        foreach (var fixture in AuthFixtures.Passwords)
        {
            _hasher.Verify(fixture.Hash, fixture.Password)
                .ShouldBeTrue($"{fixture.Kind}: {fixture.Hash[..Math.Min(30, fixture.Hash.Length)]}");
        }
    }

    [Fact]
    [Trait("AC", "001-26")]
    public void Verify_ReadsTheRealProductionFormat_Scrypt32768_8_1()
    {
        var scrypt = AuthFixtures.Passwords.First(p => p.Kind == "scrypt-default");

        scrypt.Hash.ShouldStartWith("scrypt:32768:8:1$");
        _hasher.Verify(scrypt.Hash, scrypt.Password).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "001-50")]
    public void Verify_RejectsWrongPassword()
    {
        foreach (var fixture in AuthFixtures.Passwords)
        {
            _hasher.Verify(fixture.Hash, fixture.Password + "x").ShouldBeFalse(fixture.Kind);
            _hasher.Verify(fixture.Hash, string.Empty).ShouldBeFalse(fixture.Kind);
        }
    }

    [Theory]
    [Trait("AC", "001-25")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-puro")]
    [InlineData("md5$salt$abcdef")]
    [InlineData("sha1$salt$abcdef")]
    [InlineData("pbkdf2:sha256:abc$salt$abcdef")]
    [InlineData("pbkdf2:sha256:1000$salt")]
    [InlineData("pbkdf2:md5:1000$salt$abcdef")]
    [InlineData("pbkdf2:sha256:0$salt$abcdef")]
    [InlineData("pbkdf2:sha256:999999999999$salt$abcdef")]
    [InlineData("scrypt:3:8:1$salt$abcdef")]            // N não é potência de 2
    [InlineData("scrypt:32768:8$salt$abcdef")]          // faltam parâmetros
    [InlineData("scrypt:1073741824:8:1$salt$abcdef")]   // memória absurda (limite anti-DoS)
    [InlineData("$$")]
    public void Verify_ReturnsFalseForUnknownOrMalformedFormats(string? hash)
    {
        _hasher.Verify(hash!, "qualquer").ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "001-13")]
    public void Hash_ProducesTheWerkzeugPbkdf2Format()
    {
        var hash = _hasher.Hash("SenhaSegura123");

        Regex.IsMatch(hash, @"^pbkdf2:sha256:1000\$[A-Za-z0-9]{16}\$[0-9a-f]{64}$").ShouldBeTrue(hash);
    }

    [Fact]
    [Trait("AC", "001-50")]
    public void Hash_RoundTripsAndUsesARandomSaltEveryTime()
    {
        var first = _hasher.Hash("SenhaSegura123");
        var second = _hasher.Hash("SenhaSegura123");

        first.ShouldNotBe(second);
        _hasher.Verify(first, "SenhaSegura123").ShouldBeTrue();
        _hasher.Verify(second, "SenhaSegura123").ShouldBeTrue();
        _hasher.Verify(first, "outra").ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "001-50")]
    public void Pbkdf2Hash_ReproducesTheWerkzeugOutputBitForBit()
    {
        // Mesma senha, salt e iterações do hash gerado pelo Werkzeug => saída idêntica.
        foreach (var fixture in AuthFixtures.Passwords.Where(p => p.Kind == "pbkdf2-1000"))
        {
            var parts = fixture.Hash.Split('$');
            var recomputed = WerkzeugPasswordHasher.Pbkdf2Hash(fixture.Password, parts[1], 1000);

            recomputed.ShouldBe(fixture.Hash, fixture.Password);
        }
    }

    [Fact]
    public void Constructor_RejectsIterationsBelowTheMinimum()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new WerkzeugPasswordHasher(iterations: 10));
    }
}
