using System.Security.Cryptography;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Infrastructure;

public class FernetEncryptionServiceTests
{
    private static FernetEncryptionService Create(string? key = null, DateTimeOffset? now = null) =>
        new(key ?? AuthFixtures.FernetKey, new FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    [Trait("AC", "001-51")]
    public void Decrypt_AcceptsTokensGeneratedByThePythonReferenceImplementation()
    {
        AuthFixtures.FernetCases.Count.ShouldBeGreaterThan(0);

        foreach (var fernetCase in AuthFixtures.FernetCases)
        {
            Create().Decrypt(fernetCase.Token).ShouldBe(fernetCase.Plaintext);
        }
    }

    [Fact]
    [Trait("AC", "001-51")]
    public void EncryptFromParts_ProducesExactlyTheSameTokenAsPython()
    {
        for (var i = 0; i < AuthFixtures.FernetCases.Count; i++)
        {
            var fernetCase = AuthFixtures.FernetCases[i];
            var iv = Enumerable.Range(0, 16).Select(j => (byte)((i * 16) + j)).ToArray(); // mesmo IV do gerador Python

            var token = Create().EncryptFromParts(fernetCase.Plaintext, fernetCase.Timestamp, iv);

            token.ShouldBe(fernetCase.Token, $"caso {i}");
        }
    }

    [Fact]
    public void Decrypt_DoesNotApplyATimeToLive()
    {
        // O Python decifra sem ttl: um token de 2023 continua válido em 2030.
        var fernetCase = AuthFixtures.FernetCases[0];
        var farFuture = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Create(now: farFuture).Decrypt(fernetCase.Token).ShouldBe(fernetCase.Plaintext);
    }

    [Fact]
    public void Encrypt_RoundTripsWithRandomIvAndFernetPrefix()
    {
        var service = Create();

        var first = service.Encrypt("minha-chave-de-api");
        var second = service.Encrypt("minha-chave-de-api");

        first.ShouldNotBe(second);
        first.ShouldStartWith("gAAAAA");
        service.Decrypt(first).ShouldBe("minha-chave-de-api");
    }

    [Fact]
    [Trait("AC", "001-13")]
    public void Encrypt_Of43CharKey_Gives140Characters_WhichDoesNotFitVarchar100()
    {
        // Motivo do desvio 017: api_key_automate era varchar(100).
        Create().Encrypt(new string('x', 43)).Length.ShouldBe(140);
    }

    [Fact]
    public void Decrypt_RejectsTamperedToken()
    {
        Should.Throw<CryptographicException>(() => Create().Decrypt(AuthFixtures.FernetTamperedToken));
    }

    [Fact]
    public void Decrypt_RejectsTokenEncryptedWithAnotherKey()
    {
        var otherKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()).Replace('+', '-').Replace('/', '_');
        var token = Create(otherKey).Encrypt("segredo");

        Should.Throw<CryptographicException>(() => Create().Decrypt(token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("gAAAAA")]
    [InlineData("não é base64 ###")]
    public void Decrypt_RejectsGarbage(string token)
    {
        Should.Throw<CryptographicException>(() => Create().Decrypt(token));
    }

    [Theory]
    [InlineData("gAAAAABlabcdefghijklmnopqrstuvwxyz0123456789", true)]
    [InlineData("gAAAAA", false)]                     // curto demais
    [InlineData("chave-em-texto-puro-sem-prefixo-fernet-abc", false)]
    [InlineData("", false)]
    public void LooksEncrypted_UsesTheFlaskHeuristic(string value, bool expected)
    {
        Create().LooksEncrypted(value).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "001-52")]
    public void DecryptOrPlain_AcceptsBothEncryptedAndLegacyPlainText()
    {
        var service = Create();

        service.DecryptOrPlain(service.Encrypt("nova-chave")).ShouldBe("nova-chave");
        service.DecryptOrPlain("chave-legada-em-texto-puro-de-40-chars").ShouldBe("chave-legada-em-texto-puro-de-40-chars");
        // Parece Fernet mas não decifra: devolve o valor original, como decrypt_safe do Flask.
        var fake = "gAAAAAB" + new string('x', 60);
        service.DecryptOrPlain(fake).ShouldBe(fake);
    }

    [Theory]
    [InlineData("")]
    [InlineData("curta")]
    [InlineData("###não-é-base64###")]
    public void Constructor_RejectsInvalidKeyWithoutLeakingIt(string key)
    {
        var ex = Should.Throw<ArgumentException>(() => Create(key.Length == 0 ? " " : key));

        if (key.Length > 0)
        {
            ex.Message.ShouldNotContain(key);
        }
    }
}
