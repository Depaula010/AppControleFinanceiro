using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Endereço favorito como a rota de deslocamento o usa (<c>UserAddressService.get_user_addresses</c>).</summary>
public sealed record UserAddress(string Label, string Address, double? Lat, double? Lon);

/// <summary>Evento achado na busca para deletar (<c>CalendarManagementService.find_events_by_title</c>).</summary>
public sealed record DeletionCandidate(string Id, string Summary, string Start, string CalendarName);

/// <summary>Textos de resposta do fluxo de eventos do WhatsApp (criar, confirmar, cancelar, rota e deletar), literais do Flask.</summary>
public static partial class EventMessages
{
    public const string Cancelled = "✅ Evento cancelado com sucesso!";

    /// <summary><c>cancel_pending_event</c> sem evento (a chave sumiu entre a busca e o cancelamento).</summary>
    public const string NoPendingToCancel = "❌ Nenhum evento pendente encontrado.";

    /// <summary><c>confirm_and_create_event</c> sem evento; o handler acrescenta mais um <c>❌ </c> na frente.</summary>
    public const string NoPendingToConfirm = "❌ Nenhum evento pendente encontrado ou já expirou (5 minutos).";

    public const string CreateFailed = "Erro ao criar evento.";

    public const string DeleteFailed = "Erro ao deletar evento.";

    public const string DeleteNotFound = "Evento não encontrado ou já foi deletado.";

    public const string MissingTitleOrDate =
        "❌ Não consegui identificar o título ou data do evento. Tente algo como: 'Criar evento Academia amanhã às 7h'";

    public const string PendingError = "❌ Erro ao processar evento. Tente novamente.";

    public const string MissingDeletionTitle =
        "❌ Não consegui identificar qual evento deletar. Tente algo como: 'Deletar academia de hoje'";

    public const string RouteNoPendingEvent = "❌ Não encontrei nenhum evento pendente de confirmação.";

    public const string RouteNoLocation = "❌ Este evento não tem localização definida, não posso calcular tempo de deslocamento.";

    public const string RouteNoAddresses =
        "❌ Você não tem endereços cadastrados.\n\n" +
        "Configure um endereço primeiro:\n" +
        "Exemplo: _'Configurar endereço casa: Av Paulista 1000, São Paulo-SP'_";

    public const string RouteCalculationFailed =
        "❌ Não consegui calcular a rota.\n\n" +
        "Tente novamente mais tarde ou confirme o evento sem cálculo de tempo.";

    public const string AgendaNotConfigured =
        "⚠️ *Google Calendar não configurado*\n\nO administrador precisa configurar as credenciais OAuth2.";

    /// <summary><c>❌ Erro ao consultar agenda: {str(e)}</c> sem o motivo (desvio 008).</summary>
    public const string AgendaFailed = "❌ Erro ao consultar agenda.\n\nTente reconectar seu Google Calendar.";

    private const string Dash = " - ";

    public static string InvalidDate(string value) => "❌ Data inválida: " + value;

    public static string AgendaNotConnected(string connectUrl) =>
        "📅 *Google Calendar não conectado*\n\n" +
        "Para consultar sua agenda, conecte primeiro:\n" +
        "👉 " + connectUrl + "\n\n" +
        "✅ É rápido e seguro!\n" +
        "✅ Só precisa fazer 1 vez\n" +
        "✅ Padrão OAuth2 oficial do Google";

    /// <summary><c>CalendarManagementService.create_event</c> em caso de sucesso.</summary>
    public static string CreatedMessage(string title) => $"Evento '{title}' criado com sucesso!";

    public static string CreateFailedBecause(string reason) => "Erro ao criar evento: " + reason;

    public static string DeletedMessage(string title) => $"Evento '{title}' deletado com sucesso!";

    /// <summary>Resposta ao "sim" do usuário quando o evento foi criado.</summary>
    public static string Created(string title, string message) => $"✅ *Evento Criado!*\n\n📝 {title}\n{message}";

    /// <summary>Resposta ao "sim" quando a criação falhou (o texto de <paramref name="message"/> já pode ter o <c>❌</c> dele).</summary>
    public static string ConfirmFailed(string message) => "❌ " + message;

    /// <summary>A busca para o endereço só roda com local e usuário (<c>if localizacao and usuario_id</c>).</summary>
    public static bool NeedsAddressLookup(string? location, int userId) => !string.IsNullOrEmpty(location) && userId != 0;

    /// <summary>
    /// <c>EventConfirmationService.format_confirmation_message</c>. <paramref name="hasAddresses"/> é <c>null</c> quando a busca
    /// de endereços não se aplica (<see cref="NeedsAddressLookup"/> falso).
    /// </summary>
    public static string Confirmation(
        string title, string eventDate, string? start, string? end, string? description, string? location, bool? hasAddresses)
    {
        var message = new StringBuilder();
        message.Append("📅 *Confirmar Evento?*\n\n");
        message.Append("📌 *").Append(title).Append("*\n");
        message.Append("📆 Data: ").Append(DisplayDate(eventDate)).Append('\n');

        if (!string.IsNullOrEmpty(start))
        {
            message.Append("⏰ Horário: ").Append(start);
            if (!string.IsNullOrEmpty(end))
            {
                message.Append(Dash).Append(end);
            }

            message.Append('\n');
        }
        else
        {
            message.Append("⏰ Dia inteiro\n");
        }

        if (!string.IsNullOrEmpty(location))
        {
            message.Append("📍 Local: ").Append(location).Append('\n');
        }

        if (!string.IsNullOrEmpty(description))
        {
            message.Append("📝 Descrição: ").Append(description).Append('\n');
        }

        message.Append("\n✅ Responda *'sim'* ou *'confirmar'* para criar");
        message.Append("\n❌ Responda *'não'* ou *'cancelar'* para desistir");

        if (hasAddresses is true)
        {
            message.Append("\n\n🚗 *Deseja calcular tempo de deslocamento?*");
            message.Append("\n   Responda *'calcular rota'* para incluir tempo de viagem");
        }
        else if (hasAddresses is false)
        {
            message.Append("\n\n💡 *Dica:* Configure seus endereços favoritos para calcular tempo de deslocamento!");
            message.Append("\n   Exemplo: _'Configurar endereço casa: Av Paulista 1000, São Paulo-SP'_");
        }

        return message.ToString();
    }

