using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

public sealed record AccountBalance(string Name, string Type, double Balance);

/// <summary>Pote com o gasto do período do pote (soma bruta de <c>valor</c>, negativa para despesas).</summary>
public sealed record PotLine(string Name, double Limit, double RawSpent);

public sealed record InvoiceSummary(string CardName, double Value, DateOnly DueDate, string Status);

public sealed record InvoiceLine(string Description, double Value, DateOnly Date, string? ScheduleType, string? InstallmentInfo);

public sealed record InvoiceDetail(string CardName, double Total, DateOnly DueDate, string Status, IReadOnlyList<InvoiceLine> Lines);

/// <summary>Despesa do período (<c>descricao, valor, categoria, data, macro</c>).</summary>
public sealed record PeriodExpense(string Description, double Value, string Category, DateOnly Date, string Macro);

public sealed record CategoryExpense(string Description, double Value, DateOnly Date);

/// <summary>Ícone do tipo de conta, o mesmo em saldos, lista de contas e ajuste de saldo.</summary>
public static class AccountIcons
{
    public static string For(string type) => type == "Cartão de Crédito" ? "💳" : type == "Conta Corrente" ? "🏦" : "💰";
}

/// <summary><c>Consulta Saldo</c> (<c>whatsapp_handler.py:853-891</c>).</summary>
public static class BalanceMessages
{
    public const string NoAccounts = "❌ Você não tem contas cadastradas.";

    public static string NotFound(string name) => $"🤔 Não encontrei uma conta chamada '{name}'.";

    public static string Format(IReadOnlyList<AccountBalance> accounts)
    {
        if (accounts.Count == 0)
        {
            return NoAccounts;
        }

        if (accounts.Count == 1)
        {
            var account = accounts[0];
            return $"{AccountIcons.For(account.Type)} *{account.Name}* ({account.Type})\n\n💵 Saldo: *{MoneyText.Brl(account.Balance)}*";
        }

        var sb = new StringBuilder("💰 *Seus Saldos:*\n\n");
        double total = 0;
        foreach (var account in accounts)
        {
            sb.Append(AccountIcons.For(account.Type)).Append(" *").Append(account.Name).Append("*\n")
                .Append("   ").Append(MoneyText.Brl(account.Balance)).Append("\n\n");
            total += account.Balance;
        }

        return sb.Append("💵 *Total Geral:* ").Append(MoneyText.Brl(total)).ToString();
    }
}

/// <summary><c>Consulta Reserva</c> e <c>Consulta Detalhes Reserva</c> (<c>whatsapp_handler.py:758-850</c>).</summary>
public static class ReserveMessages
{
    public const string NoItems =
        "🆘 *Detalhes da Reserva de Emergência*\n\n⚠️ Nenhum agendamento está incluído no cálculo da reserva.\n\n💡 _Configure seus agendamentos fixos (água, luz, aluguel, etc.)_";

    public static string Summary(double monthly, double ideal, int months) =>
        "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
        + $"💰 Gasto mensal essencial: *{MoneyText.Brl(monthly)}*\n"
        + string.Create(CultureInfo.InvariantCulture, $"🎯 Reserva ideal ({months} meses): *{MoneyText.Brl(ideal)}*\n\n")
        + "💡 _Digite *'detalhes da reserva'* para ver quais contas estão incluídas no cálculo_";

