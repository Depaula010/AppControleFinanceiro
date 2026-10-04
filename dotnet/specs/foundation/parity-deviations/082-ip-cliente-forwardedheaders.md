# Desvio 082 — IP do cliente pelo ForwardedHeaders

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 009
- **Severidade:** segurança

## Comportamento atual (Flask)
Confia no primeiro IP do X-Forwarded-For, que o cliente forja.

## Comportamento proposto (.NET)
Lê da direita para a esquerda pelo proxy confiável.

## Impacto nos clientes
Bloqueio de IP deixa de ser burlável.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
