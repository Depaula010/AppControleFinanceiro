using System.Text.Json;
using MeuSecretario.Api.Accounts;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Security;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Application.Security;
using Microsoft.Extensions.Options;

namespace MeuSecretario.Api.Admin;

/// <summary>Textos exatos da spec 009 §4 (e dos desvios 077 a 080). Mudar uma palavra aqui é mudar o contrato.</summary>
public static class AdminMessages
{
    public const string Unauthorized = "Não autorizado";
    public const string InvalidApiKeyReason = "Invalid API key";

    public const string IpRequired = "Campo 'ip' obrigatório";
    public const string IpMissingInList = "Campos obrigatórios faltando: ip";
    public const string IpInvalid = "IP inválido";
    public const string ReasonNotText = "Campo 'reason' deve ser texto";
    public const string PatternNotText = "Campo 'pattern' deve ser texto";
    public const string UserIdNotInteger = "Campo 'usuario_id' deve ser um número inteiro";
    public const string UserIdMissing = "Campos obrigatórios faltando: usuario_id";
    public const string ActiveRequired = "Campo 'ativo' é obrigatório (true ou false)";
    public const string BodyNotObject = "Corpo da requisição deve ser um objeto JSON";
    public const string UserNotFound = "Usuário não encontrado";

    public const string RedisUnavailableField = "Redis não disponível";
    public const string StatsObtained = "Estatísticas de segurança obtidas";
    public const string BlacklistAddRedisDown = "Falha ao adicionar IP à blacklist (Redis indisponível)";
    public const string BlacklistRemoveRedisDown = "Falha ao remover IP da blacklist (Redis indisponível ou IP não estava na blacklist)";
    public const string CacheStatsObtained = "Estatísticas do cache obtidas com sucesso";
    public const string NotificationConfigObtained = "Configurações de notificação obtidas com sucesso";
    public const string AlertsEnabled = "Alertas financeiros ativados com sucesso";
    public const string AlertsDisabled = "Alertas financeiros desativados com sucesso";

    public const string UnblockFailed = "Erro ao desbloquear IP. Tente novamente.";
    public const string StatsFailed = "Erro ao obter estatísticas de segurança. Tente novamente.";
    public const string BlacklistAddFailed = "Erro ao adicionar IP à blacklist. Tente novamente.";
    public const string BlacklistRemoveFailed = "Erro ao remover IP da blacklist. Tente novamente.";
    public const string CacheClearFailed = "Erro ao limpar o cache do Gemini. Tente novamente.";
    public const string CacheStatsFailed = "Erro ao obter estatísticas do cache. Tente novamente.";
    public const string BotSessionFailed = "Erro ao limpar a sessão do bot. Tente novamente.";
    public const string NotificationConfigFailed = "Erro ao obter as configurações de notificação. Tente novamente.";
    public const string FinancialAlertsFailed = "Erro ao atualizar a configuração de alertas financeiros. Tente novamente.";

    public static string IpDesbloqueado(string ip) => $"IP {ip} desbloqueado";

    public static string BlacklistAdded(string ip) => $"IP {ip} adicionado à blacklist permanente";

    public static string BlacklistRemoved(string ip) => $"IP {ip} removido da blacklist permanente";

    public static string CacheClearedForUser(int deleted, int userId) => $"{deleted} chaves deletadas para usuário {userId}";

    public static string CacheClearedForPattern(int deleted, string pattern) => $"{deleted} chaves deletadas (pattern: {pattern})";

    public static string CacheClearedAll(int deleted) => $"Cache completo limpo: {deleted} chaves deletadas";

    public static string BotSessionCleared(int rows) =>
        $"Sessão do bot ('baileys_auth') limpa com sucesso. {rows} linhas deletadas.";
}

/// <summary>Logs das rotas admin (EventIds 9101+). Só <c>SafeError.Describe</c> e ids; nunca corpo, chave nem mensagem de exceção.</summary>
internal static partial class AdminLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Error, Message = "Erro na rota admin {Route}: {Error}")]
    public static partial void RouteFailed(ILogger logger, string route, string error);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information, Message = "[ADMIN-FIX] {Message}")]
    public static partial void AdminFix(ILogger logger, string message);
}

/// <summary>
/// Leitura do corpo das rotas admin (desvio 024 e 079). <see cref="JsonBody"/> cobre corpo ausente, vazio, inválido ou
/// não objeto como "objeto sem campos"; aqui se acrescenta a classificação de cada campo e o corpo opcional do
/// <c>gemini-cache-clear</c> (vazio &times; inválido, desvio 077).
/// </summary>
internal static class AdminBody
{
    public static Task<JsonElement?> ReadObjectAsync(HttpRequest request, CancellationToken cancellationToken) =>
        JsonBody.ReadObjectAsync(request, cancellationToken);

