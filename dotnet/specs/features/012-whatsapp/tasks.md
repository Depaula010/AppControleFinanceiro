# Tasks 012 — Webhook WhatsApp e intents

Regra: cada tarefa termina com testes verdes; cada sub-entrega termina com **build sem warnings**, `dotnet test dotnet/MeuSecretario.slnx` verde, este arquivo marcado e um commit. Teste primeiro, código depois. Pré-requisito: spec aprovada por delegação (só paridade e desvios já aprovados; 150 a 161 propostos e **não** implementados). **Não edita** `Program.cs`, `DependencyInjection.cs`, `Directory.Build.props`, `PostgresFixture`. Pode tocar (e relata): `UnitTests.csproj` (fixtures), `Domain.csproj` (recursos embutidos), nenhum pacote novo (ScottPlot veio da 013). Base atualizada com a 013 e a 009c (merge em 2026-10-08).

Retomada: o que está `[x]` está commitado; a próxima sub-entrega começa na primeira tarefa `[ ]`.

- [x] **T0 — Spec, plan, tasks e desvios 150 a 161** (estado `proposto`) e linhas no fim de `parity-deviations/README.md`.

## 012-0 — Webhook, HMAC, roteamento e estado Redis

- [x] **T1 — Fixtures e prompts.** `generate_whatsapp_fixtures.py` (prompts renderizados por função do Flask com marcadores, menu de ajuda, `process_confirmation_response`, `extract_mentioned_account`) ⇒ `whatsapp-fixtures.json` e `Domain/Whatsapp/Prompts/*.txt`; `WhatsAppPrompts` com teste de renderização (AC-012-010, 016).
- [x] **T2 — Regras do roteador e textos.** `WebhookSignature`, `MessageIntent`, `WhatsAppTexts`, `PyStrings` (AC-012-003, 006, 012 a 015).
- [x] **T3 — `ConfirmationFlow`** contra o Python em todos os ramos (AC-012-018).
- [x] **T4 — Portas e estado.** `IWhatsAppUserLookup`, `IPendingTransactionState`, `IPendingEventStore`, `INightlyCheckinResponder` (+ padrão), `IWhatsAppCheckinSession`; implementações (Postgres/Redis reais na integração) (AC-012-018).
- [x] **T5 — `IntentClassifier` e `IntentRouter`** (template × inline, "não entendi", `Menu de Ajuda`) (AC-012-009 a 011).
- [x] **T6 — `HandleWhatsAppMessage`** (passos 1, 2, 5, 7 e 9 da §4 como `IWhatsAppStep`; os passos 3, 4, 6 e 8 entram nas sub-entregas 012a, 012b e 012e) com fakes (AC-012-005 a 009, 012 a 015).
- [x] **T7 — Api.** `WhatsAppModule`, `WhatsAppResponses`, `WhatsAppMessages`, `WhatsAppLog`; endpoint com leitura do corpo bruto; integração por rota (AC-012-001 a 009).
- [x] **T8 — Harness de paridade.** `whatsapp_handler_harness.py` + `WhatsAppPipelineParityTests` (AC-012-017).
- [x] **T9 — Fechamento da 012-0.** Build sem warnings, testes verdes, commit.

## 012a — Transações

- [x] **T10 — Domínio.** `TransactionIntentRules`, `AccountMention`, `FeedbackMessage` contra o Python (AC-012-101 a 103, 107, 115).
- [x] **T11 — Leituras.** `IWhatsAppFinanceReader` (contas, categorias, agendamento recorrente, padrão), `IFeedbackReader` (integração Postgres) (AC-012-104, 107, 115).
- [x] **T12 — `AccountResolver` e `InvoiceForDate`** (AC-012-105, 107).
- [x] **T13 — `ExpenseIntent` e `IncomeIntent`** (AC-012-101 a 106).
- [x] **T14 — `TransactionReplyHandler`** (salvar, TROCAR, número, cancelar, parcelada, feedback; desvio 150) (AC-012-110 a 114).
- [x] **T15 — `TransferIntent` e `InvoicePaymentIntent`** (AC-012-120, 130).
- [x] **T16 — Fechamento da 012a.**

