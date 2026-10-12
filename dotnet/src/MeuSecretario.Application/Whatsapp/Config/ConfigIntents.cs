using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Settings;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Linha de <c>notificationconfigs</c> que o WhatsApp lê (resumo matinal e check-in noturno).</summary>
public sealed record WhatsAppNotificationConfig(
    bool MorningActive, TimeOnly MorningTime, bool CheckinActive, TimeOnly CheckinTime, bool FinancialAlertsActive = false);

/// <summary>Linha de <c>monthlyreportconfigs</c>.</summary>
public sealed record WhatsAppReportConfig(bool Active, string Moment, TimeOnly Time);

/// <summary>Leituras e escritas de configuração do WhatsApp (012f). As regras ficam nas intenções; aqui só o banco.</summary>
public interface IWhatsAppConfigStore
{
    /// <summary><c>get_or_create_config</c>: cria a linha padrão se não existir.</summary>
    Task<WhatsAppNotificationConfig> GetOrCreateNotificationAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>UPDATE ... RETURNING</c> do resumo matinal; <c>null</c> mantém o valor.</summary>
    Task<WhatsAppNotificationConfig> UpdateMorningAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken);

    Task<WhatsAppNotificationConfig> UpdateCheckinAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken);

    /// <summary><c>update_alertas_financeiros_config</c> (desvio 191): grava <c>alertas_financeiros_ativos</c> e devolve a linha.</summary>
    Task<WhatsAppNotificationConfig> UpdateFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken);

    Task<WhatsAppReportConfig> GetOrCreateReportAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Atualiza só o que veio (<c>null</c> mantém) e devolve a linha lida de novo.</summary>
    Task<WhatsAppReportConfig> UpdateReportAsync(int userId, bool? active, string? moment, TimeOnly? time, CancellationToken cancellationToken);

    /// <summary><c>UPDATE usuarios SET cidade, estado</c>.</summary>
    Task UpdateLocationAsync(int userId, string city, string? state, CancellationToken cancellationToken);

    /// <summary><c>SELECT api_key_automate, nome</c>; <c>null</c> sem usuário.</summary>
    Task<(string? ApiKey, string? Name)?> GetApiKeyAsync(int userId, CancellationToken cancellationToken);
}

/// <summary>
/// <c>Configurar Notificações</c> (inline, <c>whatsapp_handler.py:1192-1370</c>): resumo matinal, check-in noturno e o ramo do Gemini.
/// A hora fora de 00:00-23:59 sobe (500), como no Flask. O ramo do Gemini responde o status com as chaves que existem (desvios 152 e 191).
/// </summary>
public sealed class NotificationsIntent(ILlmClient llm, IWhatsAppConfigStore store) : IWhatsAppIntent
{
    public string Name => "Configurar Notificações";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var lower = context.Message.ToLowerInvariant();
        var userId = context.User.Id;

        if (NotificationCommand.IsMorningSummary(lower))
        {
            return await ByShortcutAsync(userId, context.Message, lower, checkin: false, cancellationToken);
        }

        if (NotificationCommand.IsNightCheckin(lower))
        {
            return await ByShortcutAsync(userId, context.Message, lower, checkin: true, cancellationToken);
        }

