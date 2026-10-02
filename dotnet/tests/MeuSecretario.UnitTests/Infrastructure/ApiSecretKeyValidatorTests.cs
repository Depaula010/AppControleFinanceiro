using MeuSecretario.Infrastructure.Security;

namespace MeuSecretario.UnitTests.Infrastructure;

/// <summary>Spec 008 AC-008-02: comparação do X-API-KEY por hash e tempo constante.</summary>
public class ApiSecretKeyValidatorTests
{
    private const string Secret = "chave-de-teste-com-mais-de-32-caracteres-0123456789";

    private static readonly ApiSecretKeyValidator Validator = new(Secret);

    [Fact]
    [Trait("AC", "008-02")]
    public void CorrectKey_IsValid() => Validator.IsValid(Secret).ShouldBeTrue();

    [Theory]
    [Trait("AC", "008-02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("errada")]
    [InlineData("chave-de-teste-com-mais-de-32-caracteres-012345678")]   // prefixo (tamanho diferente)
    [InlineData("chave-de-teste-com-mais-de-32-caracteres-0123456789x")] // maior
    [InlineData("CHAVE-DE-TESTE-COM-MAIS-DE-32-CARACTERES-0123456789")]  // caixa diferente
    [InlineData(" chave-de-teste-com-mais-de-32-caracteres-0123456789")] // sem trim
    public void WrongOrMissingKey_IsInvalid(string? presented) => Validator.IsValid(presented).ShouldBeFalse();

    [Fact]
    public void EmptySecret_NeverValidates()
    {
        var validator = new ApiSecretKeyValidator(string.Empty);

        validator.IsValid(string.Empty).ShouldBeFalse();
        validator.IsValid("qualquer").ShouldBeFalse();
    }
}
