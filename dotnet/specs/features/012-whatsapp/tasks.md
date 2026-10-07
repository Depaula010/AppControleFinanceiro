# Tasks 012 — Webhook WhatsApp e intents

Regra: cada tarefa termina com testes verdes; cada sub-entrega termina com **build sem warnings**, `dotnet test dotnet/MeuSecretario.slnx` verde, este arquivo marcado e um commit. Teste primeiro, código depois. Pré-requisito: spec aprovada por delegação (só paridade e desvios já aprovados; 150 a 161 propostos e **não** implementados). **Não edita** `Program.cs`, `DependencyInjection.cs`, `Directory.Build.props`, `PostgresFixture`. Pode tocar (e relata): `UnitTests.csproj` (fixtures), `Domain.csproj` (recursos embutidos), `Directory.Packages.props` + `Infrastructure.csproj` (ScottPlot, só 012g).

Retomada: o que está `[x]` está commitado; a próxima sub-entrega começa na primeira tarefa `[ ]`.

- [x] **T0 — Spec, plan, tasks e desvios 150 a 161** (estado `proposto`) e linhas no fim de `parity-deviations/README.md`.

## 012-0 — Webhook, HMAC, roteamento e estado Redis

- [ ] **T1 — Fixtures e prompts.** `generate_whatsapp_fixtures.py` (prompts renderizados por função do Flask com marcadores, menu de ajuda, `process_confirmation_response`, `extract_mentioned_account`) ⇒ `whatsapp-fixtures.json` e `Domain/Whatsapp/Prompts/*.txt`; `WhatsAppPrompts` com teste de renderização (AC-012-010, 016).
- [ ] **T2 — Regras do roteador e textos.** `WebhookSignature`, `MessageIntent`, `WhatsAppTexts`, `PyStrings` (AC-012-003, 006, 012 a 015).
- [ ] **T3 — `ConfirmationFlow`** contra o Python em todos os ramos (AC-012-018).
- [ ] **T4 — Portas e estado.** `IWhatsAppUserLookup`, `IPendingTransactionState`, `IPendingEventStore`, `INightlyCheckinResponder` (+ padrão), `IWhatsAppCheckinSession`; implementações (Postgres/Redis reais na integração) (AC-012-018).
- [ ] **T5 — `IntentClassifier` e `IntentRouter`** (template × inline, "não entendi", `Menu de Ajuda`) (AC-012-009 a 011).
- [ ] **T6 — `HandleWhatsAppMessage`** (passos 1 a 7 e 9 da §4 com as sub-entregas ainda não habilitadas) com fakes (AC-012-005 a 009, 012 a 015).
- [ ] **T7 — Api.** `WhatsAppModule`, `WhatsAppResponses`, `WhatsAppMessages`, `WhatsAppLog`; endpoint com leitura do corpo bruto; integração por rota (AC-012-001 a 009).
- [ ] **T8 — Harness de paridade.** `whatsapp_handler_harness.py` + `WhatsAppPipelineParityTests` (AC-012-017).
- [ ] **T9 — Fechamento da 012-0.** Build sem warnings, testes verdes, commit.

## 012a — Transações

- [ ] **T10 — Domínio.** `TransactionIntentRules`, `AccountMention`, `FeedbackMessage` contra o Python (AC-012-101 a 103, 107, 115).
- [ ] **T11 — Leituras.** `IWhatsAppFinanceReader` (contas, categorias, agendamento recorrente, padrão), `IFeedbackReader` (integração Postgres) (AC-012-104, 107, 115).
- [ ] **T12 — `AccountResolver` e `InvoiceForDate`** (AC-012-105, 107).
- [ ] **T13 — `ExpenseIntent` e `IncomeIntent`** (AC-012-101 a 106).
- [ ] **T14 — `TransactionReplyHandler`** (salvar, TROCAR, número, cancelar, parcelada, feedback; desvio 150) (AC-012-110 a 114).
- [ ] **T15 — `TransferIntent` e `InvoicePaymentIntent`** (AC-012-120, 130).
- [ ] **T16 — Fechamento da 012a.**

## 012b — Pagamentos

- [ ] **T17 — Domínio.** `PaymentExtraction`, `PaymentReply` (todas as variantes contra o Python) (AC-012-204).
- [ ] **T18 — `PaymentHandler`** (quitação, avulsa, valor, erro por item, ordem no pipeline) (AC-012-201 a 206).
- [ ] **T19 — Fechamento da 012b.**

