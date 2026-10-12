using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>Cenários do harness da 012g (tests/Fixtures/whatsapp_harness_sections/012g_analytics.py).</summary>
internal static class AnalyticsScenarios
{
    private static readonly JsonElement Root = Load();

    public static DateOnly Today => DateOnly.Parse(Root.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    public static IEnumerable<JsonElement> All() => Root.GetProperty("scenarios").EnumerateArray();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-analytics-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

internal sealed class FixedDayClock(DateOnly today) : IClock
{
    public DateTimeOffset UtcNow => new(today.ToDateTime(new TimeOnly(17, 20)), TimeSpan.Zero);

    public DateTime Now => today.ToDateTime(new TimeOnly(14, 20));

    public DateOnly Today => today;

    public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}

internal sealed class SilentDiagnostics : IWhatsAppDiagnostics
{
    public void IntentFailed(string intent, string errorType)
    {
    }
}

/// <summary>Gemini roteirizado pelos mesmos rótulos do harness (<c>extract_chart_type</c>, análise e previsão).</summary>
internal sealed class ScriptedAnalyticsLlm(JsonElement config, List<string> calls) : ILlmClient
{
    public List<string> AnalysisPrompts { get; } = [];

    public List<LlmRequest> AnalysisRequests { get; } = [];

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        string label;
        if (request.Prompt.StartsWith("Analise a mensagem: \"", StringComparison.Ordinal) && request.Prompt.Contains("Tipos de gráfico", StringComparison.Ordinal))
        {
            label = "extract_chart_type";
            calls.Add("gemini_service.extract_chart_type");
        }
        else
        {
            label = request.Prompt.StartsWith("\nVocê é um assistente financeiro. Analise os dados", StringComparison.Ordinal) ? "forecast" : "analysis";
            calls.Add("gemini_model.generate_content:" + label);
            AnalysisPrompts.Add(request.Prompt);
            AnalysisRequests.Add(request);
        }

        if (!config.GetProperty("llm").TryGetProperty(label, out var script))
        {
            throw new InvalidOperationException("Gemini não roteirizado: " + label);
        }

        return script.ValueKind == JsonValueKind.Object
            ? throw new InvalidOperationException(script.GetProperty("__raise__").GetString())
            : Task.FromResult(script.GetString()!);
    }
}

internal sealed class RecordingBot(JsonElement config, List<string> calls) : IBotClient
{
    public List<string> Sent { get; } = [];

    public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken)
    {
        calls.Add("notification_service.enviar_imagem_whatsapp_bytes");
        Sent.Add($"{number}|{!image.IsEmpty}|{caption}");
        if (config.TryGetProperty("send_raise", out var raise) && raise.ValueKind == JsonValueKind.True)
        {
            throw new InvalidOperationException("boom");
        }

        return Task.FromResult(!config.TryGetProperty("send_ok", out var ok) || ok.ValueKind != JsonValueKind.False);
    }
}

internal sealed class RecordingRenderer : IChartRenderer
{
    public IReadOnlyList<(string Label, double Value)>? Pie { get; private set; }

    public IReadOnlyList<(string Label, double Expenses, double Income)>? Bars { get; private set; }

    public IReadOnlyList<(DateOnly Date, double Balance)>? Line { get; private set; }

    public byte[]? RenderPie(IReadOnlyList<(string Label, double Value)> data, int days)
    {
        Pie = data;
        return [1, 2, 3];
    }

    public byte[]? RenderBars(IReadOnlyList<(string Label, double Expenses, double Income)> data)
    {
        Bars = data;
        return [1, 2, 3];
    }

    public byte[]? RenderLine(IReadOnlyList<(DateOnly Date, double Balance)> data)
    {
        Line = data;
        return [1, 2, 3];
    }
}

/// <summary>As linhas do banco falso do cenário (as mesmas que o Python recebeu), no formato do domínio.</summary>
internal sealed class ScriptedAnalyticsReader(JsonElement config) : IAnalyticsReader
{
    public List<string> Starts { get; } = [];

    private List<JsonElement> Rows(string key) =>
        config.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array ? [.. rows.EnumerateArray()] : [];

