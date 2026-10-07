# Tasks 013 — Jobs (Quartz.NET)

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro, código depois. Pré-requisito: spec aprovada por delegação (só paridade e desvios 011/012/013/015/020; 130 a 139 propostos e **não** implementados). Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Dockerfile`; únicos arquivos compartilhados: `Directory.Packages.props`, `MeuSecretario.Infrastructure.csproj` e o fim do `parity-deviations/README.md`. Comandos: `dotnet build dotnet/MeuSecretario.slnx` e `dotnet test dotnet/MeuSecretario.slnx`. Sem migration.

- [x] **T0 — Spec, plan, tasks e desvios 130 a 139** (estado `proposto`) e linhas no fim de `parity-deviations/README.md`.

## Fundação

- [x] **T1 — Pacotes, agendador e runner.** `Quartz.Extensions.Hosting` (+ `ScottPlot`) no `Directory.Packages.props`; `JobClock`, `JobSchedules`, `IScheduledJob`, `JobRunner` (lock Redis fail-open, erros, log), `JobsOptions`, `JobLog`, `JobsScheduler`, `ScheduledJobAdapter`, `JobsModule` (AC-013-01 a 05, 80, 81).

## Domain e regras compartilhadas

- [x] **T2 — Dinheiro, números e formatadores.** `PtBrMoney`, `PyNumber`, `FinancialAlertFormatter`, `InvoiceNotificationFormatter`, `BillsReminderFormatter` com texto literal (AC-013-31, 32, 51, 53).
- [x] **T3 — Regras de agendamento (desvio 011).** `ScheduleRules` (tipo, sinal, parcela, `>=`, corte do último dia, lembrete) (AC-013-54, 60 a 63).

## Faturas e agendamentos (consumidos pela 004 e pela 009c)

- [x] **T4 — `ScheduleExecutor` e `ScheduleExecutionRepository`.** PostgreSQL real: idempotência por `agendamento_id` e mês, fallback por descrição, fatura do ciclo, parcela `>=`, `FOR UPDATE`, concorrência (AC-013-54, 61, 62, 64, 65).
- [x] **T5 — Populador.** Mantido o da 011 (`InvoiceSchedulePopulator`/`IInvoiceScheduleProcessor`); teste de host confere que ele é o efetivo (AC-013-01, 55).
- [x] **T6 — Processador de faturas.** `InvoiceJobRepository`, `InvoiceProcessorJob`: fechar, notificar com dedupe Redis, `EnsureCurrent`, popular todas, aviso de 3 dias (AC-013-50 a 55).
- [x] **T7 — Motor de agendamentos.** `ScheduleMotor` (`IScheduleMotor`, lock `motor-agendamentos`, lembrete com `motor_lembrete`), seleção SQL, mensagem literal (AC-013-60 a 66).

## Notificações

- [x] **T8 — Alertas de tarefas.** `TaskAlertsJob` (AC-013-10).
- [x] **T9 — Alertas financeiros e contas a vencer.** `FinancialAlertsRepository`, `BillsReminderRepository`, `BillsNotificationJob` (AC-013-30, 32).
- [x] **T10 — Resumo matinal.** Regras do briefing, prompt literal (snapshot), `DailyBriefingService`, `DailyBriefingJob` (job, trigger e teste), `NotificationConfigRepository` (AC-013-20 a 25).
- [x] **T11 — Check-in noturno.** `CheckinQueries` (SQL do Python + fallback do desvio 012), `NightlyCheckinService`, sessão no Redis no formato do Python, `CheckinMessageFormatter` (paridade com o 130), `NightlyCheckinJob` (AC-013-40 a 47).
- [x] **T12 — Relatórios mensais.** `MonthlyReportRepository`, `MonthlyReportService`, `IChartRenderer` (ScottPlot), os dois jobs mensais com checagem de último dia, `IMonthlyReportJob` (AC-013-70 a 75).

## Fechamento

- [x] **T13 — Contrato e desvios.** Revisar cada mensagem contra o Python lido; spec §10 preenchida; build sem warnings e testes verdes; relatório com os arquivos compartilhados tocados e o que a 009c e a 012 consomem.