    /// <summary>Nome do rótulo (<c>UserAddressService.LABEL_NAMES.get(label, label.capitalize())</c>).</summary>
    public static string LabelName(string label) => label switch
    {
        "casa" => "Casa",
        "trabalho" => "Trabalho",
        "outro" => "Outro",
        _ => PyStrings.Capitalize(label),
    };

    public static string LabelEmoji(string label) => label switch
    {
        "casa" => "🏠",
        "trabalho" => "💼",
        "outro" => "➕",
        _ => "📍",
    };

    public static string RouteDestinationNotFound(string location) =>
        $"❌ Não consegui localizar o destino:\n'{location}'\n\nVerifique se o endereço está completo e tente novamente.";

    public static string RouteCalculated(string originLabel, string destination, int minutes, string distanceKm) =>
        "✅ *Rota Calculada!*\n\n" +
        $"📍 Origem: {LabelEmoji(originLabel)} {LabelName(originLabel)}\n" +
        $"📍 Destino: {destination}\n\n" +
        $"⏱️ *Tempo estimado:* {minutes.ToString(CultureInfo.InvariantCulture)} minutos\n" +
        $"📏 *Distância:* {distanceKm} km\n\n" +
        "💡 Responda *'sim'* para confirmar o evento\n" +
        "ou *'não'* para cancelar";

    /// <summary>A resposta <c>casa</c>/<c>trabalho</c>/<c>outro</c> a esta pergunta é tratada pelo passo de rota (desvio 156).</summary>
    public static string RouteAskOrigin(IReadOnlyList<UserAddress> addresses)
    {
        var message = new StringBuilder("🚗 *Qual endereço usar como origem?*\n\n");
        foreach (var address in addresses)
        {
            message.Append(LabelEmoji(address.Label)).Append(" *").Append(LabelName(address.Label)).Append("*: ").Append(address.Address).Append('\n');
        }

        message.Append("\n💬 Responda com: _'casa'_, _'trabalho'_ ou _'outro'_");
        return message.ToString();
    }

    /// <summary>Resposta à pergunta de origem com um rótulo que o usuário não tem cadastrado (desvio 156).</summary>
    public static string RouteOriginUnknown(string label, IReadOnlyList<UserAddress> addresses) =>
        $"❌ Você não tem um endereço *{label}* salvo.\n\n" + RouteAskOrigin(addresses);

    public static string DeleteNothingFound(string search, string? when)
    {
        var message = $"🤔 Não encontrei eventos com '{search}'";
        if (!string.IsNullOrEmpty(when))
        {
            message += " para " + when;
        }

        return message + ".\n\nTente buscar com outras palavras.";
    }

    public static string DeleteResult(bool success, string message) => (success ? "✅ " : "❌ ") + message;

    public static string DeleteMany(IReadOnlyList<DeletionCandidate> events)
    {
        var message = new StringBuilder();
        message.Append("📋 Encontrei ").Append(events.Count.ToString(CultureInfo.InvariantCulture)).Append(" eventos:\n\n");
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            string when;
            if (item.Start.Contains('T', StringComparison.Ordinal))
            {
                var wall = CalendarTimes.Wall(item.Start)
                           ?? throw new FormatException("Data de evento inválida.");
                when = wall.ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);
            }
            else
            {
                var day = DateTime.ParseExact(item.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                when = day.ToString("dd/MM", CultureInfo.InvariantCulture);
            }

            message.Append(index + 1).Append(". *").Append(item.Summary).Append("*\n");
            message.Append("   📅 ").Append(when).Append('\n');
            message.Append("   📂 ").Append(item.CalendarName).Append('\n');
            message.Append("   _ID: ").Append(item.Id).Append("_\n\n");
        }

        message.Append("Para deletar um específico, envie:\n");
        message.Append("'Deletar evento ").Append(events[0].Id).Append('\'');
        return message.ToString();
    }

    /// <summary><c>date.fromisoformat(texto).strftime('%d/%m/%Y')</c>; texto que não é data volta como veio.</summary>
    public static string DisplayDate(string value) =>
        TryParseIsoDate(value, out var date) ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : value;

    /// <summary><c>date.fromisoformat</c> para texto: <c>YYYY-MM-DD</c> ou <c>YYYYMMDD</c>, com data de calendário existente.</summary>
    public static bool TryParseIsoDate(string? value, out DateOnly date)
    {
        date = default;
        if (value is null)
        {
            return false;
        }

        var match = IsoDate().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var extended = match.Groups[1].Success;
        var year = int.Parse(match.Groups[extended ? 1 : 4].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[extended ? 2 : 5].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[extended ? 3 : 6].Value, CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    [GeneratedRegex(@"^(?:([0-9]{4})-([0-9]{2})-([0-9]{2})|([0-9]{4})([0-9]{2})([0-9]{2}))\z")]
    private static partial Regex IsoDate();
}
