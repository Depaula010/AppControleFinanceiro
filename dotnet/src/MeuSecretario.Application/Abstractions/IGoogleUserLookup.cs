namespace MeuSecretario.Application.Abstractions;

/// <summary>Dados do usuário para as páginas e notificações do Google (porta interna da 010d-1).</summary>
public interface IGoogleUserLookup
{
    /// <summary><c>SELECT nome, numero_whatsapp FROM usuarios WHERE id = @id</c>; <c>null</c> se não existe.</summary>
    Task<GoogleUserInfo?> FindAsync(int userId, CancellationToken cancellationToken);

    Task<string?> FindNameAsync(int userId, CancellationToken cancellationToken);
}

public sealed record GoogleUserInfo(string? Name, string? WhatsappNumber);
