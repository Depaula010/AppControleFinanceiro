# Desvio 001 — Receita x Renda em transações

- **Estado:** aprovado
- **Features afetadas:** 004, 006, 011, 012
- **Severidade:** correção

## Comportamento atual (Flask)
A API REST grava e filtra `tipo_transacao` como `Receita`/`Despesa` (`api.py:796,1369`). O CHECK do banco só aceita `Renda`, `Despesa`, `Transferência`, `Pagamento Fatura`, então `Receita` é rejeitada. O resumo do dashboard soma apenas `Renda` (`api.py:137`) e o bot grava `Renda`. `POST/PUT /api/transacao` também grava `Receita`. **Confirmado em produção (2026-10-02):** das 330 transações existentes, 300 são `Despesa` e 30 são `Renda`; nenhuma `Receita`. O `POST /api/transactions` ainda falha por outro motivo (desvio 027), então a rota nunca gravou uma receita.

## Comportamento proposto (.NET)
O contrato HTTP continua usando `Receita` (entrada, saída e filtro `?tipo=`). O domínio converte `Receita` ⇄ `Renda` na borda; filtros por `Receita` encontram as transações do bot; o resumo soma `Renda`.

## Impacto nos clientes
Nenhuma mudança de campo. Receitas criadas pelo site passam a aparecer em `receitas_mes`; receitas do bot passam a aparecer em `?tipo=Receita`.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. O CHECK `transacoes_tipo_transacao_check` foi lido em produção e aceita só `Renda`, `Despesa`, `Transferência` e `Pagamento Fatura`, confirmando que `Receita` seria rejeitada.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
