# Desvio 032 — Reativar conta inativa de mesmo nome

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
O POST de conta com nome de uma conta **inativa** do mesmo usuário falha: hoje 500 pela UNIQUE `contas_usuario_id_nome_conta_key` (`api.py:420-431`); com o desvio 016, 400 `"Já existe uma conta com este nome"`. Excluir "Nubank" e recriar "Nubank" é impossível e a mensagem engana, porque a conta antiga está invisível. Produção hoje: 4 contas, todas ativas.

## Comportamento proposto (.NET)
O POST com nome de conta inativa do mesmo usuário reativa a linha (`ativa = true`) e sobrescreve tipo, saldo inicial, cor, ícone, limite e dias com o corpo recebido; responde 201 normal. Seguro porque a remoção só ocorre sem transações.

## Impacto nos clientes
O 400 enganoso some; o `id` da conta recriada é o antigo; `faturas` e `agendamentos` antigos ligados à conta reaparecem.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
