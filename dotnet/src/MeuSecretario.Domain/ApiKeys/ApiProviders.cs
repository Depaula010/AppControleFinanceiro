namespace MeuSecretario.Domain.ApiKeys;

/// <summary>Provedores de API de terceiros suportados (RN-008-01). Comparação exata, minúsculas.</summary>
public static class ApiProviders
{
    public const string Gemini = "gemini";
    public const string Weather = "weather";
    public const string OpenRoute = "openroute";

    /// <summary>Texto da mensagem "Provedor deve ser um de: ...".</summary>
    public const string ValidListText = "gemini, weather, openroute";

    public static IReadOnlyList<string> All { get; } = [Gemini, Weather, OpenRoute];

    public static bool IsValid(string? provider) => provider is not null && All.Contains(provider, StringComparer.Ordinal);
}

/// <summary>Tipo de chave usada numa chamada: do usuário ou do sistema (cobrada).</summary>
public static class KeyTypes
{
    public const string Own = "propria";
    public const string System = "sistema";
}
