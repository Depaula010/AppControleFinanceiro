using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Settings;

namespace MeuSecretario.Api.Settings;

/// <summary>
/// Blocos de resposta da 007, montados uma vez e usados pelas rotas individuais e por <c>GET /api/user/settings</c>
/// (RN-007-12). As chaves saem na ordem da spec §3.
/// </summary>
public static class SettingsResponses
{
    public static Dictionary<string, object?> Profile(ProfileRow p) => new()
    {
        ["id"] = p.Id,
        ["nome"] = p.Nome,
        ["email"] = p.Email,
        ["numero_whatsapp"] = p.NumeroWhatsapp,
        ["cidade"] = p.Cidade,
        ["estado"] = p.Estado,
        ["fuso_horario"] = p.FusoHorario,
        ["meses_reserva_emergencia"] = p.MesesReservaEmergencia,
    };

    public static List<Dictionary<string, object?>> ApiKeys(IReadOnlyList<ApiKeyPreferenceRow> rows) =>
        rows.Select(r => new Dictionary<string, object?>
        {
            ["provedor"] = r.Provider,
            ["usar_chave_propria"] = r.UseOwnKey,
            ["has_key"] = r.HasKey,
        }).ToList();

    public static Dictionary<string, object?> Notifications(NotificationView n) => new()
    {
        ["morningBriefing"] = new Dictionary<string, object?> { ["enabled"] = n.MorningEnabled, ["time"] = n.MorningTime },
        ["eveningCheckIn"] = new Dictionary<string, object?> { ["enabled"] = n.EveningEnabled, ["time"] = n.EveningTime },
        ["financialAlerts"] = new Dictionary<string, object?> { ["enabled"] = n.FinancialEnabled, ["daysBeforeDue"] = n.DaysBeforeDue },
    };

    /// <summary><c>coordinates</c> só existe se latitude e longitude forem ambos não nulos (RN-007-11).</summary>
    public static List<Dictionary<string, object?>> Addresses(IReadOnlyList<AddressRow> rows) =>
        rows.Select(a =>
        {
            var item = new Dictionary<string, object?>
            {
                ["id"] = a.Id,
                ["label"] = a.Label,
                ["address"] = a.Address,
            };
            if (a.Latitude is { } lat && a.Longitude is { } lng)
            {
                item["coordinates"] = new Dictionary<string, object?> { ["lat"] = (double)lat, ["lng"] = (double)lng };
            }
            return item;
        }).ToList();

    public static Dictionary<string, object?> Settings(UserSettings s) => new()
    {
        ["profile"] = Profile(s.Profile),
        ["apiKeys"] = ApiKeys(s.ApiKeys),
        ["notifications"] = Notifications(s.Notifications),
        ["addresses"] = Addresses(s.Addresses),
    };

    public static IResult Error(string message, int statusCode) => Envelopes.A.Error(message, statusCode);
}
