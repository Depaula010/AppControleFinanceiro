# Desvio 077 — gemini-cache-clear com corpo vazio limpa tudo

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 009
- **Severidade:** correção

## Comportamento atual (Flask)
Corpo vazio gera 500.

## Comportamento proposto (.NET)
Corpo vazio equivale a limpar todo o cache.

## Impacto nos clientes
Quem chama sem corpo passa a funcionar.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
