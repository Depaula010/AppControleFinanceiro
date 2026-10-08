using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Resumo matinal e check-in noturno (<c>whatsapp_handler.py:1192-1370</c> e <c>notification_config_service.py</c>).</summary>
public static partial class NotificationCommand
{
    private static readonly string[] MorningWords = ["resumo", "matinal", "briefing", "preparação do dia"];
    private static readonly string[] CheckinWords = ["check-in", "checkin", "check in", "noturno", "contas pendentes"];
    private static readonly string[] EnableWords = ["ativar", "ligar", "ativa", "ative"];
    private static readonly string[] MorningDisableWords = ["desativar", "desligar", "desative"];
    private static readonly string[] CheckinDisableWords = ["desativar", "desligar", "desative", "off"];

    public const string NoChange = "Nenhuma alteração fornecida";
    public const string CheckinRange = "Horário deve estar entre 18:00 e 23:00";
    public const string UpdateFailed = "Erro ao atualizar configuração.";
    public const string UnknownAction = "Ação não reconhecida";
    public const string UnknownType = "🤔 Não entendi qual tipo de notificação você quer configurar.";

    public static bool IsMorningSummary(string lower) => MorningWords.Any(word => lower.Contains(word, StringComparison.Ordinal));

    public static bool IsNightCheckin(string lower) => CheckinWords.Any(word => lower.Contains(word, StringComparison.Ordinal));

    /// <summary>
    /// <c>ativar</c> vence <c>desativar</c>: o Flask testa "ativar/ligar/ativa/ative" por <c>in</c> primeiro e essas palavras estão dentro de
    /// "desativar", "desligar" e "desative" (desvio 190, proposto). Só <c>off</c> (check-in) chega ao ramo de desativar.
    /// </summary>
    public static string Action(string lower, bool checkin)
    {
        if (EnableWords.Any(word => lower.Contains(word, StringComparison.Ordinal)))
        {
            return "ativar";
        }

        var disable = checkin ? CheckinDisableWords : MorningDisableWords;
        return disable.Any(word => lower.Contains(word, StringComparison.Ordinal)) ? "desativar" : "configurar";
    }

    /// <summary><c>re.search(r'(\d{1,2})[h:](\d{2})?', texto)</c> e <c>f"{h:02d}:{m:02d}"</c>; <c>null</c> sem hora.</summary>
    public static string? ExtractTime(string text)
    {
        var match = TimePattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var hour = Digits(match.Groups[1].Value);
        var minute = match.Groups[2].Success ? Digits(match.Groups[2].Value) : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{hour:00}:{minute:00}");
    }

    /// <summary>Texto de sucesso do serviço: "Resumo matinal ativado e horário configurado para 08:00".</summary>
    public static string UpdateMessage(string subject, bool? active, TimeOnly? time)
    {
        var parts = new List<string>();
        if (active is { } flag)
        {
            parts.Add($"{subject} {(flag ? "ativado" : "desativado")}");
        }

        if (time is { } value)
        {
            parts.Add("horário configurado para " + value.ToString("HH:mm", CultureInfo.InvariantCulture));
        }

        return parts.Count > 0 ? string.Join(" e ", parts) : "Configuração atualizada";
    }

    public static string MorningStatus(string message, bool active, TimeOnly time) =>
        $"✅ {message}\n\n" +
        "📱 *Resumo Matinal - Status atual:*\n" +
        $"• Ativo: {(active ? "Sim" : "Não")}\n" +
        $"• Horário: {time.ToString("HH:mm", CultureInfo.InvariantCulture)}\n\n" +
        "💡 Configure sua localização para receber informações de clima:\n" +
        "\"Configurar localização: [Cidade], [Estado]\"";

    public static string CheckinStatus(string message, bool active, TimeOnly time) =>
        $"✅ {message}\n\n" +
        "🌙 *Check-in Noturno - Status atual:*\n" +
        $"• Ativo: {(active ? "Sim" : "Não")}\n" +
        $"• Horário: {time.ToString("HH:mm", CultureInfo.InvariantCulture)}\n\n" +
        "💡 O check-in envia uma lista de contas pendentes dos últimos 7 dias.\n" +
        "Você pode confirmar todas de uma vez ou parcialmente.\n\n" +
        "⏰ Horário permitido: 18:00 às 23:00";

    public static string Failure(string message) => "❌ " + message;

