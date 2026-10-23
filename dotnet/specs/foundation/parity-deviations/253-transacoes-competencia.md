# Desvio 253 — Transação de agendamento guarda a que vencimento se refere (competencia)

- **Estado:** implementado (PR #39)
- **Features afetadas:** 013, 012
- **Severidade:** correção

## Comportamento atual (Flask)
A consulta do check-in só sabia "pago no mesmo mês do vencimento" ou "até 20 dias depois". O pagamento atrasado de um mês escondia o vencimento do mês seguinte (a Água de setembro paga em 19/10 fez o vencimento de 27/10 sumir).

## Comportamento proposto (.NET)
`transacoes.competencia date NULL` (migration 0080, aditiva): o vencimento que o lançamento quita. O check-in grava o `data_vencimento_real` confirmado; o motor grava a data em que lança. As consultas e a idempotência do executor casam por `agendamento_id + competencia`; linhas antigas (NULL) seguem as regras antigas. Complementado pelo desvio 258 (índice único).

## Impacto nos clientes
Nenhuma rota muda. Uma linha antiga que esconde um vencimento posterior pode ser corrigida com `UPDATE` pontual (feito para a transação 433).

## Alternativas
Janela de datas mais estreita (menos preciso). Manter como está (rejeitado: fatos reais em produção).

## Recomendação
Aprovar.

## Decisão do Rafael
Proposto pelo engenheiro e aceito pelo Rafael na conversa de 20/10/2026 ("acho que essa é a melhor solução … Concorda?"). Implementado no PR #39.
