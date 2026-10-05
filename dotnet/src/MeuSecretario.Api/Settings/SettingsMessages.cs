using MeuSecretario.Domain.Settings;

namespace MeuSecretario.Api.Settings;

/// <summary>Textos exatos da spec 007 (§3) e dos desvios 084 e 085. Mudar uma palavra aqui é mudar o contrato.</summary>
public static class SettingsMessages
{
    public const string UserNotFound = "Usuário não encontrado";
    public const string InvalidData = "Dados inválidos";

    public const string ProfileFetchFailed = "Erro ao buscar perfil";
    public const string ProfileUpdated = "Perfil atualizado com sucesso";
    public const string ProfileUpdateFailed = "Erro ao atualizar perfil";
    public const string EmailTaken = "E-mail já cadastrado";

    public const string ApiKeysFetchFailed = "Erro ao buscar chaves API";
    public const string InvalidProvider = "Provedor inválido. Use: gemini, weather, openroute";
    public const string KeyRequired = "Chave API obrigatória quando usar_chave_propria=true";
    public const string ApiKeySaved = "Chave API salva com sucesso";
    public const string ApiKeySaveFailed = "Erro ao salvar chave API";
    public const string ValidateFieldsRequired = "Campos 'type' e 'key' são obrigatórios";
    public const string ValidateUnsupportedProvider = "Provedor não suportado para validação";
    public const string ValidateUnavailable = "Não foi possível validar (timeout ou erro de rede)";
    public const string ValidateFailed = "Erro ao validar chave API";

    public const string NotificationsFetchFailed = "Erro ao buscar notificações";
    public const string InvalidTimeFormat = "Formato de horário inválido (use HH:MM)";
    public const string EveningHourRange = "Horário do check-in noturno deve ser entre 18:00 e 23:00";
    public const string NotificationsSaved = "Notificações atualizadas com sucesso";
    public const string NotificationsSaveFailed = "Erro ao salvar notificações";

    public const string AddressesFetchFailed = "Erro ao buscar endereços";
    public const string InvalidLabel = "Label inválido. Use: casa, outro, trabalho";
    public const string AddressRequired = "Endereço é obrigatório";
    public const string InvalidCoordinates = "Coordenadas inválidas";
    public const string AddressSaved = "Endereço salvo com sucesso";
    public const string AddressSaveFailed = "Erro ao salvar endereço";
    public const string AddressNotFound = "Endereço não encontrado";
    public const string AddressDeleted = "Endereço removido com sucesso";
    public const string AddressDeleteFailed = "Erro ao remover endereço";

    public const string SettingsFetchFailed = "Erro ao buscar configurações";

    public static string For(ProfileError error) => error switch
    {
        ProfileError.NothingToUpdate => "Nenhum campo válido para atualizar",
        ProfileError.MonthsNotInteger => "Meses de reserva deve ser um número inteiro",
        ProfileError.NameTooShort => "Nome deve ter pelo menos 2 caracteres",
        ProfileError.StateLength => "Estado deve ter 2 caracteres (ex: SP)",
        ProfileError.MonthsRange => "Meses de reserva deve ser entre 1 e 36",
        ProfileError.InvalidData => InvalidData,
        ProfileError.NameTooLong => $"Nome deve ter no máximo {ProfileRules.NameMax} caracteres",
        ProfileError.EmailTooLong => $"E-mail deve ter no máximo {ProfileRules.EmailMax} caracteres",
        ProfileError.CityTooLong => $"Cidade deve ter no máximo {ProfileRules.CityMax} caracteres",
        ProfileError.InvalidTimeZone => "Fuso horário inválido",
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };

    public static string For(SaveKeyError error) => error switch
    {
        SaveKeyError.InvalidProvider => InvalidProvider,
        SaveKeyError.InvalidData => InvalidData,
        SaveKeyError.KeyRequired => KeyRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };

    public static string For(ValidateKeyError error) => error switch
    {
        ValidateKeyError.MissingFields => ValidateFieldsRequired,
        ValidateKeyError.UnsupportedProvider => ValidateUnsupportedProvider,
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };

    public static string For(NotificationError error) => error switch
    {
        NotificationError.InvalidTimeFormat => InvalidTimeFormat,
        NotificationError.EveningHourRange => EveningHourRange,
        NotificationError.InvalidData => InvalidData,
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };

    public static string For(SaveAddressError error) => error switch
    {
        SaveAddressError.InvalidLabel => InvalidLabel,
        SaveAddressError.AddressRequired => AddressRequired,
        SaveAddressError.InvalidData => InvalidData,
        SaveAddressError.InvalidCoordinates => InvalidCoordinates,
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };
}
