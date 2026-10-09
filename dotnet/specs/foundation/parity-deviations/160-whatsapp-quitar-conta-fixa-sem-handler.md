# Desvio 160 — Quitar Conta Fixa está no prompt do Gemini mas não tem ramo

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
O prompt de intenção (`gemini_service.py:222`) lista `Quitar Conta Fixa`; `whatsapp_handler.py` não tem ramo para ela (a quitação acontece antes, pelo passo de `paguei...`). Se o modelo a devolver, a resposta é `🤔 Não entendi...`.

## Comportamento proposto (.NET)
Remover a linha do prompt (ou rotear para o fluxo de pagamento).

## Impacto nos clientes
Menos respostas "não entendi" para frases como "marcar a luz como paga".

## Alternativas
Manter como está (paridade estrita: o texto do prompt é contrato), que é o que a 012 implementa.

## Recomendação
Aprovar rotear `Quitar Conta Fixa` para o fluxo de pagamento.

## Decisão do Rafael
Adiado para depois da virada (2026-10-09); continua `proposto`, não implementado.
