using System.Net;
using System.Text;
using MeuSecretario.Application.Caching;
using MeuSecretario.Infrastructure.Bot;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Caching;

/// <summary>AC-010-34: cache de sessão do bot contra o Redis real (010b depende da 010a).</summary>
[Trait("Category", "Docker")]
public sealed class BotSessionRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IDisposable
{
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);
    private readonly string _sessionName = $"sessao-{Guid.NewGuid():N}";

    public void Dispose() => _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private sealed class Bot : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Calls { get; } = [];

        public string? SessionName { get; set; }

        public string CurrentId { get; set; } = "00000000-0000-4000-8000-000000000001";

        public string? RejectId { get; set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Get)
            {
                var body = $"{{\"data\":[{{\"session_id\":\"{CurrentId}\",\"session_name\":\"{SessionName}\"}}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }

            var status = RejectId is not null && path.Contains(RejectId, StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private BotClient Build(Bot bot)
    {
        bot.SessionName = _sessionName;
        var settings = new AppSettingsIntegrationSettings(AppSettings.Load(key => key switch
        {
            "DATABASE_URL" => "postgresql://u:p@h:5432/db",
            "API_SECRET_KEY" => "0123456789abcdef0123456789abcdef",
            "ENCRYPTION_KEY" => "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            "GEMINI_API_KEY" => "g",
            "REDIS_URL" => redis.RedisUrl,
            "BOT_WHATSAPP_URL" => "http://bot.test",
            "BOT_API_KEY" => "k",
            "BOT_SESSION_NAME" => _sessionName,
            _ => null,
        }));
        var resolver = new BotSessionResolver(bot, _cache, settings, NullLogger<BotSessionResolver>.Instance);
        return new BotClient(bot, resolver, _cache, settings, NullLogger<BotClient>.Instance);
    }

    [Fact]
    [Trait("AC", "010-34")]
    public async Task SessionIsCachedAsAQuotedJsonStringWithTenMinuteTtl()
    {
        var bot = new Bot();
        var client = Build(bot);

        (await client.SendTextAsync("5531999990000", "ola", CancellationToken.None)).ShouldBeTrue();
        (await client.SendTextAsync("5531999990000", "ola", CancellationToken.None)).ShouldBeTrue();

        bot.Calls.Count(c => c.StartsWith("GET", StringComparison.Ordinal)).ShouldBe(1);
        await using var raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var key = CacheKeys.BotSessionId(_sessionName);
        ((string?)await raw.GetDatabase().StringGetAsync(key)).ShouldBe($"\"{bot.CurrentId}\"");
        (await raw.GetDatabase().KeyTimeToLiveAsync(key))!.Value.TotalSeconds.ShouldBeInRange(595, 600);
    }

    [Fact]
    [Trait("AC", "010-34")]
    public async Task Python404Cycle_DeletesTheKey_ResolvesAgainAndResendsOnce()
    {
        const string oldId = "11111111-1111-4111-8111-111111111111";
        var bot = new Bot { CurrentId = "22222222-2222-4222-8222-222222222222", RejectId = oldId };
        var client = Build(bot);
        await using var raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var key = CacheKeys.BotSessionId(_sessionName);
        // Valor como o Flask grava: json.dumps(session_id)
        await raw.GetDatabase().StringSetAsync(key, $"\"{oldId}\"", TimeSpan.FromSeconds(600));

        (await client.SendTextAsync("5531999990000", "ola", CancellationToken.None)).ShouldBeTrue();

        bot.Calls.ShouldBe(
        [
            $"POST /api/v1/sessions/{oldId}/send-message",
            "GET /api/v1/sessions",
            $"POST /api/v1/sessions/{bot.CurrentId}/send-message",
        ]);
        ((string?)await raw.GetDatabase().StringGetAsync(key)).ShouldBe($"\"{bot.CurrentId}\"");
    }
}
