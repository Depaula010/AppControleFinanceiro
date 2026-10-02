using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.Common;

namespace MeuSecretario.UnitTests.Application.Integrations;

/// <summary>RN-010-07: strings idênticas às do Python.</summary>
public class CacheKeysTests
{
    [Fact]
    [Trait("AC", "010-10")]
    public void Keys_MatchTheFormatsOfTheSpec()
    {
        CacheKeys.PendingTx("5531", "tx1").ShouldBe("pending_tx:5531:tx1");
        CacheKeys.LastPending("5531").ShouldBe("last_pending:5531");
        CacheKeys.PendingEvent("5531", "ev1").ShouldBe("pending_event:5531:ev1");
        CacheKeys.PendingEventPattern("5531").ShouldBe("pending_event:5531:*");
        CacheKeys.NightlyCheckin("5531", "9").ShouldBe("nightly_checkin:5531:9");
        CacheKeys.NightlyCheckinActive("5531").ShouldBe("nightly_checkin_active:5531");
        CacheKeys.InvoiceClosed(42).ShouldBe("invoice_closed:42");
        CacheKeys.InvoiceDueWarning3d(42).ShouldBe("invoice_due_warning:42:3d");
        CacheKeys.CalendarDisconnectedAlert(7).ShouldBe("calendar_disconnected_alert:7");
        CacheKeys.CalendarTokenRevokedAlert(7).ShouldBe("calendar_token_revoked_alert:7");
        CacheKeys.JobLock("nightly").ShouldBe("job_lock:nightly");
        CacheKeys.TravelTimeLimit(7, new DateOnly(2026, 10, 2)).ShouldBe("travel_time_limit:7:2026-10-02");
        CacheKeys.BotSessionId("sessao-principal").ShouldBe("bot_session_id:sessao-principal");
    }

    [Fact]
    [Trait("AC", "010-10")]
    public void CalendarAlert_IsTheMd5OfUserEventAndStart()
    {
        // hashlib.md5("7:abc:2026-10-02T10:00:00-03:00".encode()).hexdigest()
        CacheKeys.CalendarAlert(7, "abc", "2026-10-02T10:00:00-03:00")
            .ShouldBe("calendar_alert:5aef3b874c52d2afaf362b25687d0b4e");
    }

    [Fact]
    [Trait("AC", "010-10")]
    public void CalendarAlert_MatchesThePythonFixtures()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "integrations-fixtures.json")));
        var cases = document.RootElement.GetProperty("calendar_alert").EnumerateArray().ToList();

        cases.ShouldNotBeEmpty();
        foreach (var c in cases)
        {
            CacheKeys.CalendarAlert(c.GetProperty("user_id").GetInt32(), c.GetProperty("event_id").GetString()!, c.GetProperty("start").GetString()!)
                .ShouldBe(c.GetProperty("key").GetString());
        }
    }

    [Fact]
    [Trait("AC", "010-10")]
    public void GeminiCache_UsesGlobalWhenThereIsNoUser()
    {
        CacheKeys.GeminiCache("category", 12, "900150983cd2").ShouldBe("gemini_cache:category:12:900150983cd2");
        CacheKeys.GeminiCache("intent", null, "900150983cd2").ShouldBe("gemini_cache:intent:global:900150983cd2");
    }
}

public class PhoneMaskTests
{
    [Theory]
    [InlineData("5511999991072", "…1072")]
    [InlineData("1072", "…1072")]
    [InlineData("123", "…")]
    [InlineData("", "…")]
    [InlineData(null, "…")]
    public void Last4_KeepsOnlyTheLastFourDigits(string? number, string expected)
    {
        PhoneMask.Last4(number).ShouldBe(expected);
    }
}
