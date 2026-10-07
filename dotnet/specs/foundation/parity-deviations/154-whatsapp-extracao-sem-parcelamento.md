# Desvio 154 — A extração de despesa e renda não pede data, conta, categoria nem parcelamento

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`extract_expense_params`/`extract_income_params` usam `extract_transaction_details`, cujo prompt só pede `valor_decimal` e `descricao_bruta` (`gemini_service.py:390-406`), mas leem `data`, `conta`, `categoria` e `parcelamento` do resultado (`:1858-1861`). Esses campos só existem se o modelo os inventar. A validação de parcelas (2 a 48) e o fluxo parcelado (`create_parcelamento_agendamento`) são, na prática, código morto; `extract_parcelamento_info` (`:1137-1188`) tem prompt pronto e nenhum chamador.

## Comportamento proposto (.NET)
Chamar `extract_parcelamento_info` para despesas (e passar a data relativa pedida), alimentando o fluxo que já existe.

## Impacto nos clientes
"Comprei um notebook de 3000 em 10x" passa a registrar a compra parcelada.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa (campos lidos do JSON do modelo se existirem).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
