using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.Application.Settings;

// As quatro leituras abaixo (perfil, chaves, notificações, endereços) são os leitores compartilhados entre
// os GET individuais e GET /api/user/settings (RN-007-12).

/// <summary><c>GET /api/user/profile</c>: <c>null</c> = usuário inexistente ou inativo.</summary>
public sealed class GetProfile(IProfileRepository profiles)
{
    public Task<ProfileRow?> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        profiles.GetAsync(userId, cancellationToken);
}

public abstract record UpdateProfileResult
{
    private UpdateProfileResult()
    {
    }

    public sealed record Updated(ProfileRow Profile) : UpdateProfileResult;

    public sealed record Invalid(ProfileError Error) : UpdateProfileResult;

    public sealed record NotFound : UpdateProfileResult;

    public sealed record EmailTaken : UpdateProfileResult;
}

/// <summary><c>PUT /api/user/profile</c> (spec 007 §3.2).</summary>
public sealed class UpdateProfile(IProfileRepository profiles)
{
    public async Task<UpdateProfileResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = ProfileRules.Validate(body);
        if (validation is not ProfileValidation.Ok ok)
        {
            return new UpdateProfileResult.Invalid(((ProfileValidation.Rejected)validation).Error);
        }

        return await profiles.UpdateAsync(userId, ok.Changes, cancellationToken) switch
        {
            ProfileUpdateOutcome.Updated updated => new UpdateProfileResult.Updated(updated.Profile),
            ProfileUpdateOutcome.EmailTaken => new UpdateProfileResult.EmailTaken(),
            ProfileUpdateOutcome.NotFound => new UpdateProfileResult.NotFound(),
            _ => throw new InvalidOperationException("Resultado de atualização de perfil desconhecido."),
        };
    }
}

/// <summary><c>GET /api/user/api-keys</c>.</summary>
public sealed class GetApiKeySettings(IApiKeySettingsRepository keys)
{
    public Task<IReadOnlyList<ApiKeyPreferenceRow>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        keys.ListAsync(userId, cancellationToken);
}

public abstract record SaveOwnApiKeyResult
{
    private SaveOwnApiKeyResult()
    {
    }

    public sealed record Saved : SaveOwnApiKeyResult;

    public sealed record Invalid(SaveKeyError Error) : SaveOwnApiKeyResult;
}

/// <summary><c>PUT /api/user/api-keys</c> (spec 007 §3.4). A chave nunca é logada nem devolvida.</summary>
public sealed class SaveOwnApiKey(IApiKeySettingsRepository keys, IEncryptionService encryption)
{
    public async Task<SaveOwnApiKeyResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = ApiKeyRules.ValidateSave(body);
        if (validation is not SaveKeyValidation.Ok ok)
        {
            return new SaveOwnApiKeyResult.Invalid(((SaveKeyValidation.Rejected)validation).Error);
        }

        // Cifra a chave já sem trim; falha de cifra ou de banco sobe como exceção (500 de texto fixo na rota).
        var encrypted = ok.UseOwnKey && ok.RawKey.Length > 0 ? encryption.Encrypt(ok.RawKey) : null;
        await keys.SaveOwnKeyAsync(userId, ok.Provider, ok.UseOwnKey, encrypted, cancellationToken);
        return new SaveOwnApiKeyResult.Saved();
    }
}

public abstract record ValidateProviderKeyResult
{
    private ValidateProviderKeyResult()
    {
    }

    public sealed record Checked(bool IsValid) : ValidateProviderKeyResult;

    public sealed record Invalid(ValidateKeyError Error) : ValidateProviderKeyResult;

    /// <summary>O provedor não respondeu; <see cref="Error"/> é só para o log (via descrição segura).</summary>
    public sealed record Unavailable(Exception Error) : ValidateProviderKeyResult;
}

/// <summary><c>POST /api/user/api-keys/validate</c> (spec 007 §3.5). Nunca grava a chave.</summary>
public sealed class ValidateProviderKey(IProviderKeyValidator validator)
{
    public async Task<ValidateProviderKeyResult> ExecuteAsync(RawBody? body, CancellationToken cancellationToken)
    {
        var validation = ApiKeyRules.ValidateCheck(body);
        if (validation is not ValidateKeyValidation.Ok ok)
        {
            return new ValidateProviderKeyResult.Invalid(((ValidateKeyValidation.Rejected)validation).Error);
        }

        try
        {
            return await validator.ValidateAsync(ok.Provider, ok.RawKey, cancellationToken) switch
            {
                KeyValidation.Valid => new ValidateProviderKeyResult.Checked(true),
                KeyValidation.Invalid => new ValidateProviderKeyResult.Checked(false),
                KeyValidation.NotSupported => new ValidateProviderKeyResult.Invalid(ValidateKeyError.UnsupportedProvider),
                _ => throw new InvalidOperationException("Resultado de validação de chave desconhecido."),
            };
        }
        catch (ProviderUnavailableException ex)
        {
            return new ValidateProviderKeyResult.Unavailable(ex);
        }
    }
}

