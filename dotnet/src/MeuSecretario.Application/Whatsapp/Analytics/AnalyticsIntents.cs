using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Leituras das análises e do gráfico (<c>analytics_service.py</c>, <c>forecast_service.py</c>, <c>chart_service.py</c>). As datas chegam
/// como parâmetro (<c>IClock</c>, fuso de São Paulo); a implementação só consulta o banco.
/// </summary>
public interface IAnalyticsReader
{
    /// <summary><c>get_spending_analysis</c>: as seis consultas que entram no prompt.</summary>
    Task<SpendingAnalysis> GetSpendingAnalysisAsync(int userId, DateOnly today, CancellationToken cancellationToken);

    /// <summary><c>get_forecast_data</c> (histórico de 6 meses): as quatro consultas que entram no prompt.</summary>
    Task<ForecastData> GetForecastDataAsync(int userId, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Despesas por macro categoria nos dois meses (<c>YYYY-MM</c>), do maior total para o menor.</summary>
    Task<IReadOnlyList<MacroMonthTotal>> GetMonthlyComparisonAsync(int userId, string month, string previousMonth, CancellationToken cancellationToken);

    /// <summary>Despesas por categoria desde <paramref name="start"/>, em valor absoluto, do maior para o menor.</summary>
    Task<IReadOnlyList<PieRow>> GetPieRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken);

    /// <summary>Despesas e rendas por mês desde <paramref name="start"/>, em ordem cronológica.</summary>
    Task<IReadOnlyList<BarRow>> GetBarRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken);

    /// <summary>Saldo inicial das contas e o saldo acumulado por transação desde <paramref name="start"/>.</summary>
    Task<LineSeries> GetLineSeriesAsync(int userId, DateOnly start, CancellationToken cancellationToken);
}

/// <summary><c>Análise Inteligente</c> (template; desvio 063: o Gemini recebe o usuário e a chave dele).</summary>
public sealed class AnalysisIntent(ILlmClient llm, IAnalyticsReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Análise Inteligente";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await reader.GetSpendingAnalysisAsync(context.User.Id, clock.Today, cancellationToken);
        var raw = await llm.GenerateTextAsync(new LlmRequest(AnalyticsReport.Prompt(data), context.User.Id), cancellationToken);
        return AnalyticsReport.Header + raw.Trim() + AnalyticsReport.Footer;
    }
}

/// <summary><c>Comparação Mensal</c> (template): o mês de hoje contra o anterior.</summary>
public sealed class MonthlyComparisonIntent(IAnalyticsReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Comparação Mensal";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var month = MonthlyComparison.MonthKey(today);
        var previous = MonthlyComparison.PreviousMonthKey(today);
        var rows = await reader.GetMonthlyComparisonAsync(context.User.Id, month, previous, cancellationToken);
        return rows.Count == 0 ? MonthlyComparison.NoData(month) : MonthlyComparison.Format(month, previous, rows);
    }
}

/// <summary><c>Previsão de Gastos</c> (template; desvio 063).</summary>
public sealed class ForecastIntent(ILlmClient llm, IAnalyticsReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Previsão de Gastos";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await reader.GetForecastDataAsync(context.User.Id, clock.Today, cancellationToken);

        // Desvio 158: sem histórico o Flask quebrava com KeyError; aqui a previsão sai com média zero.
        var projection = ForecastProjection.Calculate(data);

        var raw = await llm.GenerateTextAsync(new LlmRequest(ForecastReport.Prompt(data, projection), context.User.Id), cancellationToken);
        return ForecastReport.Header + raw.Trim() + ForecastReport.Footer(data);
    }
}

