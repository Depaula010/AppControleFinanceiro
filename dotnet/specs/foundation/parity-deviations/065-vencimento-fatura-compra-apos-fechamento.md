# Desvio 065 — Vencimento da fatura de compra após o fechamento

- **Estado:** implementado (feature 004, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Quando a compra é depois do fechamento e o dia de vencimento é maior ou igual ao de fechamento, a compra cai na fatura de 2 meses à frente em vez de 1. O ensure_current_invoice_exists usa outra fórmula, então a fatura populada e a da compra podem ser duas diferentes.

## Comportamento proposto (.NET)
Uma função única (InvoiceCycle.ForPurchase) calcula fechamento e vencimento para compra, populador e pagamento.

## Impacto nos clientes
Cartões com vencimento >= fechamento passam a ter a compra na fatura certa. Condição: conferir Q-004-2 (consulta 1 do 06-conferencia-onda-2.sql).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
