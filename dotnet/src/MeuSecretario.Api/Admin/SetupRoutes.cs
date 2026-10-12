using MeuSecretario.Api.Contracts;

namespace MeuSecretario.Api.Admin;

/// <summary>Formato de resposta original de cada rota de setup (spec 009 §4.10).</summary>
public enum SetupFormat
{
    /// <summary>Corpo = a mensagem, <c>text/html</c> (o Flask devolve <c>str</c> como HTML).</summary>
    Text,

    /// <summary><c>&lt;pre&gt;</c> + mensagem + <c>&lt;/pre&gt;</c>.</summary>
    Pre,

    /// <summary>Envelope B <c>{"status":"sucesso","mensagem":...}</c>, sem as chaves extras do Flask.</summary>
    EnvelopeB,
}

/// <summary>
/// As 13 rotas de setup (10 a 22) viram no-op com <c>x-api-key</c> (desvio 002): o esquema é das migrations. O handler não
/// recebe nada do contêiner (nem banco): só a tabela abaixo decide o formato.
/// </summary>
public static class SetupRoutes
{
    public static readonly IReadOnlyList<(string Method, string Path, SetupFormat Format)> Table =
    [
        ("GET", "/admin/setup-database", SetupFormat.Text),
        ("GET", "/admin/populate-global-categories", SetupFormat.Text),
        ("GET", "/admin/setup-user-data", SetupFormat.EnvelopeB),
        ("GET", "/admin/setup-calendar-table", SetupFormat.Text),
        ("GET", "/admin/setup-monthly-reports-table", SetupFormat.EnvelopeB),
        ("POST", "/admin/setup-api-keys-tables", SetupFormat.EnvelopeB),
        ("GET", "/admin/setup-resumo-matinal", SetupFormat.Pre),
        ("GET", "/admin/setup-checkin-noturno", SetupFormat.Pre),
        ("GET", "/admin/setup-potes-alerts", SetupFormat.Pre),
        ("GET", "/admin/setup-alertas-financeiros", SetupFormat.Pre),
        ("GET", "/admin/setup-reserva-emergencia", SetupFormat.Pre),
        ("GET", "/admin/cleanup-deprecated-notification-fields", SetupFormat.Pre),
        ("GET", "/admin/setup-budget-violations", SetupFormat.Pre),
    ];

    internal static void Map(RouteGroupBuilder admin)
    {
        foreach (var (method, path, format) in Table)
        {
            var relative = path["/admin".Length..];
            Delegate handler = () => Respond(format);
            if (method == "POST")
            {
                admin.MapPost(relative, handler);
            }
            else
            {
                admin.MapGet(relative, handler);
            }
        }
    }

    internal static IResult Respond(SetupFormat format) => format switch
    {
        SetupFormat.Text => Results.Content(AdminMessages.SetupNoOp, "text/html; charset=utf-8"),
        SetupFormat.Pre => Results.Content("<pre>" + AdminMessages.SetupNoOp + "</pre>", "text/html; charset=utf-8"),
        _ => Envelopes.B.Success(AdminMessages.SetupNoOp),
    };
}
