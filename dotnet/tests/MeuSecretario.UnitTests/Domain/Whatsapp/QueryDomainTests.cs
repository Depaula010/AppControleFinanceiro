using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

public class QueryDomainTests
{
    [Theory]
    [InlineData("hoje", "2026-10-07", "2026-10-07", "hoje (07/10/2026)")]
    [InlineData("ontem", "2026-10-06", "2026-10-06", "ontem (06/10/2026)")]
    [InlineData("final_de_semana", "2026-10-03", "2026-10-04", "no final de semana (03/10 e 04/10)")]
    [InlineData("esta_semana", "2026-10-05", "2026-10-07", "esta semana (desde 05/10)")]
    [InlineData("semana_passada", "2026-09-28", "2026-10-04", "na semana passada (28/09 a 04/10)")]
    [InlineData("ultimos_7_dias", "2026-10-01", "2026-10-07", "nos últimos 7 dias (desde 01/10)")]
    [InlineData("este_mes", "2026-10-01", "2026-10-07", "este mês (Outubro/2026)")]
    [InlineData("mes_passado", "2026-09-01", "2026-09-30", "no mês passado (Setembro/2026)")]
    [InlineData("qualquer", "2026-10-07", "2026-10-07", "hoje")]
    [InlineData(null, "2026-10-07", "2026-10-07", "hoje")]
    [Trait("AC", "012-307")]
    public void Range_MatchesThePeriodQueryService(string? type, string start, string end, string description)
    {
        var (from, to, text) = PeriodQuery.Range(type, new DateOnly(2026, 10, 7));
        (from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), text)
            .ShouldBe((start, end, description));
    }

    [Theory]
    [InlineData("2026-10-04", "final_de_semana", "2026-10-03", "2026-10-04")] // domingo
    [InlineData("2026-10-05", "final_de_semana", "2026-10-03", "2026-10-04")] // segunda
    [InlineData("2026-10-10", "final_de_semana", "2026-10-03", "2026-10-04")] // sábado
    public void Weekend_IsTheLastSaturdayAndSunday(string today, string type, string saturday, string sunday)
    {
        var (from, to, _) = PeriodQuery.Range(type, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));
        from.ShouldBe(DateOnly.Parse(saturday, System.Globalization.CultureInfo.InvariantCulture));
        to.ShouldBe(DateOnly.Parse(sunday, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("ajustar saldo nubank 1.500,50", 1500.5)]
    [InlineData("saldo 12.50", 1250d)]
    [InlineData("saldo 0,5", 0.5)]
    [InlineData("saldo 100", 100d)]
    [Trait("AC", "012-305")]
    public void AdjustBalance_ParsesTheValueLikeThePython(string message, double expected) =>
        AdjustBalance.ParseValue(message).ShouldBe(expected);

    [Fact]
    public void AdjustBalance_NoNumber_IsNull() => AdjustBalance.ParseValue("ajustar saldo nubank").ShouldBeNull();

    [Fact]
    [Trait("AC", "012-304")]
    public void AccountList_GroupsByTypeInFirstAppearanceOrder()
    {
        AccountListMessage.Format([new AccountRef(1, "A", "Dinheiro"), new AccountRef(2, "B", "Conta Corrente"), new AccountRef(3, "C", "Dinheiro")])
            .ShouldBe("📋 *Suas Contas Cadastradas:*\n\n💰 *Dinheiro*\n   • A\n   • C\n\n🏦 *Conta Corrente*\n   • B\n\n_Total: 3 conta(s)_");
    }

    [Fact]
    public void InvoiceMessages_DetectsDetailWords()
    {
        InvoiceMessages.WantsDetails("DETALHAR a fatura").ShouldBeTrue();
        InvoiceMessages.WantsDetails("quanto está a fatura").ShouldBeFalse();
    }
}
