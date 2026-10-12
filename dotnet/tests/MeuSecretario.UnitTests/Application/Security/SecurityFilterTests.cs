using MeuSecretario.Application.Security;

namespace MeuSecretario.UnitTests.Application.Security;

internal sealed class FakeSecurityStore : ISecurityStore
{
    public bool Blacklisted { get; set; }

    public bool Blocked { get; set; }

    public TrackResult Track { get; set; } = TrackResult.Tracked(1);

    public List<string> Calls { get; } = [];

    public List<(string Ip, string Reason)> Tracked { get; } = [];

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsBlacklistedAsync(string ip, CancellationToken cancellationToken)
    {
        Calls.Add("blacklist");
        return Task.FromResult(Blacklisted);
    }

    public Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken cancellationToken)
    {
        Calls.Add("blocked");
        return Task.FromResult(Blocked);
    }

    public Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken cancellationToken)
    {
        Calls.Add("track");
        Tracked.Add((ip, reason));
        return Task.FromResult(Track);
    }

    public Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> RemoveTempBlockAsync(string ip, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken cancellationToken) => Task.FromResult(SecurityStats.Unavailable);
}

public class SecurityFilterTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 0);
    private readonly FakeSecurityStore _store = new();

    private Task<SecurityVerdict> Evaluate(string path = "/rota-que-nao-existe", string? ua = null, bool registered = false) =>
        new SecurityFilter(_store).EvaluateAsync("203.0.113.7", path, ua, registered, Now, CancellationToken.None);

    [Fact]
    [Trait("AC", "009-04")]
    public async Task Blacklist_ComesBeforeTheTemporaryBlock()
    {
        _store.Blacklisted = true;
        _store.Blocked = true;

        (await Evaluate("/", registered: true)).Kind.ShouldBe(SecurityVerdictKind.Blacklisted);
        _store.Calls.ShouldBe(["blacklist"]);
    }

    [Fact]
    [Trait("AC", "009-03")]
    public async Task TemporaryBlock_AppliesEvenToRegisteredRoutes_WithoutTracking()
    {
        _store.Blocked = true;

        (await Evaluate("/api/health", registered: true)).Kind.ShouldBe(SecurityVerdictKind.TemporarilyBlocked);
        _store.Tracked.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-01")]
    public async Task UnknownPath_IsTrackedAndAnswered404()
    {
        var verdict = await Evaluate("/rota-que-nao-existe");

        verdict.Kind.ShouldBe(SecurityVerdictKind.Suspicious);
        verdict.Reason.ShouldBe("Unknown endpoint: /rota-que-nao-existe");
        _store.Tracked.ShouldBe([("203.0.113.7", "Unknown endpoint: /rota-que-nao-existe")]);
        _store.Calls.ShouldBe(["blacklist", "blocked", "track"]);
    }

    [Fact]
    [Trait("AC", "009-02")]
    public async Task FifthAttempt_IsAutoBlocked()
    {
        _store.Track = TrackResult.Blocked(5, 1, Now.AddMinutes(60), true);

        var verdict = await Evaluate("/.env");

        verdict.Kind.ShouldBe(SecurityVerdictKind.AutoBlocked);
        verdict.Reason.ShouldBe(@"Suspicious URL pattern: /\.env");
        verdict.Track!.BlockCount.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "009-08")]
    public async Task RegisteredRoute_WithSuspiciousSegment_IsAllowed_WithoutTracking()
    {
        (await Evaluate("/admin/config-alertas-financeiros", registered: true)).Kind.ShouldBe(SecurityVerdictKind.Allow);
        _store.Tracked.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-07")]
    public async Task SuspiciousUserAgent_OnRegisteredRoute_IsTracked()
    {
        var verdict = await Evaluate("/", "sqlmap/1.7", registered: true);

        verdict.Kind.ShouldBe(SecurityVerdictKind.Suspicious);
        verdict.Reason.ShouldBe("Suspicious User-Agent: sqlmap");
    }

    [Fact]
    [Trait("AC", "009-10")]
    public async Task WithoutRedis_SuspiciousRequestIsStillSuspiciousButNeverBlocked()
    {
        _store.Track = TrackResult.Unavailable;

        var verdict = await Evaluate();

        verdict.Kind.ShouldBe(SecurityVerdictKind.Suspicious);
        verdict.Track!.Outcome.ShouldBe(TrackOutcome.RedisUnavailable);
    }

    [Fact]
    public async Task NormalRequest_IsAllowed()
    {
        (await Evaluate("/api/health", "Mozilla/5.0", registered: true)).Kind.ShouldBe(SecurityVerdictKind.Allow);
    }
}
