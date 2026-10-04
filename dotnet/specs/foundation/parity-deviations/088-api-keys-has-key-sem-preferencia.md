# Desvio 088 — has_key reflete chave sem linha de preferência

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 007
- **Severidade:** correção

## Comportamento atual (Flask)
has_key sai false quando há chave cadastrada sem linha de preferência (3 chaves em produção).

## Comportamento proposto (.NET)
LEFT JOIN: has_key reflete a chave real.

## Impacto nos clientes
A tela mostra o estado correto.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 007.
