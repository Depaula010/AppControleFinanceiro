namespace MeuSecretario.Application.Abstractions;

/// <summary>Criptografia simétrica compatível com o Fernet do Python (<c>cryptography.fernet</c>).</summary>
public interface IEncryptionService
{
    /// <summary>Cifra o texto e devolve o token Fernet (base64 url-safe, começa com <c>gAAAAA</c>).</summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decifra um token Fernet, sem validar idade (como o Python sem <c>ttl</c>).
    /// Lança <see cref="System.Security.Cryptography.CryptographicException"/> se o token for inválido.
    /// </summary>
    string Decrypt(string token);

    /// <summary>Heurística do Flask: começa com <c>gAAAAA</c> e tem pelo menos 40 caracteres.</summary>
    bool LooksEncrypted(string value);

    /// <summary>
    /// Decifra se parecer cifrado; texto puro legado (como a <c>api_key_automate</c> do usuário atual)
    /// e tokens que falham ao decifrar voltam sem alteração.
    /// </summary>
    string DecryptOrPlain(string value);
}
