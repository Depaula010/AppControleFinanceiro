using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// A definição única, em SQL, de duas perguntas sobre um agendamento. O check-in (<see cref="CheckinQueries"/>) e o executor
/// (<see cref="ScheduleExecutionRepository"/>) usam estes fragmentos, então o que o check-in oferece é exatamente o que o executor aceita
/// e o que o executor considera pago é o que o check-in deixa de listar (os bugs de 15 a 20/10/2026 eram essas regras em divergência).
/// <list type="bullet">
/// <item><see cref="Postable"/> ↔ <see cref="ScheduleRules.IsPostable"/> (o teste de integração compara os dois).</item>
/// <item><see cref="Paid"/>: a ocorrência de um vencimento já está quitada.</item>
/// </list>
/// </summary>
internal static class SchedulePaymentSql
{
    /// <summary>
    /// Agendamento que dá para lançar pelo check-in (que confirma lembretes variáveis, desvio 252): ativo, de tipo lançável e, se parcelado,
    /// com parcela restante. Espelho de <see cref="ScheduleRules.IsPostable"/> com <c>allowVariableReminder = true</c>.
    /// </summary>
    public static string Postable(string schedule) => $"""
        ({schedule}.ativo = TRUE
         AND {schedule}.tipo_agendamento IN ('{ScheduleKinds.Fixed}', '{ScheduleKinds.VariableReminder}', '{ScheduleKinds.Installment}')
         AND ({schedule}.tipo_agendamento <> '{ScheduleKinds.Installment}'
              OR {schedule}.total_parcelas IS NULL
              OR COALESCE({schedule}.parcelas_executadas, 0) < {schedule}.total_parcelas))
        """;

    /// <summary>
    /// A ocorrência do vencimento <paramref name="expected"/> (expressão SQL de data) está paga, por uma transação <paramref name="tx"/>.
    /// <list type="number">
    /// <item>Transação do agendamento com <c>competencia</c> = vencimento: quita exatamente aquele vencimento (desvio 253).</item>
    /// <item>Transação antiga (<c>competencia</c> NULL): do agendamento ou, sem vínculo, de mesma conta e descrição (desvio 012), no
    /// mesmo mês/ano do vencimento, ou depois dele (sem limite se for do agendamento, até 20 dias se for pela descrição; desvio 251).</item>
    /// </list>
    /// <paramref name="description"/> é a descrição que a transação teria (a do agendamento, ou a da parcela no executor).
    /// </summary>
    public static string Paid(string schedule, string tx, string expected, string description) => $"""
        (({tx}.agendamento_id = {schedule}.id AND {tx}.competencia = {expected})
         OR ({tx}.competencia IS NULL
             AND ({tx}.agendamento_id = {schedule}.id
                  OR ({tx}.agendamento_id IS NULL AND {tx}.usuario_id = {schedule}.usuario_id
                      AND {tx}.conta_id = {schedule}.conta_id AND {tx}.descricao = {description}))
             AND ((EXTRACT(MONTH FROM {tx}.data_transacao) = EXTRACT(MONTH FROM {expected})
                   AND EXTRACT(YEAR FROM {tx}.data_transacao) = EXTRACT(YEAR FROM {expected}))
                  OR ({tx}.data_transacao > {expected}
                      AND ({tx}.agendamento_id = {schedule}.id OR {tx}.data_transacao <= {expected} + INTERVAL '20 days')))))
        """;
}
