# Desvio 025 — Tipo 'Cartao de Credito' sem acento no handler do Automate

- **Estado:** implementado (feature 011, 2026-10-05)
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
`transaction_handler.py:77,92` compara com `'Cartao de Credito'`; o banco guarda `Cartão de Crédito`. O ramo de cartão do `/webhook-automate` nunca dispara.

## Comportamento proposto (.NET)
Usar o tipo correto (constante única de domínio).

## Impacto nos clientes
Compras via Automate em cartão passam a ser vinculadas à fatura.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
