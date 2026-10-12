# Desvio 010 — Qual fatura é paga

- **Estado:** aprovado
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Pagamento de fatura marca como `Paga` a fatura do ciclo da data do pagamento (`get_or_create_fatura`), sem comparar valor (`transaction_service.py:154`). Pagar uma fatura fechada antes do novo ciclo marca a errada.

## Comportamento proposto (.NET)
Pagar a fatura `Fechada` mais antiga; se não houver, a do ciclo corrente. Valor pago não é comparado (mantido).

## Impacto nos clientes
Fluxo de pagamento pelo bot/site paga a fatura que o usuário espera.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
