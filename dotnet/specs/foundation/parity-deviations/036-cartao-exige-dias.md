# Desvio 036 — Contas: cartão de crédito exige os dois dias

- **Estado:** aprovado
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
O POST aceita cartão sem `dia_fechamento`/`dia_vencimento` (`api.py:394-416`); o cadastro (`auth.py`) os exige; sem os dias `get_or_create_fatura` devolve `None` (`invoice_service.py:23-24`) e as compras no cartão ficam sem fatura, sem erro.

## Comportamento proposto (.NET)
No POST, cartão exige os dois dias (400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"`); no PUT, não aceitar `null` em cartão.

## Impacto nos clientes
Clientes que criam cartão sem dias passam a receber 400.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
