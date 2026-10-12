# Desvio 100 — Confirmação de transação: restaurar o emoji do cartão

- **Estado:** implementado (feature 011, 2026-10-09)
- **Features afetadas:** 011, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`transaction_confirmation_service.py:137,140` tem o caractere de substituição U+FFFD no lugar do emoji (arquivo corrompido): a mensagem de confirmação de compra no cartão mostra `� Nubank (na fatura)`. O .NET reproduz o caractere.

## Comportamento proposto (.NET)
Usar `💳` (o emoji do contexto, como `tipo_pagamento == 'credito'` em `format_confirmation_message`).

## Impacto nos clientes
Só o texto da mensagem de confirmação no WhatsApp.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