        return await ByGeminiAsync(context, cancellationToken);
    }

    private async Task<string> ByShortcutAsync(int userId, string message, string lower, bool checkin, CancellationToken cancellationToken)
    {
        var action = NotificationCommand.Action(lower, checkin);
        var time = NotificationCommand.ExtractTime(message);

        var (success, text, config) = action switch
        {
            "ativar" => await UpdateAsync(userId, checkin, true, time, cancellationToken),
            "desativar" => await UpdateAsync(userId, checkin, false, null, cancellationToken),
            _ => await UpdateAsync(userId, checkin, true, time, cancellationToken),
        };

        if (!success || config is null)
        {
            return NotificationCommand.Failure(text);
        }

        return checkin
            ? NotificationCommand.CheckinStatus(text, config.CheckinActive, config.CheckinTime)
            : NotificationCommand.MorningStatus(text, config.MorningActive, config.MorningTime);
    }

    /// <summary>
    /// <c>update_resumo_matinal_config</c> / <c>update_checkin_noturno_config</c>. A hora ilegível sobe (<c>strptime</c> fora do
    /// <c>try</c> do Flask); falha do banco no <c>UPDATE</c> vira a mensagem sem o motivo (desvio 008).
    /// </summary>
    private async Task<(bool Success, string Message, WhatsAppNotificationConfig? Config)> UpdateAsync(
        int userId, bool checkin, bool? active, string? timeText, CancellationToken cancellationToken)
    {
        await store.GetOrCreateNotificationAsync(userId, cancellationToken);

        TimeOnly? time = null;
        if (timeText is not null)
        {
            time = PyTimeText.TryParseHourMinute(timeText, out var parsed)
                ? parsed
                : throw new FormatException("Hora inválida para strptime('%H:%M').");

            if (checkin && (parsed < new TimeOnly(18, 0) || parsed > new TimeOnly(23, 0)))
            {
                return (false, NotificationCommand.CheckinRange, null);
            }
        }

        if (active is null && time is null)
        {
            return (false, NotificationCommand.NoChange, null);
        }

        try
        {
            var config = checkin
                ? await store.UpdateCheckinAsync(userId, active, time, cancellationToken)
                : await store.UpdateMorningAsync(userId, active, time, cancellationToken);
            return (true, NotificationCommand.UpdateMessage(checkin ? "Check-in noturno" : "Resumo matinal", active, time), config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, NotificationCommand.UpdateFailed, null);
        }
    }

    /// <summary>
    /// Ramo do Gemini (<c>tipo</c> = <c>agenda_diaria</c> | <c>contas_vencer</c>). <c>agenda_diaria</c> grava o resumo matinal e
    /// <c>contas_vencer</c> grava <c>alertas_financeiros_ativos</c>; os dois respondem o status atual (desvios 152 e 191).
    /// <c>dias_antes</c> e <c>hora</c> de <c>contas_vencer</c> seguem ignorados (os alertas rodam com o resumo matinal).
    /// </summary>
    private async Task<string> ByGeminiAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.NotificationConfig(context.Message), context.User.Id, cancellationToken);
        var type = GeminiFields.StringOnly(GeminiFields.Get(data, "tipo"));
        var action = GeminiFields.StringOnly(GeminiFields.Get(data, "acao"));
        var hour = GeminiFields.Get(data, "hora");

        if (type == "agenda_diaria")
        {
            bool? active;
            TimeOnly? time = null;
            switch (action)
            {
                case "ativar":
                    active = true;
                    break;
                case "desativar":
                    active = false;
                    break;
                case "configurar":
                    active = true;
                    if (hour is { ValueKind: JsonValueKind.String } text)
                    {
                        time = PyTimeText.TryParseHourMinute(text.GetString(), out var parsed)
                            ? parsed
                            : throw new FormatException("Hora inválida para strptime('%H:%M').");
                    }

                    break;
                default:
                    return NotificationCommand.Failure(NotificationCommand.UnknownAction);
            }

            var (success, message, _) = await UpdateAsync(context.User.Id, false, active, time?.ToString("HH:mm", CultureInfo.InvariantCulture), cancellationToken);
            if (!success)
            {
                return NotificationCommand.Failure(message);
            }

            var current = await store.GetOrCreateNotificationAsync(context.User.Id, cancellationToken);
            return NotificationCommand.DailyAgendaStatus(message, current.MorningActive, current.MorningTime);
        }

        if (type == "contas_vencer")
        {
            if (action is not ("ativar" or "desativar" or "configurar"))
            {
                return NotificationCommand.Failure(NotificationCommand.UnknownAction);
            }

            await store.GetOrCreateNotificationAsync(context.User.Id, cancellationToken);
            try
            {
                var updated = await store.UpdateFinancialAlertsAsync(context.User.Id, action != "desativar", cancellationToken);
                return NotificationCommand.FinancialAlertsStatus(
                    NotificationCommand.FinancialAlertsMessage(updated.FinancialAlertsActive), updated.FinancialAlertsActive, updated.MorningTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return NotificationCommand.Failure(NotificationCommand.UpdateFailed);
            }
        }

        return NotificationCommand.UnknownType;
    }
}

