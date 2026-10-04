using System.Text;
using MeuSecretario.Api.Contracts;

namespace MeuSecretario.Api.Google;

/// <summary>Textos fixos das rotas de OAuth (spec 010 §7.4 a §7.6; desvio 008: nenhum <c>str(e)</c>).</summary>
public static class GoogleMessages
{
    public const string UserNotFound = "Erro: Usuario nao encontrado";
    public const string MissingCodeOrState = "Erro: codigo ou state faltando";
    public const string ConnectFailed = "Erro ao conectar";
    public const string DisconnectFailed = "Erro ao desconectar Google Calendar";
    public const string Disconnected = "Google Calendar desconectado com sucesso";
    public const string Unauthorized = "Não autorizado";
    public const string DatabaseUnavailable = "Banco de dados temporariamente indisponivel";
    public const string InvalidStateText = "link de autorizacao invalido ou expirado";
    public const string InternalErrorText = "erro interno";
    public const string ForbiddenLink = "Link de conexao invalido ou expirado. Peca um novo link pelo WhatsApp.";
}

/// <summary>Páginas HTML do OAuth montadas a partir das constantes geradas do Python (<see cref="GooglePages"/>).</summary>
public static class GooglePageRenderer
{
    /// <summary>B.3 com o nome escapado (desvio 060: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c>).</summary>
    public static string Connected(string? name) =>
        GooglePages.Connected.Replace("{nome}", HtmlEscape(string.IsNullOrEmpty(name) ? "Usuario" : name), StringComparison.Ordinal);

    /// <summary>B.4 com um dos dois textos fixos (nunca texto de exceção).</summary>
    public static string Error(string fixedText) =>
        GooglePages.Error.Replace("{texto}", fixedText, StringComparison.Ordinal);

    /// <summary>403 de <c>connect-calendar</c> sem token válido (desvio 053): HTML simples.</summary>
    public static string Forbidden() =>
        "<!DOCTYPE html>\n<html>\n<head>\n    <title>Link invalido</title>\n    <meta charset=\"utf-8\">\n</head>\n<body>\n" +
        "    <h1>Link invalido ou expirado</h1>\n    <p>" + GoogleMessages.ForbiddenLink + "</p>\n</body>\n</html>\n";

    public static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}

/// <summary>Respostas das rotas de OAuth: cada ramo tem o seu formato (não usam o envelope A).</summary>
public static class GoogleResponses
{
    /// <summary><c>text/html; charset=utf-8</c>, como o Flask faz com <c>str</c>.</summary>
    public static IResult Html(string body, int statusCode) =>
        Results.Content(body, "text/html; charset=utf-8", Encoding.UTF8, statusCode);

    /// <summary>503 de <c>connect-calendar</c>: JSON com a chave <c>resposta</c>.</summary>
    public static IResult DatabaseUnavailable() =>
        Envelopes.E.Error(GoogleMessages.DatabaseUnavailable, StatusCodes.Status503ServiceUnavailable);

    public static IResult DisconnectUnauthorized(HttpContext _) =>
        Envelopes.B.Error(GoogleMessages.Unauthorized, StatusCodes.Status401Unauthorized);
}
