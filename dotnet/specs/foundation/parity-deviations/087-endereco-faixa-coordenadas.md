# Desvio 087 — Faixa de latitude e longitude

- **Estado:** implementado (feature 007, 2026-10-05)
- **Features afetadas:** 007
- **Severidade:** correção

## Comportamento atual (Flask)
Aceita qualquer número.

## Comportamento proposto (.NET)
400 Coordenadas inválidas fora de -90..90 e -180..180.

## Impacto nos clientes
Nenhum cliente legítimo afetado.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 007.
