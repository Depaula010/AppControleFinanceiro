# Desvio 286 — Macro global "Trabalho" (custos com trabalho)

- **Estado:** implementado (migration `0140_categoria_trabalho`); aprovado pelo Rafael em 2026-11-20 ("incluir outra categoria de despesa: Trabalho")
- **Features afetadas:** 003 (dados de referência)
- **Severidade:** acréscimo

## Comportamento atual
Não há CRUD de categoria; as globais vêm só de migration (18 macros, 60 subcategorias). Gastos de trabalho (Claude Code, gasolina, marmita) caem em macros que misturam uso pessoal.

## Comportamento proposto (.NET)
Migration aditiva e idempotente cria a macro global `Trabalho` (grupo `Despesa Essencial`, `ordem_macro` 6) com as subcategorias:
`Ferramentas e Assinaturas (Claude Code, etc.)`, `Combustível e Deslocamento (Trabalho)`, `Alimentação no Trabalho (Marmita)`, `Equipamentos e Material de Trabalho`, `Outros`.
- Entra em `?tipo=Despesa` e na listagem completa; nenhuma rota, campo ou filtro muda.
- Só insere se não existir macro global `Trabalho`; em banco já migrado não faz nada.
- O reset dos testes continua semeando só a `0020`, então os ACs 003-01..45 (18 macros/60 subs) seguem valendo; o teste novo cobre a `0140`.

## Impacto nos clientes
Angular e bot passam a listar mais uma macro de despesa (leitura dinâmica). Relatórios por grupo contam `Trabalho` em `Despesa Essencial`.

## Decisão do Rafael
Grupo `Despesa Essencial` é a escolha padrão; se preferir `Despesa Discricionária`, trocar na migration antes do deploy.
