using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Application.Caching;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.CalendarAlerts;

/// <summary>AC-014-43 contra o Redis real: o claim <c>SET NX EX 7200</c> do Python e o segundo ciclo sem reenvio.</summary>
[Trait("Category", "Docker")]
public sealed class CalendarAlertServiceRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IDisposable
{
    private const string Start = "2026-10-05T18:00:00-03:00";

    private sealed class RecordingBot : IBotClient
    {
        public List<string> Messages { get; } = [];

        public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }

        public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class OneEventCalendar : ICalendarClient
    {
        public Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarInfo>>([new CalendarInfo("primary", "Pessoal", true)]);

        public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarEvent>>([new CalendarEvent("ev1", "Reunião", Start, Start, "Sala 2", string.Empty, false, null)]);

        public Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private readonly ConnectionMultiplexer _mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);
    private readonly RecordingBot _bot = new();
    private readonly int _userId = Random.Shared.Next(100000, 999999);

    public void Dispose() => _mux.Dispose();

    private CalendarAlertService Service()
    {
        // 17:59:20 em São Paulo.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 20, 59, 20, TimeSpan.Zero));
        return new CalendarAlertService(
            new OneEventCalendar(), _cache, _bot, new BrazilClock(time), new NullDiagnostics());
    }

    [Fact]
    [Trait("AC", "014-43")]
    public async Task Claim_WritesTheExactKeyWithProcessingAndTwoHourTtl_AndSecondCycleSendsNothing()
    {
        var service = Service();

        var first = await service.ProcessAlertsForUserAsync(_userId, "5511900000001", 1, CancellationToken.None);
        var second = await service.ProcessAlertsForUserAsync(_userId, "5511900000001", 1, CancellationToken.None);

        first.ShouldBe(1);
        second.ShouldBe(0);
        _bot.Messages.ShouldBe(["⚠️ Faltam 1 min: Reunião\n🕒 18:00\n📍 Sala 2"]);
        var key = CacheKeys.CalendarAlert(_userId, "ev1", Start);
        var db = _mux.GetDatabase();
        ((string?)await db.StringGetAsync(key)).ShouldBe("processing");
        var ttl = await db.KeyTimeToLiveAsync(key);
        ttl.ShouldNotBeNull();
        ttl.Value.TotalSeconds.ShouldBeInRange(7190, 7200);
    }

    private sealed class NullDiagnostics : ICalendarAlertDiagnostics
    {
        public void ConfigUpdateFailed(int userId, Exception exception)
        {
        }

        public void EventsLookupFailed(int userId, Exception exception)
        {
        }

        public void CalendarEventsFailed(int userId, Exception exception)
        {
        }

        public void AlertSendFailed(int userId, Exception exception)
        {
        }

        public void ProcessFailed(int userId, Exception exception)
        {
        }

        public void RedisUnavailable(int userId)
        {
        }
    }
}
