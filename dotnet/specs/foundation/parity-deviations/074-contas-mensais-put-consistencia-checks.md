# Desvio 074 — PUT confere o estado final contra os CHECK

- **Estado:** implementado (feature 005, 2026-10-04)
- **Features afetadas:** 005
- **Severidade:** correção

## Comportamento atual (Flask)
Editar ANUAL para outra periodicidade ou sair de PARCELADO viola chk_anual_mes/chk_parcelado e gera 500; ANUAL sem mês no POST também.

## Comportamento proposto (.NET)
Estado final validado (400); mes_execucao e total_parcelas são limpos quando deixam de se aplicar.

## Impacto nos clientes
Corrige 500 que o Angular já provoca.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 005.
