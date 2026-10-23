using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Pedidos de relatório por palavra-chave (desvio 256): o que é relatório, e o que o Gemini continua tratando.</summary>
public class ReportCommandParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Theory]
    [InlineData("fatura do inter por categoria", ReportKind.CardInvoice, ReportPeriod.Default, 0, 0)]
    [InlineData("me mostra a fatura detalhada", ReportKind.CardInvoice, ReportPeriod.Default, 0, 0)]
    [InlineData("Detalhes da fatura de Setembro", ReportKind.CardInvoice, ReportPeriod.Specific, 2026, 9)]
    [InlineData("fatura passada por categoria", ReportKind.CardInvoice, ReportPeriod.Previous, 0, 0)]
    [InlineData("fatura de novembro por categoria", ReportKind.CardInvoice, ReportPeriod.Specific, 2026, 11)]
    [InlineData("fatura de março por categoria", ReportKind.CardInvoice, ReportPeriod.Specific, 2026, 3)]
    [InlineData("gastos do mês por categoria", ReportKind.MonthByCategory, ReportPeriod.Default, 0, 0)]
    [InlineData("gastos por categoria do mês passado", ReportKind.MonthByCategory, ReportPeriod.Previous, 0, 0)]
    [InlineData("quanto gastei por categoria em setembro", ReportKind.MonthByCategory, ReportPeriod.Specific, 2026, 9)]
    [InlineData("despesas por categoria de dezembro", ReportKind.MonthByCategory, ReportPeriod.Specific, 2025, 12)]
    [InlineData("onde gastei mais este mês", ReportKind.MonthByCategory, ReportPeriod.Default, 0, 0)]
    [InlineData("relatório do mês", ReportKind.MonthlyReport, ReportPeriod.Default, 0, 0)]
    [InlineData("me manda o relatório", ReportKind.MonthlyReport, ReportPeriod.Default, 0, 0)]
    [InlineData("relatório do mês passado", ReportKind.MonthlyReport, ReportPeriod.Previous, 0, 0)]
    [InlineData("relatório de agosto", ReportKind.MonthlyReport, ReportPeriod.Specific, 2026, 8)]
    [InlineData("gera o relatório do mês", ReportKind.MonthlyReport, ReportPeriod.Default, 0, 0)]
    [InlineData("envia o relatório mensal", ReportKind.MonthlyReport, ReportPeriod.Default, 0, 0)]
    [InlineData("relatório", ReportKind.MonthlyReport, ReportPeriod.Default, 0, 0)]
    public void Recognizes_TheThreeReports(string message, ReportKind kind, ReportPeriod period, int year, int month)
    {
        var request = ReportCommandParser.Parse(message, Today);

        request.ShouldNotBeNull();
        (request.Kind, request.Period, request.Year, request.Month).ShouldBe((kind, period, year, month));
    }

    [Theory]
    [InlineData("qual valor da fatura?")]
    [InlineData("paguei 500 da fatura do nubank")]
    [InlineData("pagamento da fatura detalhada")]
    [InlineData("configurar relatório mensal")]
    [InlineData("como está meu relatório?")]
    [InlineData("consultar relatório mensal")]
    [InlineData("quero um relatório financeiro")]
    [InlineData("quero receber relatório todo dia 1")]
    [InlineData("ativar relatório mensal às 10h")]
    [InlineData("meu relatório mensal está configurado?")]
    [InlineData("ativar relatório mensal")]
    [InlineData("quanto gastei hoje?")]
    [InlineData("quanto gastei com comida?")]
    [InlineData("gastei 50 em comida")]
    [InlineData("analisar meus gastos")]
    [InlineData("gráfico de gastos")]
    [InlineData("minha agenda amanhã")]
    [InlineData("")]
    public void LeavesEverythingElseToTheOtherSteps(string message) =>
        ReportCommandParser.Parse(message, Today).ShouldBeNull();

    [Fact]
    public void MonthStart_FollowsThePeriod()
    {
        var previous = ReportCommandParser.Parse("relatório do mês passado", Today)!;
        var specific = ReportCommandParser.Parse("gastos por categoria em setembro", Today)!;
        var current = ReportCommandParser.Parse("relatório do mês", Today)!;

        previous.MonthStart(Today).ShouldBe(new DateOnly(2026, 9, 1));
        specific.MonthStart(Today).ShouldBe(new DateOnly(2026, 9, 1));
        current.MonthStart(Today).ShouldBe(new DateOnly(2026, 10, 1));
        previous.MonthStart(new DateOnly(2026, 1, 15)).ShouldBe(new DateOnly(2025, 12, 1));
    }

    [Fact]
    public void Normalize_RemovesAccentsAndCase() =>
        ReportCommandParser.Normalize("  Março   FATURA do Cartão ").ShouldBe("marco fatura do cartao");
}
