# Desvio 044 — LGPD: deletar-conta apaga de fato

- **Estado:** aprovado
- **Features afetadas:** 008
- **Severidade:** segurança

## Comportamento atual (Flask)
`DELETE /api-keys/lgpd/deletar-conta/<id>` só apaga 6 tabelas (chaves, preferências, uso, logs, consentimentos, assinaturas); `usuarios` e os dados financeiros ficam, mas a resposta diz que tudo foi deletado (`servico_consentimento_lgpd.py:470-488`).

## Comportamento proposto (.NET)
Na mesma transação e em ordem explícita (os `RESTRICT` de `transacoes` e `agendamentos` exigem): apaga `transacoes`, `agendamentos`, `contas` (cascata `faturas`), `potesdegastos`, `subcategoria` e `macrocategoria` do usuário e a linha de `usuarios` (cascata do resto, inclusive `googlecalendartokens`); apaga também as chaves Redis do usuário (`pending_*`, `nightly_checkin*`); `registros_deletados` ganha chaves extras e `total_registros` as soma; usuário inexistente ⇒ 404.

## Impacto nos clientes
A operação passa a ser realmente irreversível e acionável por quem tem a chave global; hoje é inofensiva. Nenhum cliente conhecido a chama.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar com condições (ver abaixo).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condições:** (1) backup completo do banco antes da virada; (2) a rota fica desligada em produção pelo desvio 052; (3) as chaves Redis do usuário também são apagadas.