    private static int Digits(string text)
    {
        var value = 0;
        foreach (var ch in text)
        {
            value = (value * 10) + (int)char.GetNumericValue(ch);
        }

        return value;
    }

    [GeneratedRegex(@"(\d{1,2})[h:](\d{2})?")]
    private static partial Regex TimePattern();
}

/// <summary>Localização do usuário (<c>location_service.py</c>) e a intenção <c>Configurar Localização</c>.</summary>
public static class LocationMessages
{
    public const string MissingCity =
        "❌ Não consegui identificar a cidade.\n\nPor favor, envie no formato:\n\"Configurar localização: São Paulo, SP\"";

    public const string InvalidCity = "Cidade inválida";
    public const string InvalidState = "Estado deve ter 2 letras (ex: SP, RJ, MG)";

    /// <summary><c>Erro ao salvar localização: {str(e)}</c> sem o motivo (desvio 008).</summary>
    public const string SaveFailed = "Erro ao salvar localização.";

    public static string Saved(string city, string? state) =>
        "📍 Localização configurada: " + city + (string.IsNullOrEmpty(state) ? string.Empty : ", " + state);

    public static string Reply(bool success, string message) =>
        success ? $"✅ {message}\n\nAgora você receberá informações de clima nos resumos matinais!" : $"❌ {message}";
}

/// <summary>Relatório mensal (<c>monthly_report_config_service.py</c> e <c>ConfigurarRelatorioMensalIntent</c>).</summary>
public static class ReportConfigMessages
{
    public static readonly string[] ValidMoments = ["INICIO_MES", "FIM_MES"];

    public static string Consult(bool active, string moment, TimeOnly time) =>
        "📊 *CONFIGURAÇÃO DO RELATÓRIO MENSAL*\n\n" +
        $"Status: {(active ? "✅ Ativo" : "❌ Desativado")}\n" +
        $"Momento: {(moment == "INICIO_MES" ? "Início do mês (dia 1)" : "Fim do mês (último dia)")}\n" +
        $"Horário: {time.ToString("HH:mm", CultureInfo.InvariantCulture)}\n\n" +
        "_Para alterar, envie: 'configurar relatório mensal no início do mês às 10h'_";

    public const string Disabled = "✅ Relatório mensal desativado com sucesso!\n\n_Para reativar, envie: 'ativar relatório mensal'_";

    public static string Enabled(string moment, TimeOnly time) =>
        "✅ *Relatório mensal ativado!*\n\n" +
        $"📅 Momento: {MomentText(moment)}\n" +
        $"🕐 Horário: {time.ToString("HH:mm", CultureInfo.InvariantCulture)}\n\n" +
        "📊 *O que você vai receber:*\n" +
        "• Gastos totais do mês\n" +
        "• Top 5 categorias\n" +
        "• Comparação com mês anterior\n" +
        "• Status dos potes de gastos\n" +
        "• Contas pagas vs pendentes\n" +
        "• Gráfico de pizza com categorias\n\n" +
        "_Você receberá automaticamente no horário configurado!_";

    public static string Updated(string moment, TimeOnly time) =>
        "✅ *Configuração atualizada!*\n\n" +
        $"📅 Momento: {MomentText(moment)}\n" +
        $"🕐 Horário: {time.ToString("HH:mm", CultureInfo.InvariantCulture)}\n\n" +
        "O relatório será enviado automaticamente no horário configurado.";

    public const string NothingToConfigure =
        "❌ Não entendi o que você quer configurar.\n\n" +
        "Exemplos:\n" +
        "• 'quero receber no início do mês às 8h'\n" +
        "• 'mudar hora do relatório para 14:00'\n" +
        "• 'receber no fim do mês'";

    public const string UnknownAction =
        "❌ Não entendi a ação desejada.\n\n" +
        "Exemplos:\n" +
        "• 'ativar relatório mensal'\n" +
        "• 'desativar relatório mensal'\n" +
        "• 'configurar relatório mensal às 10h'\n" +
        "• 'como está configurado meu relatório?'";

    private static string MomentText(string moment) => moment == "INICIO_MES" ? "início do mês (dia 1)" : "fim do mês (último dia)";
}

/// <summary>Endereços favoritos (<c>user_address_service.py</c> e <c>config_intents.py</c>).</summary>
public static class AddressMessages
{
    public static readonly string[] ValidLabels = ["casa", "trabalho", "outro"];

