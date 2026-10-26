using System.Security.Cryptography;
using System.Text;

namespace MeuSecretario.Application.CoachAgenda;

/// <summary>Chave recém-gerada: o texto só existe neste objeto (RN-016-03); o banco guarda o hash.</summary>
public sealed record ChaveGerada(string Chave, string Final, DateTime CriadaEm);

/// <summary>O que se pode dizer de uma chave já gerada: nunca a chave (AC-016-02).</summary>
public sealed record ChaveExistente(string Final, DateTime CriadaEm, DateTime? UltimoUsoEm);

/// <summary>Spec 016: as chaves de leitura da agenda, uma ativa por pessoa. Só o hash é guardado.</summary>
public interface IChavesDaAgenda
{
    /// <summary>O dono de uma chave ativa (e marca o uso), ou <c>null</c> se desconhecida ou revogada.</summary>
    Task<int?> ResolverAsync(string chave, CancellationToken cancellationToken);

    /// <summary>Gera a chave e revoga a anterior, na mesma transação.</summary>
    Task<ChaveGerada> GerarAsync(int userId, CancellationToken cancellationToken);

    Task<ChaveExistente?> ConsultarAsync(int userId, CancellationToken cancellationToken);

    Task<bool> RevogarAsync(int userId, CancellationToken cancellationToken);
}

public static class FormaDaChave
{
    /// <summary>32 bytes aleatórios em hexadecimal (64 caracteres).</summary>
    public static string Nova() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string chave) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)));

    public static string Final(string chave) => chave[^4..];
}
