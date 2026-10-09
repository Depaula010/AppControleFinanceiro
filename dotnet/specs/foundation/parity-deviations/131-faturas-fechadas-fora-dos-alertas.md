# Desvio 131 — Faturas `Fechada` ficam fora dos alertas de vencimento

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 013 (check-in e resumo matinal), 012
- **Severidade:** correção

## Comportamento atual (Flask)
`faturas_queries.py:43` (`get_faturas_vencidas`), `:111` (`get_faturas_vencendo_hoje`) e `finance_service.py:1162` (`get_upcoming_bills_and_invoices`, resumo matinal) filtram `f.status = 'Aberta'`. O job das 00:01 (`invoice_service.py:456`) fecha a fatura no dia seguinte ao fechamento, então a fatura que está para vencer (ou venceu) quase sempre já é `Fechada`: não aparece em "FATURAS VENCIDAS", em "FATURAS QUE VENCEM HOJE" nem nos alertas financeiros do resumo. O aviso de 3 dias (`get_invoices_due_soon`) olha só `Fechada` e `get_overdue_invoices` olha `Aberta` e `Fechada`: inconsistência entre as consultas.

## Comportamento proposto (.NET)
`status IN ('Aberta','Fechada')` nas três consultas (nunca `Paga`).

## Impacto nos clientes
Mais avisos: o check-in e o resumo passam a listar a fatura fechada que vence hoje ou já venceu e não foi paga.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
