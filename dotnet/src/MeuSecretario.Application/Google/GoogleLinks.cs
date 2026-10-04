using System.Globalization;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Google;

/// <summary>
/// Link de conexão do Google Calendar (RN-010-41 e desvio 053). Usado por 012 e 014 no lugar do <c>rsplit</c> repetido.
/// O host é o de <c>GOOGLE_REDIRECT_URI</c> (hoje o <c>oauth_proxy</c> do Render, que repassa a query string ao servidor).
/// </summary>
public sealed class GoogleLinks(IIntegrationSettings settings, ConnectLinkTokenCodec tokens)
{
    /// <summary><c>GOOGLE_REDIRECT_URI</c> até antes da última <c>/</c> (<c>rsplit('/', 1)[0]</c>); vazio = sem link.</summary>
    public string? Base()
    {
        var redirectUri = settings.GoogleRedirectUri;
        if (string.IsNullOrEmpty(redirectUri))
        {
            return null;
        }

        var slash = redirectUri.LastIndexOf('/');
        return slash >= 0 ? redirectUri[..slash] : redirectUri;
    }

    /// <summary>
    /// <c>{base}/connect-calendar/{id}?t={token}</c> com o token assinado de 7 dias (desvio 053); <c>null</c> sem
    /// <c>GOOGLE_REDIRECT_URI</c> (cada consumidor tem a sua variante de texto sem link).
    /// </summary>
    public string? ConnectUrl(int userId)
    {
        var baseUrl = Base();
        return baseUrl is null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{baseUrl}/connect-calendar/{userId}?t={Uri.EscapeDataString(tokens.Create(userId))}");
    }
}

/// <summary>Textos de WhatsApp da 010d, literais do Python (<c>google_calendar_oauth_service.py:225-239</c>, <c>calendar_handler.py:174-184</c>).</summary>
public static class GoogleNotificationMessages
{
    private const string RevokedHeader =
        "⚠️ *Google Calendar - Reconexão Necessária*\n\n" +
        "Detectamos que sua conexão com o Google Calendar expirou ou foi revogada.\n\n" +
        "🔴 *Seus alertas de tarefas estão PAUSADOS* até você reconectar.\n\n";

    private const string RevokedFooter =
        "\n\n_Esta notificação é enviada 1x por semana enquanto a reconexão for necessária._";

    /// <summary>Variante com link quando há <paramref name="connectUrl"/>, senão a variante sem link.</summary>
    public static string TokenRevoked(string? connectUrl) =>
        connectUrl is null
            ? RevokedHeader + "✅ *Como resolver:* Acesse as configurações do app e clique em \"Conectar Google Calendar\"." + RevokedFooter
            : RevokedHeader + "👉 *Reconecte agora:*\n" + connectUrl + RevokedFooter;

    /// <summary>B.5: enviada pelo callback do OAuth depois de gravar os tokens.</summary>
    public static string Connected(string? name) =>
        "*Google Calendar + Drive Conectados!*\n\n" +
        $"Ola {name}! Sua conta Google foi conectada com sucesso.\n\n" +
        "*Calendario:*\n" +
        "- Tenho compromisso hoje?\n" +
        "- Minha agenda amanha\n\n" +
        "*Google Drive:*\n" +
        "- Envie uma imagem/documento com a legenda:\n" +
        "  _salvar no drive pasta Notas Fiscais_\n\n" +
        "Seus dados estao seguros via OAuth2!";
}