    public const string MissingData =
        "❌ Não entendi o endereço.\n\n" +
        "Use o formato:\n" +
        "*'Configurar endereço casa: Rua X, 123, Bairro, Cidade-SP'*\n\n" +
        "Tipos de endereço:\n" +
        "• Casa\n" +
        "• Trabalho\n" +
        "• Outro";

    /// <summary><c>❌ Erro ao salvar endereço: {str(e)}</c> sem o motivo (desvio 008).</summary>
    public const string SaveFailed = "❌ Erro ao salvar endereço.";

    public const string DeleteFailed = "❌ Erro ao deletar endereço.";

    public const string EmptyList =
        "📍 *Seus Endereços Favoritos*\n\n" +
        "Você ainda não tem endereços cadastrados.\n\n" +
        "Para adicionar um endereço, envie:\n" +
        "*'Configurar endereço casa: [endereço completo]'*\n\n" +
        "Exemplo:\n" +
        "Configurar endereço casa: Av Paulista 1000, Consolação, São Paulo-SP";

    public static string InvalidLabel(string label) => $"❌ Label inválido: '{label}'. Use: casa, trabalho ou outro";

    public static string NotLocated(string address) =>
        "❌ Não consegui localizar o endereço:\n" +
        $"'{address}'\n\n" +
        "Por favor, seja mais específico e inclua:\n" +
        "• Rua e número\n" +
        "• Bairro\n" +
        "• Cidade e Estado\n\n" +
        "Exemplo: Av Paulista 1000, Consolação, São Paulo-SP";

    public static string Saved(string label, string address) =>
        $"✅ Endereço *'{EventMessages.LabelName(label)}'* salvo com sucesso!\n\n" +
        $"{EventMessages.LabelEmoji(label)} {address}\n\n" +
        "Você pode usar este endereço ao calcular tempo de deslocamento.";

    public static string NotRegistered(string label) => $"❌ Você não tem endereço '{EventMessages.LabelName(label)}' cadastrado.";

    public static string Deleted(string label) =>
        $"✅ Endereço *'{EventMessages.LabelName(label)}'* removido com sucesso! {EventMessages.LabelEmoji(label)}";

    public static string List(IReadOnlyList<UserAddress> addresses)
    {
        if (addresses.Count == 0)
        {
            return EmptyList;
        }

        var message = new StringBuilder("📍 *Seus Endereços Favoritos*\n\n");
        foreach (var address in addresses)
        {
            message.Append(EventMessages.LabelEmoji(address.Label)).Append(" *").Append(EventMessages.LabelName(address.Label)).Append(":* ")
                .Append(address.Address).Append('\n');
        }

        message.Append("\n━━━━━━━━━━━━━━━━\n");
        message.Append("\n*Para adicionar/alterar:*\n");
        message.Append("Configurar endereço casa: [endereço]\n\n");
        message.Append("*Para remover:*\n");
        message.Append("Deletar endereço casa");
        return message.ToString();
    }
}

/// <summary><c>SolicitarApiKeyIntent</c>: a chave sai do jeito que está na coluna (desvio 153, proposto).</summary>
public static class ApiKeyMessage
{
    public const string NotFound = "❌ Não encontrei sua API Key. Entre em contato com o suporte.";

    public static string Format(string apiKey, string? name)
    {
        var message = new StringBuilder("🔑 *Sua API Key*\n\n");
        if (!string.IsNullOrEmpty(name))
        {
            message.Append("Olá ").Append(name).Append("!\n\n");
        }

        message.Append("Sua chave de acesso:\n");
        message.Append('`').Append(apiKey).Append("`\n\n");
        message.Append("⚠️ *Importante:*\n");
        message.Append("• Não compartilhe esta chave com ninguém\n");
        message.Append("• Use-a para configurar automações no iPhone\n");
        message.Append("• Esta chave dá acesso total à sua conta\n\n");
        message.Append("📱 *Para usar no iPhone:*\n");
        message.Append("1. Copie a chave acima\n");
        message.Append("2. No atalho, cole no campo `user_api_key`\n");
        message.Append("3. Teste enviando um gasto!\n\n");
        message.Append("💡 *Endpoint:*\n");
        message.Append("`POST /api/transacao`");
        return message.ToString();
    }
}
