using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Time;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>O passo dos relatórios sob comando (desvio 256) com leitor e relatório mensal falsos.</summary>
public class ReportCommandStepTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly IReportsReader _reader = Substitute.For<IReportsReader>();
    private readonly IMonthlyReportJob _monthly = Substitute.For<IMonthlyReportJob>();
    private readonly ReportCommandStep _step;

    public ReportCommandStepTests()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(DateTimeOffset.Parse("2026-10-22T15:00:00-03:00", CultureInfo.InvariantCulture));
        _step = new ReportCommandStep(_reader, _monthly, new BrazilClock(time));
    }

    private static PipelineContext Context(string message) =>
        new(new WhatsAppUser(7, "Rafael"), "5531999990001", message, message.ToLowerInvariant(), null);

    private static CardInvoiceReport Invoice() => new(
        "Cartão Inter", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 13), "Paga", 100m, 0m, 1,
        [new CategoryAmount("Streaming (Netflix, Spotify, etc.)", 100m, 1)], [], 0, 0m);

    [Fact]
    public async Task AnythingElse_PassesThrough_WithoutTouchingTheDatabase()
    {
        (await _step.HandleAsync(Context("qual valor da fatura?"), CancellationToken.None)).ShouldBeNull();
        (await _step.HandleAsync(Context("paguei 500 da fatura do inter"), CancellationToken.None)).ShouldBeNull();

        await _reader.DidNotReceiveWithAnyArgs().ListCardsAsync(default, default);
        await _monthly.DidNotReceiveWithAnyArgs().SendManualAsync(default, default, default);
    }

    [Fact]
    public async Task Invoice_WithASingleCard_UsesItAndTheOpenInvoiceByDefault()
    {
        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns([new ReportCard(2, "Cartão Inter")]);
        _reader.GetInvoiceAsync(7, 2, new InvoiceSelection(InvoiceSelectionKind.Open), Today, Arg.Any<CancellationToken>()).Returns(Invoice());

        var reply = await _step.HandleAsync(Context("fatura por categoria"), CancellationToken.None);

        reply.ShouldBe(ReportMessages.Invoice(Invoice()));
    }

    [Theory]
    [InlineData("fatura de setembro por categoria", InvoiceSelectionKind.DueMonth, "2026-09-01")]
    [InlineData("fatura passada por categoria", InvoiceSelectionKind.Previous, null)]
    public async Task Invoice_PassesThePeriodAsked(string message, InvoiceSelectionKind kind, string? month)
    {
        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns([new ReportCard(2, "Cartão Inter")]);
        DateOnly? dueMonth = month is null ? null : DateOnly.Parse(month, CultureInfo.InvariantCulture);
        _reader.GetInvoiceAsync(7, 2, new InvoiceSelection(kind, dueMonth), Today, Arg.Any<CancellationToken>()).Returns(Invoice());

        (await _step.HandleAsync(Context(message), CancellationToken.None)).ShouldBe(ReportMessages.Invoice(Invoice()));
    }

    [Fact]
    public async Task Invoice_WithSeveralCards_PicksTheOneNamedInTheMessage_OrAsks()
    {
        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns([new ReportCard(2, "Cartão Inter"), new ReportCard(3, "Nubank")]);
        _reader.GetInvoiceAsync(7, 3, Arg.Any<InvoiceSelection>(), Today, Arg.Any<CancellationToken>()).Returns(Invoice());

        (await _step.HandleAsync(Context("fatura do nubank por categoria"), CancellationToken.None)).ShouldBe(ReportMessages.Invoice(Invoice()));
        await _reader.Received(1).GetInvoiceAsync(7, 3, Arg.Any<InvoiceSelection>(), Today, Arg.Any<CancellationToken>());

        var asked = await _step.HandleAsync(Context("fatura por categoria"), CancellationToken.None);

        asked.ShouldBe(ReportMessages.ChooseCard(["Cartão Inter", "Nubank"]));
    }

    [Fact]
    public async Task Invoice_WithoutCardOrInvoice_SaysSo()
    {
        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns([]);
        (await _step.HandleAsync(Context("fatura por categoria"), CancellationToken.None)).ShouldBe(ReportMessages.NoCard);

        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns([new ReportCard(2, "Cartão Inter")]);
        _reader.GetInvoiceAsync(7, 2, Arg.Any<InvoiceSelection>(), Today, Arg.Any<CancellationToken>()).Returns((CardInvoiceReport?)null);

        (await _step.HandleAsync(Context("fatura de agosto por categoria"), CancellationToken.None))
            .ShouldBe("💳 Não encontrei a fatura do Cartão Inter de agosto/2026.");
    }

    [Fact]
    public async Task MonthByCategory_FormatsTheReader_OrSaysThereIsNothing()
    {
        var report = new MonthByCategoryReport(new DateOnly(2026, 10, 1), Today, true, 50m, 0m, [new CategoryDelta("Streaming", 50m, 0m, 1)]);
        _reader.GetMonthByCategoryAsync(7, new DateOnly(2026, 10, 1), Today, Arg.Any<CancellationToken>()).Returns(report);

        (await _step.HandleAsync(Context("gastos do mês por categoria"), CancellationToken.None)).ShouldBe(ReportMessages.MonthByCategory(report));

        _reader.GetMonthByCategoryAsync(7, new DateOnly(2026, 9, 1), Today, Arg.Any<CancellationToken>())
            .Returns(new MonthByCategoryReport(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), false, 0m, 0m, []));

        (await _step.HandleAsync(Context("gastos por categoria do mês passado"), CancellationToken.None))
            .ShouldBe("📊 Não encontrei gastos em setembro/2026.");
    }

    [Theory]
    [InlineData("relatório do mês", MonthlyMoment.FimMes)]
    [InlineData("relatório do mês passado", MonthlyMoment.InicioMes)]
    public async Task MonthlyReport_TriggersTheExistingJob_ForTheCurrentOrThePreviousMonth(string message, MonthlyMoment moment)
    {
        _monthly.SendManualAsync(7, moment, Arg.Any<CancellationToken>())
            .Returns(new ManualReportResult(true, null, 7, "Rafael", true, true, null));

        (await _step.HandleAsync(Context(message), CancellationToken.None)).ShouldBe(ReportMessages.MonthlyReportSent);

        await _monthly.Received(1).SendManualAsync(7, moment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MonthlyReport_OtherMonths_AreRefused_AndNoDataIsExplained()
    {
        (await _step.HandleAsync(Context("relatório de agosto"), CancellationToken.None)).ShouldBe(ReportMessages.MonthlyReportRange);
        await _monthly.DidNotReceiveWithAnyArgs().SendManualAsync(default, default, default);

        var empty = new MonthlyReportData(10, 2026, Today, Today, new ReportTotals(0, 0, 0, 0), [], null!, [], null!);
        _monthly.SendManualAsync(7, MonthlyMoment.FimMes, Arg.Any<CancellationToken>())
            .Returns(new ManualReportResult(false, "Nenhuma transação encontrada no período", 7, "Rafael", false, false, empty));

        (await _step.HandleAsync(Context("relatório do mês"), CancellationToken.None))
            .ShouldBe("📊 Não encontrei transações em outubro/2026 para o relatório.");
    }

    [Fact]
    public async Task AFailure_BecomesAGenericMessage_WithoutTheReason()
    {
        _reader.ListCardsAsync(7, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ReportCard>>(_ => throw new InvalidOperationException("senha=segredo"));

        var reply = await _step.HandleAsync(Context("fatura por categoria"), CancellationToken.None);

        reply.ShouldBe(ReportMessages.FailedGeneric);
        reply!.ShouldNotContain("segredo");
    }
}
