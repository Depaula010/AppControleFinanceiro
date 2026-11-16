# Desvio 278 — Saldo de hoje; cadastro público desligado; flag dos alertas de tarefa

- **Estado:** implementado
- **Features afetadas:** 001, 002, 006, 012, 013, 014
- **Severidade:** correção

## 1. Saldo de hoje (achado M10)
**Antes:** o saldo (dashboard `saldo_total`, `GET /api/accounts` campo `saldo`, "qual meu saldo" do bot, rodapé do feedback) somava todas as transações consolidadas, inclusive as de data futura. O populador de faturas cria as contas do cartão do mês seguinte logo que a fatura abre (em 14/11 já existiam lançamentos de 01/12 a 31/12), então o saldo caía semanas antes do vencimento, enquanto `receitas_mes`/`despesas_mes` só somavam até hoje.
**Agora:** o JOIN das quatro consultas ganha `data_transacao <= hoje` (hoje = `IClock.Today`, fuso America/Sao_Paulo, nunca `CURRENT_DATE` do banco, que está em UTC). Conta sem movimento continua listada com o saldo inicial. O rodapé do bot continua sem filtro de `consolidada` (como antes), só ganha o corte de data.
**Efeito:** lançamento manual com data futura (ex.: salário de amanhã) só passa a contar no dia. Saldo "projetado" como campo novo ficou para um desvio posterior.
Exemplo: corrente 5.000, compra de 800 em 10/11 e conta fixa de 200 em 15/12, hoje 16/11: total passa de 4.000 para 4.200 e volta a 4.000 em 15/12.

## 2. Cadastro público (`POST /auth/register`)
`REGISTRATION_ENABLED` (padrão **false**): desligado, a rota continua registrada (rota desconhecida faria o filtro do 009 bloquear o IP) e responde `403 {"status":"error","message":"Cadastro desativado"}`. O link "Cadastre-se" saiu do front junto. Para criar um segundo usuário, ligue a variável.

## 3. Alertas de tarefa (014)
`TASK_ALERTS_ENABLED` (padrão **true**, o Rafael usa): `false` não agenda o job `ALERTAS-TAREFAS` (a cada minuto). Nada muda em produção sem a variável.

## Fora do escopo
`BillsNotificationJob` não é agendado (só a rota admin o chama), então não havia o que desligar.

## Impacto nos clientes
Nenhuma rota nova nem campo renomeado. Valor do saldo muda como descrito.

## Decisão do Rafael
Pareceres do agente Financeiro (opção A) e do agente de Produto; "pode fazer tudo", "não desligue o meu" (alertas de tarefa) e "não vou liberar para outros usuários" (cadastro) na conversa de 16/11/2026.
