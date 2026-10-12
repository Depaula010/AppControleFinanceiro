# Desvio 096 — SMS: remover a chamada inútil ao Gemini

- **Estado:** implementado (feature 011, 2026-10-09)
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
`transaction_handler.py:541-545` chama `extract_transaction_details` no ramo sem conta fixa e **ignora** o resultado. A chamada consome a cota da chave do usuário (008) e, se o Gemini falhar (cota, bloqueio, JSON inválido), a rota inteira responde 500 sem gravar o pagamento.

## Comportamento proposto (.NET)
Não chamar o Gemini nesse ramo; só a categorização (`categorize_transaction`) usa a IA.

## Impacto nos clientes
Nenhum campo muda. O pagamento deixa de falhar por causa de uma resposta que ninguém lê.

## Alternativas
Manter como está (paridade estrita), que é o que a feature 011 implementa até a decisão.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
