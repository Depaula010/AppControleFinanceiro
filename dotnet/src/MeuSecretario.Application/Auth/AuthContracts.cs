using MeuSecretario.Domain.Auth;

namespace MeuSecretario.Application.Auth;

public sealed record UserRecord(int Id, string Nome, string NumeroWhatsapp, string? SenhaHash);

public sealed record NewUser(string Nome, string NumeroWhatsapp, string SenhaHash, string ApiKeyAutomate);

/// <summary>Resultado da gravação do usuário: criado, ou número já cadastrado (inclusive por corrida, desvio 031).</summary>
public sealed record CreateUserResult(int? UserId)
{
    public bool WhatsappTaken => UserId is null;

    public static CreateUserResult Created(int userId) => new(userId);

    public static CreateUserResult Taken { get; } = new((int?)null);
}

public interface IUserRepository
{
    Task<UserRecord?> FindByWhatsappAsync(string digits, CancellationToken cancellationToken);

    /// <summary>Insere o usuário e as contas padrão numa única transação (tudo ou nada).</summary>
    Task<CreateUserResult> CreateWithDefaultAccountsAsync(
        NewUser user, IReadOnlyList<NewAccount> accounts, CancellationToken cancellationToken);
}
