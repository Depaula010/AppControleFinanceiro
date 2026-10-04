using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Security;

/// <summary>Motivo pelo qual uma requisição é suspeita; o texto vai para o log e para <c>security-stats</c>.</summary>
public sealed record SuspicionResult(string Reason);

/// <summary>
/// Padrões de scanners e bots copiados literalmente de <c>app/middleware/security.py:30-76</c> (RN-009-04), na ordem
/// do Flask. Funções puras, sem I/O.
/// </summary>
public static class SuspiciousRules
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Expressões regulares do Python (texto usado na razão), na ordem original.</summary>
    public static IReadOnlyList<string> UrlPatterns { get; } =
    [
        @"/\.env",
        @"/\.git",
        @"/phpMyAdmin",
        @"/phpmyadmin",
        @"/wp-admin",
        @"/wp-login",
        @"/xmlrpc\.php",
        @"/\.php",
        @"/cgi-bin",
        @"/shell",
        @"/config",
        @"/backup",
        @"/sql",
        @"/db",
        @"/database",
        @"/\_",
        @"/\.\.",
        @"/%2e%2e",
        @"/nice%20ports",
        @"/Tri%6Eity\.txt",
        @"/devicedesc\.xml",
        @"/dana-",
        @"/CFIDE",
        @"/geoserver",
        @"/fog/",
        @"/magento",
        @"/webui",
        @"/\.bak",
        @"/\.old",
        @"/\.backup",
    ];

    public static IReadOnlyList<string> UserAgentPatterns { get; } =
    [
        "sqlmap",
        "nikto",
        "nmap",
        "masscan",
        "acunetix",
        "nessus",
        "openvas",
        "metasploit",
        "havij",
        "zgrab",
    ];

    private static readonly Regex[] UrlRegexes = [.. UrlPatterns.Select(Compile)];

    private static readonly Regex[] UserAgentRegexes = [.. UserAgentPatterns.Select(Compile)];

    /// <summary>
    /// User-Agent vale sempre; padrões de URL e <c>Unknown endpoint</c> só para caminho não registrado (desvio 005).
    /// <c>null</c> = não suspeita.
    /// </summary>
    public static SuspicionResult? Evaluate(string path, string? userAgent, bool isRegisteredRoute)
    {
        if (!string.IsNullOrEmpty(userAgent))
        {
            for (var i = 0; i < UserAgentRegexes.Length; i++)
            {
                if (IsMatch(UserAgentRegexes[i], userAgent))
                {
                    return new SuspicionResult($"Suspicious User-Agent: {UserAgentPatterns[i]}");
                }
            }
        }

        if (isRegisteredRoute)
        {
            return null;
        }

        for (var i = 0; i < UrlRegexes.Length; i++)
        {
            if (IsMatch(UrlRegexes[i], path))
            {
                return new SuspicionResult($"Suspicious URL pattern: {UrlPatterns[i]}");
            }
        }

        return new SuspicionResult($"Unknown endpoint: {path}");
    }

    private static Regex Compile(string pattern) =>
        // O .NET rejeita "\_" (escape de caractere de palavra desconhecido); no Python é o próprio "_".
        new(pattern.Replace(@"\_", "_", StringComparison.Ordinal), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);

    private static bool IsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
