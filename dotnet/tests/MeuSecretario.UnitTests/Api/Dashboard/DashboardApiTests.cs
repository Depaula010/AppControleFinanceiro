using MeuSecretario.Api.Dashboard;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Domain.Dashboard;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Api.Dashboard;

public class DashboardApiTests
{
    [Fact]
    [Trait("AC", "006-05")]
    public void FixedTexts_AreTheExactFlaskOnes()
    {
        DashboardMessages.MonthsOutOfRange.ShouldBe("Parâmetro 'meses' deve estar entre 1 e 12");
        DashboardMessages.SummaryFailed.ShouldBe("Erro ao carregar resumo do dashboard");
        DashboardMessages.ChartsFailed.ShouldBe("Erro ao carregar dados dos gráficos");
        DashboardMessages.RecentFailed.ShouldBe("Erro ao carregar transações recentes");
        DashboardMessages.AlertsFailed.ShouldBe("Erro ao buscar alertas");
    }

    [Fact]
    [Trait("AC", "006-19")]
    public async Task Summary_HasFiveKeysInOrder()
    {
        var (status, json) = await ResultRunner.Run(
            DashboardResponses.Summary(new DashboardSummary(5430.5, 8000, 3245.3, 4754.7, "Outubro/2026")));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","data":{"saldo_total":5430.5,"receitas_mes":8000,"despesas_mes":3245.3,"saldo_mes":4754.7,"mes_referencia":"Outubro/2026"}}""");
    }

    [Fact]
    public async Task Charts_HasThreeBlocksWithTheFlaskKeys()
    {
        var charts = new DashboardCharts(
            [new MonthlyChartItem("2026-10", 3200.5)],
            [new CategoryChartItem("Alimentação", "Supermercado", 1200, 45)],
            [new WeekdayChartItem("Monday", 450, 12)]);

        var (_, json) = await ResultRunner.Run(DashboardResponses.Charts(charts));

        json.ShouldBe(
            """{"status":"success","data":{"gastos_mensais":[{"mes":"2026-10","total":3200.5}],"gastos_categoria":[{"macro_categoria":"Alimentação","subcategoria":"Supermercado","total":1200,"quantidade":45}],"gastos_dia_semana":[{"dia":"Monday","total":450,"quantidade":12}]}}""");
    }

    [Fact]
    [Trait("AC", "006-42")]
    public async Task Recent_KeepsTheDatabaseType_AndTheSign()
    {
        IReadOnlyList<RecentTransaction> rows =
        [
            new(330, "Mercado", -85.9m, "Despesa", new DateOnly(2026, 10, 3), "Alimentação", "Mercado pago"),
            new(331, "Salário", 100m, "Renda", new DateOnly(2026, 10, 2), "Renda", "Carteira"),
        ];

        var (_, json) = await ResultRunner.Run(DashboardResponses.Recent(rows));

        json.ShouldBe(
            """{"status":"success","data":[{"id":330,"descricao":"Mercado","valor":-85.9,"tipo":"Despesa","data":"2026-10-03","categoria":"Alimentação","conta":"Mercado pago"},{"id":331,"descricao":"Salário","valor":100,"tipo":"Renda","data":"2026-10-02","categoria":"Renda","conta":"Carteira"}]}""");
    }

    [Fact]
    [Trait("AC", "006-59")]
    public async Task Alerts_HaveStringIdAndNineKeysInOrder()
    {
        IReadOnlyList<DashboardAlert> alerts =
        [
            new("42", "Aluguel", null, null, 5, 2, new DateOnly(2026, 10, 6), "warning", "FIXO"),
        ];

        var (_, json) = await ResultRunner.Run(DashboardResponses.Alerts(alerts));

        json.ShouldBe(
            """{"status":"success","data":[{"id":"42","descricao":"Aluguel","subcategoria":null,"valor_previsto":null,"dia_execucao":5,"dias_restantes":2,"data_vencimento":"2026-10-06","tipo":"warning","tipo_agendamento":"FIXO"}]}""");
    }

    [Fact]
    public async Task EmptyLists_StillSendData()
    {
        (await ResultRunner.Run(DashboardResponses.Recent([]))).Json.ShouldBe("""{"status":"success","data":[]}""");
        (await ResultRunner.Run(DashboardResponses.Alerts([]))).Json.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    public async Task Error_IsEnvelopeA()
    {
        var (status, json) = await ResultRunner.Run(DashboardResponses.Error(DashboardMessages.AlertsFailed, 500));

        status.ShouldBe(500);
        json.ShouldBe("""{"status":"error","message":"Erro ao buscar alertas"}""");
    }
}
