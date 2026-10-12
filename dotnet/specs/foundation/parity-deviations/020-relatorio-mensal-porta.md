# Desvio 020 — Relatórios mensais nunca rodam

- **Estado:** aprovado
- **Features afetadas:** 013
- **Severidade:** operação

## Comportamento atual (Flask)
Os jobs do Ofelia fazem `curl http://localhost:5000/admin/trigger-monthly-reports-*`, mas o servidor escuta na porta 8000 (`docker-compose.yml:66,71`). A conexão é recusada; os relatórios não saem.

## Comportamento proposto (.NET)
Quartz executa os relatórios diretamente (dia 1 às 08:00 e diariamente às 23:00 com verificação de último dia do mês).

## Impacto nos clientes
Usuários com relatório mensal ativo passam a receber mensagens que hoje não recebem. Avisar antes da virada.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar, com comunicação aos usuários afetados.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** avisar os usuários com relatório mensal ativo antes da virada (hoje ninguém recebe).
