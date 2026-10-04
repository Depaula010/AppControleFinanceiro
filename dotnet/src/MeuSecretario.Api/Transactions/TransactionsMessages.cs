using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Api.Transactions;

/// <summary>Textos exatos do Flask e dos desvios 066 e 068 (spec 004). Mudar uma palavra aqui é mudar o contrato.</summary>
public static class TransactionsMessages
{
    public const string NotFound = "Transação não encontrada";
    public const string AccountNotFound = "Conta não encontrada ou não pertence ao usuário";
    public const string SubcategoryNotFound = "Subcategoria não encontrada";

    public const string Created = "Transação criada com sucesso";
    public const string Updated = "Transação atualizada com sucesso";
    public const string Deleted = "Transação deletada com sucesso";

    public const string ListFailed = "Erro ao carregar transações";
    public const string CreateFailed = "Erro ao criar transação";
    public const string UpdateFailed = "Erro ao atualizar transação";
    public const string DeleteFailed = "Erro ao deletar transação";

    public static string For(ListQueryError error) => error switch
    {
        ListQueryError.LimitRange => "Parâmetro 'limit' deve estar entre 1 e 100",
        ListQueryError.OffsetNegative => "Parâmetro 'offset' deve ser maior ou igual a 0",
        ListQueryError.TypeInvalid => "Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'",
        ListQueryError.DateFormat => "Datas devem estar no formato YYYY-MM-DD",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Erro de listagem sem mensagem."),
    };

    public static string For(TransactionError error) => error switch
    {
        TransactionError.DescriptionRequired => "Campo 'descricao' é obrigatório",
        TransactionError.DescriptionTooLong => "Campo 'descricao' deve ter no máximo 255 caracteres",
        TransactionError.DescriptionInvalid => "Campo 'descricao' inválido",
        TransactionError.ValueRequired => "Campo 'valor' é obrigatório",
        TransactionError.ValueInvalid => "Valor inválido",
        TransactionError.ValueNegative => "Valor deve ser positivo",
        TransactionError.TypeInvalid => "Tipo deve ser 'Receita' ou 'Despesa'",
        TransactionError.DateRequired => "Campo 'data' é obrigatório",
        TransactionError.DateFormat => "Data deve estar no formato YYYY-MM-DD",
        TransactionError.SubcategoryRequired => "Campo 'subcategoria_id' é obrigatório",
        TransactionError.AccountRequired => "Campo 'conta_id' é obrigatório",
        TransactionError.NotesInvalid => "Campo 'observacoes' inválido",
        TransactionError.ConsolidatedInvalid => "Campo 'consolidada' inválido",
        TransactionError.NoDataProvided => "Nenhum dado fornecido para atualização",
        TransactionError.NoValidField => "Nenhum campo válido para atualização",
        TransactionError.NotEditable => "Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente.",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Erro de transação sem mensagem."),
    };
}
