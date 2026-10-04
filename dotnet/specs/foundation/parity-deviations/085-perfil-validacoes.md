# Desvio 085 — Perfil: e-mail único, limites, fuso e horários válidos

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 007
- **Severidade:** correção

## Comportamento atual (Flask)
E-mail repetido dá 500; sem limites de tamanho; fuso e horários sem validação.

## Comportamento proposto (.NET)
409 E-mail já cadastrado; limites; trim no nome; fuso IANA; horários do resumo matinal e do check-in validados.

## Impacto nos clientes
Dados inválidos passam a ser recusados.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 007.
