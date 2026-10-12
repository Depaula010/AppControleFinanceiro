using System.Globalization;
using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Domain.Weather;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.External.Weather;

/// <summary>
/// WeatherAPI (spec 010 RN-010-55/56/58). HTTPS (desvio 022). A chave vai na query (a API não aceita header), por isso
/// a URL nunca é registrada em log nem a mensagem das exceções de rede. Qualquer falha devolve <c>null</c>; sem
/// chave não há requisição. Timeout de 5 s vem do pipeline do <c>HttpClient</c> "weather".
/// </summary>
public sealed class WeatherApiClient(
    IHttpClientFactory httpClients,
    IProviderKeySelector keys,
    ILogger<WeatherApiClient> logger) : IWeatherClient
{
    private const string Service = "WeatherAPI";

    public async Task<WeatherInfo?> GetWeatherAsync(string city, string? state, int? userId, CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(userId, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return null;
        }

        var location = !string.IsNullOrEmpty(state) ? $"{city},{state}"
            : city.Contains(',', StringComparison.Ordinal) ? city : $"{city},Brazil";
        using var document = await GetAsync("current.json", $"key={Uri.EscapeDataString(key)}&q={Uri.EscapeDataString(location)}&lang=pt", cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        try
        {
            var current = Child(document.RootElement, "current");
            var condition = Child(current, "condition");
            var text = condition.ValueKind == JsonValueKind.Object ? ReadString(condition, "text") : null;
            int? code = condition.ValueKind == JsonValueKind.Object && condition.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32()
                : null;
            bool? isDay = current.ValueKind == JsonValueKind.Object && current.TryGetProperty("is_day", out var dayElement) && dayElement.ValueKind == JsonValueKind.Number
                ? dayElement.GetInt32() != 0
                : null;

            var translated = WeatherConditions.Translate(text, code, isDay);
            var temperature = ReadInt(current, "temp_c");
            var feelsLike = ReadInt(current, "feelslike_c");
            var humidity = ReadInt(current, "humidity");
            var description = string.Create(CultureInfo.InvariantCulture, $"{temperature}°C, {translated.Name} {translated.Emoji}");
            return new WeatherInfo(temperature, translated.Name, translated.Emoji, feelsLike, humidity, null, description);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException or OverflowException)
        {
            ExternalLog.Failed(logger, Service, ex.GetType().Name);
            return null;
        }
    }

    public async Task<ForecastInfo?> GetForecastAsync(string city, string? state, int days, int? userId, CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(userId, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return null;
        }

        var location = !string.IsNullOrEmpty(state) ? $"{city},{state}" : $"{city},Brazil";
        var query = string.Create(CultureInfo.InvariantCulture, $"key={Uri.EscapeDataString(key)}&q={Uri.EscapeDataString(location)}&days={Math.Min(days, 3)}&lang=pt");
        using var document = await GetAsync("forecast.json", query, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        try
        {
            var forecastDays = Child(Child(document.RootElement, "forecast"), "forecastday");
            if (forecastDays.ValueKind != JsonValueKind.Array || forecastDays.GetArrayLength() == 0)
            {
                return null;
            }

            var day = Child(forecastDays[0], "day");
            return new ForecastInfo(ReadInt(day, "daily_chance_of_rain"), ReadInt(day, "maxtemp_c"), ReadInt(day, "mintemp_c"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException or OverflowException)
        {
            ExternalLog.Failed(logger, Service, ex.GetType().Name);
            return null;
        }
    }

    private async Task<string?> ResolveKeyAsync(int? userId, CancellationToken cancellationToken)
    {
        var selection = await keys.SelectAsync(userId, ApiProviders.Weather, cancellationToken).ConfigureAwait(false);
        if (selection.Failure is { } failure)
        {
            ExternalLog.KeyUnavailable(logger, Service, failure.Reason.ToString());
            return null;
        }

        if (!selection.HasKey)
        {
            ExternalLog.WeatherDisabled(logger);
            return null;
        }

        return selection.Key;
    }

    /// <summary>GET; <c>null</c> em status diferente de 200, JSON inválido, timeout ou rede.</summary>
    private async Task<JsonDocument?> GetAsync(string path, string query, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClients.CreateClient(ExternalHttpOptions.WeatherClient);
            using var response = await client.GetAsync(new Uri($"{ExternalHttpOptions.WeatherBaseUrl}/{path}?{query}"), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                ExternalLog.HttpStatus(logger, Service, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonDocument.Parse(body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A mensagem da exceção de rede pode trazer a URL (com a chave): só o tipo vai para o log.
            ExternalLog.Failed(logger, Service, ex.GetType().Name);
            return null;
        }
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) ? child : default;

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary><c>int(valor)</c> do Python: ausente vale 0, número trunca; outro tipo é erro.</summary>
    private static int ReadInt(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number
            ? WeatherFormatter.Truncate(value.GetDouble())
            : throw new FormatException("Valor numérico esperado.");
    }
}
