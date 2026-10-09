# Desvio 192 — Deletar Evento: qualquer valor de `quando` diferente de `hoje` vira amanhã

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`DeletarEventoIntent.execute` (`calendar_intents.py`) usa `data_alvo = hoje if quando == 'hoje' else hoje + 1`. O prompt só prevê `hoje`, `amanha` ou `null`, mas um valor inesperado do modelo (por exemplo `semana`) filtra os eventos de **amanhã** e mostra "para semana" na mensagem de não encontrado.

## Comportamento proposto (.NET)
Aceitar só `hoje` e `amanha`; qualquer outro valor é tratado como sem filtro.

## Impacto nos clientes
Menos exclusões ou buscas na data errada.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar (baixa prioridade).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
