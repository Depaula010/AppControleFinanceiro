using System.Text.RegularExpressions;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Lê <c>gemini.modelo</c> e <c>gemini.versao_api</c> da tabela <c>configuracoes</c> e guarda o resultado por 60 s, então
/// uma troca por SQL vale em até 1 minuto, sem deploy. Linha ausente ou valor fora do formato (vai para a URL) cai no
/// padrão (<c>GEMINI_MODEL</c> / <see cref="GeminiHttpOptions.DefaultApiVersion"/>). Se o banco falhar, mantém o último
/// valor bom: o Gemini não pode parar por causa da tabela de configuração.
/// </summary>
public sealed partial class DbGeminiEndpointProvider(
    IDbConnectionFactory connections,
    IIntegrationSettings settings,
    TimeProvider clock,
    ILogger<DbGeminiEndpointProvider> logger) : IGeminiEndpointProvider
{
    public const string ModelKey = "gemini.modelo";
    public const string ApiVersionKey = "gemini.versao_api";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private (GeminiEndpoint Value, DateTimeOffset ExpiresAt)? _cached;

    public async Task<GeminiEndpoint> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (_cached is { } hit && hit.ExpiresAt > now)
        {
            return hit.Value;
        }

        GeminiEndpoint endpoint;
        try
        {
            endpoint = await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LlmLog.ConfigReadFailed(logger, ex.GetType().Name);
            endpoint = _cached?.Value ?? Fallback(null, null);
        }

        lock (_gate)
        {
            _cached = (endpoint, now + CacheTtl);
        }

        return endpoint;
    }

    private async Task<GeminiEndpoint> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await connection.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT chave AS Key, valor AS Value FROM configuracoes WHERE chave IN (@modelKey, @versionKey)",
            new { modelKey = ModelKey, versionKey = ApiVersionKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToDictionary(r => r.Key, r => r.Value.Trim());

        rows.TryGetValue(ModelKey, out var model);
        rows.TryGetValue(ApiVersionKey, out var version);
        return Fallback(model, version);
    }

    private GeminiEndpoint Fallback(string? model, string? version)
    {
        if (model is not null && !ValidModel().IsMatch(model))
        {
            LlmLog.ConfigInvalid(logger, ModelKey);
            model = null;
        }

        if (version is not null && !ValidVersion().IsMatch(version))
        {
            LlmLog.ConfigInvalid(logger, ApiVersionKey);
            version = null;
        }

        var envModel = settings.GeminiModel?.Trim();
        return new GeminiEndpoint(
            model ?? (string.IsNullOrEmpty(envModel) ? GeminiHttpOptions.DefaultModel : envModel),
            version ?? GeminiHttpOptions.DefaultApiVersion);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")]
    private static partial Regex ValidModel();

    [GeneratedRegex("^v[0-9]{1,2}([a-z]+[0-9]*)?$")]
    private static partial Regex ValidVersion();
}
