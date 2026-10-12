# Desvio 080 — Usuário inexistente na configuração de notificação: 404

- **Estado:** implementado (feature 009a, 2026-10-04); aprovado na onda 2 em 2026-10-04
- **Features afetadas:** 009
- **Severidade:** correção

## Comportamento atual (Flask)
500 com str(e).

## Comportamento proposto (.NET)
404 com mensagem em português.

## Impacto nos clientes
Resposta de erro mais clara.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
