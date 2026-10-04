using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Api.Transactions;

/// <summary>Corpos de resposta de transações (envelope A). As chaves saem na ordem da spec 004 §3.</summary>
public static class TransactionsResponses
{
    public static IResult List(TransactionPage page, ListFilter filter) =>
        Envelopes.A.Success(data: new Dictionary<string, object?>
        {
            ["total"] = page.Total,
            ["limit"] = filter.Limit,
            ["offset"] = filter.Offset,
            ["transactions"] = page.Items.Select(Item).ToList(),
        });

    /// <summary>O eco é o da entrada: <c>valor</c> com sinal sem arredondar, <c>tipo</c> como veio e <c>data</c> como texto recebido.</summary>
    public static IResult Created(NewTransactionData input, PostedTransaction posted)
    {
        var amount = (double)input.Amount;
        return Envelopes.A.Success(
            new Dictionary<string, object?>
            {
                ["id"] = posted.TransactionId,
                ["descricao"] = input.Description,
                ["valor"] = input.Kind == TransactionKind.Income ? amount : -amount,
                ["tipo"] = input.Kind == TransactionKind.Income ? "Receita" : "Despesa",
                ["data"] = input.DateText,
            },
            TransactionsMessages.Created,
            StatusCodes.Status201Created);
    }

    /// <summary>O PUT devolve o que foi gravado (valor arredondado pelo banco, tipo mapeado, data ISO).</summary>
    public static IResult Updated(UpdatedTransaction transaction) =>
        Envelopes.A.Success(
            new Dictionary<string, object?>
            {
                ["id"] = transaction.Id,
                ["descricao"] = transaction.Description,
                ["valor"] = (double)transaction.Amount,
                ["tipo"] = TransactionTypes.ToApi(transaction.DbType),
                ["data"] = Iso(transaction.Date),
            },
            TransactionsMessages.Updated);

    public static IResult Deleted() => Envelopes.A.Success(message: TransactionsMessages.Deleted);

    public static IResult Error(string message, int statusCode) => Envelopes.A.Error(message, statusCode);

    private static Dictionary<string, object?> Item(TransactionListItem t) => new()
    {
        ["id"] = t.Id,
        ["descricao"] = t.Description,
        ["valor"] = (double)t.Amount,
        ["tipo"] = TransactionTypes.ToApi(t.DbType),
        ["data_transacao"] = Iso(t.Date),
        ["categoria"] = t.Category,
        ["subcategoria"] = t.Subcategory,
        ["conta"] = t.AccountName,
        ["consolidada"] = t.Consolidated,
        // Desvio 067: só acréscimo, no fim, com os nomes que o Angular já lê.
        ["conta_id"] = t.AccountId,
        ["subcategoria_id"] = t.SubcategoryId,
        ["observacoes"] = t.Notes,
        ["data"] = Iso(t.Date),
        ["conta_bancaria"] = t.AccountName,
        ["conta_bancaria_id"] = t.AccountId,
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
