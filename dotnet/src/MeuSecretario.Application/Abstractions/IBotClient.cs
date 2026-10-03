namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Cliente do bot WhatsApp. Devolve <c>true</c> somente se o bot respondeu HTTP 200; nunca lança por falha de rede.
/// URL, chave e sessão vêm da configuração (spec 010 §5.1).
/// </summary>
public interface IBotClient
{
    Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken);

    Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken);
}
