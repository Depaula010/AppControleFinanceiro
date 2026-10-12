# Desvio 152 — Configurar Notificações pelo ramo do Gemini grava e responde 500

- **Estado:** implementado (feature 012, 2026-10-10)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
Quando o texto não fala de resumo matinal nem de check-in, `whatsapp_handler.py:1306-1370` pergunta ao Gemini o `tipo` (`agenda_diaria` ou `contas_vencer`). Os dois ramos atualizam `notificationconfigs` (via os métodos obsoletos que redirecionam para o resumo matinal e os alertas financeiros) e depois leem `config['agenda_diaria_ativa']`/`config['contas_vencer_ativa']`, chaves que `get_or_create_config` não devolve: `KeyError` ⇒ 500 `Erro ao processar.` **depois** de gravar. Tipo desconhecido responde `🤔 Não entendi qual tipo de notificação você quer configurar.`.

## Comportamento proposto (.NET)
Responder o status atual com as chaves que existem (`resumo_matinal_*` e `alertas_financeiros_ativos`).

## Impacto nos clientes
O usuário passa a receber a confirmação (hoje recebe erro mesmo com a configuração gravada).

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
