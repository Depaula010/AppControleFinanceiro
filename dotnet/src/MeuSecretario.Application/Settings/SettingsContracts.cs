using MeuSecretario.Domain.Settings;

namespace MeuSecretario.Application.Settings;

/// <summary>Linha de <c>usuarios</c> devolvida pelo perfil (spec 007 §3.1).</summary>
public sealed record ProfileRow(
    int Id,
    string Nome,
    string? Email,
    string NumeroWhatsapp,
    string? Cidade,
    string? Estado,
    string FusoHorario,
    int? MesesReservaEmergencia);

public abstract record ProfileUpdateOutcome
{
    private ProfileUpdateOutcome()
    {
    }

    public sealed record Updated(ProfileRow Profile) : ProfileUpdateOutcome;

    public sealed record NotFound : ProfileUpdateOutcome;

    /// <summary>Violação de UNIQUE do e-mail (desvio 085).</summary>
    public sealed record EmailTaken : ProfileUpdateOutcome;
}

public interface IProfileRepository
{
    /// <summary>Só usuário ativo (<c>ativo = true</c>).</summary>
    Task<ProfileRow?> GetAsync(int userId, CancellationToken cancellationToken);

    Task<ProfileUpdateOutcome> UpdateAsync(int userId, ProfileChanges changes, CancellationToken cancellationToken);
}

/// <summary>Um dos três provedores com a escolha "chave própria" e se existe chave ativa (<c>has_key</c>).</summary>
public sealed record ApiKeyPreferenceRow(string Provider, bool UseOwnKey, bool HasKey);

/// <summary>Repositório próprio da 007 (Q-007-1): preferência e chave gravadas na mesma transação.</summary>
public interface IApiKeySettingsRepository
{
    /// <summary>Sempre os três provedores, na ordem fixa <c>gemini</c>, <c>weather</c>, <c>openroute</c> (desvio 088).</summary>
    Task<IReadOnlyList<ApiKeyPreferenceRow>> ListAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Uma transação: upsert da preferência; com <paramref name="encryptedKey"/> não nulo, upsert da chave com
    /// <c>ativo = true</c>; com <paramref name="useOwnKey"/> falso, desativa a chave existente (sem apagar).
    /// </summary>
    Task SaveOwnKeyAsync(int userId, string provider, bool useOwnKey, string? encryptedKey, CancellationToken cancellationToken);
}

/// <summary>Linha de <c>notificationconfigs</c>; horas como texto do banco (<c>07:00:00</c>).</summary>
public sealed record NotificationRow(
    bool MorningEnabled,
    string MorningTime,
    bool EveningEnabled,
    string EveningTime,
    bool? FinancialEnabled,
    int DaysBeforeDue);

/// <summary>Notificações como a API devolve (horas com 5 caracteres, <c>daysBeforeDue</c> já lido).</summary>
public sealed record NotificationView(
    bool MorningEnabled,
    string MorningTime,
    bool EveningEnabled,
    string EveningTime,
    bool FinancialEnabled,
    int DaysBeforeDue);

public interface IUserNotificationSettingsRepository
{
    Task<NotificationRow?> GetAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Upsert por usuário dos seis valores; as horas já validadas vão como texto e o banco as converte.</summary>
    Task UpsertAsync(int userId, NotificationInput input, CancellationToken cancellationToken);
}

public sealed record AddressRow(int Id, string Label, string Address, decimal? Latitude, decimal? Longitude);

/// <summary>Porta de <c>enderecosfavoritos</c>; a 012 (WhatsApp) a reutiliza.</summary>
public interface IFavoriteAddressRepository
{
    /// <summary>Ordenada por <c>label</c> (ordem do banco: <c>casa</c>, <c>outro</c>, <c>trabalho</c>).</summary>
    Task<IReadOnlyList<AddressRow>> ListAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Upsert por (usuário, rótulo). Com <paramref name="coordinates"/> o par é gravado como veio; sem (<c>null</c>), as
    /// coordenadas existentes são mantidas quando o texto do endereço não mudou e viram <c>NULL</c> quando mudou (desvio 086).
    /// </summary>
    Task UpsertAsync(int userId, string label, string address, AddressCoordinates? coordinates, CancellationToken cancellationToken);

    /// <summary><c>false</c> quando nenhuma linha foi apagada.</summary>
    Task<bool> DeleteAsync(int userId, string label, CancellationToken cancellationToken);
}

/// <summary>Resposta de <c>GET /api/user/settings</c>: o que as rotas 1, 3, 6 e 8 devolvem.</summary>
public sealed record UserSettings(
    ProfileRow Profile,
    IReadOnlyList<ApiKeyPreferenceRow> ApiKeys,
    NotificationView Notifications,
    IReadOnlyList<AddressRow> Addresses);
