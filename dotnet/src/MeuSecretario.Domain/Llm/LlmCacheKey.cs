using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MeuSecretario.Domain.Llm;

/// <summary>
/// Chave do cache do Gemini, idêntica à do Python: <c>gemini_cache:{prefixo}:{uid|global}:{md5(prompt)[:12]}</c>
/// (spec 010 RN-010-26). A chave não inclui o modelo nem a chave de API.
/// </summary>
public static class LlmCacheKey
{
    public const string Root = "gemini_cache";

    /// <summary><c>uid</c> só entra quando o cache é específico do usuário e o id é maior que zero (o <c>if usuario_id</c> do Python).</summary>
    public static string Build(string prefix, int? userId, bool userSpecific, string prompt)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(prompt);
        var user = userSpecific && userId is > 0 and var id ? id.ToString(CultureInfo.InvariantCulture) : "global";
        return $"{Root}:{prefix}:{user}:{Hash12(prompt)}";
    }

    /// <summary>12 primeiros hex minúsculos do MD5 do prompt em UTF-8 (<c>abc</c> => <c>900150983cd2</c>).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "MD5 só compõe a chave de cache, idêntica à do Python (o Redis é compartilhado); não é segurança.")]
    public static string Hash12(string prompt) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(prompt)))[..12];
}
