# Plan 013 — Jobs (Quartz.NET)

- **Spec:** `spec.md` (aprovada por delegação; só paridade e desvios 011/012/013/015/020 e correlatos; 130 a 139 propostos e **não** implementados)

Reaproveita: `IClock`/`BrazilClock`, `ICache`, `CacheKeys` (`JobLock`, `InvoiceClosed`, `InvoiceDueWarning3d`, `NightlyCheckin*`), `IBotClient`, `ILlmClient`, `ICalendarClient`, `IWeatherClient`, `WeatherFormatter`, `CalendarAlertService` e `ICalendarAlertConfigRepository` (014), `IInvoiceService`/`IInvoiceSchedulePopulator` (004), `InvoiceCycle`/`MonthDays` (Domain 004), `IDbConnectionFactory`, `MessageChunker` (já dentro do `IBotClient`), `PyRound`, `PostgresFixture`/`RedisFixture`, `InMemoryCache`, `FakeTimeProvider`.

**Arquivos compartilhados tocados (únicos):** `dotnet/Directory.Packages.props` (3 `PackageVersion`: `Quartz.Extensions.Hosting`, `ScottPlot`, `SkiaSharp.NativeAssets.Linux.NoDependencies` se o ScottPlot não trouxer), `MeuSecretario.Infrastructure.csproj` (3 `PackageReference`), `parity-deviations/README.md` (linhas no fim) e os arquivos de desvio novos. **Não** toca `Program.cs` (o módulo é descoberto por reflexão), `DependencyInjection.cs`, `PostgresFixture`, `Dockerfile` nem migrations. O populador de faturas continua sendo o da 011 (`InvoiceSchedulePopulator`); o `JobsModule` não registra outro.

## 1. Domain (`MeuSecretario.Domain/Jobs`)

| Tipo | Papel |
|---|---|
| `JobClock` | `HourMinute(DateTime now)` (zera segundos), `Window(DateTime now, minutes)` (aritmética de hora do dia com volta de meia-noite, como `datetime.combine(...)±timedelta`), `IsLastDayOfMonth(DateOnly)` |
| `ScheduleKinds` / `ScheduleRules` | `FIXO`/`PARCELADO`/`LEMBRETE_VARIAVEL`; `TransactionType(group)`, `SignedAmount`, `InstallmentDescription` (`X (n/total)`, `None` se nulo), `NextProgress(executed, total)` (`>=`), `CanRunInstallment`, `DateInMonth(year, month, day)` (corte do último dia), `ReminderDueDate`/`IsReminderDue(day, notifyBefore, today)` (aritmética de data), `MotorDayMatches` |
| `PtBrMoney` | `formatar_moeda`: `R$ 1.234,56`, `R$ 0,00` para nulo |
| `PyNumber` | `{:,.2f}` (milhar com vírgula, ponto decimal), `{:+.1f}`, `{:.2f}` com vírgula decimal |
| `FinancialAlerts` + `FinancialAlertFormatter` | listas de contas e faturas de hoje/amanhã; texto literal com e sem saudação |
| `InvoiceNotificationFormatter` | `format_invoice_closed`, `format_due_date_warning`, `format_overdue_alert` (esta sem uso nos jobs; portada para a 012) |
| `BriefingEvent`, `BriefingRules` | `ExtractLocations`, `CalculateGaps`, `DetectType`, contagens |
| `BriefingPrompt` | prompt literal (`Build(data)`), `CleanResponse` (regex), `NoEventsMessage`, `FallbackMessage` |
| `CheckinBill`, `CheckinRules` | `DaysOverdue`, `StatusText`, `OrderForSession`, `IsCardExpense`, `Dedupe` |
| `CheckinMessageFormatter` | mensagem consolidada literal (job e modo leitura); lança em fatura vencida (paridade 130) |
| `CheckinSessionDto` | formato JSON do Python (`[JsonPropertyName]` snake_case; `items` por índice em texto) |
| `MonthlyReportPeriod`, `MonthlyReportData`, `MonthlyReportCalculator`, `MonthlyReportFormatter` | período por momento, percentuais, comparação, mensagem literal (paridade, com sinal) |
| `BillsReminderFormatter` | mensagem de `processar_contas_vencer` |

