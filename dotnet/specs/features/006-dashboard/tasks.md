# Tasks 006 — Dashboard

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro, código depois. Pré-requisito: spec aprovada por delegação (só paridade e desvios 001/007/008/009; 089 a 095 propostos e **não** implementados). Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`. Comandos: `dotnet build dotnet/MeuSecretario.slnx` e `dotnet test dotnet/MeuSecretario.slnx`. Sem migration.

- [x] **T0 — Spec, plan, tasks e desvios 089 a 095** (estado `proposto`) e linhas no `parity-deviations/README.md`.

## Domain

- [ ] **T1 — Resumo.** `DashboardMonths`, `DashboardSummaryCalculator` (saldo, `abs`, `saldo_mes` com `PyRound`) (AC-006-10 a 20).
- [ ] **T2 — Janelas e parâmetro.** `ChartsWindow`, `MonthsParameter` (`int()` do Python, fallback 3, faixa 1..12), `WeekdayNames` (AC-006-30 a 32, 36, 37).
- [ ] **T3 — Alertas.** `DashboardAlerts` (ANUAL, SEMANAL/QUINZENAL/DIARIA, MENSAL, janela de 7 dias, nível, zero ⇒ null, ordenação estável) (AC-006-51 a 60).

## Application

- [ ] **T4 — Porta e casos de uso.** `IDashboardRepository`, 4 casos de uso com repositório falso e relógio fixo (AC-006-14, 30, 38, 61).

## Infrastructure

- [ ] **T5 — `DashboardRepository`.** Testes de integração primeiro (SQL real): saldo (consolidada, ativa), `SUM` por tipo, mensal, categoria (ordem e limite), dia da semana, recentes, agendamentos ativos, tenant.

## Api

- [ ] **T6 — Respostas, mensagens e log.** `DashboardResponses`, `DashboardMessages`, `DashboardLog` (6001–6004), 500 fixo (AC-006-05, 19, 42, 59).
- [ ] **T7 — Endpoints.** `DashboardModule` com os 8 caminhos; integração por rota com corpo exato; `RequireUser`; roteamento e isolamento (AC-006-01 a 06, 10 a 62).

## Fechamento

- [ ] **T8 — Contrato e desvios.** Revisar cada resposta contra o Python lido; spec §10 preenchida; build sem warnings e testes verdes; relatório com os arquivos compartilhados tocados.
