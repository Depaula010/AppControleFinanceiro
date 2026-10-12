using MeuSecretario.Domain.Security;

namespace MeuSecretario.UnitTests.Domain.Security;

public class SuspiciousRulesTests
{
    // Um caminho exemplo por padrão, na ordem do Flask (security.py:30-62). "/phpmyadmin" (4º) nunca é o primeiro a casar
    // porque o Python também ignora a caixa: o 3º ("/phpMyAdmin") vence, como no Flask.
    public static TheoryData<string, string> UrlCases => new()
    {
        { "/.env", @"/\.env" },
        { "/.git", @"/\.git" },
        { "/phpMyAdmin/index", "/phpMyAdmin" },
        { "/phpmyadmin/index", "/phpMyAdmin" },
        { "/wp-admin/setup", "/wp-admin" },
        { "/wp-login.x", "/wp-login" },
        { "/xmlrpc.php", @"/xmlrpc\.php" },
        { "/.php", @"/\.php" },
        { "/cgi-bin/x", "/cgi-bin" },
        { "/shell", "/shell" },
        { "/config", "/config" },
        { "/backup", "/backup" },
        { "/sql", "/sql" },
        { "/db", "/db" },
        { "/database", "/database" },
        { "/_profiler", @"/\_" },
        { "/..", @"/\.\." },
        { "/%2e%2e/x", "/%2e%2e" },
        { "/nice%20ports", "/nice%20ports" },
        { "/Tri%6Eity.txt", @"/Tri%6Eity\.txt" },
        { "/devicedesc.xml", @"/devicedesc\.xml" },
        { "/dana-na/x", "/dana-" },
        { "/CFIDE/x", "/CFIDE" },
        { "/geoserver/web", "/geoserver" },
        { "/fog/x", "/fog/" },
        { "/magento/x", "/magento" },
        { "/webui/x", "/webui" },
        { "/.bak", @"/\.bak" },
        { "/.old", @"/\.old" },
        { "/.backup", @"/\.backup" },
    };

    public static TheoryData<string> UserAgents => new()
    {
        "sqlmap", "nikto", "nmap", "masscan", "acunetix", "nessus", "openvas", "metasploit", "havij", "zgrab",
    };

    [Fact]
    public void Lists_HaveTheFlaskSizes()
    {
        SuspiciousRules.UrlPatterns.Count.ShouldBe(30);
        SuspiciousRules.UserAgentPatterns.Count.ShouldBe(10);
        UrlCases.Count.ShouldBe(30);
    }

    [Theory]
    [MemberData(nameof(UrlCases))]
    [Trait("AC", "009-07")]
    public void UnregisteredPath_MatchesTheFirstPattern(string path, string expectedPattern)
    {
        SuspiciousRules.Evaluate(path, null, isRegisteredRoute: false)!.Reason
            .ShouldBe($"Suspicious URL pattern: {expectedPattern}");
    }

    [Theory]
    [MemberData(nameof(UrlCases))]
    [Trait("AC", "009-08")]
    public void RegisteredPath_IsNeverSuspiciousByUrl(string path, string _)
    {
        SuspiciousRules.Evaluate(path, "Mozilla/5.0", isRegisteredRoute: true).ShouldBeNull();
    }

    [Fact]
    public void UnregisteredPath_WithoutPattern_IsUnknownEndpoint()
    {
        SuspiciousRules.Evaluate("/rota-que-nao-existe", null, isRegisteredRoute: false)!.Reason
            .ShouldBe("Unknown endpoint: /rota-que-nao-existe");
    }

    [Theory]
    [InlineData("/PHPMYADMIN")]
    [InlineData("/.ENV")]
    [InlineData("/Wp-Admin")]
    public void UrlPatterns_IgnoreCase(string path)
    {
        SuspiciousRules.Evaluate(path, null, false)!.Reason.ShouldStartWith("Suspicious URL pattern: ");
    }

    [Theory]
    [MemberData(nameof(UserAgents))]
    [Trait("AC", "009-07")]
    public void UserAgent_IsSuspiciousOnAnyRoute(string pattern)
    {
        var ua = $"Mozilla/5.0 {pattern.ToUpperInvariant()}/1.7";

        SuspiciousRules.Evaluate("/", ua, isRegisteredRoute: true)!.Reason.ShouldBe($"Suspicious User-Agent: {pattern}");
        SuspiciousRules.Evaluate("/qualquer", ua, isRegisteredRoute: false)!.Reason.ShouldBe($"Suspicious User-Agent: {pattern}");
    }

    [Fact]
    public void UserAgent_IsCheckedBeforeTheUrl()
    {
        SuspiciousRules.Evaluate("/.env", "sqlmap/1.7", false)!.Reason.ShouldBe("Suspicious User-Agent: sqlmap");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36")]
    public void NormalOrMissingUserAgent_OnRegisteredRoute_IsNotSuspicious(string? ua)
    {
        SuspiciousRules.Evaluate("/api/health", ua, true).ShouldBeNull();
    }

    [Fact]
    public void Evaluate_WithHugeInput_DoesNotHang()
    {
        var path = "/" + new string('a', 200_000);

        SuspiciousRules.Evaluate(path, null, false)!.Reason.ShouldStartWith("Unknown endpoint: ");
    }
}
