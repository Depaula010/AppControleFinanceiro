namespace MeuSecretario.Application.Bills;

/// <summary>
/// Porta pública da reserva de emergência (spec 005 §7; desvio 014). Única implementação do cálculo: o resumo da 005,
/// a 011 (<c>PATCH .../reserva</c>) e a 012 (consulta e detalhes no WhatsApp) usam esta porta e não repetem fórmula nem SQL.
/// </summary>
public interface IEmergencyReserveService
{
    /// <summary><c>get_reserva_status</c>: (gasto_mensal_equivalente, reserva_ideal, meses), sem arredondar.</summary>
    Task<EmergencyReserveStatus> GetStatusAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Linhas incluídas na reserva, por impacto decrescente (detalhes da 012 e a reserva_handler da 011).</summary>
    Task<IReadOnlyList<EmergencyReserveItem>> GetItemsAsync(int userId, CancellationToken cancellationToken);
}

public sealed record EmergencyReserveStatus(double MonthlyEquivalent, double Total, int Months);

public sealed record EmergencyReserveItem(
    int ScheduleId,
    string Description,
    double Amount,
    string Periodicity,
    string? Category,
    double Impact);