## 012b — Pagamentos

- [x] **T17 — Domínio.** `PaymentExtraction`, `PaymentReply` (todas as variantes contra o Python) (AC-012-204).
- [x] **T18 — `PaymentHandler`** (quitação, avulsa, valor, erro por item, ordem no pipeline) (AC-012-201 a 206).
- [x] **T19 — Fechamento da 012b.**

## 012c — Consultas financeiras

- [x] **T20 — Domínio.** `BalanceMessages`, `ReserveMessages`, `PotMessages`, `AccountListMessage`, `InvoiceMessages`, `CategorySpendMessage`, `PeriodRange`, `PeriodMessages` (AC-012-301 a 307).
- [x] **T21 — Leituras (integração Postgres).** saldos, potes por período, faturas, período, categoria, ajuste de saldo (AC-012-303, 305 a 307).
- [x] **T22 — Intenções** `BalanceIntent`, `ReserveIntent`, `ReserveDetailsIntent`, `PotsIntent`, `ListAccountsIntent`, `AdjustBalanceIntent`, `InvoiceValueIntent`, `CategorySpendIntent`, `PeriodIntent` (AC-012-301 a 307).
- [x] **T23 — Fechamento da 012c.**

## 012d — Contas fixas e vencimentos

- [x] **T24 — Domínio.** `FixedBillsMessages`, `DueDatesMessage`, `CheckinSnapshotMessage` (AC-012-401 a 403).
- [x] **T25 — Leituras (integração Postgres).** pendentes do mês, catálogo, vencimentos (virada de mês), snapshot do check-in (AC-012-401 a 403).
- [x] **T26 — Intenções** `FixedBillsIntent`, `AllBillsIntent`, `DueTodayIntent`, `DueTomorrowIntent`, `DueWeekIntent`, `OverdueIntent` (usa `NightlyCheckinService` da 013) (AC-012-401 a 403).
- [x] **T26b — Resposta ao check-in.** `CheckinReplyParser`, `CheckinResponder : INightlyCheckinResponder` (`process_response`, `mark_bills_as_paid` por `IScheduleExecutor`) (AC-012-404).

- [x] **T27 — Fechamento da 012d.**

## 012e — Calendário

- [x] **T28 — Domínio.** `EventMessages`, `AgendaPeriods`, `AgendaTimeFilter`, `SchedulePatterns`, `FreeSlots` + `FreeSlotsMessage` (AC-012-501, 505, 506).
- [x] **T29 — Serviços.** `AgendaService`, `FreeTimeFinder` com `ICalendarClient` falso (AC-012-505, 506).
- [x] **T30 — Eventos.** `CreateEventIntent`, `EventReplyHandler`, `RouteRequestHandler`, `DeleteEventIntent` (AC-012-501 a 504).
- [x] **T31 — `AgendaIntent` e `FreeSlotsIntent`** (AC-012-505, 506).
- [x] **T32 — Fechamento da 012e.** Fixtures do Flask em `whatsapp_sections/012e_calendar.py` (serviços reais com Google falso e relógio congelado), 74 cenários do handler real em `whatsapp_scenarios_efh.py` (`CalendarConfigParityTests`), desvios 062, 053, 055 e 008 aplicados; `Erro ao consultar/deletar/criar evento` sem o motivo (exceto "usuário sem Google conectado"). Divergências: busca para deletar limita a 5 por calendário no código (o cliente da 010 busca 10 e já ordena por início).

## 012f — Configurações

- [x] **T33 — Domínio.** `NotificationCommand`, `ReportConfigMessages`, `AddressMessages`, `LocationMessage`, `ApiKeyMessage` (AC-012-601 a 603).
- [x] **T34 — Leituras e escritas (integração Postgres).** `IWhatsAppConfigStore` (AC-012-601 a 603).
- [x] **T35 — Intenções** `NotificationsIntent`, `LocationIntent`, `MonthlyReportIntent`, `AddressConfigIntent`, `AddressListIntent`, `AddressDeleteIntent`, `ApiKeyIntent` (AC-012-601 a 603).
- [x] **T36 — Fechamento da 012f.** Fixtures em `whatsapp_sections/012f_config.py` (intenções e serviços reais com banco falso); `NotificationsIntent` reproduz o Flask inclusive o desvio 152 (ramo do Gemini grava e responde 500) e os novos 190 a 192 (propostos, não implementados).

