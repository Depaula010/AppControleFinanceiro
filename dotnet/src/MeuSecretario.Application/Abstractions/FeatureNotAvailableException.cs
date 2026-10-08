namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Lançada pelas implementações <c>Unavailable*</c> das portas dos jobs (009c) enquanto a feature que as implementa não
/// está registrada. As rotas <c>/admin/*</c> dependentes convertem em 503 <c>Funcionalidade ainda não disponível</c>
/// (AC-009-39). Nunca carrega dados do usuário nem segredo.
/// </summary>
public sealed class FeatureNotAvailableException : Exception
{
    public FeatureNotAvailableException()
        : base("Funcionalidade ainda não disponível")
    {
    }

    public FeatureNotAvailableException(string message)
        : base(message)
    {
    }

    public FeatureNotAvailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