/// <summary><c>GET /api/user/notifications</c>: sem linha, os padrões (a linha não é criada).</summary>
public sealed class GetNotificationSettings(IUserNotificationSettingsRepository notifications)
{
    public async Task<NotificationView> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        var row = await notifications.GetAsync(userId, cancellationToken);
        return row is null
            ? new NotificationView(
                true, NotificationDefaults.MorningTime, true, NotificationDefaults.EveningTime, true, NotificationDefaults.DaysBeforeDue)
            : new NotificationView(
                row.MorningEnabled,
                NotificationDefaults.First5(row.MorningTime),
                row.EveningEnabled,
                NotificationDefaults.First5(row.EveningTime),
                row.FinancialEnabled ?? false,
                NotificationDefaults.ReadDaysBeforeDue(row.DaysBeforeDue));
    }
}

public abstract record SaveNotificationsResult
{
    private SaveNotificationsResult()
    {
    }

    public sealed record Saved : SaveNotificationsResult;

    public sealed record Invalid(NotificationError Error) : SaveNotificationsResult;
}

/// <summary><c>PUT /api/user/notifications</c> (spec 007 §3.7): PUT total, campo ausente volta ao padrão.</summary>
public sealed class SaveNotificationSettings(IUserNotificationSettingsRepository notifications)
{
    public async Task<SaveNotificationsResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = NotificationRules.Validate(body);
        if (validation is not NotificationValidation.Ok ok)
        {
            return new SaveNotificationsResult.Invalid(((NotificationValidation.Rejected)validation).Error);
        }

        await notifications.UpsertAsync(userId, ok.Input, cancellationToken);
        return new SaveNotificationsResult.Saved();
    }
}

/// <summary><c>GET /api/user/addresses</c>.</summary>
public sealed class ListAddresses(IFavoriteAddressRepository addresses)
{
    public Task<IReadOnlyList<AddressRow>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        addresses.ListAsync(userId, cancellationToken);
}

public abstract record SaveAddressResult
{
    private SaveAddressResult()
    {
    }

    public sealed record Saved : SaveAddressResult;

    public sealed record Invalid(SaveAddressError Error) : SaveAddressResult;
}

/// <summary><c>POST /api/user/addresses</c> (spec 007 §3.9): rótulo antes de endereço.</summary>
public sealed class SaveAddress(IFavoriteAddressRepository addresses)
{
    public async Task<SaveAddressResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = AddressRules.Validate(body);
        if (validation is not SaveAddressValidation.Ok ok)
        {
            return new SaveAddressResult.Invalid(((SaveAddressValidation.Rejected)validation).Error);
        }

        await addresses.UpsertAsync(userId, ok.Label, ok.Address, ok.Coordinates, cancellationToken);
        return new SaveAddressResult.Saved();
    }
}

public enum DeleteAddressResult
{
    Deleted,
    InvalidLabel,
    NotFound,
}

/// <summary><c>DELETE /api/user/addresses/{label}</c> (spec 007 §3.10).</summary>
public sealed class DeleteAddress(IFavoriteAddressRepository addresses)
{
    public async Task<DeleteAddressResult> ExecuteAsync(int userId, string label, CancellationToken cancellationToken)
    {
        if (!AddressLabels.TryNormalizePath(label, out var normalized))
        {
            return DeleteAddressResult.InvalidLabel;
        }

        return await addresses.DeleteAsync(userId, normalized, cancellationToken)
            ? DeleteAddressResult.Deleted
            : DeleteAddressResult.NotFound;
    }
}

/// <summary><c>GET /api/user/settings</c>: os quatro leitores em sequência; perfil sem linha encerra (as outras três nem rodam).</summary>
public sealed class GetUserSettings(
    GetProfile profile, GetApiKeySettings apiKeys, GetNotificationSettings notifications, ListAddresses addresses)
{
    public async Task<UserSettings?> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        var profileRow = await profile.ExecuteAsync(userId, cancellationToken);
        if (profileRow is null)
        {
            return null;
        }

        return new UserSettings(
            profileRow,
            await apiKeys.ExecuteAsync(userId, cancellationToken),
            await notifications.ExecuteAsync(userId, cancellationToken),
            await addresses.ExecuteAsync(userId, cancellationToken));
    }
}
