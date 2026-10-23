# Desvio 257 — Check-in: "Previsto em" da receita pendente usa o vencimento real

- **Estado:** implementado (PR #37)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
`format_consolidated_checkin_message` escreve "Previsto em {dia do agendamento}/{mês de hoje}". Como a consulta aceita vencimento do mês anterior até 7 dias após a virada, em 03/11 uma receita do dia 28 aparecia como "28/11" (data futura).

## Comportamento proposto (.NET)
Usa o dia e o mês do `data_vencimento_real` (28/10). Sem vencimento real mantém o comportamento do Flask.

## Impacto nos clientes
Só a data escrita na linha das receitas pendentes; nenhum cenário de fixture do Flask mudou.

## Alternativas
Manter (a data futura confunde).

## Recomendação
Aprovar.

## Decisão do Rafael
Auditoria do engenheiro na conversa de 20/10/2026, aceita pelo Rafael ("pode fazer"). Implementado no PR #37 (RN-013-45).
