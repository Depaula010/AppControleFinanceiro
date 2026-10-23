using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Cenários do harness da 012d (tests/Fixtures/whatsapp_harness_sections/012d_*): o handler real do Flask com intenções e check-in reais.</summary>
internal static class BillsScenarios
{
    private static readonly JsonElement Root = Load();

    public static DateOnly DefaultToday => DateOnly.Parse(Root.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    public static IEnumerable<JsonElement> All => Root.GetProperty("scenarios").EnumerateArray();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-fixedbills-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

internal static class ScenarioJson
{
    public static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static decimal? Money(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();

    public static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static int? Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
}

/// <summary>Leituras roteirizadas: cada consulta grava o mesmo rótulo que o banco simulado do harness em Python.</summary>
internal sealed class ScriptedFixedBillsReader(JsonElement config, List<string> calls) : IWhatsAppFixedBillsReader
{
    private void Fail(string kind)
    {
        if (ScenarioJson.Text(config, "db_fail") == kind)
        {
            throw new InvalidOperationException("banco indisponivel");
        }
    }

    public Task<IReadOnlyList<PendingBill>> ListPendingAsync(int userId, DateOnly firstDay, DateOnly lastDay, int month, CancellationToken cancellationToken)
    {
        calls.Add($"pendentes:{ScenarioJson.Iso(firstDay)}:{ScenarioJson.Iso(lastDay)}:{month}");
        Fail("pendentes");

        // O SQL de verdade só devolve o que não tem lançamento no mês; o roteiro traz também as já executadas.
        return Task.FromResult<IReadOnlyList<PendingBill>>([.. ScenarioJson.Items(config, "pending_rows")
            .Where(r => (ScenarioJson.Number(r, "ja_executado") ?? 0) == 0)
            .Select(r => new PendingBill(
                r.GetProperty("descricao").GetString()!, (double)(ScenarioJson.Money(r.GetProperty("valor_previsto")) ?? 0m), r.GetProperty("categoria").GetString()!,
                r.GetProperty("dia_execucao").GetInt32(), r.GetProperty("tipo_conta").GetString()!, r.GetProperty("nome_grupo").GetString()!))]);
    }

    public Task<IReadOnlyList<CatalogBill>> ListCatalogAsync(int userId, CancellationToken cancellationToken)
    {
        calls.Add("catalogo");
        Fail("catalogo");
        return Task.FromResult<IReadOnlyList<CatalogBill>>([.. ScenarioJson.Items(config, "catalog_rows").Select(r => new CatalogBill(
            r.GetProperty("descricao").GetString()!, (double)(ScenarioJson.Money(r.GetProperty("valor_previsto")) ?? 0m), r.GetProperty("dia_execucao").GetInt32(),
            ScenarioJson.Number(r, "mes_execucao"), ScenarioJson.Text(r, "periodicidade"), r.GetProperty("categoria").GetString()!,
            r.GetProperty("tipo_conta").GetString()!, r.GetProperty("nome_grupo").GetString()!))]);
    }

    public Task<IReadOnlyList<DueBill>> ListDueBillsAsync(int userId, DueSlice slice, CancellationToken cancellationToken)
    {
        var kind = (slice.DayFrom, slice.DayTo) switch
        {
            ({ }, { }) => "single",
            ({ }, null) => "first",
            _ => "second",
        };
        var from = slice.DayFrom?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var to = slice.DayTo?.ToString(CultureInfo.InvariantCulture) ?? "-";
        calls.Add($"vencimentos:{kind}:{from}:{to}:{slice.Month}/{slice.Year}");
        Fail("vencimentos");

        var rows = config.TryGetProperty("due_bills", out var due) && due.TryGetProperty(kind, out var list) ? list.EnumerateArray() : [];
        return Task.FromResult<IReadOnlyList<DueBill>>([.. rows.Select(r => new DueBill(
            r.GetProperty("descricao").GetString()!, ScenarioJson.Money(r.GetProperty("valor_previsto")), r.GetProperty("dia_execucao").GetInt32(),
            r.GetProperty("nome_grupo").GetString()!))]);
    }

    public Task<IReadOnlyList<DueInvoice>> ListDueInvoicesAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        calls.Add($"faturas:{ScenarioJson.Iso(first)}:{ScenarioJson.Iso(last)}");
        return Task.FromResult<IReadOnlyList<DueInvoice>>([.. ScenarioJson.Items(config, "due_invoices").Select(i => new DueInvoice(
            i.GetProperty("nome_conta").GetString()!, ScenarioJson.Day(i.GetProperty("data_vencimento").GetString()!), ScenarioJson.Money(i.GetProperty("valor_fatura")) ?? 0m))]);
    }
}

/// <summary>Snapshot do check-in roteirizado (as quatro consultas do Flask viram uma chamada, rotulada como a primeira).</summary>
internal sealed class ScriptedCheckinQueries(JsonElement config, List<string> calls) : ICheckinQueries
{
    public Task<CheckinSnapshot> GetSnapshotAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        calls.Add("checkin.snapshot");
        if (ScenarioJson.Text(config, "db_fail") == "snapshot")
        {
            throw new InvalidOperationException("banco indisponivel");
        }

        var snapshot = config.TryGetProperty("snapshot", out var s) ? s : default;
        var pending = ScenarioJson.Items(snapshot, "pending_bills").Select(Bill).ToList();
        var overdue = ScenarioJson.Items(snapshot, "overdue_bills").Select(Bill);
        return Task.FromResult(new CheckinSnapshot(
            pending,
            CheckinRules.Deduplicate(pending, overdue),
            [.. ScenarioJson.Items(snapshot, "overdue_invoices").Select(i => new OverdueInvoice(
                i.GetProperty("id").GetInt32(), i.GetProperty("cartao").GetString()!, ScenarioJson.Day(i.GetProperty("data_vencimento").GetString()!),
                i.GetProperty("status").GetString()!, i.GetProperty("valor_total").GetDecimal()))],
            [.. ScenarioJson.Items(snapshot, "invoices_due_today").Select(i => new DueTodayInvoice(
                i.GetProperty("id").GetInt32(), i.GetProperty("nome_conta").GetString()!, ScenarioJson.Day(i.GetProperty("data_vencimento").GetString()!),
                i.GetProperty("status").GetString()!, i.GetProperty("valor_total").GetDecimal()))]));
    }

    private static CheckinBill Bill(JsonElement r) => new(
        r.GetProperty("id").GetInt32(), r.GetProperty("descricao").GetString()!, ScenarioJson.Money(r.GetProperty("valor_previsto")),
        r.GetProperty("dia_execucao").GetInt32(), r.GetProperty("conta_id").GetInt32(), r.GetProperty("subcategoria_id").GetInt32(),
        r.GetProperty("usuario_id").GetInt32(), r.GetProperty("nome_conta").GetString()!, r.GetProperty("tipo_conta").GetString()!,
        r.GetProperty("categoria").GetString()!, r.GetProperty("nome_macro").GetString()!, r.GetProperty("nome_grupo").GetString()!,
        r.GetProperty("tipo_agendamento").GetString()!, ScenarioJson.Number(r, "parcelas_executadas"), ScenarioJson.Number(r, "total_parcelas"),
        ScenarioJson.Text(r, "data_vencimento_real") is { } due ? ScenarioJson.Day(due) : null);
}

/// <summary>Rotinas de execução que só registram o pedido, com os mesmos rótulos do <c>create_transaction</c> do harness.</summary>
internal sealed class RecordingExecutor(JsonElement config, List<string> calls) : IScheduleExecutor
{
    public List<ScheduleExecutionRequest> Requests { get; } = [];

