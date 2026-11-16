# Desvio 285 — Correções menores do backend (baixos da auditoria 2026-11-16)

- **Estado:** implementado
- **Features afetadas:** 005, 011, 012, 013
- **Severidade:** correção

## O que muda
- **(a) Lock dos jobs:** o valor do `job_lock:*` é único por execução (Guid) e só o dono o libera (`ICache.DeleteIfEqualsAsync`, Lua no Redis). Antes qualquer execução apagava o lock, inclusive o de outra que o pegou depois do TTL de 5 min. As rotas admin `trigger-monthly-reports-inicio/fim` passam pela mesma trava do job agendado (`LockedMonthlyReportJob`); com o lock ocupado respondem com o erro `em_execucao` sem rodar.
- **(b) Relatório mensal:** o percentual das categorias é a fatia do **total de despesas** do período (o Flask dividia pela soma do top 5, então as cinco somavam 100%). "Pendentes" só conta o que ainda não foi pago no período (regra do check-in, `SchedulePaymentSql`); antes contava todo agendamento ativo.
- **(c) Quinzenal e arredondamento:** o fator quinzenal é um só, 26/12 por mês (`Periodicities.BiweeklyPerMonth`, decimal), no resumo, na reserva (N x 26/12, era 2N) e no total do catálogo "Todas as contas" (era 2,16). `total_despesas_mensais`, `total_despesas_anuais` e `total_receitas_mensais` saem com 2 casas (era 300.30000000000004). Valores da reserva mudam: ex. quinzenal 50 com 6 meses passa de 600 para 650.
- **(d) Parcelado quitado:** `executadas == total` com o agendamento ainda ativo não entra mais no resumo de contas (o PUT que iguala total e executadas continua aceito; o executor, os avisos e agora o resumo já ignoram o parcelado sem parcela restante).
- **(e) Aviso de fatura fechada que falhou no envio** é reenviado: ver desvio 282.
- **(f) Textos:** "Transação salva!", "Descrição", "Não encontrei... então" (WhatsApp; as mensagens JSON da API não mudam); receita não aparece mais como "(Débito)" na confirmação; menu e prompt de intenção não citam "6x/6 meses" de reserva; "não será mais cobrada automaticamente este mês" virou "Conta marcada como paga neste mês."; o texto de "sem cadastro" não traz mais o link morto `app.meusecretario.com/register`.
- **(g) Dinheiro com milhar** ("R$ 1.234,56") nos alertas financeiros, no resumo da manhã e nas notificações de fatura (era "R$ 1234,56").
- **(h)** `Quitar Conta Fixa` sai do prompt de intenção (desvio 160 implementado).
- **(i) Código morto:** `InvoiceNotificationFormatter.Overdue`, `AnalyticsReport.Money` e o comentário desatualizado do `FixedBillsReader`.
- **(j) Populador:** respeita `data_inicio` futura (consulta até a data de vencimento da fatura), grava `competencia` = data da conta no INSERT (o índice único 0090 passa a protegê-lo) e trava a linha do agendamento com `FOR UPDATE OF a` até o commit (job das 00:01 e `EnsureCurrent` não processam o mesmo agendamento ao mesmo tempo).

## Fixtures de paridade ajustadas
Textos e números acima são do Flask nos JSON gerados; os trechos afetados (menu, prompt, confirmação de renda, "sem cadastro", alertas, notificação de fatura, quitação) foram atualizados à mão nos JSON de `dotnet/tests/Fixtures`, e os totais de reserva/catálogo por `Deviation285` nos testes.

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026.
