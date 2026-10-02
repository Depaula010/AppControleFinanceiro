using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Application.Accounts;

/// <summary><c>GET /api/accounts</c> (<c>api.py:283-335</c>).</summary>
public sealed class ListAccounts(IAccountRepository accounts)
{
    public Task<IReadOnlyList<AccountListItem>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        accounts.ListActiveAsync(userId, cancellationToken);
}

/// <summary><c>DELETE /api/accounts/{id}</c> (<c>api.py:660-721</c>). A checagem e a gravação são do repositório (uma transação).</summary>
public sealed class DeleteAccount(IAccountRepository accounts)
{
    public Task<DeleteAccountResult> ExecuteAsync(int userId, int accountId, CancellationToken cancellationToken) =>
        accounts.SoftDeleteAsync(userId, accountId, cancellationToken);
}

/// <summary>
/// <c>POST /api/accounts</c> (<c>api.py:340-498</c>): validações da spec §3.2 linhas 1 a 12 no Domain; a linha 13
/// (nome ativo em uso) e a reativação (desvio 032) no repositório.
/// </summary>
public sealed class CreateAccount(IAccountRepository accounts)
{
    public async Task<CreateAccountResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        return CreateRules.Validate(body) switch
        {
            CreateValidation.Rejected rejected => new CreateAccountResult.Invalid(rejected.Reason),
            CreateValidation.Ok ok => await accounts.CreateAsync(userId, ok.Data, cancellationToken),
            _ => throw new InvalidOperationException("Resultado de validação desconhecido."),
        };
    }
}

/// <summary>
/// <c>PUT /api/accounts/{id}</c> (<c>api.py:501-657</c>). A ordem é a da spec §3.3: corpo, 404, nome (e duplicidade),
/// demais campos, "nenhum campo", consistência com a conta atual, gravação.
/// </summary>
public sealed class UpdateAccount(IAccountRepository accounts)
{
    public async Task<UpdateAccountResult> ExecuteAsync(int userId, int accountId, RawBody? body, CancellationToken cancellationToken)
    {
        if (body is null || body.IsEmpty)
        {
            return new UpdateAccountResult.Invalid(AccountError.DataMissing);
        }

        var current = await accounts.GetActiveAsync(userId, accountId, cancellationToken);
        if (current is null)
        {
            return new UpdateAccountResult.NotFound();
        }

        string? newName = null;
        switch (UpdateRules.ReadName(body))
        {
            case NameStep.Failed failed:
                return new UpdateAccountResult.Invalid(failed.Reason);
            case NameStep.Value value:
                if (await accounts.NameTakenAsync(userId, value.Name, accountId, cancellationToken))
                {
                    return new UpdateAccountResult.NameTaken();
                }
                newName = value.Name;
                break;
        }

        var read = UpdateRules.ReadFields(body);
        if (read is FieldsResult.Rejected rejected)
        {
            return new UpdateAccountResult.Invalid(rejected.Reason);
        }

        var fields = ((FieldsResult.Ok)read).Changes;
        var changes = newName is null ? fields : fields.WithName(newName);
        if (changes.IsEmpty)
        {
            return new UpdateAccountResult.Invalid(AccountError.NothingToUpdate);
        }

        if (UpdateRules.CheckConsistency(current.TipoConta, current.DiaFechamento, current.DiaVencimento, changes) is { } inconsistent)
        {
            return new UpdateAccountResult.Invalid(inconsistent);
        }

        return await accounts.UpdateAsync(userId, accountId, changes, cancellationToken);
    }
}
