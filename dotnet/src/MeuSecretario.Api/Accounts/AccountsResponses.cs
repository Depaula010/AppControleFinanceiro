using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Accounts;

namespace MeuSecretario.Api.Accounts;

/// <summary>Corpos de resposta de contas (envelope A). As chaves saem na ordem da spec 002 §3.</summary>
public static class AccountsResponses
{
    public static IResult List(IReadOnlyList<AccountListItem> accounts) =>
        Envelopes.A.Success(
            data: accounts.Select(a => new Dictionary<string, object?>
            {
                ["id"] = a.Id,
                ["nome"] = a.NomeConta,
                ["banco"] = a.TipoConta,   // a coluna banco não existe: o Flask repete o tipo (api.py)
                ["tipo"] = a.TipoConta,
                ["saldo"] = (double)a.Saldo,
                ["nome_conta"] = a.NomeConta,
                ["tipo_conta"] = a.TipoConta,
                ["saldo_inicial"] = (double)a.SaldoInicial,
                ["cor_hex"] = a.CorHex,
                ["icone"] = a.Icone,
                ["limite_credito"] = Limit(a.LimiteCredito),
                ["dia_fechamento"] = a.DiaFechamento,
                ["dia_vencimento"] = a.DiaVencimento,
            }).ToList());

    public static IResult Created(AccountDetails account) =>
        Envelopes.A.Success(Details(account), AccountsMessages.Created, StatusCodes.Status201Created);

    public static IResult Updated(AccountDetails account) =>
        Envelopes.A.Success(Details(account), AccountsMessages.Updated);

    public static IResult Deleted() => Envelopes.A.Success(message: AccountsMessages.Deleted);

    public static IResult Error(string message, int statusCode) => Envelopes.A.Error(message, statusCode);

    private static Dictionary<string, object?> Details(AccountDetails a) => new()
    {
        ["id"] = a.Id,
        ["nome_conta"] = a.NomeConta,
        ["tipo_conta"] = a.TipoConta,
        ["saldo_inicial"] = (double)a.SaldoInicial,
        ["cor_hex"] = a.CorHex,
        ["icone"] = a.Icone,
        ["limite_credito"] = Limit(a.LimiteCredito),
        ["dia_fechamento"] = a.DiaFechamento,
        ["dia_vencimento"] = a.DiaVencimento,
    };

    /// <summary>RN-002-11: o Flask testa a falsidade do limite, então 0 sai <c>null</c> (desvio 049 rejeitado).</summary>
    private static double? Limit(decimal? limit) => limit is null or 0m ? null : (double)limit.Value;
}