    public static string Details(double monthly, double ideal, int months, IReadOnlyList<ReserveLine> items)
    {
        var sb = new StringBuilder("🆘 *Detalhes da Reserva de Emergência*\n\n");
        sb.Append("📊 *Resumo:*\n")
            .Append("• Gasto mensal essencial: *").Append(MoneyText.Brl(monthly)).Append("*\n")
            .Append(CultureInfo.InvariantCulture, $"• Reserva {months} meses: *{MoneyText.Brl(ideal)}*\n\n")
            .Append("📋 *Contas incluídas:*\n\n");

        foreach (var item in items)
        {
            var emoji = item.Periodicity switch
            {
                "MENSAL" => "📅",
                "ANUAL" => "🗓️",
                "SEMANAL" => "📆",
                "QUINZENAL" => "📋",
                "DIARIA" => "⏰",
                _ => "📌",
            };
            sb.Append(emoji).Append(" *").Append(item.Description).Append("*\n")
                .Append("   └ ").Append(MoneyText.Brl(item.Amount)).Append('/').Append(item.Periodicity.ToLowerInvariant());

            var impact = MoneyText.Brl(item.Impact);
            switch (item.Periodicity)
            {
                case "MENSAL":
                    sb.Append(CultureInfo.InvariantCulture, $" → {impact} (×{months})\n");
                    break;
                case "ANUAL":
                    sb.Append(" → ").Append(impact).Append(" (integral)\n");
                    break;
                case "SEMANAL":
                    sb.Append(CultureInfo.InvariantCulture, $" → {impact} (×{(int)Math.Round(months * 4.33, MidpointRounding.ToEven)})\n");
                    break;
                case "QUINZENAL":
                    sb.Append(CultureInfo.InvariantCulture, $" → {impact} (×{months * 2})\n");
                    break;
                case "DIARIA":
                    sb.Append(CultureInfo.InvariantCulture, $" → {impact} (×{months * 30})\n");
                    break;
                default:
                    sb.Append(" → ").Append(impact).Append('\n');
                    break;
            }
        }

        return sb.Append("\n━━━━━━━━━━━━━━━━━━\n")
            .Append("💰 *Total: ").Append(MoneyText.Brl(ideal)).Append("*\n\n")
            .Append("💡 _Use a aplicação web para editar quais contas incluir_")
            .ToString();
    }
}

public sealed record ReserveLine(string Description, double Amount, string Periodicity, double Impact);

/// <summary><c>Consulta Potes</c> (<c>whatsapp_handler.py:933-944</c>).</summary>
public static class PotMessages
{
    public const string None = "Você não tem potes configurados.";

    public static string Format(IReadOnlyList<PotLine> pots)
    {
        if (pots.Count == 0)
        {
            return None;
        }

        var sb = new StringBuilder("📊 *Seus Potes:*\n\n");
        foreach (var pot in pots)
        {
            var spent = pot.RawSpent * -1.0;
            var rest = pot.Limit - spent;
            sb.Append("🏷️*").Append(pot.Name).Append("*\n")
                .Append("  Gasto: ").Append(MoneyText.Brl(spent)).Append('\n')
                .Append("  Limite: ").Append(MoneyText.Brl(pot.Limit)).Append('\n')
                .Append("  Resta: ").Append(MoneyText.Brl(rest)).Append("\n\n");
        }

        return sb.ToString();
    }
}

/// <summary><c>Listar Contas</c> (<c>admin_intents.py:ListarContasIntent</c>): agrupa por tipo na ordem da primeira aparição.</summary>
public static class AccountListMessage
{
    public const string None = "❌ Você não tem contas cadastradas.";

