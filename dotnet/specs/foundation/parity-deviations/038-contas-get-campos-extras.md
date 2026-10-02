# Desvio 038 — Contas: GET devolve também os campos que o Angular espera

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
O GET devolve só `{id, nome, banco, tipo, saldo}`; POST/PUT exigem `nome_conta`/`tipo_conta`/`saldo_inicial`; o front usa outros nomes (`nome`, `tipo`, `saldo`, `cor`, `limite`) e o filtro de grupos compara `'corrente'` com `"Conta Corrente"`. Resultado: criar conta pelo site dá 400, editar só zera os dias e nenhuma conta aparece nos grupos.

## Comportamento proposto (.NET)
Só acréscimo: o GET passa a devolver também `nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento`, mantendo `id, nome, banco, tipo, saldo`. POST/PUT **não** mudam e o backend não aceita aliases. O Angular é corrigido no repositório do front (decisão G-8).

## Impacto nos clientes
Campos novos no JSON (clientes ignoram). O Angular precisa de ajuste de código para funcionar.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (acréscimo no GET + correção no front).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
