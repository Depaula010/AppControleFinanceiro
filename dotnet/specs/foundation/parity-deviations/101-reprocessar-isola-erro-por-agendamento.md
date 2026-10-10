# Desvio 101 — Reprocessar fatura: isolar o erro de cada agendamento

- **Estado:** implementado (feature 011, 2026-10-09)
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
`process_invoice_schedules` captura `Exception` por agendamento e segue (`invoice_service.py`, bloco `except` dentro do laço), mas o erro SQL já abortou a transação do PostgreSQL: os comandos seguintes falham, o `commit` desfaz tudo e a rota devolve 200 com estatísticas de lançamentos que **não** foram gravados. O .NET reproduz o "nada fica gravado", mas devolve 500.

## Comportamento proposto (.NET)
Um `SAVEPOINT` por agendamento: o erro de um não perde os outros; o 200 só conta o que foi gravado e `stats` ganha a contagem de erros.

## Impacto nos clientes
Só a manutenção que chama a rota (e o populador automático da fatura nova).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
