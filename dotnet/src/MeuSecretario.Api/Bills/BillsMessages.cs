using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Api.Bills;

/// <summary>Textos exatos do Flask e dos desvios 072 a 074 (spec 005). Mudar uma palavra aqui é mudar o contrato.</summary>
public static class BillsMessages
{
    public const string NotFound = "Conta mensal não encontrada";
    public const string AccountNotFound = "Conta bancária não encontrada";
    public const string SubcategoryNotFound = "Subcategoria não encontrada";

    public const string Deleted = "Conta mensal removida com sucesso";

    public const string ListFailed = "Erro ao carregar contas mensais";
    public const string CreateFailed = "Erro ao criar conta mensal";
    public const string UpdateFailed = "Erro ao atualizar conta mensal";
    public const string DeleteFailed = "Erro ao remover conta mensal";
    public const string SummaryFailed = "Erro ao calcular resumo";

    private static readonly string Types = string.Join(", ", BillTypes.All);
    private static readonly string Periods = string.Join(", ", Periodicities.All);

    public static string For(BillError error) => error switch
    {
        BillError.DataMissing => "Dados não fornecidos",
        BillError.DescriptionRequired => "Descrição é obrigatória",
        BillError.DescriptionTooLong => "Descrição deve ter no máximo 255 caracteres",
        BillError.TypeInvalidCreate => $"Tipo de agendamento inválido. Valores permitidos: {Types}",
        BillError.PeriodicityInvalidCreate => $"Periodicidade inválida. Valores permitidos: {Periods}",
        BillError.SubcategoryRequired => "Subcategoria é obrigatória",
        BillError.AccountRequired => "Conta bancária é obrigatória",
        BillError.DayInvalid => "Dia de execução deve ser entre 1 e 31",
        BillError.AmountRequired => "Valor previsto é obrigatório para contas fixas ou parceladas",
        BillError.AmountNotPositive => "Valor previsto deve ser um número positivo",
        BillError.InstallmentsInvalid => "Número de parcelas deve ser um inteiro maior ou igual a 2",
        BillError.MonthInvalidCreate => "Mês de execução deve ser entre 1 e 12",
        BillError.MonthRequiredForAnnual => "Mês de execução é obrigatório para periodicidade anual",
        BillError.StartDateInvalid => "Data de início deve estar no formato YYYY-MM-DD",
        BillError.ReserveFlagInvalid => "Campo 'incluir_na_reserva' deve ser verdadeiro ou falso",
        BillError.DescriptionEmpty => "Descrição não pode ser vazia",
        BillError.TypeInvalidUpdate => $"Tipo inválido. Valores: {Types}",
        BillError.PeriodicityInvalidUpdate => $"Periodicidade inválida. Valores: {Periods}",
        BillError.MonthInvalidUpdate => "Mês deve ser entre 1 e 12",
        BillError.AmountInvalidUpdate => "Valor previsto inválido",
        BillError.InstallmentsRequired => "Número de parcelas é obrigatório para contas parceladas",
        BillError.InstallmentsBelowExecuted => "Número de parcelas não pode ser menor que as parcelas já executadas",
        BillError.NothingToUpdate => "Nenhum campo para atualizar",
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Erro de agendamento sem mensagem."),
    };
}
