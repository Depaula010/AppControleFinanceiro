# Desvio 012 — Detecção de pagamento do mês por descrição

- **Estado:** aprovado
- **Features afetadas:** 004, 005, 013
- **Severidade:** correção

## Comportamento atual (Flask)
Queries antigas detectam "já pago" por igualdade de `descricao` no mês; o check-in usa `agendamento_id` com janela de +20 dias (`agendamentos_queries.py`). `forecast_service.py:117` usa `ILIKE '%'||descricao||'%'`. Descrições editadas ou parecidas geram falso positivo/negativo.

## Comportamento proposto (.NET)
Usar `agendamento_id` em todas as consultas; `descricao` só como fallback para transações antigas sem vínculo. Regra do ADR 5 (check-in noturno) preservada.

## Impacto nos clientes
Alertas de contas pendentes mais precisos.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
