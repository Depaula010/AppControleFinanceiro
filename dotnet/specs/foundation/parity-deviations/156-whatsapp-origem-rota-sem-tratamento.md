# Desvio 156 — Resposta casa/trabalho/outro à pergunta de origem da rota não é tratada

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
Com dois ou mais endereços favoritos, `calcular rota` responde `🚗 *Qual endereço usar como origem?* ... Responda com: casa, trabalho ou outro` (`whatsapp_handler.py:251-265`). Nenhum código guarda esse estado nem reconhece a resposta: `casa` vai ao Gemini como uma mensagem qualquer.

## Comportamento proposto (.NET)
Guardar a escolha pendente (Redis, TTL curto) e, ao receber `casa`/`trabalho`/`outro`, calcular a rota com aquela origem.

## Impacto nos clientes
O fluxo de rota com vários endereços passa a funcionar.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
