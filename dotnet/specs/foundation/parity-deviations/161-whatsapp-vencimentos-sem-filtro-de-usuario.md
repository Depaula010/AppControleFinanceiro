# Desvio 161 — Vencimentos comparam a descrição sem filtrar o usuário

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`get_vencimentos_periodo` (`finance_service.py:1491-1496, 1526-1531, 1563-1568`) decide "já pago" com `NOT EXISTS (SELECT 1 FROM Transacoes t WHERE t.descricao = a.descricao AND mês/ano ...)` **sem** `t.usuario_id`: uma transação de outro usuário com a mesma descrição esconde a conta. Hoje há um só usuário. `fixed_bills_service.get_pending_bills_for_month` filtra o usuário.

## Comportamento proposto (.NET)
Acrescentar `AND t.usuario_id = a.usuario_id` (e, pelo desvio 012, preferir `agendamento_id`).

## Impacto nos clientes
Nenhum com um usuário; corrige o isolamento com vários.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar antes de existir um segundo usuário.

## Decisão do Rafael
(pendente)