    public Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        calls.Add($"create_transaction:ag={request.ScheduleId.ToString(CultureInfo.InvariantCulture)}:data={ScenarioJson.Iso(request.Date)}");
        calls.Add("finance_service.create_transaction");

        if (config.TryGetProperty("returns", out var returns) && returns.TryGetProperty("finance_service.create_transaction", out var scripted)
            && scripted.ValueKind == JsonValueKind.Object && scripted.TryGetProperty("__raise__", out var message))
        {
            throw new InvalidOperationException(message.GetString());
        }

        return Task.FromResult<ScheduleExecutionOutcome>(new ScheduleExecutionOutcome.Posted(1, "x", 1m, null));
    }
}

internal sealed class NullCheckinDiagnostics : ICheckinDiagnostics
{
    public List<string> Failures { get; } = [];

    public void ReplyFailed(string errorType) => Failures.Add(errorType);
}

/// <summary>Pipeline (check-in e intenções da 012d reais) sobre o mundo simulado de um cenário.</summary>
internal sealed class BillsRig
{
    public BillsRig(JsonElement scenarioConfig)
    {
        var userExists = !scenarioConfig.TryGetProperty("user", out var user) || user.GetBoolean();
        var today = ScenarioJson.Text(scenarioConfig, "today") is { } text ? ScenarioJson.Day(text) : BillsScenarios.DefaultToday;
        var clock = new FinanceClock(today);

        Cache = new InMemoryCache(new FakeTimeProvider());
        Llm = new LabelledLlm(scenarioConfig, Calls);
        var reader = new ScriptedFixedBillsReader(scenarioConfig, Calls);
        Executor = new RecordingExecutor(scenarioConfig, Calls);
        var session = new CheckinSessionState(Cache);
        var nightly = new NightlyCheckinService(new ScriptedCheckinQueries(scenarioConfig, Calls), Cache, clock);

        var steps = new List<IWhatsAppStep>
        {
            new CheckinStep(session, new CheckinResponder(new CheckinDialogStore(Cache, session), Executor, clock, Diagnostics)),
        };
        var intents = new List<IWhatsAppIntent>
        {
            new HelpIntent(),
            new FixedBillsIntent(reader, clock),
            new AllBillsIntent(reader),
            new DueTodayIntent(reader, clock),
            new DueTomorrowIntent(reader, clock),
            new DueWeekIntent(reader, clock),
            new OverdueIntent(nightly, clock),
        };

        UseCase = new HandleWhatsAppMessage(new FakeUsers(userExists, Calls), steps, new IntentClassifier(Llm), new IntentRouter(intents, new NullDiagnostics()));
    }

    public List<string> Calls { get; } = [];

    public InMemoryCache Cache { get; }

    public LabelledLlm Llm { get; }

    public RecordingExecutor Executor { get; }

    public NullCheckinDiagnostics Diagnostics { get; } = new();

    public HandleWhatsAppMessage UseCase { get; }

    public Task<string> SendAsync(string text, string rawNumber) =>
        UseCase.ExecuteAsync(new WebhookInput(text, rawNumber, null), CancellationToken.None);

    public async Task SeedAsync(string key, JsonNode? value) =>
        await Cache.SetJsonAsync(key, value, TimeSpan.FromSeconds(300));
}
