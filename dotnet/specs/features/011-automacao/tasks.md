# Tasks 011 — Automação

Regra: cada tarefa termina com testes verdes e commit. Teste primeiro (vermelho), código depois. Pré-requisito: spec aprovada por delegação; 001a, 003, 004, 005, 008, 010 na base. **Não edita arquivos compartilhados** (`Program.cs`, `DependencyInjection.cs`, `Directory.*.props`, `PostgresFixture`).

## Domain

- [x] **T1 — Fixtures do Python.** `tests/Fixtures/generate_automation_fixtures.py` + `automation-fixtures.json` (rapidfuzz 3.14.3, `sanitize_input`, `formatar_moeda`, `budget_validation_service`, prompts, mensagem de confirmação, `float()`/`repr`). Sem teste próprio; os testes seguintes a consomem.
- [x] **T2 — Texto e números.** `PyJsonValue`, `InputSanitizer`, `PaymentType`, `MoneyText`, `PythonJson`, `PyIsoDate` (AC-011-032, 033, 040, 140, 145).
- [x] **T3 — `FuzzyScorer` e `BillMatching`** contra o `rapidfuzz` (AC-011-141, 142).
- [x] **T4 — Pote.** `BudgetPeriods`, `BudgetValidation` (AC-011-143).
- [x] **T5 — Mensagens e prompts.** `AutomationPrompts`, `PendingTransactionData`, `ConfirmationMessage`, `AutomationTexts` (AC-011-144, 148).
- [x] **T6 — Entradas.** `ApiTransactionInput`, `NotificationExtraction`, `AutomateChoice`, `ReserveListQuery`, `ReserveToggleInput`, `InvoiceScheduleRules` (AC-011-017, 031, 105, 146).

## Application e Infrastructure

- [x] **T7 — Autenticação por chave.** `IUserApiKeyAuthenticator`, `UserApiKeyAuthenticator`, `UserKeyReader` (unitário + integração: cifrada, texto puro, token que não decifra, `ativo = false`) (AC-011-001 a 003, 147).
- [x] **T8 — Leituras.** `IAutomationLookups`/`AutomationLookups`, `IBudgetReader`/`BudgetReader` (integração: conta ativa, três buscas, ordem, categorias por grupo, rótulo, contas fixas, potes) (AC-011-014, 035).
- [x] **T9 — Populador de faturas.** `IInvoiceScheduleProcessor`, `InvoiceScheduleProcessor`, `InvoiceSchedulePopulator`, SQL da §3 do plan (integração: FIXO/PARCELADO/LEMBRETE, `ANUAL`, dia 31, idempotência, desativação, rollback, outra conta/inativo) (AC-011-122 a 127, 146).
- [x] **T10 — Pendência.** `IPendingTransactionStore`, `PendingTransactionStore`, `IPendingIdGenerator` (chaves, TTL, formato) (AC-011-011).
- [x] **T11 — `ProcessAutomateNotification`** com fakes (AC-011-010 a 019a).
- [x] **T12 — `BudgetValidator` e `RegisterApiTransaction`** (AC-011-030 a 042).
- [x] **T13 — `ProcessSmsPayment`** (AC-011-060 a 068).
- [x] **T14 — Reserva.** `IReserveScheduleStore`/`ReserveScheduleStore`, `ToggleReserveSchedule`, `ListReserveSchedules` (integração + unitário) (AC-011-080 a 107).
- [x] **T15 — `ReprocessInvoice`** (AC-011-120 a 126).

## Api

- [x] **T16 — Esqueleto.** `AutomationModule`, `AutomationMessages`, `AutomationResponses`, `AutomationLog`; teste de resolução do populador real (AC-011-127) e de roteamento (AC-011-004).
- [x] **T17 — Endpoints** das seis rotas, corpo exato como texto, 500 sem vazamento, isolamento de usuários (AC-011-005, 006, 010 a 019a, 030 a 042, 060 a 068, 080 a 107, 120 a 126).

## Fechamento

- [x] **T18 — Desvios e spec.** Criar `parity-deviations/096` a `102` (estado `proposto`), atualizar `README` dos desvios (e marcar `003`, `005`, `014`, `025`, `039`: parte 011 implementada); spec atualizada para `implementada`; build sem warnings e `dotnet test dotnet/MeuSecretario.slnx` verde.

Definição de pronto: `constitution.md` §10.
