# Desvio 086 — Endereço preserva coordenadas quando o texto não muda

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 007
- **Severidade:** correção

## Comportamento atual (Flask)
O Angular nunca envia coordinates, então editar o endereço apaga a latitude e longitude geocodificadas.

## Comportamento proposto (.NET)
Coordenadas preservadas se o texto do endereço não mudou.

## Impacto nos clientes
Evita nova geocodificação e perda de dados.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 007.
