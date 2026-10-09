# Desvio 150 — Confirmar despesa/renda por WhatsApp falha quando não há agendamento

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`RendaIntent`/`DespesaIntent` gravam a pendência com `descricao_final = descricao_agendamento` (`transaction_intents.py:105, 266`), que é `null` quando a mensagem não casa com um agendamento recorrente (o caso comum: "gastei 50 na padaria"). Ao confirmar, `whatsapp_handler.py:343` faz `dados.get('descricao_final', dados.get('descricao'))`: a chave existe com valor `null`, então o `get` devolve `None` e `create_transaction` insere `descricao = NULL`. O dump de produção diz `transacoes.descricao NOT NULL` (`data-model.md`): o `INSERT` falha, a pendência já foi apagada e o webhook responde 500 `Erro ao processar.`. Só a confirmação de transações ligadas a um agendamento (ou vindas de `/api/transacao`, que preenche `descricao_final`) funciona.

## Comportamento proposto (.NET)
Usar `descricao_final` só se for verdadeiro, senão `descricao` (`dados.get('descricao_final') or dados.get('descricao')`).

## Impacto nos clientes
O `OK` de despesas e rendas registradas pelo WhatsApp passa a gravar. Nenhum campo de API muda.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 012 implementa até a decisão** (a confirmação sem agendamento dá 500, como no Flask).

## Recomendação
Aprovar com urgência: é o fluxo central do bot. Antes, confirmar em produção se `transacoes.descricao` é mesmo `NOT NULL` (Q-012-1).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
