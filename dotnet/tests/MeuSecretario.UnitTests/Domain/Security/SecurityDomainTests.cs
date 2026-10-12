using System.Net;
using MeuSecretario.Domain.Security;

namespace MeuSecretario.UnitTests.Domain.Security;

public class IpClassifierTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.5.5.5", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("192.169.1.1", false)]
    [InlineData("169.254.10.10", true)]
    [InlineData("203.0.113.7", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fdff::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("febf::1", true)]
    [InlineData("fec0::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:10.0.0.5", true)]
    [InlineData("::ffff:203.0.113.7", false)]
    public void IsTrusted_FollowsThePrivateRanges(string ip, bool expected)
    {
        IpClassifier.IsTrusted(IPAddress.Parse(ip)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("2001:DB8:0:0:0:0:0:1", "2001:db8::1")]
    public void Normalize_MapsToIPv4_AndUsesCanonicalForm(string ip, string expected)
    {
        IpClassifier.Normalize(IPAddress.Parse(ip)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("  1.2.3.4 ", true, "1.2.3.4")]
    [InlineData("0:0:0:0:0:0:0:1", true, "::1")]
    [InlineData("::ffff:1.2.3.4", true, "1.2.3.4")]
    [InlineData("1", false, "")]
    [InlineData("1.2.3", false, "")]
    [InlineData("999.1.1.1", false, "")]
    [InlineData("abc", false, "")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void TryParseInput_ValidatesAndNormalizes(string? text, bool ok, string normalized)
    {
        IpClassifier.TryParseInput(text, out var result).ShouldBe(ok);
        result.ShouldBe(normalized);
    }
}

public class AttemptWindowTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    [Trait("AC", "009-14")]
    public void Prune_KeepsOnlyAttemptsNewerThanTenMinutes()
    {
        var attempts = new List<Attempt>
        {
            new(Now.AddMinutes(-11), "a"),
            new(Now.AddMinutes(-10), "borda"),
            new(Now.AddMinutes(-10).AddTicks(1), "dentro"),
            new(Now.AddMinutes(-1), "b"),
        };

        AttemptWindow.Prune(attempts, Now).Select(a => a.Reason).ShouldBe(["dentro", "b"]);
    }

    [Fact]
    public void Constants_AreTheFlaskOnes()
    {
        AttemptWindow.BlockThreshold.ShouldBe(5);
        AttemptWindow.WindowMinutes.ShouldBe(10);
        AttemptWindow.BlockMinutes.ShouldBe(60);
    }
}

public class SecurityKeysTests
{
    [Fact]
    public void Keys_AndTtls_MatchTheFlask()
    {
        SecurityKeys.Blocked("1.2.3.4").ShouldBe("security:blocked:1.2.3.4");
        SecurityKeys.Attempts("1.2.3.4").ShouldBe("security:attempts:1.2.3.4");
        SecurityKeys.Blacklist("::1").ShouldBe("security:blacklist:::1");
        SecurityKeys.BlockedTtl.TotalSeconds.ShouldBe(3600);
        SecurityKeys.AttemptsTtl.TotalSeconds.ShouldBe(600);
        SecurityKeys.BlacklistTtl.TotalSeconds.ShouldBe(31_536_000);
    }
}

public class PyTimedeltaTests
{
    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(59, "0:00:59")]
    [InlineData(3552, "0:59:12")]
    [InlineData(3600, "1:00:00")]
    [InlineData(36_000 + 125, "10:02:05")]
    [InlineData(86_400, "1 day, 0:00:00")]
    [InlineData(2 * 86_400 + 3661, "2 days, 1:01:01")]
    public void Format_MatchesPythonStrTimedelta(int seconds, string expected)
    {
        PyTimedelta.Format(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void Format_DropsMicroseconds()
    {
        PyTimedelta.Format(new TimeSpan(0, 0, 59, 12, 987)).ShouldBe("0:59:12");
    }
}

public class SecurityLogTextTests
{
    [Fact]
    [Trait("AC", "009-17")]
    public void Sanitize_ReplacesControlCharactersWithQuestionMark()
    {
        SecurityLogText.Sanitize("/a\nb\rc\td\u0000e\u001b").ShouldBe("/a?b?c?d?e?");
    }

    [Fact]
    public void Sanitize_KeepsNormalText_AndHandlesNull()
    {
        SecurityLogText.Sanitize("/ação/ü").ShouldBe("/ação/ü");
        SecurityLogText.Sanitize(null).ShouldBe(string.Empty);
    }
}

public class SecurityTimestampTests
{
    [Fact]
    public void Format_MatchesPythonIsoformat()
    {
        SecurityTimestamp.Format(new DateTime(2026, 10, 4, 10, 0, 0, 123, DateTimeKind.Unspecified).AddTicks(4560))
            .ShouldBe("2026-10-04T10:00:00.123456");
        SecurityTimestamp.Format(new DateTime(2026, 10, 4, 10, 0, 0)).ShouldBe("2026-10-04T10:00:00");
    }

    [Theory]
    [InlineData("2026-10-04T10:00:00.123456")]
    [InlineData("2026-10-04T10:00:00")]
    public void TryParse_ReadsBothPythonForms(string text)
    {
        SecurityTimestamp.TryParse(text, out var value).ShouldBeTrue();
        value.Year.ShouldBe(2026);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é data")]
    [InlineData("2026-10-04 10:00:00")]
    public void TryParse_RejectsGarbage(string text)
    {
        SecurityTimestamp.TryParse(text, out _).ShouldBeFalse();
    }
}
