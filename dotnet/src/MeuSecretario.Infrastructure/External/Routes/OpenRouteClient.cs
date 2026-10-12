using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Domain.Routes;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.External.Routes;

/// <summary>
/// OpenRouteService (RN-010-60/61/62). Chave só via <see cref="IProviderKeySelector"/> e só nesta requisição (nunca em
/// <c>DefaultRequestHeaders</c>). Até 2 tentativas, sem espera, em timeout, erro de rede e 5xx (desvio 064); 4xx falha
/// na 1ª requisição; JSON inválido ou campo ausente devolve <c>null</c> sem repetir. Falha da chave do usuário propaga
/// <see cref="ApiKeyUnavailableException"/>. Logs nunca trazem chave, URL, endereço nem coordenadas.
/// </summary>
public sealed class OpenRouteClient(
    IHttpClientFactory httpClients,
    IProviderKeySelector keys,
    IIntegrationSettings settings,
    ILogger<OpenRouteClient> logger) : IRouteClient
{
    private const string Geocode = "geocode";
    private const string Directions = "directions";

    public async Task<GeocodeResult?> GeocodeAsync(string address, int? userId, CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(userId, Geocode, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return null;
        }

        var url = $"{BaseUrl()}/geocode/search?api_key={Uri.EscapeDataString(key)}&text={Uri.EscapeDataString(address)}&boundary.country=BR&size=1";
        using var document = await SendAsync(Geocode, () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        try
        {
            if (!document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array || features.GetArrayLength() == 0)
            {
                ExternalLog.RouteNoResult(logger, Geocode, "sem resultado");
                return null;
            }

            var feature = features[0];
            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            var lon = coordinates[0].GetDouble();
            var lat = coordinates[1].GetDouble();
            var label = feature.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String
                    ? labelElement.GetString()!
                    : address;

            if (!BrazilBounds.Contains(lat, lon))
            {
                ExternalLog.RouteNoResult(logger, Geocode, "fora do Brasil");
                return null;
            }

            return new GeocodeResult(lat, lon, label);
        }
        catch (Exception ex) when (IsBadPayload(ex))
        {
            ExternalLog.Failed(logger, "OpenRoute", ex.GetType().Name);
            return null;
        }
    }

    public async Task<TravelResult?> CalculateTravelTimeAsync(Coordinates origin, Coordinates destination, int? userId, CancellationToken cancellationToken)
    {
        var key = await ResolveKeyAsync(userId, Directions, cancellationToken).ConfigureAwait(false);
        if (key is null)
        {
            return null;
        }

        var body = Payload(origin, destination);
        var url = $"{BaseUrl()}/v2/directions/driving-car";
        using var document = await SendAsync(Directions, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.TryAddWithoutValidation("Authorization", key);
            return request;
        }, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        try
        {
            if (!document.RootElement.TryGetProperty("routes", out var routes) || routes.ValueKind != JsonValueKind.Array || routes.GetArrayLength() == 0)
            {
                ExternalLog.RouteNoResult(logger, Directions, "sem rota");
                return null;
            }

            var route = routes[0];
            var summary = route.GetProperty("summary");
            var minutes = TravelMath.DurationMinutes(summary.GetProperty("duration").GetDouble());
            var km = TravelMath.DistanceKm(summary.GetProperty("distance").GetDouble());
            return new TravelResult(minutes, km, FirstStepName(route));
        }
        catch (Exception ex) when (IsBadPayload(ex))
        {
            ExternalLog.Failed(logger, "OpenRoute", ex.GetType().Name);
            return null;
        }
    }

    private static string? FirstStepName(JsonElement route)
    {
        if (!route.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() == 0
            || !segments[0].TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() == 0)
        {
            return null;
        }

        var step = steps[0];
        if (!step.TryGetProperty("name", out var name))
        {
            return string.Empty;
        }

        return name.ValueKind == JsonValueKind.String ? name.GetString() : null;
    }

    /// <summary><c>{"coordinates":[[lon,lat],[lon,lat]]}</c>, ordem do ORS (longitude primeiro).</summary>
    private static byte[] Payload(Coordinates origin, Coordinates destination)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("coordinates");
            foreach (var point in new[] { origin, destination })
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(point.Lon);
                writer.WriteNumberValue(point.Lat);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private string BaseUrl() => settings.OpenRouteBaseUrl.Trim().TrimEnd('/');

    private async Task<string?> ResolveKeyAsync(int? userId, string operation, CancellationToken cancellationToken)
    {
        var selection = await keys.SelectAsync(userId, ApiProviders.OpenRoute, cancellationToken).ConfigureAwait(false);
        if (selection.Failure is { } failure)
        {
            // Como o Python, a falha da chave do usuário propaga (travel_time_service.py:37-39).
            throw new ApiKeyUnavailableException(failure.Message);
        }

        if (!selection.HasKey)
        {
            ExternalLog.RouteDisabled(logger, operation);
            return null;
        }

        return selection.Key;
    }

    /// <summary>
    /// Envia com até 2 tentativas (timeout, rede e 5xx). Devolve o JSON de um 2xx; <c>null</c> em 4xx, falha final ou
    /// corpo que não é JSON (sem repetir).
    /// </summary>
    private async Task<JsonDocument?> SendAsync(string operation, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= ExternalHttpOptions.RouteMaxAttempts; attempt++)
        {
            string reason;
            try
            {
                using var request = build();
                using var client = httpClients.CreateClient(ExternalHttpOptions.RouteClient);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return JsonDocument.Parse(text);
                    }
                    catch (JsonException ex)
                    {
                        ExternalLog.Failed(logger, "OpenRoute", ex.GetType().Name);
                        return null;
                    }
                }

                ExternalLog.HttpStatus(logger, "OpenRoute", status);
                if (response.StatusCode < HttpStatusCode.InternalServerError)
                {
                    return null;
                }

                reason = $"HTTP {status.ToString(CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex) when (ExternalHttpOptions.IsTransient(ex, cancellationToken))
            {
                reason = ex.GetType().Name;
            }

            if (attempt < ExternalHttpOptions.RouteMaxAttempts)
            {
                ExternalLog.RouteRetry(logger, operation, attempt, reason);
            }
            else
            {
                ExternalLog.Failed(logger, "OpenRoute", reason);
            }
        }

        return null;
    }

    private static bool IsBadPayload(Exception ex) =>
        ex is InvalidOperationException or KeyNotFoundException or FormatException or JsonException or IndexOutOfRangeException or ArgumentOutOfRangeException;
}
