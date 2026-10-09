# Desvio 190 — Desativar resumo matinal ou check-in por WhatsApp ativa

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`whatsapp_handler.py:1204-1208` e `:1259-1263` testam `ativar/ligar/ativa/ative` com `in` **antes** de `desativar/desligar/desative`. Como "desativar", "desligar" e "desative" contêm "ativar", "ligar" e "ative", a mensagem "desativar resumo matinal" cai no ramo de **ativar** (o usuário continua recebendo a notificação e a resposta diz "Resumo matinal ativado"). Só "off" (check-in) chega ao ramo de desativar. Confirmado pelo harness (cenários `resumo_desativar_vira_ativar`, `resumo_desligar_vira_ativar`, `checkin_desligar_vira_ativar`).

## Comportamento proposto (.NET)
Testar as palavras de desativar primeiro (ou casar por palavra inteira).

## Impacto nos clientes
"Desativar/desligar" passa a desligar a notificação, como o usuário pede.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