/// <summary><c>Configurar Localização</c> (<c>ConfigurarLocalizacaoIntent</c> e <c>LocationService.update_user_location</c>).</summary>
public sealed class LocationIntent(ILlmClient llm, IWhatsAppConfigStore store) : IWhatsAppIntent
{
    public string Name => "Configurar Localização";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.LocationConfig(context.Message), context.User.Id, cancellationToken);
        var cityValue = GeminiFields.Get(data, "cidade");
        var stateValue = GeminiFields.Get(data, "estado");

        if (!GeminiFields.Truthy(cityValue))
        {
            return LocationMessages.MissingCity;
        }

        // cidade.strip()/estado.upper() sobre algo que não é texto quebram no Python (AttributeError) e a intenção responde o genérico.
        var city = GeminiFields.StringOnly(cityValue) ?? throw new InvalidOperationException("cidade não é texto.");
        string? state = null;
        if (stateValue is { ValueKind: JsonValueKind.String } stateText)
        {
            state = stateText.GetString();
        }
        else if (stateValue is { } other && other.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidOperationException("estado não é texto.");
        }

        if (city.Trim().Length == 0)
        {
            return LocationMessages.Reply(false, LocationMessages.InvalidCity);
        }

        if (!string.IsNullOrEmpty(state))
        {
            state = state.ToUpperInvariant().Trim();
            if (state.EnumerateRunes().Count() != 2)
            {
                return LocationMessages.Reply(false, LocationMessages.InvalidState);
            }
        }

        city = city.Trim();
        try
        {
            await store.UpdateLocationAsync(context.User.Id, city, state, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LocationMessages.Reply(false, LocationMessages.SaveFailed);
        }

        return LocationMessages.Reply(true, LocationMessages.Saved(city, state));
    }
}

/// <summary><c>Configurar Relatório Mensal</c> (<c>ConfigurarRelatorioMensalIntent</c> e <c>monthly_report_config_service.update_config</c>).</summary>
public sealed class MonthlyReportIntent(ILlmClient llm, IWhatsAppConfigStore store) : IWhatsAppIntent
{
    public string Name => "Configurar Relatório Mensal";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.MonthlyReportConfig(context.Message), context.User.Id, cancellationToken);
        var action = GeminiFields.StringOnly(GeminiFields.Get(data, "acao"));
        var moment = GeminiFields.Get(data, "momento_envio");
        var hour = GeminiFields.Get(data, "hora_envio");
        var userId = context.User.Id;

        var current = await store.GetOrCreateReportAsync(userId, cancellationToken);

        switch (action)
        {
            case "consultar":
                return ReportConfigMessages.Consult(current.Active, current.Moment, current.Time);
            case "desativar":
                await UpdateAsync(userId, false, null, null, cancellationToken);
                return ReportConfigMessages.Disabled;
            case "ativar":
            {
                var updated = await UpdateAsync(userId, true, moment, hour, cancellationToken);
                return ReportConfigMessages.Enabled(updated.Moment, updated.Time);
            }

            case "configurar":
            {
                if (!GeminiFields.Truthy(moment) && !GeminiFields.Truthy(hour))
                {
                    return ReportConfigMessages.NothingToConfigure;
                }

                var updated = await UpdateAsync(userId, null, moment, hour, cancellationToken);
                return ReportConfigMessages.Updated(updated.Moment, updated.Time);
            }

            default:
                return ReportConfigMessages.UnknownAction;
        }
    }

    /// <summary><c>update_config</c>: momento fora de <c>INICIO_MES|FIM_MES</c> e hora fora de <c>HH:MM[:SS]</c> levantam <c>ValueError</c>.</summary>
    private async Task<WhatsAppReportConfig> UpdateAsync(
        int userId, bool? active, JsonElement? momentValue, JsonElement? hourValue, CancellationToken cancellationToken)
    {
        string? moment = null;
        if (GeminiFields.Truthy(momentValue))
        {
            moment = GeminiFields.StringOnly(momentValue);
            if (moment is null || Array.IndexOf(ReportConfigMessages.ValidMoments, moment) < 0)
            {
                throw new FormatException("momento_envio deve ser INICIO_MES ou FIM_MES");
            }
        }

        TimeOnly? time = null;
        if (GeminiFields.Truthy(hourValue))
        {
            var text = GeminiFields.StringOnly(hourValue) ?? throw new InvalidOperationException("hora_envio não é texto.");
            if (text.Length == 5)
            {
                text += ":00";
            }

            time = PyTimeText.TryParseHourMinuteSecond(text, out var parsed)
                ? parsed
                : throw new FormatException("hora_envio deve estar no formato 'HH:MM' ou 'HH:MM:SS'");
        }

        await store.GetOrCreateReportAsync(userId, cancellationToken);
        return await store.UpdateReportAsync(userId, active, moment, time, cancellationToken);
    }
}