/// <summary>
/// <c>Gráfico de Gastos</c> (template): o Gemini escolhe pizza, barras ou linha; o PNG vai pelo bot (desvio 026, aprovado: o
/// <see cref="IBotClient"/> comum). Falha ao ler ou desenhar vira "sem dados", como no <c>chart_service</c>.
/// </summary>
public sealed class ChartIntent(ILlmClient llm, IAnalyticsReader reader, IChartRenderer renderer, IBotClient bot, IClock clock) : IWhatsAppIntent
{
    public string Name => "Gráfico de Gastos";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.ChartType(context.Message), context.User.Id, LlmCachePolicies.ChartType), cancellationToken);
        var info = QueryJson.Object(raw);

        var type = info.TryGetProperty("tipo_grafico", out var typeElement)
            ? (typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null)
            : "pizza";
        var days = Param(info, "periodo_dias", 30);
        var months = Param(info, "num_meses", 6);

        if (string.IsNullOrEmpty(context.Number))
        {
            return ChartRules.NoNumber;
        }

        string caption;
        byte[]? image;
        switch (type)
        {
            case "pizza":
                caption = ChartRules.PieCaption(days);
                image = await SafeAsync(() => PieAsync(context.User.Id, days, cancellationToken));
                break;
            case "barras":
                caption = ChartRules.BarsCaption(months);
                image = await SafeAsync(() => BarsAsync(context.User.Id, months, cancellationToken));
                break;
            case "linha":
                caption = ChartRules.LineCaption(months);
                image = await SafeAsync(() => LineAsync(context.User.Id, months, cancellationToken));
                break;
            default:
                return ChartRules.NoData;
        }

        if (image is null)
        {
            return ChartRules.NoData;
        }

        var sent = await bot.SendImageAsync(context.Number, image, caption, cancellationToken);
        return sent ? ChartRules.Sent(caption) : ChartRules.SendFailed;
    }

    private async Task<byte[]?> PieAsync(int userId, ChartParam days, CancellationToken cancellationToken)
    {
        if (ChartRules.PieStart(clock.Today, days) is not { } start)
        {
            return null;
        }

        var rows = await reader.GetPieRowsAsync(userId, start, cancellationToken);
        return ChartRules.PieData(rows) is { } data && days.Value is { } value
            ? renderer.RenderPie(data, (int)Math.Floor(value))
            : null;
    }

    private async Task<byte[]?> BarsAsync(int userId, ChartParam months, CancellationToken cancellationToken)
    {
        if (ChartRules.MonthsStart(clock.Today, months) is not { } start)
        {
            return null;
        }

        var rows = await reader.GetBarRowsAsync(userId, start, cancellationToken);
        return rows.Count == 0
            ? null
            : renderer.RenderBars([.. rows.Select(r => (ChartRules.MonthLabel(r.Month), r.Expenses, r.Income))]);
    }

    private async Task<byte[]?> LineAsync(int userId, ChartParam months, CancellationToken cancellationToken)
    {
        if (ChartRules.MonthsStart(clock.Today, months) is not { } start)
        {
            return null;
        }

        var series = await reader.GetLineSeriesAsync(userId, start, cancellationToken);
        return series.Rows.Count == 0 ? null : renderer.RenderLine(ChartRules.Balances(series));
    }

    /// <summary>O <c>try/except Exception</c> de cada <c>generate_*</c> do <c>chart_service</c>: qualquer falha devolve <c>None</c>.</summary>
    private static async Task<byte[]?> SafeAsync(Func<Task<byte[]?>> generate)
    {
        try
        {
            return await generate();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary><c>chart_info.get(chave, padrão)</c> como o Python o usa: número, booleano (é inteiro) ou valor sem sentido numérico.</summary>
    private static ChartParam Param(JsonElement info, string name, int fallback)
    {
        if (!info.TryGetProperty(name, out var element))
        {
            return new ChartParam(fallback.ToString(CultureInfo.InvariantCulture), fallback);
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return new ChartParam("True", 1);
            case JsonValueKind.False:
                return new ChartParam("False", 0);
            case JsonValueKind.Number:
                var text = element.GetRawText();
                var isFloat = text.Contains('.', StringComparison.Ordinal) || text.Contains('e', StringComparison.OrdinalIgnoreCase);
                if (!isFloat)
                {
                    var big = BigInteger.Parse(text, CultureInfo.InvariantCulture);
                    return new ChartParam(big.ToString(CultureInfo.InvariantCulture), (double)big);
                }

                var number = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                return new ChartParam(MeuSecretario.Domain.Automation.PyFloat.Repr(number), number);
            case JsonValueKind.Null:
                return new ChartParam("None", null);
            default:
                return new ChartParam(element.GetRawText(), null);
        }
    }
}
