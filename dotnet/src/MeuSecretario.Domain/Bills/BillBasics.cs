namespace MeuSecretario.Domain.Bills;

/// <summary>Tipos de agendamento (CHECK <c>agendamentos_tipo_agendamento_check</c>), na ordem da mensagem (RN-005-04).</summary>
public static class BillTypes
{
    public const string Fixed = "FIXO";
    public const string Variable = "LEMBRETE_VARIAVEL";
    public const string Installment = "PARCELADO";

    public static IReadOnlyList<string> All { get; } = [Fixed, Variable, Installment];

    /// <summary>Comparação exata: maiúsculas contam.</summary>
    public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);

    /// <summary><c>FIXO</c> e <c>PARCELADO</c> exigem valor previsto (RN-005-06).</summary>
    public static bool RequiresAmount(string type) => type is Fixed or Installment;
}

/// <summary>Periodicidades (CHECK <c>agendamentos_periodicidade_check</c>), na ordem da mensagem.</summary>
public static class Periodicities
{
    public const string Daily = "DIARIA";
    public const string Weekly = "SEMANAL";
    public const string Biweekly = "QUINZENAL";
    public const string Monthly = "MENSAL";
    public const string Annual = "ANUAL";

    public static IReadOnlyList<string> All { get; } = [Daily, Weekly, Biweekly, Monthly, Annual];

    public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Receita ou despesa de um agendamento, derivada da categoria (RN-005-09; desvio 075).</summary>
public static class BillKinds
{
    public const string IncomeGroup = "Renda";
    public const string GeneralGroup = "Geral";
    public const string IncomeFallbackMacro = "Receitas Gerais";

    /// <summary>Grupo <c>Renda</c> ou a macro <c>Receitas Gerais</c> do grupo <c>Geral</c> (mesmo predicado da 003).</summary>
    public static bool IsIncome(string? group, string? macro) =>
        group == IncomeGroup || (group == GeneralGroup && macro == IncomeFallbackMacro);
}

/// <summary>Texto da descrição: tamanho em pontos de código (Q-005-5: acima de 255 é recusado).</summary>
public static class BillDescription
{
    public const int MaxLength = 255;

    public static bool Fits(string trimmed) => trimmed.EnumerateRunes().Count() <= MaxLength;
}

/// <summary>Erros de validação das regras de agendamento (sem texto em português; o endpoint mapeia para a mensagem).</summary>
public enum BillError
{
    DataMissing,
    DescriptionRequired,
    DescriptionTooLong,
    TypeInvalidCreate,
    PeriodicityInvalidCreate,
    SubcategoryRequired,
    AccountRequired,
    DayInvalid,
    AmountRequired,
    AmountNotPositive,
    InstallmentsInvalid,
    MonthInvalidCreate,
    MonthRequiredForAnnual,
    StartDateInvalid,
    ReserveFlagInvalid,
    DescriptionEmpty,
    TypeInvalidUpdate,
    PeriodicityInvalidUpdate,
    MonthInvalidUpdate,
    AmountInvalidUpdate,
    InstallmentsRequired,
    InstallmentsBelowExecuted,
    NothingToUpdate,
}
