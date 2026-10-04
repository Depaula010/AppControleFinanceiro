# Desvio 059 — Bot: dividir mensagens com mais de 4096 caracteres

- **Estado:** implementado (feature 010, 2026-10-02)
- **Features afetadas:** 010, 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
O bot recusa texto acima de 4096 caracteres com 400 (`messageRoutes.js:46`) e o Flask conta como falha silenciosa.

## Comportamento proposto (.NET)
`IBotClient.SendTextAsync` divide o texto em partes de até 4096 caracteres (em quebra de linha) e envia em sequência.

## Impacto nos clientes
Resumos e relatórios longos chegam em mais de uma mensagem em vez de se perderem.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