/// <summary><c>Configurar Endereço</c> (<c>ConfigurarEnderecoIntent</c> e <c>UserAddressService.save_favorite_address</c>).</summary>
public sealed class AddressConfigIntent(ILlmClient llm, IFavoriteAddressRepository addresses, IRouteClient routes) : IWhatsAppIntent
{
    public string Name => "Configurar Endereço";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.AddressConfig(context.Message), context.User.Id, cancellationToken);
        var labelValue = GeminiFields.Get(data, "label");
        var addressValue = GeminiFields.Get(data, "endereco_completo");

        if (!GeminiFields.Truthy(labelValue) || !GeminiFields.Truthy(addressValue))
        {
            return AddressMessages.MissingData;
        }

        try
        {
            var label = (GeminiFields.StringOnly(labelValue) ?? throw new InvalidOperationException("label não é texto.")).ToLowerInvariant();
            if (Array.IndexOf(AddressMessages.ValidLabels, label) < 0)
            {
                return AddressMessages.InvalidLabel(label);
            }

            var address = GeminiFields.Text(addressValue)!;
            var geocoded = await routes.GeocodeAsync(address, null, cancellationToken);
            if (geocoded is null)
            {
                return AddressMessages.NotLocated(address);
            }

            var formatted = string.IsNullOrEmpty(geocoded.Label) ? address : geocoded.Label;
            await addresses.UpsertAsync(
                context.User.Id, label, formatted, new AddressCoordinates((decimal)geocoded.Lat, (decimal)geocoded.Lon), cancellationToken);
            return AddressMessages.Saved(label, formatted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AddressMessages.SaveFailed;
        }
    }
}

/// <summary><c>Listar Endereços</c> (<c>format_address_list_message</c>).</summary>
public sealed class AddressListIntent(UserAddresses addresses) : IWhatsAppIntent
{
    public string Name => "Listar Endereços";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) =>
        AddressMessages.List(await addresses.GetAsync(context.User.Id, cancellationToken));
}

/// <summary><c>Deletar Endereço</c> (<c>DeletarEnderecoIntent</c> e <c>UserAddressService.delete_address</c>).</summary>
public sealed class AddressDeleteIntent(ILlmClient llm, IFavoriteAddressRepository addresses) : IWhatsAppIntent
{
    public string Name => "Deletar Endereço";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.AddressDeletion(context.Message), context.User.Id, cancellationToken);

        try
        {
            var label = (GeminiFields.StringOr(data, "label", "outro") ?? throw new InvalidOperationException("label não é texto.")).ToLowerInvariant();
            return await addresses.DeleteAsync(context.User.Id, label, cancellationToken)
                ? AddressMessages.Deleted(label)
                : AddressMessages.NotRegistered(label);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AddressMessages.DeleteFailed;
        }
    }
}

/// <summary><c>Solicitar API Key</c>: decifra o valor da coluna quando parece cifrado (desvio 153).</summary>
public sealed class ApiKeyIntent(IWhatsAppConfigStore store, IEncryptionService encryption) : IWhatsAppIntent
{
    public string Name => "Solicitar API Key";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var row = await store.GetApiKeyAsync(context.User.Id, cancellationToken);
        if (row is not { ApiKey: { Length: > 0 } stored })
        {
            return ApiKeyMessage.NotFound;
        }

        string key;
        try
        {
            key = encryption.LooksEncrypted(stored) ? encryption.Decrypt(stored) : stored;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Token ilegível: nunca mostrar o texto cifrado como se fosse a chave.
            return ApiKeyMessage.NotFound;
        }

        return ApiKeyMessage.Format(key, row.Value.Name);
    }
}
