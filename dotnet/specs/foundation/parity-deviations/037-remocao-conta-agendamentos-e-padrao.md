# Desvio 037 — Contas: remoção considera agendamentos ativos e conta padrão

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
O DELETE só conta `transacoes` (`api.py:681-691`). Conta com `agendamentos` ativos ou usada como `usuarios.conta_padrao_*` é inativada sem aviso e o motor continua lançando nela; faturas ficam órfãs. **Produção hoje:** a conta 2 (Cartão Inter) tem 5 agendamentos ativos e a conta 3 (Mercado pago) tem 23.

## Comportamento proposto (.NET)
Bloquear com 400 `"Não é possível excluir esta conta. Existem N agendamentos ativos vinculados."` e, ao remover, zerar `usuarios.conta_padrao_id/_renda_id/_despesa_id` que a referenciem, na mesma transação.

## Impacto nos clientes
Novo 400: o usuário precisa desativar os agendamentos antes. Na prática, Cartão Inter e Mercado pago deixam de poder ser excluídas enquanto tiverem agendamentos ativos.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (as duas partes).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Dado de produção (2026-10-02):** a conta 2 tem 5 agendamentos ativos e a conta 3 tem 23; as duas deixam de poder ser excluídas até os agendamentos serem desativados.
