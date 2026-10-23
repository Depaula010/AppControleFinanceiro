namespace MeuSecretario.Infrastructure.Configuration;

/// <summary>
/// Configuração da aplicação, lida de variáveis de ambiente com os MESMOS nomes dos secrets
/// atuais do GitHub (specs/foundation/integrations.md §2). Falha rápido, listando os nomes
/// que faltam; nunca inclui valores na mensagem.
/// </summary>
public sealed class AppSettings
{
    public const int MinSecretKeyLength = 32;

    public required string DatabaseUrl { get; init; }
    public required string ConnectionString { get; init; }
    public required string ApiSecretKey { get; init; }
    public required string EncryptionKey { get; init; }
    public required string GeminiApiKey { get; init; }
    public required string RedisUrl { get; init; }

    /// <summary><c>GEMINI_MODEL</c> (opcional).</summary>
    public string GeminiModel { get; init; } = "gemini-2.5-flash-lite";

    public string WebhookSignatureKey { get; init; } = string.Empty;
    public string BotWhatsappUrl { get; init; } = string.Empty;
    public string BotSessionName { get; init; } = "sessao-principal";
    public string BotSessionId { get; init; } = string.Empty;
    public string BotApiKey { get; init; } = string.Empty;
    public string GoogleClientId { get; init; } = string.Empty;
    public string GoogleClientSecret { get; init; } = string.Empty;
    public string GoogleRedirectUri { get; init; } = string.Empty;
    /// <summary>
    /// <c>GOOGLE_CONNECT_LEGACY_UNTIL</c> (opcional, desvio 053): aceita links de conexão sem <c>t</c> até esta data.
    /// Só data (<c>2026-11-30</c>) vale até o fim desse dia em São Paulo (-03:00); data e hora usam o deslocamento informado
    /// (ou UTC, sem deslocamento). Ausente = desligado.
    /// </summary>
    public DateTimeOffset? GoogleConnectLegacyUntil { get; init; }

    public string WeatherApiKey { get; init; } = string.Empty;
    public string OpenRouteApiKey { get; init; } = string.Empty;
    public string OpenRouteBaseUrl { get; init; } = "https://api.openrouteservice.org";
    public int TravelTimeDailyLimit { get; init; } = 10;
    public int PasswordHashIterations { get; init; } = Security.WerkzeugPasswordHasher.DefaultIterations;
    public string LogDirectory { get; init; } = "logs";
    public bool LogToFile { get; init; } = true;

    /// <summary>
    /// <c>API_KEYS_ROUTES_ENABLED</c>: só com <c>true</c> as rotas <c>/api-keys/*</c> existem (desvio 052).
    /// Padrão false: desligadas respondem 404.
    /// </summary>
    public bool ApiKeysRoutesEnabled { get; init; }

