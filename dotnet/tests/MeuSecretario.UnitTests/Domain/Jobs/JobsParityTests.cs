using System.Text.Json;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Textos idênticos aos do Python (fixtures geradas pelo código do Flask).</summary>
public class JobsParityTests
{
    private static readonly DateOnly Today = new(2026, 3, 18);

    [Fact]
    [Trait("AC", "013-31")]
    public void FinancialAlerts_MatchPython()
    {
        foreach (var c in JobsFixture.Cases("financial_alerts"))
        {
            var input = c.GetProperty("input");
            var alerts = JobsFixture.Alerts(input.GetProperty("hoje"), input.GetProperty("amanha"), input.GetProperty("faturas_hoje"), input.GetProperty("faturas_amanha"));
            var expected = c.GetProperty("output").ValueKind == JsonValueKind.Null ? null : c.GetProperty("output").GetString();

            FinancialAlertFormatter.Format(alerts, c.GetProperty("greeting").GetBoolean()).ShouldBe(expected);
        }
    }

    [Fact]
    [Trait("AC", "013-51")]
    public void InvoiceClosedMessage_MatchesPython()
    {
        foreach (var c in JobsFixture.Cases("invoice_closed"))
        {
            var invoice = new ClosedInvoice(1, 2, "Nubank", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 17), c.GetProperty("total").GetDouble(), 3, "55");
            InvoiceNotificationFormatter.Closed(invoice).ShouldBe(c.GetProperty("output").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-53")]
    public void InvoiceDueWarning_MatchesPython()
    {
        foreach (var c in JobsFixture.Cases("invoice_due"))
        {
            var invoice = new DueSoonInvoice(1, 2, "Inter", new DateOnly(2026, 3, 21), 456.7, 3, "55", c.GetProperty("days").GetInt32());
            InvoiceNotificationFormatter.DueDateWarning(invoice).ShouldBe(c.GetProperty("output").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-23")]
    public void BriefingPromptAndFallback_MatchPython()
    {
        foreach (var c in JobsFixture.Cases("briefing_prompt"))
        {
            var data = JobsFixture.Briefing(c.GetProperty("input"));

            BriefingPrompt.Build(data).ShouldBe(c.GetProperty("prompt").GetString());
            BriefingPrompt.Fallback(data).ShouldBe(c.GetProperty("fallback").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-23")]
    public void BriefingCleanup_MatchesPython()
    {
        foreach (var c in JobsFixture.Cases("briefing_clean"))
        {
            BriefingPrompt.CleanResponse(c.GetProperty("input").GetString()!).ShouldBe(c.GetProperty("output").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-22")]
    public void BriefingGaps_MatchPython()
    {
        var section = JobsFixture.Section("briefing_gaps");
        var events = section.GetProperty("events").EnumerateArray()
            .Select(e => new BriefingEvent(string.Empty, JobsFixture.Str(e, "start"), JobsFixture.Str(e, "end"), string.Empty, string.Empty, e.GetProperty("all_day").GetBoolean()));

        var gaps = BriefingRules.CalculateGaps(events);

        gaps.Select(g => (g.Start, g.End, g.Minutes)).ShouldBe(
            section.GetProperty("gaps").EnumerateArray().Select(g => (JobsFixture.Str(g, "inicio"), JobsFixture.Str(g, "fim"), g.GetProperty("duracao_minutos").GetInt32())));
    }

    [Fact]
    [Trait("AC", "013-22")]
    public void BriefingLocations_MatchPython()
    {
        var section = JobsFixture.Section("briefing_locations");
        var events = section.GetProperty("events").EnumerateArray()
            .Select(e => new BriefingEvent(string.Empty, string.Empty, string.Empty, JobsFixture.Str(e, "location"), string.Empty, false));

        var locations = BriefingRules.ExtractLocations(events).OrderBy(l => l.City, StringComparer.Ordinal).ThenBy(l => l.State, StringComparer.Ordinal);

        locations.Select(l => new[] { l.City, l.State }).ShouldBe(
            section.GetProperty("locations").EnumerateArray().Select(l => new[] { l[0].GetString()!, l[1].GetString()! }));
    }

    [Fact]
    [Trait("AC", "013-22")]
    public void BriefingEventType_MatchesPython()
    {
        foreach (var c in JobsFixture.Section("briefing_types").EnumerateArray())
        {
            var e = c.GetProperty("event");
            var evt = new BriefingEvent(JobsFixture.Str(e, "summary"), string.Empty, string.Empty, JobsFixture.Str(e, "location"), JobsFixture.Str(e, "description"), false);

            BriefingRules.DetectType(evt).ShouldBe(c.GetProperty("type").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-45")]
    public void CheckinMessage_MatchesPython_ForJobAndReadOnlyModes()
    {
        foreach (var c in JobsFixture.Cases("checkin_messages"))
        {
            var input = c.GetProperty("input");
            var snapshot = new CheckinSnapshot(
                [.. input.GetProperty("pending").EnumerateArray().Select(JobsFixture.CheckinBill)],
                [.. input.GetProperty("overdue").EnumerateArray().Select(JobsFixture.CheckinBill)],
                [.. input.GetProperty("overdue_invoices").EnumerateArray().Select(i => new OverdueInvoice(
                    i.GetProperty("id").GetInt32(), JobsFixture.Str(i, "cartao"), JobsFixture.Date(JobsFixture.Str(i, "data_vencimento")),
                    JobsFixture.Str(i, "status"), (decimal)i.GetProperty("valor_total").GetDouble()))],
                [.. input.GetProperty("due_today").EnumerateArray().Select(i => new DueTodayInvoice(
                    i.GetProperty("id").GetInt32(), JobsFixture.Str(i, "nome_conta"), JobsFixture.Date(JobsFixture.Str(i, "data_vencimento")),
                    JobsFixture.Str(i, "status"), (decimal)i.GetProperty("valor_total").GetDouble()))]);
            var id = input.GetProperty("checkin_id").ValueKind == JsonValueKind.String ? input.GetProperty("checkin_id").GetString() : null;
            var expected = c.GetProperty("output").ValueKind == JsonValueKind.Null ? null : c.GetProperty("output").GetString();

            CheckinMessageFormatter.Format(snapshot, Today, id).ShouldBe(expected);
        }
    }

    [Fact]
    [Trait("AC", "013-46")]
    public void CheckinMessage_WithOverdueInvoice_WritesTheCardName_Deviation130()
    {
        // Evidência do comportamento antigo: o Flask lança KeyError ao ler 'nome_conta' (a query devolve 'cartao').
        JobsFixture.Section("checkin_key_error").GetString().ShouldBe("'nome_conta'");
        var pending = JobsFixture.Cases("checkin_messages")[0].GetProperty("input").GetProperty("pending")[0];
        var snapshot = new CheckinSnapshot(
            [JobsFixture.CheckinBill(pending)], [], [new OverdueInvoice(1, "Inter", new DateOnly(2026, 3, 10), "Aberta", 10m)], []);

        var message = CheckinMessageFormatter.Format(snapshot, Today, "abcdef12");

        message.ShouldNotBeNull();
        message.ShouldContain("🔴 *FATURAS VENCIDAS:*\n• Inter - R$ 10,00 - Venceu em 10/03/2026\n");
    }

    [Fact]
    [Trait("AC", "013-46")]
    public void CheckinMessage_PendingIncomeFromThePreviousMonth_ShowsItsRealDueDate_Deviation252()
    {
        // Hoje 03/11: a receita do dia 28 venceu em 28/10 (dentro dos 7 dias) e não pode sair como "28/11" (futuro).
        var salary = new CheckinBill(
            1, "Salário", 1000m, 28, 1, 1, 1, "Conta", "Conta Corrente", "Salário", "Renda", "Renda", "FIXO", null, null, new DateOnly(2026, 10, 28));
        var snapshot = new CheckinSnapshot([salary], [], [], []);

        var message = CheckinMessageFormatter.Format(snapshot, new DateOnly(2026, 11, 3), "abcdef12");

        message.ShouldNotBeNull();
        message.ShouldContain("1. Salário - R$ 1.000,00 - Previsto em 28/10\n");
        message.ShouldNotContain("28/11");
    }

    [Fact]
    [Trait("AC", "013-44")]
    public void CheckinSession_MatchesPythonOrderAndFields()
    {
        var section = JobsFixture.Section("checkin_session");
        var bills = section.GetProperty("bills").EnumerateArray().Select(JobsFixture.CheckinBill);

        var session = CheckinSession.Build(bills, Today);

        var expected = section.GetProperty("session");
        session.TotalItems.ShouldBe(expected.GetProperty("total_items").GetInt32());
        session.CreatedAt.ShouldBe(expected.GetProperty("created_at").GetString());
        var json = JsonSerializer.SerializeToElement(session);
        var actualItems = json.GetProperty("items");
        foreach (var expectedItem in expected.GetProperty("items").EnumerateObject())
        {
            var actual = actualItems.GetProperty(expectedItem.Name);
            foreach (var field in expectedItem.Value.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.Number)
                {
                    actual.GetProperty(field.Name).GetDouble().ShouldBe(field.Value.GetDouble(), $"item {expectedItem.Name}, campo {field.Name}");
                }
                else
                {
                    actual.GetProperty(field.Name).ToString().ShouldBe(field.Value.ToString(), $"item {expectedItem.Name}, campo {field.Name}");
                }
            }
        }
    }

    [Fact]
    [Trait("AC", "013-44")]
    public void CheckinDaysOverdue_MatchesPython()
    {
        foreach (var c in JobsFixture.Cases("checkin_days"))
        {
            CheckinRules.DaysOverdue(c.GetProperty("dia").GetInt32(), JobsFixture.Date(c.GetProperty("hoje").GetString()!))
                .ShouldBe(c.GetProperty("dias").GetInt32());
        }
    }

    [Fact]
    [Trait("AC", "013-72")]
    public void MonthlyReportMessage_IsThePythonTextWithPtBrNumbers_Deviation133()
    {
        // 'output_ptbr' = a saída do formatador REAL do Flask com 1,234.56 trocado por 1.234,56 e +8.0 por +8,0.
        foreach (var c in JobsFixture.Cases("monthly_messages"))
        {
            var data = JobsFixture.Report(c.GetProperty("input"));

            MonthlyReportFormatter.Format(data, c.GetProperty("name").GetString()!).ShouldBe(c.GetProperty("output_ptbr").GetString());
        }
    }

    [Fact]
    [Trait("AC", "013-72")]
    public void MonthlyReportMessage_NeverUsesTheEnglishNumberFormat()
    {
        foreach (var c in JobsFixture.Cases("monthly_messages"))
        {
            var message = MonthlyReportFormatter.Format(JobsFixture.Report(c.GetProperty("input")), "Rafael");

            System.Text.RegularExpressions.Regex.IsMatch(message, @"\d,\d{3}\.\d|\d\.\d%").ShouldBeFalse(message);
        }
    }
}