    private void FailIfAsked(params string[] labels)
    {
        if (config.TryGetProperty("db_fail", out var fail) && fail.EnumerateArray().Any(f => labels.Contains(f.GetString())))
        {
            throw new InvalidOperationException("banco fora com segredo");
        }
    }

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<SpendingAnalysis> GetSpendingAnalysisAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        FailIfAsked("by_category", "weekday", "comparison", "pots", "top", "fixed");
        return Task.FromResult(new SpendingAnalysis(
            MonthlyComparison.MonthKey(today),
            [.. Rows("comparison").Select(r => new MonthTotal(r[0].GetString()!, r[1].GetDouble()))],
            [.. Rows("by_category").Select(r => new CategorySpend(r[0].GetString()!, r[1].GetString()!, r[2].GetDouble(), r[3].GetInt32()))],
            [.. Rows("weekday").Select(r => new WeekdaySpend(r[0].GetString()!.Trim(), r[1].GetDouble(), r[2].GetInt32()))],
            [.. Rows("pots").Select(r => new PotUsage(r[0].GetString()!, r[1].GetDouble(), r[3].GetDouble()))],
            [.. Rows("top").Select(r => new TopExpense(r[0].GetString()!, r[1].GetDouble(), Day(r[2].GetString()!)))],
            [.. Rows("fixed").Select(r => new FixedCharge(r[0].GetString()!, r[1].GetDouble(), r[2].GetString()!))]));
    }

    public Task<ForecastData> GetForecastDataAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        FailIfAsked("monthly", "f_fixed", "f_mtd", "f_pending");
        var mtd = config.TryGetProperty("f_mtd", out var m) ? m : default;
        var spent = mtd.ValueKind == JsonValueKind.Array && mtd[0].ValueKind == JsonValueKind.Number ? mtd[0].GetDouble() : 0.0;
        return Task.FromResult(new ForecastData(
            today,
            DateTime.DaysInMonth(today.Year, today.Month),
            6,
            [.. Rows("monthly").Select(r => new MonthTotal(r[0].GetString()!, r[1].GetDouble()))],
            [.. Rows("f_fixed").Select(r => new FixedCharge(r[0].GetString()!, r[1].GetDouble(), r[2].GetString()!))],
            spent,
            [.. Rows("f_pending").Select(r => new PendingBill(
                r[0].GetString()!, r[1].GetDouble(), r[2].ValueKind == JsonValueKind.Number ? r[2].GetInt32() : null, r[3].GetString(), r[4].GetString()))]));
    }

    public Task<IReadOnlyList<MacroMonthTotal>> GetMonthlyComparisonAsync(int userId, string month, string previousMonth, CancellationToken cancellationToken)
    {
        FailIfAsked("mc");
        return Task.FromResult<IReadOnlyList<MacroMonthTotal>>(
            [.. Rows("mc").Select(r => new MacroMonthTotal(r[0].GetString()!, r[1].GetString()!, r[2].GetDouble()))]);
    }

    public Task<IReadOnlyList<PieRow>> GetPieRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        Starts.Add($"pie|{start:yyyy-MM-dd}");
        FailIfAsked("pie");
        return Task.FromResult<IReadOnlyList<PieRow>>([.. Rows("pie_rows").Select(r => new PieRow(r[0].GetString(), r[1].GetDouble()))]);
    }

    public Task<IReadOnlyList<BarRow>> GetBarRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        Starts.Add($"bar|{start:yyyy-MM-dd}");
        FailIfAsked("bar");
        return Task.FromResult<IReadOnlyList<BarRow>>([.. Rows("bar_rows").Select(r => new BarRow(Day(r[0].GetString()!), r[1].GetDouble(), r[2].GetDouble()))]);
    }

    public Task<LineSeries> GetLineSeriesAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        FailIfAsked("initial_balance");
        Starts.Add($"line|{start:yyyy-MM-dd}");
        FailIfAsked("line");
        var initial = config.TryGetProperty("saldo_inicial", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0.0;
        return Task.FromResult(new LineSeries(initial, [.. Rows("line_rows").Select(r => new LineRow(Day(r[0].GetString()!), r[1].GetDouble()))]));
    }
}

/// <summary>As quatro intenções da 012g sobre os fakes do cenário.</summary>
internal sealed class AnalyticsRig
{
    public AnalyticsRig(JsonElement config)
    {
        var clock = new FixedDayClock(AnalyticsScenarios.Today);
        Llm = new ScriptedAnalyticsLlm(config, Calls);
        Bot = new RecordingBot(config, Calls);
        Reader = new ScriptedAnalyticsReader(config);
        Renderer = new RecordingRenderer();
        Router = new IntentRouter(
            [
                new AnalysisIntent(Llm, Reader, clock),
                new MonthlyComparisonIntent(Reader, clock),
                new ForecastIntent(Llm, Reader, clock),
                new ChartIntent(Llm, Reader, Renderer, Bot, clock),
            ],
            new SilentDiagnostics());
    }

    public List<string> Calls { get; } = [];

    public ScriptedAnalyticsLlm Llm { get; }

    public RecordingBot Bot { get; }

    public ScriptedAnalyticsReader Reader { get; }

    public RecordingRenderer Renderer { get; }

    public IntentRouter Router { get; }
}
