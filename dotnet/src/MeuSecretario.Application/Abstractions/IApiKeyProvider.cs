using System.Globalization;

namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Porta que decide qual chave de API usar numa chamada a um provedor externo (equivale a
/// <c>GerenciadorChavesApi.obter_chave_api</c>). Consumida pela 010 (Gemini, clima, rotas).
/// </summary>
/// <remarks>
/// Algoritmo (spec 008 §5): provedor inválido, sem preferência, chave própria ausente, chave do sistema
/// indisponível ou limite do plano atingido viram <see cref="ApiKeyResolution.Failed"/> com a mensagem literal do
/// Flask, que o consumidor mostra ao usuário. Erros de banco em tarefas acessórias (último uso, contagem de uso,
/// log, limite) são engolidos e registrados. Token Fernet da chave própria que não decifra lança
/// <see cref="System.Security.Cryptography.CryptographicException"/> (nunca devolve o texto cifrado).
/// O consumidor nunca deve registrar <see cref="ApiKeyResolution.Resolved.Key"/>.
/// </remarks>
public interface IApiKeyProvider
{
    Task<ApiKeyResolution> ResolveAsync(int userId, string provider, CancellationToken cancellationToken);
}

/// <summary>Motivos de falha da resolução (<see cref="ApiKeyResolution.Failed"/>).</summary>
public enum ApiKeyFailure
{
    InvalidProvider,
    NoPreference,
    OwnKeyMissing,
    SystemKeyUnavailable,
    LimitReached,
}

public abstract record ApiKeyResolution
{
    /// <summary>Chave resolvida; <paramref name="KeyType"/> é <c>propria</c> ou <c>sistema</c> (<c>KeyTypes</c>).</summary>
    public sealed record Resolved(string Key, string KeyType) : ApiKeyResolution
    {
        /// <summary>Nunca inclui a chave (AC-008-68).</summary>
        public override string ToString() => $"Resolved {{ KeyType = {KeyType}, Key = *** }}";
    }

    /// <summary><paramref name="Message"/> é o texto literal do Flask, para exibir ao usuário.</summary>
    public sealed record Failed(ApiKeyFailure Reason, string Message) : ApiKeyResolution;
}

/// <summary>Textos literais das falhas da porta (<c>gerenciador_chaves_api.py</c>).</summary>
public static class ApiKeyMessages
{
    public static string InvalidProvider(string provider) => $"Provedor inválido: {provider}";

    public static string NoPreference(string provider) =>
        $"⚙️ Você ainda não configurou suas preferências para {provider}.\n" +
        "Acesse o dashboard e escolha:\n" +
        "• Usar sua própria chave (grátis)\n" +
        "• Usar chave do sistema (com custo adicional)";

    public static string OwnKeyMissing(string provider) =>
        $"🔑 Você configurou para usar sua chave de {provider}, mas ainda não cadastrou.\n" +
        "Acesse o dashboard para cadastrar sua chave.\n" +
        $"Ou responda: *Cadastrar chave de {provider}*";

    public static string SystemKeyUnavailable(string provider) =>
        $"❌ Chave do sistema para {provider} indisponível no momento.\n" +
        "Entre em contato com o suporte ou configure sua própria chave.";

    public static string LimitReached(string provider, string planName, int limit, long used) =>
        $"🚫 Limite mensal atingido para {provider}!\n" +
        string.Create(CultureInfo.InvariantCulture, $"Seu plano {planName} permite {limit} chamadas/mês.\n") +
        string.Create(CultureInfo.InvariantCulture, $"Você já usou {used} chamadas.\n\n") +
        "💡 Opções:\n" +
        "• Faça upgrade do plano\n" +
        "• Use sua própria chave (sem limites)";
}
