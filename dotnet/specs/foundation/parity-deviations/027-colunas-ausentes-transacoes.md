# Desvio 027 — POST/PUT /api/transactions gravam colunas que não existem

- **Estado:** aprovado
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
`api.py:1417` insere em `observacoes` e `origem`, e `api.py:1588-1590` atualiza `observacoes`. **Confirmado em produção:** `transacoes` não tem essas colunas (14 colunas, de `id` a `agendamento_id`). Portanto criar transação pelo site e editar com observação devolvem 500 hoje; as 330 transações existentes vieram do bot e dos atalhos. O modal do Angular envia `observacoes` (`transaction-modal.component.ts`).

## Comportamento proposto (.NET)
Migration aditiva (EF) cria `observacoes text NULL` e `origem varchar(20) NULL`. O POST grava `origem = 'api'` como o código já pretendia. Transações antigas ficam com `NULL`.

## Impacto nos clientes
Criar e editar transação pelo site passa a funcionar (hoje falha).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. Confirmar com o Rafael se o site realmente falha hoje ao salvar uma transação.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
