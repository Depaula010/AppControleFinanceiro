using System.Globalization;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Admin;

/// <summary>Resposta de <c>GET /admin/oauth-config-check</c>: só booleanos e a URL pública de callback (desvio 002).</summary>
public sealed record OAuthConfigCheck(bool ClientIdConfigured, bool ClientSecretConfigured, string RedirectUri);

/// <summary><c>GET /admin/oauth-config-check</c> (RN-009-17): nenhum fragmento do client id nem do secret sai daqui.</summary>
public sealed class GetOAuthConfigCheck(IIntegrationSettings settings)
{
    public OAuthConfigCheck Execute() => new(
        !string.IsNullOrEmpty(settings.GoogleClientId),
        !string.IsNullOrEmpty(settings.GoogleClientSecret),
        settings.GoogleRedirectUri);
}

/// <param name="Connected">Falso quando o usuário não está conectado (resposta sem <c>&lt;pre&gt;</c>).</param>
/// <param name="Html">Corpo final (<c>&lt;pre&gt;</c> ou, sem conexão, linhas unidas por <c>&lt;br&gt;</c>).</param>
public sealed record CalendarDebugReport(bool Connected, string Html);

/// <summary>
/// <c>GET /admin/debug-calendar</c> (RN-009-18): as 5 etapas do Flask para o usuário 1, sem token, sem expiração e sem
/// e-mail/id de evento. Qualquer exceção sobe ao endpoint, que responde 500 de texto fixo.
/// </summary>
public sealed class RunCalendarDebug(IGoogleAccountService accounts, ICalendarClient calendar, IClock clock)
{
    /// <summary>Usuário fixo, como no Flask (Q-009-6).</summary>
    public const int DebugUserId = 1;

    private const string Separator = "============================================================";

    public async Task<CalendarDebugReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var output = new List<string>
        {
            Separator,
            "🧪 TESTE DE DEBUG - GOOGLE CALENDAR",
            Separator,
            "\n[TESTE 1] Verificando conexão...",
        };

        var connected = await accounts.IsConnectedAsync(DebugUserId, cancellationToken);
        output.Add("✅ Usuário conectado? " + (connected ? "True" : "False"));
        if (!connected)
        {
            return new CalendarDebugReport(false, string.Join("<br>", output) + "<br>❌ Usuário não conectado!");
        }

        output.Add("\n[TESTE 2] Obtendo credenciais...");
        var credentials = await accounts.GetCredentialsAsync(DebugUserId, cancellationToken);
        if (credentials.Status == GoogleCredentialStatus.Connected)
        {
            output.Add("✅ Credenciais obtidas");
            output.Add("   - Token: [oculto]");
        }

        output.Add("\n[TESTE 3] Criando serviço Calendar...");
        output.Add("✅ Serviço criado");

        output.Add("\n[TESTE 4] Listando calendários...");
        var calendars = await calendar.ListSelectedCalendarsAsync(DebugUserId, cancellationToken);
        output.Add($"✅ {calendars.Count.ToString(CultureInfo.InvariantCulture)} calendários encontrados");

        output.Add("\n[TESTE 5] Buscando eventos de HOJE...");
        var today = clock.Today;
        output.Add("   Data: " + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var events = await calendar.GetEventsInRangeAsync(DebugUserId, today, today, cancellationToken);
        output.Add($"✅ API retornou {events.Count.ToString(CultureInfo.InvariantCulture)} eventos");
        if (events.Count > 0)
        {
            output.Add("\n   Eventos encontrados:");
            for (var i = 0; i < Math.Min(3, events.Count); i++)
            {
                // O título vem do Google e entra num <pre> HTML: escapar.
                output.Add($"   {(i + 1).ToString(CultureInfo.InvariantCulture)}. {Escape(events[i].Summary)}");
            }
        }

        output.Add("\n✅ TODOS OS TESTES PASSARAM!");
        return new CalendarDebugReport(true, "<pre>" + string.Join("\n", output) + "</pre>");
    }

    /// <summary>Escapa só o que o HTML interpreta; acentos seguem como estão (o corpo é UTF-8).</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