    private static DateTimeOffset? ParseLegacyUntil(string text)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", invariant, System.Globalization.DateTimeStyles.None, out var date))
        {
            // Válido até o fim do dia em São Paulo (sem horário de verão desde 2019).
            return new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3));
        }

        return DateTimeOffset.TryParse(text, invariant, System.Globalization.DateTimeStyles.AssumeUniversal, out var moment)
            ? moment
            : null;
    }

    /// <param name="get">Leitor de variáveis (ex.: <c>key =&gt; configuration[key]</c>).</param>
    public static AppSettings Load(Func<string, string?> get)
    {
        var errors = new List<string>();

        string Required(string key)
        {
            var value = get(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{key} é obrigatória");
                return string.Empty;
            }
            return value.Trim();
        }

        string Optional(string key, string fallback = "")
        {
            var value = get(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        var databaseUrl = Required("DATABASE_URL");
        var apiSecretKey = Required("API_SECRET_KEY");
        var encryptionKey = Required("ENCRYPTION_KEY");
        var geminiApiKey = Required("GEMINI_API_KEY");
        var redisUrl = Required("REDIS_URL");

        if (apiSecretKey.Length is > 0 and < MinSecretKeyLength)
        {
            errors.Add($"API_SECRET_KEY deve ter no mínimo {MinSecretKeyLength} caracteres");
        }

        if (encryptionKey.Length > 0 && !Security.FernetEncryptionService.IsValidKey(encryptionKey))
        {
            errors.Add("ENCRYPTION_KEY inválida: deve ser uma chave Fernet (base64 url-safe de 32 bytes)");
        }

        var connectionString = string.Empty;
        if (databaseUrl.Length > 0)
        {
            try
            {
                connectionString = DatabaseUrlParser.ToConnectionString(databaseUrl);
            }
            catch (FormatException ex)
            {
                errors.Add(ex.Message);
            }
        }

        var dailyLimit = 10;
        var dailyLimitText = Optional("TRAVEL_TIME_DAILY_LIMIT");
        if (dailyLimitText.Length > 0 && !int.TryParse(dailyLimitText, out dailyLimit))
        {
            errors.Add("TRAVEL_TIME_DAILY_LIMIT deve ser um número inteiro");
        }

        var iterations = Security.WerkzeugPasswordHasher.DefaultIterations;
        var iterationsText = Optional("PASSWORD_HASH_ITERATIONS");
        if (iterationsText.Length > 0
            && (!int.TryParse(iterationsText, out iterations)
                || iterations < Security.WerkzeugPasswordHasher.MinIterations
                || iterations > Security.WerkzeugPasswordHasher.MaxIterationsAllowed))
        {
            errors.Add($"PASSWORD_HASH_ITERATIONS deve ser um inteiro entre {Security.WerkzeugPasswordHasher.MinIterations} e {Security.WerkzeugPasswordHasher.MaxIterationsAllowed}");
        }

        DateTimeOffset? legacyUntil = null;
        var legacyText = Optional("GOOGLE_CONNECT_LEGACY_UNTIL");
        if (legacyText.Length > 0)
        {
            legacyUntil = ParseLegacyUntil(legacyText);
            if (legacyUntil is null)
            {
                errors.Add("GOOGLE_CONNECT_LEGACY_UNTIL deve ser uma data (AAAA-MM-DD) ou data e hora ISO 8601");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Configuração inválida: " + string.Join("; ", errors));
        }

        return new AppSettings
        {
            DatabaseUrl = databaseUrl,
            ConnectionString = connectionString,
            ApiSecretKey = apiSecretKey,
            EncryptionKey = encryptionKey,
            GeminiApiKey = geminiApiKey,
            RedisUrl = redisUrl,
            GeminiModel = Optional("GEMINI_MODEL", "gemini-2.5-flash-lite"),
            WebhookSignatureKey = Optional("WEBHOOK_SIGNATURE_KEY", apiSecretKey),
            BotWhatsappUrl = Optional("BOT_WHATSAPP_URL"),
            BotSessionName = Optional("BOT_SESSION_NAME", "sessao-principal"),
            BotSessionId = Optional("BOT_SESSION_ID"),
            BotApiKey = Optional("BOT_API_KEY"),
            GoogleClientId = Optional("GOOGLE_CLIENT_ID"),
            GoogleClientSecret = Optional("GOOGLE_CLIENT_SECRET"),
            GoogleRedirectUri = Optional("GOOGLE_REDIRECT_URI"),
            GoogleConnectLegacyUntil = legacyUntil,
            WeatherApiKey = Optional("WEATHER_API_KEY"),
            OpenRouteApiKey = Optional("OPENROUTE_API_KEY"),
            OpenRouteBaseUrl = Optional("OPENROUTE_BASE_URL", "https://api.openrouteservice.org"),
            TravelTimeDailyLimit = dailyLimit,
            PasswordHashIterations = iterations,
            LogDirectory = Optional("LOG_DIR", "logs"),
            LogToFile = !string.Equals(Optional("LOG_TO_FILE", "true"), "false", StringComparison.OrdinalIgnoreCase),
            ApiKeysRoutesEnabled = string.Equals(Optional("API_KEYS_ROUTES_ENABLED", "false"), "true", StringComparison.OrdinalIgnoreCase),
        };
    }
}
