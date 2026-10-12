using System.Globalization;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Common;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp.Analytics;

/// <summary>Total do mês (<c>TO_CHAR(data_transacao, 'YYYY-MM')</c>) como o Flask o lê: despesa guardada com sinal negativo.</summary>
public sealed record MonthTotal(string Month, double Total);

public sealed record CategorySpend(string Category, string Subcategory, double Total, int Quantity);

/// <summary>Dia da semana em inglês, como o <c>TO_CHAR(..., 'Day')</c> do Postgres entrega (sem espaços de preenchimento).</summary>
public sealed record WeekdaySpend(string Day, double Total, int Quantity);

public sealed record PotUsage(string Name, double Limit, double Spent)
{
    /// <summary><c>round(gasto / limite * 100, 1)</c> se o limite é positivo; senão o inteiro <c>0</c> (texto <c>0</c>, não <c>0.0</c>).</summary>
    public string UsageText => Limit > 0 ? PyFloat.Repr(PyFloat.Round(Spent / Limit * 100, 1)) : "0";
}

public sealed record TopExpense(string Description, double Value, DateOnly Date);

public sealed record FixedCharge(string Description, double Value, string Periodicity);

/// <summary>O dicionário de <c>get_spending_analysis</c> que entra no prompt (as chaves sem uso no Flask não são lidas).</summary>
public sealed record SpendingAnalysis(
    string CurrentMonth,
    IReadOnlyList<MonthTotal> Comparison,
    IReadOnlyList<CategorySpend> Categories,
    IReadOnlyList<WeekdaySpend> Weekdays,
    IReadOnlyList<PotUsage> Pots,
    IReadOnlyList<TopExpense> TopExpenses,
    IReadOnlyList<FixedCharge> FixedCharges);

/// <summary>
/// Texto de <c>Análise Inteligente</c> (<c>analytics_service.generate_ai_insights</c>). Desvio 158 (implementado): o prompt usa valores
/// absolutos de despesa e <c>formatar_moeda</c> (<c>R$ 1.234,50</c>); o Flask mandava <c>R$ -1,234.50</c> ao Gemini.
/// </summary>
public static class AnalyticsReport
{
    public const string Header = "📊 *Análise Inteligente de Gastos*\n\n";

    public const string Footer = "\n\n💬 _Para análises mais específicas, pergunte:_\n• _\"Quanto gastei com [categoria]?\"_\n• _\"Comparar gastos de [mês]\"_";

    /// <summary><c>f"{x:,.2f}"</c>. O zero negativo (<c>-0.0</c>) mantém o sinal, como no Python.</summary>
    public static string Money(double value) => value == 0 && double.IsNegative(value) ? "-" + PyNumber.Thousands(0.0) : PyNumber.Thousands(value);

    /// <summary><c>f"{x:+.1f}"</c>. O zero negativo (<c>-0.0</c>) sai com o sinal de menos, como no Python.</summary>
    public static string WithSign(double value) => value == 0 && double.IsNegative(value) ? "-0.0" : PyNumber.WithSign(value);

    /// <summary>Valor para o prompt (desvio 158): módulo no formato de <c>formatar_moeda</c>, sem o prefixo (<c>1.234,50</c>).</summary>
    public static string PromptMoney(double value) => MoneyText.Brl(Math.Abs(value))["R$ ".Length..];

    public static string Prompt(SpendingAnalysis data)
    {
        // dict.get(mes_atual, 0) e o primeiro valor das outras chaves (no máximo uma: o SQL só traz o mês atual e o anterior).
        var current = data.Comparison.Where(m => m.Month == data.CurrentMonth).Select(m => m.Total).DefaultIfEmpty(0.0).Last();
        var previous = data.Comparison.Where(m => m.Month != data.CurrentMonth).Select(m => m.Total).FirstOrDefault();

        // Desvio 158: a variação é entre gastos (módulos); as despesas chegam negativas do banco.
        var variation = previous != 0 ? PyFloat.Round((Math.Abs(current) - Math.Abs(previous)) / Math.Abs(previous) * 100, 1) : 0.0;
        static string Money(double value) => PromptMoney(value);

        var categories = string.Join('\n', data.Categories.Take(5).Select(c =>
            $"- {c.Category} / {c.Subcategory}: R$ {Money(c.Total)} ({c.Quantity.ToString(CultureInfo.InvariantCulture)} transações)"));
        var weekdays = string.Join('\n', data.Weekdays.Select(d =>
            $"- {WeekdayNames.ToPortuguese(d.Day)}: R$ {Money(d.Total)} ({d.Quantity.ToString(CultureInfo.InvariantCulture)} transações)"));
        var pots = data.Pots.Count > 0
            ? string.Join('\n', data.Pots.Select(p => $"- {p.Name}: R$ {Money(p.Spent)} / R$ {Money(p.Limit)} ({p.UsageText}%)"))
            : "- Nenhum pote configurado";
        var top = string.Join('\n', data.TopExpenses.Select(e =>
            $"- {e.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} - {e.Description}: R$ {Money(e.Value)}"));
        var fixedCharges = data.FixedCharges.Count > 0
            ? string.Join('\n', data.FixedCharges.Take(5).Select(c => $"- {c.Description}: R$ {Money(c.Value)} ({c.Periodicity})"))
            : "- Nenhuma conta fixa cadastrada";

        var text = $"""

            Você é um assistente financeiro pessoal. Analise os dados abaixo e gere insights personalizados e acionáveis.

            *DADOS DO USUÁRIO:*

            📊 *Resumo Mensal:*
            - Total gasto este mês: R$ {Money(current)}
            - Mês anterior: R$ {Money(previous)}
            - Variação: {WithSign(variation)}%

            💰 *Gastos por Categoria (Top 5):*
            {categories}

            📅 *Padrão Semanal:*
            {weekdays}

            🎯 *Status dos Potes:*
            {pots}

            💳 *Maiores Gastos:*
            {top}

            🔧 *Contas Fixas:*
            {fixedCharges}

            ---

            *INSTRUÇÕES:*

            Gere um relatório com as seguintes seções (use emojis e formatação clara):

            1. 📊 Resumo do Mês: Resumo geral dos gastos (2-3 linhas)
            2. 🔍 Principais Insights (3-5 pontos):
               - Identifique padrões importantes
               - Compare com meses anteriores
               - Destaque categorias que chamam atenção
               - Analise dias da semana com mais gastos
            3. ⚠️ Alertas (se aplicável):
               - Potes próximos ou acima do limite
               - Categorias com aumento significativo
               - Gastos atípicos
            4. 💡 Sugestões de Economia (2-4 sugestões práticas e acionáveis):
               - Baseie-se nos dados reais
               - Seja específico com valores
               - Priorize as oportunidades mais relevantes

            *FORMATO DE RESPOSTA:*
            - Seja direto e objetivo
            - Use valores reais dos dados
            - Evite frases genéricas
            - Foque em insights acionáveis
            - Máximo de 15 linhas no total
            - Use asteriscos simples * para negrito (ex: *texto*), NÃO use asteriscos duplos **

            """;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
