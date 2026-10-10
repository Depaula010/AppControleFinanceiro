# Plan 006 — Dashboard

- **Spec:** `spec.md` (aprovada por delegação; só paridade e desvios 001/007/008/009)

Reaproveita: `Envelopes.A`, `RequireUser()`/`ICurrentUser`, `IDbConnectionFactory`, `SafeError`, `IClock`, `PyRound` (Domain/Common), `PostgresFixture`/`AuthFixtures` e, nos testes de integração, `TransactionsTestData` e `BillsTestData` (apoio das features 004 e 005, sem editá-los). **Nenhum arquivo compartilhado é editado**: sem `Program.cs`, sem `DependencyInjection.cs`, sem pacote novo, **sem migration**. `IEmergencyReserveService` e `InvoiceCycle` não entram (o dashboard do Flask não calcula reserva nem fatura). Sem EF: Dapper.

## 1. Domain (`MeuSecretario.Domain/Dashboard`)

Funções puras, sem I/O, todas com teste unitário (meta ≥ 90%).

| Tipo | Papel |
|---|---|
| `DashboardMonths` | `Reference(DateOnly)` ⇒ `"Outubro/2026"` (12 nomes em português fixos, sem cultura) |
| `DashboardSummaryCalculator` | `Calculate(IEnumerable<decimal> balances, decimal income, decimal expense, DateOnly today)` ⇒ `DashboardSummary` (saldo = soma, receitas/despesas = `abs`, `saldo_mes` = diferença antes do `PyRound.Round2`, `mes_referencia`) |
| `ChartsWindow` | `MonthStart(today)`; `MonthlyStart(today, months)` = dia 1 − 30 × meses dias; `NextMonthStart(today)`; `WeekdayStart(today)` = hoje − 90 |
| `MonthsParameter` | `Parse(string? raw)` ⇒ `int` ou rejeitado: `int()` do Python (espaços, sinal, `_`, número gigante via `BigInteger`), falha ⇒ 3; faixa 1..12 |
| `WeekdayNames` | `For(int dow)` ⇒ `Sunday` ... `Saturday` (desvio 009) |
| `DashboardAlerts` | `Build(IEnumerable<ScheduleRow>, DateOnly today)` ⇒ `IReadOnlyList<DashboardAlert>`: `NextDue` (ANUAL, SEMANAL/QUINZENAL/DIARIA, MENSAL), janela de 7 dias, nível, valor zero ⇒ `null`, ordenação estável |

## 2. Application (`MeuSecretario.Application/Dashboard`)

`IDashboardRepository` com: saldos das contas ativas (`ORDER BY tipo_conta, nome_conta, id`), soma por tipo no intervalo, gastos mensais (`ORDER BY mes DESC`), gastos por categoria (`total DESC LIMIT 10`), gastos por dia da semana, recentes (`LIMIT 10`) e agendamentos ativos. Casos de uso (um por rota, `ExecuteAsync`, sem HTTP): `GetDashboardSummary(repo, clock)`, `GetDashboardCharts(repo, clock)` (recebe `months` já validado), `GetRecentTransactions(repo)`, `GetDashboardAlerts(repo, clock)`. Textos em português ficam no endpoint (`DashboardMessages`).

## 3. Infrastructure (`MeuSecretario.Infrastructure/Dashboard`)

`DashboardRepository(IDbConnectionFactory)` com Dapper, registrado pelo módulo. Colunas minúsculas, sem aspas; parâmetros tipados (`DateOnly`); SQL fixo. `SUM(valor)` ⇒ `decimal` ⇒ `(double)` na resposta (igual a `float(Decimal)`). O dia da semana vem de `EXTRACT(DOW FROM data_transacao)::int` e o mês de `TO_CHAR(data_transacao, 'YYYY-MM')` (formato fixo, independente de `lc_time`).

## 4. Api (`MeuSecretario.Api/Dashboard`)

- `DashboardModule : IFeatureModule`: `AddServices` (repositório e 4 casos de uso); `MapEndpoints`: `MapGet` nos 8 caminhos com `.RequireUser()`. Métodos diferentes de GET respondem 405 pelo roteamento; o literal `/api/transactions/recent` convive com o `{id}` da 004.
- `DashboardResponses` (`Envelopes.A`) com dicionários em ordem; `DashboardMessages`; `DashboardLog` (EventIds **6001–6004**, só `SafeError.Describe` e `userId`).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com o texto fixo (desvio 008). Charts valida `meses` depois do `RequireUser`, dentro do mesmo `try`.

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `DashboardSummaryCalculatorTests`, `ChartsWindowTests`, `MonthsParameterTests`, `WeekdayNamesTests`, `DashboardAlertsTests` | AC-006-10 a 20, 30 a 32, 36, 37, 51 a 60 |
| Unitário (Application) | `DashboardUseCasesTests` | janelas por `IClock` (23:30 de São Paulo), tradução das linhas, `meses` repassado |
| Unitário (Api) | `DashboardApiTests` | corpo exato, ordem das chaves, 500 fixo sem trecho da exceção (AC-006-05, 19, 42, 59) |
| Integração | `DashboardRepositoryTests` | SQL real: saldo, janela do mês, ordem, `LIMIT 10`, DOW, tenant, `ativo` |
| Integração | `DashboardEndpointsTests` (resumo, gráficos, recentes, alertas) | cada linha da §3 com corpo exato; `RequireUser` real; relógio fixo |
| Integração | `DashboardRoutingAndIsolationTests` | aliases, barra final, 404, 405, `/api/transactions/recent`, isolamento A × B (AC-006-01 a 06) |
| Contrato (Fase 3) | goldens do Flask de homologação | 4 rotas com os dados reais |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","006-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Riscos e decisões

- **Colisão de roteamento com a 004:** `/api/transactions/{id}` (`Map`, todos os métodos) e o literal `/api/transactions/recent` (GET): o literal vence; teste obrigatório (AC-006-02, 04).
- **Recentes `Renda`:** o front depende dele (089, rejeitado); `TransactionTypes.ToApi` **não** é usado aqui.
- **`double` do resumo:** soma em `decimal` exato e conversão antes do `PyRound` (parcelas de 2 casas ⇒ mesmo resultado que o `sum` de `float` do Python depois do `round`).
- **Relógio:** todas as janelas saem do `IClock.Today`, não do `CURRENT_DATE` do banco (UTC).
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
