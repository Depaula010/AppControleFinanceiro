# Desvio 048 — API keys: tipos JSON inesperados viram 400

- **Estado:** aprovado
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
O Flask repassa ao banco valores de tipo errado (aceita `"true"` em booleanos, `usuario_id: "5"`, `limit=1_0`) ou dá 500.

## Comportamento proposto (.NET)
400 com a mensagem de dados incompletos da rota para tipo errado.

## Impacto nos clientes
Baixo risco; só entradas fora do contrato.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