## 012g — Análises e gráfico

- [x] **T37 — Domínio.** `AnalyticsReport`, `ForecastReport`/`ForecastProjection`, `MonthlyComparison` e `ChartRules` contra o Python (AC-012-701, 702): prompts, textos e arredondamentos conferidos com o `analytics_service`/`forecast_service` reais pelo harness (`AnalyticsParityTests`); zero negativo (`-0.0`) com o sinal do Python.
- [x] **T38 — Leituras (integração Postgres).** `IAnalyticsReader`/`AnalyticsReader` (AC-012-701 a 703): datas por parâmetro (`IClock`), `ORDER BY` fechados; as consultas sem uso no prompt (`gastos_mensais` da análise, `delivery`, histórico por categoria, padrão diário) não são executadas.
- [x] **T39 — Intenções** `AnalysisIntent`, `MonthlyComparisonIntent`, `ForecastIntent`, `ChartIntent` com `IChartRenderer` falso (AC-012-701 a 703); módulo `WhatsAppAnalyticsModule`; o gráfico sai por `IBotClient.SendImageAsync` (desvio 026).
- [x] **T40 — Barras e linha** no `ScottPlotChartRenderer` da 013 (a pizza já existe) (AC-012-703): `IChartRenderer` ganhou `RenderBars` e `RenderLine`; PNG válido, `null` sem dados ou se o desenho falha. Divergência registrada: imagem diferente do matplotlib (estilo, fonte), dados, título e legenda iguais (spec §13.1, Q-012-8).
- [x] **T41 — Fechamento da 012g.** Harness `whatsapp_harness_sections/012g_analytics.py` (82 cenários: o `WhatsAppHandler` real com `analytics_intents.py`, `analytics_service.py`, `forecast_service.py` e `chart_service.py` reais; saída `whatsapp-analytics-scenarios.json`); desvios 158 (valores negativos, `KeyError` sem histórico) reproduzidos; novos 230 a 233 propostos e **não** implementados.

## 012h — Drive

- [x] **T42 — `DriveFolderPath` e `DriveUploadIntent`** (AC-012-801, 802).
- [x] **T43 — Fechamento da 012h.** Fixtures em `whatsapp_sections/012h_drive.py` (`UploadDriveIntent` real, 28 textos de pasta, 23 fluxos e 30 casos de `b64decode`); falha do upload responde sempre o texto genérico (desvio 159 proposto).

## Desvios aprovados (calendário, configurações, Drive e análises), 2026-10-10

- [x] **T45 — Desvios 152, 153, 159, 190, 191, 192, 230, 231, 232 e 233** implementados (AC-012-152 a 233): `NotificationsIntent`/`NotificationCommand.Action` (152, 190, 191), `ApiKeyIntent` com `IEncryptionService` (153), `DriveMessages.UploadFailure` (159), `DeleteEventIntent` (192), `ChartIntent.Param` (230), `AnalyticsReader` (231, 232, 233), `MonthlyComparison` (232) e `WeekdayNames` (233). Os cenários do Flask que embutem o comportamento antigo seguem nos JSONs de paridade como evidência e são pulados nos testes de paridade (`SupersededByDeviations`); o comportamento novo é provado em `NotificationDeviationTests`, `AnalyticsDeviationTests`, `DriveUploadIntentTests`, `CalendarServicesTests` e nos testes de integração. 154, 157 e 160 seguem adiados.

## Fechamento da feature

- [ ] **T44 — Desvios e spec.** Criar `parity-deviations/150` a `161` (estado `proposto`), atualizar `README` (só linhas no fim; marcar 014, 026, 039, 062, 063: parte 012 implementada), spec atualizada para `implementada`, relatório com arquivos compartilhados tocados.

Definição de pronto: `constitution.md` §10.