    public static string Format(IReadOnlyList<AccountRef> accounts)
    {
        if (accounts.Count == 0)
        {
            return None;
        }

        var groups = new List<(string Type, List<string> Names)>();
        foreach (var account in accounts)
        {
            var index = groups.FindIndex(g => g.Type == account.Type);
            if (index < 0)
            {
                groups.Add((account.Type, [account.Name]));
            }
            else
            {
                groups[index].Names.Add(account.Name);
            }
        }

        var sb = new StringBuilder("📋 *Suas Contas Cadastradas:*\n\n");
        foreach (var (type, names) in groups)
        {
            sb.Append(AccountIcons.For(type)).Append(" *").Append(type).Append("*\n");
            foreach (var name in names)
            {
                sb.Append("   • ").Append(name).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.Append(CultureInfo.InvariantCulture, $"_Total: {accounts.Count} conta(s)_").ToString();
    }
}

/// <summary><c>Ajustar Saldo Inicial</c> (<c>admin_intents.py:AjustarSaldoIntent</c>).</summary>
public static partial class AdjustBalance
{
    public const string MissingValue = "🤔 Não consegui identificar o valor. Exemplo: 'ajustar saldo inicial Banco Inter 5000'";
    public const string Failed = "❌ Erro ao atualizar o saldo inicial. Tente novamente.";

    /// <summary>Tira os pontos, troca a vírgula por ponto e pega o primeiro número (<c>\d+(?:\.\d+)?</c>).</summary>
    public static double? ParseValue(string message)
    {
        var clean = message.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        var match = Number().Match(clean);
        return match.Success ? double.Parse(AsciiDigits(match.Value), NumberStyles.Float, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>A primeira conta cujo nome (sem diferenciar caixa) está contido na mensagem.</summary>
    public static AccountRef? FindAccount(string message, IReadOnlyList<AccountRef> accounts)
    {
        var lower = message.ToLowerInvariant();
        return accounts.FirstOrDefault(a => lower.Contains(a.Name.ToLowerInvariant(), StringComparison.Ordinal));
    }

    public static string NoAccountFound(IReadOnlyList<AccountRef> accounts)
    {
        var sb = new StringBuilder("🤔 Não consegui identificar qual conta. Contas disponíveis:\n\n");
        foreach (var account in accounts)
        {
            sb.Append("• ").Append(account.Name).Append('\n');
        }

        return sb.Append("\nTente: 'ajustar saldo inicial [nome da conta] [valor]'").ToString();
    }

    public static string Success(AccountRef account, double value) =>
        "✅ *SALDO INICIAL ATUALIZADO* ✅\n\n"
        + $"{AccountIcons.For(account.Type)} *{account.Name}*\n"
        + $"💵 Novo saldo inicial: *{MoneyText.Brl(value)}*\n\n"
        + "_O saldo atual já reflete esta mudança._";

    // \d do Python (str) casa dígitos Unicode; o valor é convertido pelos dígitos ASCII equivalentes.
    private static string AsciiDigits(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(char.IsDigit(ch) ? (char)('0' + (int)char.GetNumericValue(ch)) : ch);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)")]
    private static partial Regex Number();
}

/// <summary><c>Consulta Valor Fatura</c> (<c>admin_intents.py:ConsultaFaturaIntent</c>).</summary>
public static class InvoiceMessages
{
    private static readonly string[] DetailWords = ["detalhe", "detalhar", "detalhada", "itens", "lista", "transações", "transacoes", "compras"];

    public static bool WantsDetails(string message)
    {
        var lower = message.ToLowerInvariant();
        return DetailWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    public static string NotFound(string name) => $"🤔 Não encontrei um cartão chamado '{name}'.";

    public static string NoInvoice(string? cardName) =>
        cardName is { Length: > 0 }
            ? $"✅ Você não tem faturas em aberto no cartão '{cardName}'! 🎉"
            : "✅ Você não tem nenhuma fatura em aberto! 🎉";

    public static string Summary(IReadOnlyList<InvoiceSummary> invoices, string? cardName)
    {
        if (invoices.Count == 0)
        {
            return NoInvoice(cardName);
        }

        if (invoices.Count == 1)
        {
            var invoice = invoices[0];
            return $"💳 *Fatura {invoice.CardName}*\n\n"
                   + $"💰 Valor atual: *{MoneyText.Brl(invoice.Value)}*\n"
                   + $"📅 Vencimento: {Date(invoice.DueDate)}\n"
                   + $"📊 Status: {invoice.Status}";
        }

        var sb = new StringBuilder("💳 *Suas Faturas em Aberto:*\n\n");
        double total = 0;
        foreach (var invoice in invoices)
        {
            sb.Append("🔹 *").Append(invoice.CardName).Append("*\n")
                .Append("   💰 ").Append(MoneyText.Brl(invoice.Value)).Append(" (Venc: ").Append(DayMonth(invoice.DueDate)).Append(")\n\n");
            total += invoice.Value;
        }

        return sb.Append("💵 *Total Geral:* ").Append(MoneyText.Brl(total)).ToString();
    }

    public static string Detailed(InvoiceDetail? invoice, string? cardName)
    {
        if (invoice is null)
        {
            return NoInvoice(cardName);
        }

        var sb = new StringBuilder();
        sb.Append("💳 *Fatura ").Append(invoice.CardName).Append("*\n\n")
            .Append("💰 Valor atual: *").Append(MoneyText.Brl(invoice.Total)).Append("*\n")
            .Append("📅 Vencimento: ").Append(Date(invoice.DueDate)).Append('\n')
            .Append("📊 Status: ").Append(invoice.Status).Append("\n\n");

        if (invoice.Lines.Count == 0)
        {
            return sb.Append("✅ Nenhuma transação registrada nesta fatura.").ToString();
        }

        sb.Append("📅 *Transações por Data:*\n\n");
        foreach (var day in invoice.Lines.Select(l => l.Date).Distinct().OrderDescending())
        {
            var ofDay = invoice.Lines.Where(l => l.Date == day).ToList();
            double dayTotal = 0;
            foreach (var line in ofDay)
            {
                dayTotal += line.Value;
            }

            sb.Append('*').Append(Date(day)).Append("* - ").Append(MoneyText.Brl(dayTotal)).Append('\n');
            foreach (var line in ofDay)
            {
                var emoji = line.ScheduleType switch
                {
                    "FIXO" => "🔁 ",
                    "PARCELADO" => "📊 ",
                    _ => string.Empty,
                };
                var extra = string.IsNullOrEmpty(line.InstallmentInfo) ? string.Empty : $" ({line.InstallmentInfo})";
                sb.Append("  ").Append(emoji).Append(line.Description).Append(extra).Append(": ").Append(MoneyText.Brl(line.Value)).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string DayMonth(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);
}

/// <summary><c>Consulta Categoria Específica</c> (<c>analytics_intents.py:ConsultaCategoriaIntent</c>).</summary>
public static class CategorySpendMessage
{
    public const string Missing = "❌ Não consegui identificar a categoria. Qual categoria quer consultar?";

    public static string Format(string category, double spent) =>
        $"ℹ️ *Consulta de Categoria (Este Mês)*\n\nVocê gastou *{MoneyText.Brl(spent)}* com '{category}'.";
}

/// <summary><c>PeriodQueryService</c> (<c>period_query_service.py</c>): datas, descrições e textos das consultas por período.</summary>
public static class PeriodQuery
{
    private static readonly string[] Months =
        ["", "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    /// <summary><c>get_period_dates</c>: tipo desconhecido (ou ausente) vale <c>hoje</c>, com a descrição curta "hoje".</summary>
    public static (DateOnly Start, DateOnly End, string Description) Range(string? periodType, DateOnly today)
    {
        switch (periodType)
        {
            case "ontem":
                var yesterday = today.AddDays(-1);
                return (yesterday, yesterday, $"ontem ({Full(yesterday)})");
            case "hoje":
                return (today, today, $"hoje ({Full(today)})");
            case "final_de_semana":
                var sinceSunday = ((((int)today.DayOfWeek + 6) % 7) + 1) % 7;
                DateOnly sunday;
                DateOnly saturday;
                if (sinceSunday == 0)
                {
                    sunday = today;
                    saturday = today.AddDays(-1);
                }
                else if (sinceSunday == 1)
                {
                    sunday = today.AddDays(-1);
                    saturday = today.AddDays(-2);
                }
                else
                {
                    sunday = today.AddDays(-sinceSunday);
                    saturday = sunday.AddDays(-1);
                }

                return (saturday, sunday, $"no final de semana ({Short(saturday)} e {Short(sunday)})");
            case "esta_semana":
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return (monday, today, $"esta semana (desde {Short(monday)})");
            case "semana_passada":
                var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                var lastSunday = thisMonday.AddDays(-1);
                var lastMonday = lastSunday.AddDays(-6);
                return (lastMonday, lastSunday, $"na semana passada ({Short(lastMonday)} a {Short(lastSunday)})");
            case "ultimos_7_dias":
                var from = today.AddDays(-6);
                return (from, today, $"nos últimos 7 dias (desde {Short(from)})");
            case "este_mes":
                return (new DateOnly(today.Year, today.Month, 1), today, $"este mês ({MonthYear(today)})");
            case "mes_passado":
                var lastOfPrevious = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
                return (new DateOnly(lastOfPrevious.Year, lastOfPrevious.Month, 1), lastOfPrevious, $"no mês passado ({MonthYear(lastOfPrevious)})");
            default:
                return (today, today, "hoje");
        }
    }

    /// <summary><c>format_period_query_response</c>: agrupa por "macro → categoria", maiores subtotais primeiro (ordem estável).</summary>
    public static string General(IReadOnlyList<PeriodExpense> expenses, string description)
    {
        double total = 0;
        foreach (var expense in expenses)
        {
            total += Math.Abs(expense.Value);
        }

        if (total == 0 || expenses.Count == 0)
        {
            return $"✅ Você não teve gastos {description}! 🎉";
        }

        var groups = new List<(string Name, List<(string Description, double Value, DateOnly Date)> Items)>();
        foreach (var expense in expenses)
        {
            var name = $"{expense.Macro} → {expense.Category}";
            var index = groups.FindIndex(g => g.Name == name);
            if (index < 0)
            {
                groups.Add((name, []));
                index = groups.Count - 1;
            }

            groups[index].Items.Add((expense.Description, Math.Abs(expense.Value), expense.Date));
        }

        var sb = new StringBuilder();
        sb.Append("💸 *GASTOS ").Append(description.ToUpperInvariant()).Append("* 💸\n\n")
            .Append("💰 *Total: ").Append(MoneyText.Brl(total)).Append("*\n\n")
            .Append("━━━━━━━━━━━━━━━━━━━━\n")
            .Append("📋 *Detalhamento:*\n\n");

        foreach (var (name, items) in groups.Select(g => (g.Name, g.Items, Sum: Sum(g.Items))).OrderByDescending(g => g.Sum).Select(g => (g.Name, g.Items)))
        {
            sb.Append("🏷️ *").Append(name).Append("*: ").Append(MoneyText.Brl(Sum(items))).Append('\n');
            foreach (var item in items.Take(5))
            {
                sb.Append("   • ").Append(item.Description).Append(": ").Append(MoneyText.Brl(item.Value)).Append(" (").Append(Short(item.Date)).Append(")\n");
            }

            if (items.Count > 5)
            {
                sb.Append(CultureInfo.InvariantCulture, $"   ... e mais {items.Count - 5} transação(ões)\n");
            }

            sb.Append('\n');
        }

        return sb.ToString().Trim();
    }

    /// <summary>Consulta por período com filtro de categoria (<c>ConsultaPeriodoIntent.format_response</c>).</summary>
    public static string ForCategory(string category, double total, IReadOnlyList<CategoryExpense> expenses, string description)
    {
        if (total == 0)
        {
            return $"✅ Você não gastou nada com '{category}' {description}! 🎉";
        }

        var sb = new StringBuilder();
        sb.Append("💸 *GASTOS COM ").Append(category.ToUpperInvariant()).Append("* ").Append(description.ToUpperInvariant()).Append("\n\n")
            .Append("💰 Total: *").Append(MoneyText.Brl(total)).Append("*\n\n")
            .Append("📋 Transações:\n");
        foreach (var expense in expenses.Take(10))
        {
            sb.Append("• ").Append(expense.Description).Append(": ").Append(MoneyText.Brl(Math.Abs(expense.Value))).Append(" (").Append(Short(expense.Date)).Append(")\n");
        }

        if (expenses.Count > 10)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n... e mais {expenses.Count - 10} transação(ões)");
        }

        return sb.ToString();
    }

    public static double Total(IEnumerable<double> values)
    {
        double total = 0;
        foreach (var value in values)
        {
            total += Math.Abs(value);
        }

        return total;
    }

    private static double Sum(List<(string Description, double Value, DateOnly Date)> items)
    {
        double sum = 0;
        foreach (var item in items)
        {
            sum += item.Value;
        }

        return sum;
    }

    private static string MonthYear(DateOnly date) => $"{Months[date.Month]}/{date.Year.ToString(CultureInfo.InvariantCulture)}";

    private static string Full(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Short(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);
}
