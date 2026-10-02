# Desvio 034 — Contas: dias e limite fora de cartão e troca de tipo

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
No POST só o cartão valida `limite_credito` e `dia_*`; em outro tipo, `dia_*` não nulo estoura `chk_cartao_credito` (500 `"Erro ao criar conta"`). No PUT, gravar dia em conta que não é cartão, ou trocar o tipo de um cartão que tem dias, dá 500 `"Erro ao atualizar conta"` (`api.py:394-416, 584-605`).

## Comportamento proposto (.NET)
400 com mensagem clara, por exemplo `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"`. Trocar o tipo de um cartão que tem dias é **recusado** com 400 (não limpa os dias em silêncio).

## Impacto nos clientes
400 no lugar de 500; mensagem nova.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
