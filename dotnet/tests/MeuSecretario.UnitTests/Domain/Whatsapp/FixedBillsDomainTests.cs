using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Referência gerada pelo Python (whatsapp_sections/012d_contas_fixas.py): FixedBillsService, format_vencimentos_message e parse_checkin_response reais.</summary>
internal static class FixedBillsFixture
{
    private static readonly JsonElement Root = Load();

    public static IReadOnlyList<JsonElement> Cases(string section) => [.. Root.GetProperty(section).EnumerateArray()];

    public static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static decimal? Money(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-fixedbills-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

public class FixedBillsDomainTests
{
    [Fact]
    [Trait("AC", "012-401")]
    public void PendingBills_MatchTheFlaskText()
    {
        var cases = FixedBillsFixture.Cases("pending");
        cases.Count.ShouldBeGreaterThan(20);
        foreach (var c in cases)
        {
            var bills = c.GetProperty("rows").EnumerateArray().Select(r => new PendingBill(
                r.GetProperty("descricao").GetString()!, (double)(FixedBillsFixture.Money(r.GetProperty("valor")) ?? 0m), r.GetProperty("categoria").GetString()!,
                r.GetProperty("dia").GetInt32(), r.GetProperty("tipo_conta").GetString()!, r.GetProperty("nome_grupo").GetString()!)).ToList();
            var today = FixedBillsFixture.Date(c.GetProperty("today").GetString()!);

            FixedBillsMessages.Pending(bills, today).ShouldBe(c.GetProperty("text").GetString(), $"{c.GetProperty("label").GetString()} em {today:yyyy-MM-dd}");
        }
    }

    [Fact]
    [Trait("AC", "012-401")]
    public void Catalog_MatchesTheFlaskText()
    {
        var cases = FixedBillsFixture.Cases("catalog");
        cases.Count.ShouldBeGreaterThan(5);
        foreach (var c in cases)
        {
            var bills = c.GetProperty("rows").EnumerateArray().Select(r => new CatalogBill(
                r.GetProperty("descricao").GetString()!, (double)(FixedBillsFixture.Money(r.GetProperty("valor")) ?? 0m), r.GetProperty("dia").GetInt32(),
                r.GetProperty("mes").ValueKind == JsonValueKind.Null ? null : r.GetProperty("mes").GetInt32(),
                r.GetProperty("periodicidade").GetString(), r.GetProperty("categoria").GetString()!,
                r.GetProperty("tipo_conta").GetString()!, r.GetProperty("nome_grupo").GetString()!)).ToList();

            FixedBillsMessages.Catalog(bills).ShouldBe(c.GetProperty("text").GetString(), c.GetProperty("label").GetString());
        }
    }

    [Fact]
    [Trait("AC", "012-402")]
    public void DueDates_MatchTheFlaskText()
    {
        var cases = FixedBillsFixture.Cases("due");
        cases.Count.ShouldBeGreaterThan(30);
        foreach (var c in cases)
        {
            var bills = c.GetProperty("bills").EnumerateArray().Select(b => new DueBill(
                b.GetProperty("descricao").GetString()!, FixedBillsFixture.Money(b.GetProperty("valor")), b.GetProperty("dia").GetInt32(),
                b.GetProperty("nome_grupo").GetString()!)).ToList();
            var invoices = c.GetProperty("invoices").EnumerateArray().Select(i => new DueInvoice(
                i.GetProperty("nome_conta").GetString()!, FixedBillsFixture.Date(i.GetProperty("data_vencimento").GetString()!),
                FixedBillsFixture.Money(i.GetProperty("valor")) ?? 0m)).ToList();

            DueDatesMessage.Format(bills, invoices, c.GetProperty("period").GetString()!, FixedBillsFixture.Date(c.GetProperty("reference").GetString()!))
                .ShouldBe(c.GetProperty("text").GetString(), $"{c.GetProperty("label").GetString()} / {c.GetProperty("period").GetString()}");
        }
    }

    [Theory]
    [InlineData("2026-10-07", "2026-10-07", 1, "7-7")]
    [InlineData("2026-10-07", "2026-10-14", 1, "7-14")]
    [InlineData("2026-10-28", "2026-11-04", 2, "28->|<-4")]
    [InlineData("2026-12-28", "2027-01-04", 2, "28->|<-4")]
    [Trait("AC", "012-402")]
    public void DuePlan_OneQueryInsideAMonth_TwoAcrossTheTurn(string from, string to, int slices, string shape)
    {
        var plan = DueDatesMessage.Plan(FixedBillsFixture.Date(from), FixedBillsFixture.Date(to));
        plan.Count.ShouldBe(slices);
        var text = string.Join("|", plan.Select(s => s switch
        {
            { DayFrom: { } a, DayTo: { } b } => $"{a}-{b}",
            { DayFrom: { } a } => $"{a}->",
            _ => $"<-{s.DayTo}",
        }));
        text.ShouldBe(shape);
    }

    [Fact]
    [Trait("AC", "012-402")]
    public void DuePlan_AcrossTheTurn_UsesEachSlicesOwnMonthAndYear()
    {
        var plan = DueDatesMessage.Plan(new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 4));
        plan[0].ShouldBe(new DueSlice(28, null, 12, 2026));
        plan[1].ShouldBe(new DueSlice(null, 4, 1, 2027));
    }

    [Fact]
    [Trait("AC", "012-402")]
    public void DueDates_NothingToShow_NamesThePeriodInLowercase()
    {
        DueDatesMessage.Format([], [], DueDatesMessage.TomorrowPeriod, new DateOnly(2026, 10, 8)).ShouldBe("✅ Nenhuma conta vence amanhã!");
        DueDatesMessage.Format([], [], DueDatesMessage.WeekPeriod, new DateOnly(2026, 10, 7)).ShouldBe("✅ Nenhuma conta vence nos próximos 7 dias!");
    }

    [Fact]
    [Trait("AC", "012-403")]
    public void Overdue_EmptySnapshot_IsTheCheerfulSentence()
    {
        CheckinSnapshotMessage.Format(new CheckinSnapshot([], [], [], []), new DateOnly(2026, 10, 7))
            .ShouldBe("✅ Você não tem contas atrasadas! Tudo em dia! 🎉");
    }

    [Fact]
    [Trait("AC", "012-403")]
    public void Overdue_OnlyCardExpenses_FallsBackToTheCheerfulSentence()
    {
        // Despesa de cartão que não vence hoje não aparece em lugar nenhum: o formatador devolve vazio.
        var bill = Bill(day: 20, accountType: "Cartão de Crédito", group: "Despesa");
        CheckinSnapshotMessage.Format(new CheckinSnapshot([bill], [], [], []), new DateOnly(2026, 10, 25))
            .ShouldBe("✅ Você não tem contas atrasadas! Tudo em dia! 🎉");
    }

    [Fact]
    [Trait("AC", "012-403")]
    public void Overdue_WithBills_UsesTheReadOnlyTitle()
    {
        var text = CheckinSnapshotMessage.Format(new CheckinSnapshot([Bill(day: 7)], [], [], []), new DateOnly(2026, 10, 7));
        text.ShouldStartWith("📊 *RESUMO FINANCEIRO* 📊\n\n");
        text.ShouldContain("Aluguel - R$ 1.500,00 (Nubank) - Vence hoje");
        text.ShouldEndWith("_💡 Para confirmar pagamentos, aguarde o check-in noturno automático._");
    }

    [Fact]
    [Trait("AC", "012-403")]
    public void Overdue_WithAnOverdueInvoice_ThrowsLikeTheFlaskKeyError()
    {
        var invoice = new OverdueInvoice(1, "Inter", new DateOnly(2026, 10, 1), "Aberta", 100m);
        Should.Throw<InvalidOperationException>(() =>
            CheckinSnapshotMessage.Format(new CheckinSnapshot([], [], [invoice], []), new DateOnly(2026, 10, 7)));
    }

    [Fact]
    [Trait("AC", "012-404")]
    public void CheckinReplyParser_MatchesTheFlask()
    {
        var cases = FixedBillsFixture.Cases("parse");
        cases.Count.ShouldBeGreaterThan(100);
        foreach (var c in cases)
        {
            var message = c.GetProperty("message").GetString()!;
            var total = c.GetProperty("total").GetInt32();
            var reply = CheckinReplyParser.Parse(message, total);
            var label = $"'{message}' / {total}";

            var expectedKind = c.GetProperty("kind").GetString();
            var kind = reply.Kind.ToString().ToLowerInvariant();
            kind.ShouldBe(expectedKind, label);
            var payload = c.GetProperty("payload");
            if (payload.ValueKind == JsonValueKind.Array)
            {
                reply.Indices.ShouldBe([.. payload.EnumerateArray().Select(e => e.GetInt32())], label);
            }
            else
            {
                reply.Text.ShouldBe(payload.GetString(), label);
            }
        }
    }

    [Fact]
    [Trait("AC", "012-404")]
    public void CheckinPaymentDate_MatchesTheFlask()
    {
        var cases = FixedBillsFixture.Cases("payment_dates");
        cases.Count.ShouldBeGreaterThan(100);
        foreach (var c in cases)
        {
            var real = c.GetProperty("real").GetString();
            var today = FixedBillsFixture.Date(c.GetProperty("today").GetString()!);
            var day = c.GetProperty("day").GetInt32();
            var expected = FixedBillsFixture.Date(c.GetProperty("date").GetString()!);

            // O dia 31 num mês curto não existe: o Flask só chega ao replace(day=...) quando 0 <= hoje.day - dia <= 1.
            CheckinReplyTexts.PaymentDate(real, day, today).ShouldBe(expected, $"{real} dia {day} hoje {today:yyyy-MM-dd}");
        }
    }

    [Fact]
    [Trait("AC", "012-404")]
    public void CheckinPaymentDate_NotAnIsoDate_Throws() =>
        Should.Throw<FormatException>(() => CheckinReplyTexts.PaymentDate("amanhã", 5, new DateOnly(2026, 10, 7)));

    [Fact]
    [Trait("AC", "012-404")]
    public void CheckinConfirmed_AllOrSome()
    {
        CheckinReplyTexts.Confirmed(["Aluguel", "Luz"], 2)
            .ShouldBe("✅ *Todas as 2 contas confirmadas!*\n\n• Aluguel\n• Luz\n\n💚 Transações registradas com sucesso!");
        CheckinReplyTexts.Confirmed(["Luz"], 3)
            .ShouldBe("✅ *1 conta(s) confirmada(s):*\n\n• Luz\n\n💚 Transações registradas com sucesso!");
    }

    [Theory]
    [InlineData("10 2", 12, new[] { 2, 10 })]
    [InlineData("3 1 3", 5, new[] { 1, 3 })]
    [Trait("AC", "012-404")]
    public void CheckinReplyParser_PartialIsDistinctAndAscending(string message, int total, int[] expected) =>
        CheckinReplyParser.Parse(message, total).Indices.ShouldBe(expected);

    private static CheckinBill Bill(int day, string accountType = "Conta Corrente", string group = "Despesa") =>
        new(1, "Aluguel", 1500m, day, 1, 10, 7, "Nubank", accountType, "Moradia", "Moradia", group, "FIXO", null, null, null);
}
