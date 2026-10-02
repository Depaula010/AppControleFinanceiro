# Desvio 040 — Categorias: Receita inclui Receitas Gerais, Despesa não

- **Estado:** aprovado
- **Features afetadas:** 003
- **Severidade:** correção

## Comportamento atual (Flask)
`tipo=Despesa` usa `grupo != 'Renda'` (`api.py:1714-1715`), então a macro `Receitas Gerais` (grupo `Geral`) aparece como categoria de despesa; `tipo=Receita` usa `grupo = 'Renda'` (`api.py:1712-1713`), então o `Outros` de renda (fallback do bot, `category_service.py:62`) nunca é oferecido ao lançar uma receita.

## Comportamento proposto (.NET)
`Receita` = grupo `Renda` mais a macro `Receitas Gerais`; `Despesa` = grupos diferentes de `Renda` menos a macro `Receitas Gerais`.

## Impacto nos clientes
`Receita` passa de 4 para 5 macros (13 subs); `Despesa` de 14 para 13 macros (47 subs). Nenhum campo muda. Baixo risco.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
