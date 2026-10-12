# Desvio 030 — Subcategoria global duplicada: Outros (macro 17)

- **Estado:** aprovado
- **Features afetadas:** 003, 004, 011
- **Severidade:** correção

## Comportamento atual (Flask)
**Confirmado em produção:** existem 2 linhas globais `Outros` em `macro_id = 17` (a UNIQUE `(macro_id, nome_sub, usuario_id)` não impede duplicata quando `usuario_id` é NULL). As buscas por nome com `LIMIT 1` (por exemplo a subcategoria padrão `Outros`) escolhem uma delas sem `ORDER BY`.

## Comportamento proposto (.NET)
Toda busca por nome ordena por `id` e pega a primeira (determinístico, normalmente a mesma que o Postgres devolve hoje). Não apagar a duplicata: pode haver transações apontando para ela. Opcional: migration cria índice único parcial `(macro_id, nome_sub) WHERE usuario_id IS NULL` depois de o Rafael decidir qual manter e reapontar a outra.

## Impacto nos clientes
Nenhum.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar a ordenação por `id`. Decidir a limpeza só se quiser.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
