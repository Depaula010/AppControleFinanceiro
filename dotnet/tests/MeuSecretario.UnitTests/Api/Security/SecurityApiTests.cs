using System.Net;
using MeuSecretario.Api.Security;
using Microsoft.AspNetCore.Http;
using Serilog.Events;
using Serilog.Parsing;

namespace MeuSecretario.UnitTests.Api.Security;

public class SecurityOptionsTests
{
    private static SecurityOptions Load(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value);
        return SecurityOptions.Load(key => map.GetValueOrDefault(key));
    }

    [Fact]
    public void Defaults_AreTheDecidedOnes()
    {
        var options = Load();

        options.BotProtectionEnabled.ShouldBeTrue();
        options.SecurityHeadersEnabled.ShouldBeTrue();
        options.RateLimitEnabled.ShouldBeFalse();
        options.RateLimitPerHour.ShouldBe(300);
        options.RateLimitAdminPerHour.ShouldBe(60);
        options.LogLevel.ShouldBe(LogEventLevel.Warning);
        options.LogFile.ShouldBeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", true)]
    public void BotProtection_OnlyTrueEnables_ButEmptyKeepsTheDefault(string value, bool expected)
    {
        Load(("BOT_PROTECTION_ENABLED", value)).BotProtectionEnabled.ShouldBe(expected);
    }

    [Fact]
    public void RateLimit_ReadsFlagAndLimits()
    {
        var options = Load(("RATE_LIMIT_ENABLED", "true"), ("RATE_LIMIT_PER_HOUR", "3"), ("RATE_LIMIT_ADMIN_PER_HOUR", "2"));

        options.RateLimitEnabled.ShouldBeTrue();
        options.RateLimitPerHour.ShouldBe(3);
        options.RateLimitAdminPerHour.ShouldBe(2);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void InvalidLimits_FallBackToTheDefaults(string value)
    {
        var options = Load(("RATE_LIMIT_PER_HOUR", value), ("RATE_LIMIT_ADMIN_PER_HOUR", value));

        options.RateLimitPerHour.ShouldBe(300);
        options.RateLimitAdminPerHour.ShouldBe(60);
    }

    [Theory]
    [InlineData("DEBUG", LogEventLevel.Debug)]
    [InlineData("info", LogEventLevel.Information)]
    [InlineData("WARNING", LogEventLevel.Warning)]
    [InlineData("ERROR", LogEventLevel.Error)]
    [InlineData("CRITICAL", LogEventLevel.Fatal)]
    [InlineData("lixo", LogEventLevel.Warning)]
    public void LogLevel_UsesPythonLoggingNames(string value, LogEventLevel expected)
    {
        Load(("SECURITY_LOG_LEVEL", value)).LogLevel.ShouldBe(expected);
    }

    [Fact]
    public void LogFile_IsTrimmed()
    {
        Load(("SECURITY_LOG_FILE", "  logs/s.log ")).LogFile.ShouldBe("logs/s.log");
    }
}

public class ClientIpTests
{
    private static ClientIpInfo Resolve(string? remote, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return ClientIp.Resolve(context);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.18.0.3")]
    [InlineData("::1")]
    [InlineData("::ffff:172.18.0.3")]
    [Trait("AC", "009-05")]
    public void TrustedOriginWithoutForwardedHeader_IsInternal(string remote)
    {
        Resolve(remote).Internal.ShouldBeTrue();
    }

    [Fact]
    public void ForwardedHeaderPresent_IsNeverInternal_EvenForPrivateOrigin()
    {
        Resolve("10.0.0.5", ("X-Forwarded-For", "198.51.100.9")).Internal.ShouldBeFalse();
    }

    [Fact]
    public void OriginalForHeaderCreatedByTheForwardedHeadersMiddleware_CountsAsForwarded()
    {
        var info = Resolve("198.51.100.9", ("X-Original-For", "10.0.0.5:4000"));

        info.Internal.ShouldBeFalse();
        info.Ip.ShouldBe("198.51.100.9");
    }

    [Fact]
    public void PublicOrigin_IsExternal_AndIsNormalized()
    {
        var info = Resolve("::ffff:203.0.113.7");

        info.Internal.ShouldBeFalse();
        info.Ip.ShouldBe("203.0.113.7");
    }

    [Fact]
    public void UnknownOrigin_IsTreatedAsInternal()
    {
        var info = Resolve(null);

        info.Internal.ShouldBeTrue();
        info.Ip.ShouldBe(ClientIp.Unknown);
    }
}

public class SaoPauloTimestampEnricherTests
{
    [Fact]
    public void Enrich_AddsTheSaoPauloWallClock_IndependentOfTheMachineZone()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");
        var evt = new LogEvent(
            new DateTimeOffset(2026, 10, 4, 15, 30, 5, TimeSpan.Zero),
            LogEventLevel.Warning,
            null,
            new MessageTemplate([]),
            []);

        new SaoPauloTimestampEnricher(zone).Enrich(evt, new TestPropertyFactory());

        evt.Properties[SaoPauloTimestampEnricher.PropertyName].ToString().ShouldBe("\"2026-10-04 12:30:05\"");
    }

    private sealed class TestPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
