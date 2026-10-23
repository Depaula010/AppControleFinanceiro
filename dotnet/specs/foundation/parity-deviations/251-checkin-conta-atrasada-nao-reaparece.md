# Desvio 251 — Check-in: conta atrasada confirmada não volta no dia seguinte

- **Estado:** implementado (PR #36)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
A consulta do check-in (`agendamentos_queries.py`) só considera paga a transação feita até 20 dias depois do vencimento. O check-in lança a conta confirmada em "hoje"; uma conta com mais de 20 dias de atraso (ex.: água venceu em 27/09, confirmada em 19/10) nunca saía da lista e reaparecia a cada check-in.

## Comportamento proposto (.NET)
Transação com o `agendamento_id` depois do vencimento (exclusive) quita aquele vencimento **sem limite de dias**. Sem `agendamento_id` (fallback por descrição, desvio 012) segue o limite de 20 dias. Quatro cláusulas `NOT EXISTS` em `CheckinQueries` (pendentes e atrasadas, mês atual e anterior).

## Impacto nos clientes
O check-in deixa de repetir conta já confirmada. Nenhuma rota nem formato muda. Substituído em parte pelo desvio 253 (competência), que torna "paga" um fato e não uma inferência por janela.

## Alternativas
Manter a janela de 20 dias (rejeitado: o bug ocorreu em produção, 15 a 20/10/2026). Subir a janela para 60 dias (adiaria o problema).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido pelo Rafael na conversa de 20/10/2026 ("tem um bug, pode corrigir?"). Implementado no PR #36 (spec 013, RN-013-42).
