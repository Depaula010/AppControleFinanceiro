# Desvio 024 — Corpo sem Content-Type JSON gera 500

- **Estado:** aprovado
- **Features afetadas:** todas
- **Severidade:** correção

## Comportamento atual (Flask)
`request.json`/`get_json()` sem `Content-Type: application/json` lança 415, que dentro dos `try/except` vira 500 genérico. `POST /api/transactions` sem corpo dá 500.

## Comportamento proposto (.NET)
Corpo inválido ou ausente ⇒ 400 com a mensagem de campos obrigatórios do envelope da rota. `valor: 0` continua tratado como "faltando" onde o Flask faz isso (preservado).

## Impacto nos clientes
Cliente recebe 400 em vez de 500 em corpo inválido.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