    /// <summary>Nenhum byte = <see cref="OptionalBody.Empty"/>; bytes que não formam um objeto JSON = <see cref="OptionalBody.Invalid"/>.</summary>
    public static async Task<OptionalBody> ReadOptionalObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            return OptionalBody.Empty;
        }

        buffer.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? new OptionalBody(OptionalBodyKind.Object, document.RootElement.Clone())
                : OptionalBody.Invalid;
        }
        catch (JsonException)
        {
            return OptionalBody.Invalid;
        }
    }

    public static AdminField Field(JsonElement? body, string name)
    {
        if (body is not { } root || !root.TryGetProperty(name, out var value))
        {
            return AdminField.Absent;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return AdminField.OfText(value.GetString()!);
            case JsonValueKind.True:
                return AdminField.OfBoolean(true);
            case JsonValueKind.False:
                return AdminField.OfBoolean(false);
            case JsonValueKind.Number:
                return value.TryGetInt64(out var integer)
                    ? AdminField.OfWhole(integer)
                    : AdminField.OtherValue(!IsZero(value));
            case JsonValueKind.Array:
                return AdminField.OtherValue(value.GetArrayLength() > 0);
            case JsonValueKind.Object:
                return AdminField.OtherValue(value.EnumerateObject().Any());
            default:
                return AdminField.NullValue;
        }
    }

    private static bool IsZero(JsonElement number) => number.TryGetDouble(out var d) && d == 0;
}

internal enum OptionalBodyKind
{
    Empty,
    Invalid,
    Object,
}

internal readonly record struct OptionalBody(OptionalBodyKind Kind, JsonElement? Value = null)
{
    public static OptionalBody Empty { get; } = new(OptionalBodyKind.Empty);

    public static OptionalBody Invalid { get; } = new(OptionalBodyKind.Invalid);
}

/// <summary>
/// 401 das rotas admin: envelope B <c>Não autorizado</c>. Com o desvio 083, uma chave inválida vinda de IP externo conta
/// como tentativa suspeita (<c>Invalid API key</c>; 5 em 10 min bloqueiam por 1 h). A requisição corrente continua 401 e
/// chamadas internas (bot, Ofelia) nunca contam.
/// </summary>
public static class AdminUnauthorized
{
    public static IResult EnvelopeB(HttpContext _) => new InvalidApiKeyResult();

    private sealed class InvalidApiKeyResult : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            await TrackAsync(httpContext);
            await Envelopes.B.Error(AdminMessages.Unauthorized, StatusCodes.Status401Unauthorized).ExecuteAsync(httpContext);
        }

        private static async Task TrackAsync(HttpContext context)
        {
            var services = context.RequestServices;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(SecurityLog.Category);
            try
            {
                if (!services.GetRequiredService<IOptions<SecurityOptions>>().Value.BotProtectionEnabled)
                {
                    return;
                }

                var client = ClientIp.Resolve(context);
                if (client.Internal)
                {
                    return;
                }

                var clock = services.GetRequiredService<IClock>();
                var track = await services.GetRequiredService<ISecurityStore>()
                    .TrackAttemptAsync(client.Ip, AdminMessages.InvalidApiKeyReason, clock.Now, context.RequestAborted);

                switch (track.Outcome)
                {
                    case TrackOutcome.Tracked:
                        SecurityLog.Attempt(logger, client.Ip, track.Attempts, AdminMessages.InvalidApiKeyReason);
                        break;
                    case TrackOutcome.Blocked:
                        if (track.BlockPersisted)
                        {
                            SecurityLog.Block(logger, client.Ip, track.BlockCount, track.BlockedUntil!.Value);
                        }
                        else
                        {
                            SecurityLog.BlockNotPersisted(logger, client.Ip);
                        }

                        SecurityLog.AutoBlock(logger, client.Ip, track.Attempts);
                        break;
                    default:
                        SecurityLog.AttemptWithoutRedis(logger, client.Ip, AdminMessages.InvalidApiKeyReason);
                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fail-open: o 401 sai igual, a contagem é só um reforço.
                SecurityLog.FilterFailed(logger, SafeError.Describe(exception));
            }
        }
    }
}

/// <summary>
/// Rotas <c>/admin/.../{usuarioId}</c> usam o conversor <c>&lt;int:usuario_id&gt;</c> do Flask: só dígitos, e o que não cabe
/// em <see cref="int"/> também não é um id. Sem casar, 404 HTML <b>antes</b> da autenticação (registrado no grupo antes do
/// <c>RequireApiKey</c>).
/// </summary>
internal sealed class AdminRouteIdFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.RouteValues.TryGetValue("usuarioId", out var raw)
            && !(raw is string text && NumericIdFilter.IsNumeric(text) && int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))
        {
            return ValueTask.FromResult<object?>(new FlaskNotFoundResult());
        }

        return next(context);
    }
}
