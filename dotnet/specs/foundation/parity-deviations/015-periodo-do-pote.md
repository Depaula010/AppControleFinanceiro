# Desvio 015 — Três definições de período de pote

- **Estado:** aprovado
- **Features afetadas:** 004, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`budget_validation_service.py`: SEMANAL seg–dom, QUINZENAL 1–15/16–fim, MENSAL, ANUAL ano civil. `transaction_feedback_service.py:37`: `date_trunc('week')` para SEMANAL e QUINZENAL, mês para o resto. `pot_service.py:28`: sempre o mês corrente.

## Comportamento proposto (.NET)
Usar a definição de `budget_validation_service` (a única com testes) em todos os usos.

## Impacto nos clientes
Mensagens de feedback de pote com percentuais ligeiramente diferentes para potes semanais/quinzenais.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
