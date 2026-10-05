# Desvio 094 — Dashboard: alerta continua para a conta já paga no mês

- **Estado:** proposto
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`api.py:1212-1286`: o alerta não verifica se a ocorrência do mês já virou transação. A conta paga antes do vencimento segue em `warning` ou `danger` até o dia.

## Comportamento proposto (.NET)
Excluir a ocorrência que já tem transação ligada por `agendamento_id` no mês (regra do desvio 012; `descricao` só como fallback para transações antigas sem vínculo).

## Impacto nos clientes
Alertas de contas já pagas somem; nenhum campo muda.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar (depende da regra do desvio 012 estar na base comum de agendamentos da 013).

## Decisão do Rafael
(pendente)