## 012c — Consultas financeiras

- [ ] **T20 — Domínio.** `BalanceMessages`, `ReserveMessages`, `PotMessages`, `AccountListMessage`, `InvoiceMessages`, `CategorySpendMessage`, `PeriodRange`, `PeriodMessages` (AC-012-301 a 307).
- [ ] **T21 — Leituras (integração Postgres).** saldos, potes por período, faturas, período, categoria, ajuste de saldo (AC-012-303, 305 a 307).
- [ ] **T22 — Intenções** `BalanceIntent`, `ReserveIntent`, `ReserveDetailsIntent`, `PotsIntent`, `ListAccountsIntent`, `AdjustBalanceIntent`, `InvoiceValueIntent`, `CategorySpendIntent`, `PeriodIntent` (AC-012-301 a 307).
- [ ] **T23 — Fechamento da 012c.**

## 012d — Contas fixas e vencimentos

- [ ] **T24 — Domínio.** `FixedBillsMessages`, `DueDatesMessage`, `CheckinSnapshotMessage` (AC-012-401 a 403).
- [ ] **T25 — Leituras (integração Postgres).** pendentes do mês, catálogo, vencimentos (virada de mês), snapshot do check-in (AC-012-401 a 403).
- [ ] **T26 — Intenções** `FixedBillsIntent`, `AllBillsIntent`, `DueTodayIntent`, `DueTomorrowIntent`, `DueWeekIntent`, `OverdueIntent` (AC-012-401 a 403).
- [ ] **T27 — Fechamento da 012d.**

## 012e — Calendário

- [ ] **T28 — Domínio.** `EventMessages`, `AgendaPeriods`, `AgendaTimeFilter`, `SchedulePatterns`, `FreeSlots` + `FreeSlotsMessage` (AC-012-501, 505, 506).
- [ ] **T29 — Serviços.** `AgendaService`, `FreeTimeFinder` com `ICalendarClient` falso (AC-012-505, 506).
- [ ] **T30 — Eventos.** `CreateEventIntent`, `EventReplyHandler`, `RouteRequestHandler`, `DeleteEventIntent` (AC-012-501 a 504).
- [ ] **T31 — `AgendaIntent` e `FreeSlotsIntent`** (AC-012-505, 506).
- [ ] **T32 — Fechamento da 012e.**

## 012f — Configurações

- [ ] **T33 — Domínio.** `NotificationCommand`, `ReportConfigMessages`, `AddressMessages`, `LocationMessage`, `ApiKeyMessage` (AC-012-601 a 603).
- [ ] **T34 — Leituras e escritas (integração Postgres).** `IWhatsAppConfigStore` (AC-012-601 a 603).
- [ ] **T35 — Intenções** `NotificationsIntent`, `LocationIntent`, `MonthlyReportIntent`, `AddressConfigIntent`, `AddressListIntent`, `AddressDeleteIntent`, `ApiKeyIntent` (AC-012-601 a 603).
- [ ] **T36 — Fechamento da 012f.**

## 012g — Análises e gráfico

- [ ] **T37 — Domínio.** `AnalyticsReport`, `ForecastReport`, `MonthlyComparison` contra o Python (AC-012-701, 702).
- [ ] **T38 — Leituras (integração Postgres).** `IAnalyticsReader` (AC-012-701 a 703).
- [ ] **T39 — Intenções** `AnalysisIntent`, `MonthlyComparisonIntent`, `ForecastIntent`, `ChartIntent` com `IChartRenderer` falso (AC-012-701 a 703).
- [ ] **T40 — `ScottPlotChartRenderer`** (pacote novo; PNG válido) (AC-012-703).
- [ ] **T41 — Fechamento da 012g.**

## 012h — Drive

- [ ] **T42 — `DriveFolderPath` e `DriveUploadIntent`** (AC-012-801, 802).
- [ ] **T43 — Fechamento da 012h.**

## Fechamento da feature

- [ ] **T44 — Desvios e spec.** Criar `parity-deviations/150` a `161` (estado `proposto`), atualizar `README` (só linhas no fim; marcar 014, 026, 039, 062, 063: parte 012 implementada), spec atualizada para `implementada`, relatório com arquivos compartilhados tocados.

Definição de pronto: `constitution.md` §10.
