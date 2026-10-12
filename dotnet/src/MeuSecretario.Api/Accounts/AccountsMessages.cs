using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Api.Accounts;

/// <summary>Textos exatos do Flask e dos desvios 033 a 037 (spec 002). Mudar uma palavra aqui é mudar o contrato.</summary>
public static class AccountsMessages
{
    public const string NameDuplicate = "Já existe uma conta com este nome";
    public const string NameDuplicateOther = "Já existe outra conta com este nome";
    public const string NotFound = "Conta não encontrada";

    public const string Created = "Conta criada com sucesso";
    public const string Updated = "Conta atualizada com sucesso";
    public const string Deleted = "Conta removida com sucesso";

    public const string ListFailed = "Erro ao carregar contas";
    public const string CreateFailed = "Erro ao criar conta";
    public const string UpdateFailed = "Erro ao atualizar conta";
    public const string DeleteFailed = "Erro ao remover conta";

    public static string TypeInvalid { get; } =
        "Tipo de conta inválido. Valores permitidos: " + string.Join(", ", AccountTypes.All);

    public static string HasTransactions(int count) =>
        $"Não é possível excluir esta conta. Existem {count} transações vinculadas.";

    public static string HasActiveSchedules(int count) =>
        $"Não é possível excluir esta conta. Existem {count} agendamentos ativos vinculados.";

    public static string For(AccountError error) => error switch
    {
        AccountError.DataMissing => "Dados não fornecidos",
        AccountError.NameRequired => "Nome da conta é obrigatório",
        AccountError.TypeRequired => "Tipo da conta é obrigatório",
        AccountError.TypeInvalid => TypeInvalid,
        AccountError.BalanceInvalid => "Saldo inicial deve ser um número válido",
        AccountError.ColorInvalid => "Cor inválida. Use o formato #RRGGBB",
        AccountError.IconInvalid => "Ícone inválido",
        AccountError.LimitInvalid => "Limite de crédito deve ser um número válido",
        AccountError.DaysOnlyForCard => "Dia de fechamento e vencimento só se aplicam a Cartão de Crédito",
        AccountError.ClosingDayInvalid => "Dia de fechamento deve ser entre 1 e 31",
        AccountError.DueDayInvalid => "Dia de vencimento deve ser entre 1 e 31",
        AccountError.CardNeedsDays => "Cartão de Crédito exige dia de fechamento e dia de vencimento",
        AccountError.NameEmpty => "Nome da conta não pode ser vazio",
        AccountError.NothingToUpdate => "Nenhum campo para atualizar",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Erro de conta sem mensagem."),
    };
}
