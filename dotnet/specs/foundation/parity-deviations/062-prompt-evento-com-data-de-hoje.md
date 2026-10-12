# Desvio 062 — Gemini: prompt de criação de evento recebe a data de hoje

- **Estado:** aprovado
- **Features afetadas:** 010, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`extract_event_creation_details` (`gemini_service.py:850-873`) pede `data` em `YYYY-MM-DD` mas não informa a data atual; os exemplos fixam `2025-11-25`, então "dia 25/11" ou "sexta" dependem do chute do modelo (e do ano).

## Comportamento proposto (.NET)
Ao portar o prompt na feature 012, incluir "Hoje é {yyyy-MM-dd} ({dia da semana})" no texto.

## Impacto nos clientes
O texto do prompt muda.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar na 012.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
