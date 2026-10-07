using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Referência gerada pelo Python (tests/Fixtures/generate_jobs_fixtures.py) e conversores JSON para o Domain.</summary>
internal static class JobsFixture
{
    private static readonly JsonElement Root = Load();

    public static IReadOnlyList<JsonElement> Cases(string section) => [.. Root.GetProperty(section).EnumerateArray()];

    public static JsonElement Section(string name) => Root.GetProperty(name);

    public static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    public static double Dbl(JsonElement element, string name) => element.GetProperty(name).GetDouble();

    public static int? NullableInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public static IReadOnlyList<AlertBill> Bills(JsonElement list) =>
    [
        .. list.EnumerateArray().Select(b => new AlertBill(
            b.GetProperty("id").GetInt32(), Str(b, "descricao"), Dbl(b, "valor"), Str(b, "categoria"), Str(b, "conta"), Str(b, "tipo"))),
    ];

    public static IReadOnlyList<AlertInvoice> Invoices(JsonElement list) =>
    [
        .. list.EnumerateArray().Select(i => new AlertInvoice(Str(i, "cartao"), Dbl(i, "valor"), Date(Str(i, "vencimento")))),
    ];

    public static FinancialAlerts Alerts(JsonElement hoje, JsonElement amanha, JsonElement faturasHoje, JsonElement faturasAmanha) =>
        new(Bills(hoje), Bills(amanha), Invoices(faturasHoje), Invoices(faturasAmanha));

    public static BriefingWeather? Weather(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var weather) || weather.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new BriefingWeather(Str(weather, "descricao_completa"), 0, 0, NullableInt(weather, "chance_chuva"));
    }

    public static BriefingData Briefing(JsonElement input)
    {
        var events = input.GetProperty("eventos").EnumerateArray().Select(e => new BriefingEvent(
            Str(e, "summary"), Str(e, "start"), Str(e, "end"), Str(e, "location"), Str(e, "description"), e.GetProperty("all_day").GetBoolean())).ToList();
        var extra = input.GetProperty("climas_adicionais").EnumerateArray().Select(x =>
            new BriefingExtraWeather(Str(x, "cidade"), Str(x, "estado"), Weather(x, "clima")!)).ToList();
        var gaps = input.GetProperty("gaps").EnumerateArray().Select(g =>
            new BriefingGap(Str(g, "inicio"), Str(g, "fim"), g.GetProperty("duracao_minutos").GetInt32())).ToList();
        var alerts = input.GetProperty("alertas_financeiros");
        return new BriefingData(
            events, Weather(input, "clima_principal"), extra, gaps, 0, 0,
            Alerts(alerts.GetProperty("contas_hoje"), alerts.GetProperty("contas_amanha"), alerts.GetProperty("faturas_hoje"), alerts.GetProperty("faturas_amanha")),
            Date(Str(input, "data")));
    }

    public static CheckinBill CheckinBill(JsonElement b) => new(
        b.GetProperty("id").GetInt32(),
        Str(b, "descricao"),
        b.GetProperty("valor_previsto").ValueKind == JsonValueKind.Number ? (decimal)b.GetProperty("valor_previsto").GetDouble() : null,
        b.GetProperty("dia_execucao").GetInt32(),
        b.GetProperty("conta_id").GetInt32(),
        b.GetProperty("subcategoria_id").GetInt32(),
        b.GetProperty("usuario_id").GetInt32(),
        Str(b, "nome_conta"), Str(b, "tipo_conta"), Str(b, "categoria"), Str(b, "nome_macro"), Str(b, "nome_grupo"), Str(b, "tipo_agendamento"),
        NullableInt(b, "parcelas_executadas"), NullableInt(b, "total_parcelas"),
        b.GetProperty("data_vencimento_real").ValueKind == JsonValueKind.String ? Date(Str(b, "data_vencimento_real")) : null);

    public static MonthlyReportData Report(JsonElement r)
    {
        var totals = r.GetProperty("totais");
        var comparison = r.GetProperty("comparacao");
        var accounts = r.GetProperty("contas");
        var period = r.GetProperty("periodo");
        return new MonthlyReportData(
            r.GetProperty("mes").GetInt32(), r.GetProperty("ano").GetInt32(), Date(Str(period, "inicio")), Date(Str(period, "fim")),
            new ReportTotals(Dbl(totals, "total_despesas"), Dbl(totals, "total_rendas"), Dbl(totals, "saldo_periodo"), totals.GetProperty("total_transacoes").GetInt32()),
            [.. r.GetProperty("top_categorias").EnumerateArray().Select(c => new ReportCategory(Str(c, "categoria"), Dbl(c, "valor"), Dbl(c, "percentual")))],
            new ReportComparison(Dbl(comparison, "mes_atual"), Dbl(comparison, "mes_anterior"), Dbl(comparison, "variacao_valor"), Dbl(comparison, "variacao_percentual")),
            [.. r.GetProperty("potes").EnumerateArray().Select(p => new ReportPot(Str(p, "nome"), Dbl(p, "limite"), Dbl(p, "usado"), Dbl(p, "saldo"), Dbl(p, "percentual")))],
            new ReportAccounts(accounts.GetProperty("pagas").GetInt32(), Dbl(accounts, "valor_pago"), accounts.GetProperty("pendentes").GetInt32(), Dbl(accounts, "valor_pendente")));
    }

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "jobs-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