Domain não referencia nada; sem `DateTime.Now`.

## 2. Application (`MeuSecretario.Application/Jobs` e `Abstractions/JobPorts.cs`)

- **Portas para 009c** (`Abstractions/JobPorts.cs`): `IScheduleMotor`, `MotorRunResult`, `IBillsNotificationJob`, `BillsJobResult`, `IDailyBriefingJob`, `DailyBriefingRunResult`, `DailyBriefingTestResult`, `IMonthlyReportJob`, `MonthlyMoment`, `MonthlyReportRunResult`, `ManualReportResult`.
- **Infra de jobs:** `IScheduledJob`, `JobRunner` (RN-013-01 a 03), `IJobLog`, `JobSchedules` (nome, lock, cron Quartz, fuso), `JobsOptions` (`Enabled`).
- **Portas de dados** (implementadas na Infrastructure): `IJobNotificationConfigRepository`, `IFinancialAlertsRepository`, `IBriefingDataRepository` (cidade/estado, nome), `ICheckinQueries`, `IInvoiceJobRepository`, `IScheduleExecutionRepository`, `IMonthlyReportRepository`, `IBillsReminderRepository`, `IChartRenderer`.
- **Casos de uso / serviços** (um por responsabilidade, `ExecuteAsync`/métodos nomeados): `TaskAlertsJob`, `InvoiceProcessorJob`, `ScheduleExecutor : IScheduleExecutor`, `ScheduleMotor : IScheduleMotor, IScheduledJob`, `DailyBriefingService`, `DailyBriefingJob : IDailyBriefingJob, IScheduledJob`, `BillsNotificationJob : IBillsNotificationJob`, `NightlyCheckinService`, `NightlyCheckinJob`, `MonthlyReportService`, `MonthlyReportJob : IMonthlyReportJob` e os dois `IScheduledJob` mensais (`StartOfMonthReportJob`, `EndOfMonthReportJob`).
- Nenhuma dependência de Quartz, Npgsql, Redis ou HTTP. `ILogger` não existe aqui: `IJobLog`.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Jobs`)

- Repositórios Dapper (SQL fixo, parâmetros nomeados, datas `DbType.Date`): `NotificationConfigRepository`, `FinancialAlertsRepository`, `BriefingDataRepository`, `CheckinQueries`, `InvoiceJobRepository`, `ScheduleExecutionRepository`, `MonthlyReportRepository`, `BillsReminderRepository`. O fragmento "já lançado no mês" (`agendamento_id` ou fallback por descrição) mora em `ScheduleSql` e é reutilizado por todos.
- `ScheduleExecutionRepository.ExecuteAsync`: uma transação Npgsql: `SELECT ... FOR UPDATE` do agendamento (com grupo e tipo da conta), checagem de idempotência, resolução de fatura (`InvoiceCycle.ForPurchase`, `INSERT ... ON CONFLICT DO NOTHING RETURNING` + `SELECT`), `INSERT` da transação, `UPDATE agendamentos` (parcela), commit.
- `ScottPlotChartRenderer : IChartRenderer` (pizza PNG), `JobLog : IJobLog` (`ILogger` + arquivo `{LOG_DIR}/{nome}.log`), `JobsScheduler : IHostedService` (liga só com `JobsOptions.Enabled`), `ScheduledJobAdapter : IJob` (`[DisallowConcurrentExecution]`, resolve `IScheduledJob` por nome num escopo), `JobsServiceCollectionExtensions.AddJobs()`.
- Quartz: `AddQuartz` (agendador em memória, nome `meu-secretario`), um `JobKey` e um `TriggerKey` por `JobSchedules`, `CronScheduleBuilder.CronSchedule(cron).InTimeZone(America/Sao_Paulo).WithMisfireHandlingInstructionDoNothing()`. **Sem** `AddQuartzHostedService`: o `JobsScheduler` próprio decide iniciar.

## 4. Api (`MeuSecretario.Api/Jobs`)

`JobsModule : IFeatureModule` registra `AddJobs()` e as opções (`JOBS_ENABLED` lido por `IConfiguration` na primeira resolução, texto `true`); `MapEndpoints` vazio (sem rota). `JobsOptions` ligado ao `IConfiguration` com `Configure<IConfiguration>`.

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `JobClockTests`, `ScheduleRulesTests`, `PtBrMoneyTests`, `PyNumberTests`, `FinancialAlertFormatterTests`, `InvoiceNotificationFormatterTests`, `BriefingRulesTests`, `BriefingPromptTests` (snapshot), `CheckinRulesTests`, `CheckinMessageFormatterTests`, `CheckinSessionDtoTests`, `MonthlyReportTests`, `BillsReminderFormatterTests` | AC-013-21, 22, 23, 30, 31, 41 a 46, 53, 62, 63, 70, 72 |
| Unitário (Application) | `JobRunnerTests`, `JobSchedulesTests`, `TaskAlertsJobTests`, `InvoiceProcessorJobTests`, `InvoiceSchedulePopulatorTests`, `ScheduleMotorTests`, `DailyBriefingServiceTests`, `DailyBriefingJobTests`, `BillsNotificationJobTests`, `NightlyCheckinServiceTests`, `NightlyCheckinJobTests`, `MonthlyReportJobTests` (portas falsas com NSubstitute, `InMemoryCache`, `FakeTimeProvider`) | AC-013-02 a 05, 10, 20 a 25, 32, 40, 47, 51 a 53, 55, 60, 63 a 66, 70 a 74 |
| Integração (PostgreSQL) | `NotificationConfigRepositoryTests`, `FinancialAlertsRepositoryTests`, `CheckinQueriesTests`, `InvoiceJobRepositoryTests`, `ScheduleExecutionRepositoryTests`, `MonthlyReportRepositoryTests` | AC-013-20, 30, 41 a 43, 50, 54, 60 a 65, 70, 71 |
| Integração (Redis) | `JobRunnerRedisTests`, `CheckinSessionRedisTests`, `ScheduleMotorRedisTests` | AC-013-03, 44, 66 |
| Integração (host) | `JobsHostTests` | AC-013-01, 81: com e sem `JOBS_ENABLED`, os gatilhos e o `StopAsync` |
| Unitário (arquitetura) | acréscimo em um arquivo novo `JobsArchitectureTests` (não edita o existente) | AC-013-80 |
| Unitário (gráfico) | `ChartRendererTests` | AC-013-75 |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","013-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Riscos e decisões

- **Duplicar lançamentos antes da virada.** `JOBS_ENABLED` padrão `false` (RN-013-04). Os testes de host e o `Program` nunca ligam sozinho. Antes da virada, **nunca** ter Flask (Ofelia) e .NET com a variável ligada ao mesmo tempo (infra-deploy §4.3).
- **Populador.** Mantido o da 011 para não quebrar os testes dela; um teste de host confere que `IInvoiceSchedulePopulator` é o da 011.
- **`FOR UPDATE` e deadlock.** Um agendamento por transação, na ordem de `id`; o populador e o motor nunca seguram dois ao mesmo tempo.
- **Fuso do cron.** O Quartz recebe `TimeZoneInfo` de São Paulo (`BrazilClock.TimeZone`); testes calculam `GetFireTimeAfter` com instantes UTC.
- **Quartz sem host real nos testes unitários.** `JobsScheduler` é testado com `StdSchedulerFactory` em memória, sem `Start()`; o teste de host usa `Start()` mas com `JOBS_ENABLED` e relógio real só para o agendador subir e parar (nenhum cron dispara dentro do teste).
- **Lock Redis e Quartz.** Ambos: `DisallowConcurrentExecution` (mesmo processo) e `job_lock:*` (outras instâncias e rotas admin).
- **Gráfico.** ScottPlot 5 desenha texto com Skia; sem fontes no container o texto pode falhar (Q-013-4, fora do alcance desta entrega: o `Dockerfile` é compartilhado e fica para o fechamento da Fase 4).
- **Cobertura.** Domain ≥ 90% e Application ≥ 80%.
