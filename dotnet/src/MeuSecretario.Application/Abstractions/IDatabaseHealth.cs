namespace MeuSecretario.Application.Abstractions;

/// <param name="Connected">Se o banco respondeu a <c>SELECT 1</c>.</param>
/// <param name="Error">Rótulo curto do erro, sem texto de exceção (pode conter host e credenciais).</param>
public sealed record DatabaseStatus(bool Connected, string? Error = null);

public interface IDatabaseHealth
{
    Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken);
}
