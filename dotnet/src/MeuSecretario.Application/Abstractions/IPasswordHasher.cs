namespace MeuSecretario.Application.Abstractions;

/// <summary>Hash de senha compatível com o <c>werkzeug.security</c> do Flask (spec 001, RN-001-04).</summary>
public interface IPasswordHasher
{
    /// <summary>Gera <c>pbkdf2:sha256:&lt;iterações&gt;$&lt;salt&gt;$&lt;hex&gt;</c>.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifica contra <c>pbkdf2:sha256</c> ou <c>scrypt</c>. Formato desconhecido ou malformado
    /// devolve <c>false</c> (nunca lança).
    /// </summary>
    bool Verify(string? hash, string password);
}
