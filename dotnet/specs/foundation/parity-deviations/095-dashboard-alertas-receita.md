# Desvio 095 — Dashboard: receita aparece como conta a vencer

- **Estado:** proposto
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`api.py:1176-1196`: a consulta não olha o grupo da subcategoria; agendamentos de receita (grupo `Renda`) aparecem como alerta, inclusive `danger` no dia.

## Comportamento proposto (.NET)
Excluir agendamentos de receita (mesmo predicado dos desvios 075 e 076: grupo `Renda` ou macro `Receitas Gerais`).

## Impacto nos clientes
Salário e outras receitas deixam de aparecer em "contas a vencer".

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
