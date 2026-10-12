# Plan 005 — Contas mensais (agendamentos)

- **Spec:** `spec.md` (em revisão; P-005-1 a P-005-5 aguardam decisão, ver §7)

Reaproveita o que 001 a 003 entregaram: `Envelopes.A`, `RequireUser()`/`ICurrentUser`, `IDbConnectionFactory`, `SafeError`, `IClock`, `PostgresFixture`/`AuthFixtures`, e da 002 o `RawValue`/`RawBody`/`PyNumber` (namespace `MeuSecretario.Domain.Accounts`), o `AccountsBodyReader` (público) e o `NumericIdFilter`/`FlaskNotFoundResult` (`internal` no assembly `Api`). Mover esses tipos para um namespace comum é refatoração de arquivo compartilhado e fica fora (PR próprio, depois da onda). **Nenhum arquivo compartilhado é editado.** Sem EF (Dapper), sem pacote novo, **sem migration** (`0060-0069` reservados e não usados). A 004 e a 005 não se referenciam: cada uma tem seu repositório.

## 1. Domain (`MeuSecretario.Domain/Bills`)

Funções puras, sem I/O, todas com teste unitário (meta ≥ 90%).

| Tipo | Papel |
|---|---|
| `BillTypes` | `All` (`FIXO`, `LEMBRETE_VARIAVEL`, `PARCELADO`, nessa ordem), `IsValid`, `Installment`, `RequiresAmount(type)` (`FIXO`/`PARCELADO`) |
| `Periodicities` | `All` (`DIARIA`, `SEMANAL`, `QUINZENAL`, `MENSAL`, `ANUAL`), `IsValid`, `Annual` |
| `BillError` | enum com um membro por mensagem da spec (`DataMissing`, `DescriptionRequired`, `TypeInvalidCreate`, `PeriodicityInvalidCreate`, `SubcategoryRequired`, `AccountRequired`, `DayInvalid`, `AmountRequired`, `AmountNotPositive`, `InstallmentsInvalid`, `MonthInvalidCreate`, `DescriptionEmpty`, `TypeInvalidUpdate`, `PeriodicityInvalidUpdate`, `MonthInvalidUpdate`, `AmountInvalidUpdate`, `InstallmentsRequired`, `NothingToUpdate` e os de P-005-1/3 se aprovados) |
| `CreateBillRules.Validate(RawBody?, DateOnly today)` | linhas 1 a 11 da §3.2 (a 12, conta ativa, é do banco); `trim` e corte em 255 pontos de código; `RawValue` do tipo/periodicidade com `trim`; `valor_previsto` por tipo (positivo em `FIXO`/`PARCELADO`; `LEMBRETE_VARIAVEL`: inválido ⇒ NULL); `total_parcelas` só em `PARCELADO`; `mes_execucao` só em `ANUAL`; `notificar_antes_dias` com padrão 3; `data_inicio` texto cru ou hoje; `incluir_na_reserva` padrão `false` ⇒ `NewBillData` ou `BillError` |
| `UpdateBillRules` | passos, porque a ordem intercala banco: `ReadHead(RawBody)` (descricao, tipo, periodicidade, dia, mes, valor ⇒ `BillHeadChanges` ou erro), `SubcategoryRef`/`AccountRef` (ids crus para o caso de uso verificar no banco, na ordem do corpo: subcategoria só com P-005-2, conta sempre), `ReadTail(RawBody)` (notificar, data_inicio, incluir, total_parcelas, `InstallmentsRequired`) ⇒ `BillChanges` (`Change<T>`, `IsEmpty` ⇒ `NothingToUpdate`). O ramo "notificar inválido ⇒ 500 por parâmetro faltando" é reproduzido como um `BillError.NotificationDaysBroken` que o caso de uso converte em exceção (some se P-005-1 for aprovado: ignora o campo) |
| `BillConsistency.Check(current, changes)` | **só com P-005-3**: estado resultante vs `chk_anual_mes`, `chk_parcelado`, `valor_previsto`; devolve `BillError?` e os ajustes automáticos (`mes_execucao = null`, `total_parcelas = null`, `parcelas_executadas = 0`) |
| `ReserveFactor` | `For(string periodicity, int months)` ⇒ `double` (`MENSAL N`, `ANUAL 1`, `SEMANAL round(N × 4,33)` com `MidpointRounding.ToEven`, `QUINZENAL 2N`, `DIARIA 30N`; desconhecida ⇒ 0) (RN-005-11) |
| `EmergencyReserve` | `Calculate(int months, IEnumerable<PeriodTotal> totals)` ⇒ `(double Total, double MonthlyEquivalent)` sem arredondar; `Impact(amount, periodicity, months)` |
| `BillsSummaryCalculator` | `Calculate(IEnumerable<SummaryRow(Periodicity, bool IsIncome, double Total)>, EmergencyReserveStatus reserve, int months)` ⇒ `BillsSummary` (totais com fatores `4,33`, `2,16`, `30`; `ANUAL` de receita fora; fallback `total_despesas_mensais × meses` quando a reserva é 0; `PyRound.Round2` na meta e no equivalente; `meses ≤ 0` ⇒ equivalente 0) |
| `PyRound` | `Round2(double)`: `round(x, 2)` do Python, **meio-par sobre o valor binário exato**: `x.ToString("F30", Invariant)` ⇒ `decimal.Parse` ⇒ `decimal.Round(…, 2, ToEven)` ⇒ `double`; `Math.Round(double, 2)` não serve (escala por 100 e erra em casos como `1.115`). Fica em `Domain/Common` **como arquivo novo**, sem editar os existentes |

## 2. Application (`MeuSecretario.Application/Bills`)

**Porta pública (011/012):** `IEmergencyReserveService` + `EmergencyReserveStatus` + `EmergencyReserveItem` exatamente como a spec §7 (arquivo `EmergencyReserveContracts.cs`).

**Porta interna:**
```csharp
public interface IBillRepository
{
    Task<IReadOnlyList<BillListItem>> ListActiveAsync(int userId, CancellationToken ct);                       // 19 campos do GET
    Task<bool> ActiveScheduleExistsAsync(int userId, int billId, CancellationToken ct);                        // 404 do PUT
    Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken ct);
    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken ct);                   // só P-005-2
    Task<BillDetails> CreateAsync(int userId, NewBillData data, CancellationToken ct);                         // INSERT + leitura (16 campos)
    Task<BillDetails?> UpdateAsync(int userId, int billId, BillChanges changes, CancellationToken ct);         // null = sumiu (404)
    Task<bool> DeactivateAsync(int userId, int billId, CancellationToken ct);                                  // false = 404
    Task<CurrentBill?> GetCurrentAsync(int userId, int billId, CancellationToken ct);                          // só P-005-3 (estado atual)
    Task<IReadOnlyList<SummaryRow>> ListSummaryRowsAsync(int userId, CancellationToken ct);
    Task<IReadOnlyList<PeriodTotal>> ListReserveTotalsAsync(int userId, CancellationToken ct);
    Task<IReadOnlyList<EmergencyReserveItem>> ListReserveItemsAsync(int userId, CancellationToken ct);
    Task<int> GetReserveMonthsAsync(int userId, CancellationToken ct);                                         // COALESCE(..., 6); sem linha ⇒ 6
}
```

**Casos de uso** (uma classe cada, `ExecuteAsync`, resultado tipado sem HTTP; `userId` vem do endpoint):
- `ListBills`.
- `CreateBill(RawBody?)`: `CreateBillRules.Validate(body, clock.Today)` ⇒ erro; (P-005-2) `SubcategoryVisibleAsync` ⇒ `SubcategoryNotFound` (antes da conta? não: no Flask a conta é a única checagem de banco; com P-005-2, subcategoria depois da conta); `ActiveAccountOwnedAsync` ⇒ `AccountNotFound`; `CreateAsync`.
- `UpdateBill(billId, RawBody?)` (a ordem **é** a §3.3): corpo vazio ⇒ `DataMissing`; `ActiveScheduleExistsAsync` ⇒ `NotFound`; `ReadHead`; subcategoria (P-005-2) ⇒ `SubcategoryNotFound`; conta ⇒ `AccountNotFound`; `ReadTail`; `IsEmpty` ⇒ `NothingToUpdate`; (P-005-3: `GetCurrentAsync` + `BillConsistency.Check`); `UpdateAsync`.
- `DeleteBill(billId)`: `DeactivateAsync`.
- `GetBillsSummary`: `ListSummaryRowsAsync`, `IEmergencyReserveService.GetStatusAsync`, `GetReserveMonthsAsync` (a mesma consulta do serviço, sem duplicar SQL: o serviço expõe o número de meses dentro de `EmergencyReserveStatus.Months`) ⇒ `BillsSummaryCalculator`.
- `EmergencyReserveService : IEmergencyReserveService`: `GetStatusAsync` = `IBillRepository.GetReserveMonthsAsync` + `ListReserveTotalsAsync` ⇒ `EmergencyReserve.Calculate`; `GetItemsAsync` = `ListReserveItemsAsync` (impacto calculado no Domain, ordenado por impacto decrescente e `id`).

Textos em português ficam no endpoint (`BillsMessages`), como na 001 e 002.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Bills`)

`BillRepository(IDbConnectionFactory)` com **Dapper**, registrado pelo `BillsModule`. Colunas minúsculas, sem aspas; nenhum texto do cliente concatenado (fragmentos fixos por campo `IsSet`). `DateOnly` como parâmetro tipado (`DbType.Date`).

**Listar:**
```sql
SELECT a.id, a.descricao, a.valor_previsto, a.tipo_agendamento, a.periodicidade, a.dia_execucao, a.mes_execucao,
       a.notificar_antes_dias, a.subcategoria_id, s.nome_sub AS subcategoria_nome, m.nome_macro AS macro_categoria_nome,
       g.nome_grupo AS grupo, a.conta_id, c.nome_conta AS conta_nome, a.data_inicio, a.incluir_na_reserva,
       a.total_parcelas, a.parcelas_executadas
FROM agendamentos a
LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
LEFT JOIN macrocategoria m ON s.macro_id = m.id
LEFT JOIN grupocategoria g ON m.grupo_id = g.id
LEFT JOIN contas c ON a.conta_id = c.id
WHERE a.usuario_id = @userId AND a.ativo = true
ORDER BY a.periodicidade, a.dia_execucao, a.descricao, a.id;
```
`tipo_transacao` é derivado no Domain/Api (`grupo == "Renda"`; com P-005-4, também macro `Receitas Gerais` do grupo `Geral`: o SQL então traz também `m.nome_macro`, que já está na lista).

**Existe ativo / conta / subcategoria:**
```sql
SELECT EXISTS (SELECT 1 FROM agendamentos WHERE id = @billId AND usuario_id = @userId AND ativo = true);
SELECT EXISTS (SELECT 1 FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true);
SELECT EXISTS (SELECT 1 FROM subcategoria WHERE id = @id AND (usuario_id IS NULL OR usuario_id = @userId));   -- P-005-2
```

**Criar** (um comando + releitura na mesma conexão, `READ COMMITTED`):
```sql
INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento, periodicidade,
                          data_inicio, dia_execucao, mes_execucao, notificar_antes_dias, ativo, parcelas_executadas,
                          incluir_na_reserva, total_parcelas)
VALUES (@userId, @contaId, @subcategoriaId, @descricao, @valorPrevisto, @tipo, @periodicidade,
        @dataInicio, @diaExecucao, @mesExecucao, @notificarAntesDias, true, 0, @incluirNaReserva, @totalParcelas)
RETURNING id;

SELECT a.id, a.descricao, a.valor_previsto, a.tipo_agendamento, a.periodicidade, a.dia_execucao, a.mes_execucao,
       a.notificar_antes_dias, a.subcategoria_id, s.nome_sub AS subcategoria_nome, a.conta_id, c.nome_conta AS conta_nome,
       a.data_inicio, a.incluir_na_reserva, a.total_parcelas, a.parcelas_executadas
FROM agendamentos a
LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
LEFT JOIN contas c ON a.conta_id = c.id
WHERE a.id = @id AND a.usuario_id = @userId;
```
`data_inicio` entra como `DateOnly` (hoje do `IClock`) quando o corpo não o traz, ou como o texto cru convertido pelo servidor com `@dataInicio::date` (o Flask passa o texto ao banco; erro de data ⇒ 500, ou 400 com P-005-1).

**Atualizar** (fragmentos fixos dos campos `IsSet`; `null` explícito grava NULL onde a spec diz):
```sql
UPDATE agendamentos SET descricao = @descricao, tipo_agendamento = @tipo, periodicidade = @periodicidade,
       dia_execucao = @dia, mes_execucao = @mes, valor_previsto = @valor, subcategoria_id = @subcategoriaId,
       conta_id = @contaId, notificar_antes_dias = @notificar, data_inicio = @dataInicio::date,
       incluir_na_reserva = @incluir, total_parcelas = @totalParcelas        -- só os presentes
WHERE id = @billId AND usuario_id = @userId;
-- depois, a mesma releitura do Criar
```
Violações de `CHECK`/FK propagam como exceção ⇒ 500 (paridade; viram 400 com P-005-3/P-005-2). Com P-005-3, o `UPDATE` acrescenta `mes_execucao = NULL`, `total_parcelas = NULL`, `parcelas_executadas = 0` conforme `BillConsistency`.

**Desativar:** `UPDATE agendamentos SET ativo = false WHERE id = @billId AND usuario_id = @userId AND ativo = true` (linhas afetadas 0 ⇒ `false`).

**Resumo e reserva:**
```sql
-- ListSummaryRows (ordem fixa, RN-005-13)
SELECT a.periodicidade,
       CASE WHEN g.nome_grupo = 'Renda' /* OR (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais') -- P-005-4 */
            THEN 'Receita' ELSE 'Despesa' END AS tipo,
       COALESCE(SUM(a.valor_previsto), 0) AS total
FROM agendamentos a
LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
LEFT JOIN macrocategoria m ON s.macro_id = m.id
LEFT JOIN grupocategoria g ON m.grupo_id = g.id
WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.valor_previsto IS NOT NULL AND a.valor_previsto > 0
GROUP BY a.periodicidade, g.nome_grupo /*, m.nome_macro -- P-005-4 */
ORDER BY a.periodicidade, g.nome_grupo;

-- GetReserveMonths
SELECT COALESCE(meses_reserva_emergencia, 6) FROM usuarios WHERE id = @userId;       -- sem linha ⇒ 6

-- ListReserveTotals (desvio 014: filtro de tipo; P-005-5 acrescenta o LEFT JOIN de grupo e exclui receita)
SELECT a.periodicidade, COALESCE(SUM(a.valor_previsto), 0) AS total_periodo
FROM agendamentos a
WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.incluir_na_reserva = TRUE
  AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
GROUP BY a.periodicidade
ORDER BY a.periodicidade;

-- ListReserveItems
SELECT a.id, a.descricao, a.valor_previsto, a.periodicidade, s.nome_sub AS categoria
FROM agendamentos a JOIN subcategoria s ON a.subcategoria_id = s.id
WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.incluir_na_reserva = TRUE
  AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL');
```
`NUMERIC` ⇒ `decimal` ⇒ `double` (`(double)decimal`; para valores de 2 casas é igual a `float(Decimal)` do Python; coberto por teste). A ordenação do detalhe por impacto é no Domain (`OrderByDescending(Impact).ThenBy(Id)`).

**Migrations: nenhuma.**

## 4. Api (`MeuSecretario.Api/Bills`)

- `BillsModule : IFeatureModule`. `AddServices`: `IBillRepository`, `IEmergencyReserveService`, os casos de uso (`ListBills`, `CreateBill`, `UpdateBill`, `DeleteBill`, `GetBillsSummary`). `MapEndpoints`: para `["/api/bills", "/api/contas-mensais"]`: `MapGet(base)`, `MapPost(base)`, `.RequireUser()`; `MapPut/MapDelete($"{base}/{{id}}")` com `NumericIdFilter` antes do `RequireUser`; **só em `/api/bills`:** `MapGet("/api/bills/summary").RequireUser()` (o literal vence `{id}`); `app.Map($"{base}/{{id}}", OtherMethods)`: id numérico ⇒ 405; em `/api/bills`, `summary` ⇒ **405** (a rota estática existe para GET); outro texto ⇒ 404 HTML. Id maior que `int.MaxValue` vira 0 (404 JSON, RN-005-16).
- Corpo: `AccountsBodyReader.ReadAsync` ⇒ `RawBody?`.
- `BillsResponses` (`Envelopes.A`): GET (19 chaves na ordem; `tipo_transacao` calculado), POST (201) e PUT (200) **sem `message`** (`{"status":"success","data":{16 chaves}}`), DELETE (200 com `message`, sem `data`), resumo (chaves na ordem da §3.5; `meses_configurados` inteiro), erros; valores monetários como `double`.
- `BillsMessages` (todas as mensagens da spec, literais) e `BillsLog` (`LoggerMessage`, EventIds **5001–5005**; só `SafeError.Describe`, `userId`, `billId`).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com a mensagem fixa (desvio 008).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `BillTypesTests`, `CreateBillRulesTests`, `UpdateBillRulesTests`, `BillConsistencyTests` (P-005-3), `ReserveFactorTests`, `EmergencyReserveTests`, `BillsSummaryCalculatorTests`, `PyRoundTests` (fixtures do Python) | AC-005-10 a 23, 40 a 55, 90 a 98, 100 a 104 |
| Unitário (Application, NSubstitute) | `ListBillsTests`, `CreateBillTests`, `UpdateBillTests`, `DeleteBillTests`, `GetBillsSummaryTests`, `EmergencyReserveServiceTests` | ordem (400 antes do 404 do PUT; 404 do agendamento antes dos campos; conta só depois das validações no POST); repositório intocado em erro de validação; `AccountNotFound`/`NotFound` do repositório |
| Unitário (Api) | `BillsResponsesTests`, `BillsMessagesTests` (com `ResultRunner`) | corpo exato; ordem das chaves; POST/PUT sem `message`; `tipo_transacao`; `meses_configurados` inteiro; 500 fixo (AC-005-01, 10, 84, 98) |
| Integração | `BillRepositoryTests` | SQL real: `ativo`, ordem, `LEFT JOIN`, `RETURNING`/releitura, `UPDATE` parcial só dos `IsSet`, `CHECK` propagam (`chk_anual_mes`, `chk_parcelado`), tenant, soma e reserva com os 5 periodicidades e o filtro de tipo (AC-005-01 a 08, 20, 56, 70 a 72, 90 a 99) |
| Integração | `BillsEndpointsTests` (por rota: `BillsGetEndpointsTests`, `…Post…`, `…Put…`, `…Delete…`, `…Summary…`) | cada linha da §3 com corpo como texto exato; `RequireUser` real (AC-005-80); corpo do Angular (AC-005-27, 56) |
| Integração | `BillsRoutingTests`, `BillsTenantIsolationTests` | alias, barra final, `abc`/`-1`/`1.5` ⇒ 404 HTML sem token, 405, `summary` (405 em POST/PUT/DELETE, 404 em `/contas-mensais/summary`); isolamento A × B nas 9 operações (AC-005-81 a 86) |
| Contrato (Fase 3) | goldens do Flask de homologação | GET, POST/PUT/DELETE e o resumo com os dados reais (38 agendamentos); diferença de último dígito no resumo aceita (Q-005-4) |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","005-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Fakes e fixtures

- `PostgresFixture` (`ResetAsync` reaplica o seed de categorias); `IClock` falso para `data_inicio` (AC-005-22: 2026-10-04 23:30 em São Paulo).
- Token: `JwtTokenService`/`AuthFixtures` como na 002.
- `BillsTestData` (só teste): `InsertUserAsync(mesesReserva?)`, `InsertAccountAsync` (por cópia do helper da 002), `InsertScheduleAsync(tipo, periodicidade, dia, mes?, valor?, ativo, incluirNaReserva, parcelas?)`, `FindGlobalSubcategoryAsync(grupo)`.
- **Fixture Python do arredondamento e da reserva:** `tests/Fixtures/generate_bills_fixtures.py` chama `round(x, 2)` e `get_reserva_status` (com `conn` simulado) para ~300 valores e combinações de periodicidade e `meses` (1 a 36, inclui `50` ⇒ `round(216,5)`), e gera `bills-fixtures.json` (padrão das fixtures da 001/010).

## 7. Riscos e decisões

- **O PUT e o POST reproduzem 500 de `CHECK`** enquanto P-005-3 não for aprovado; o Angular os produz (AC-005-56). Por isso o plan isola a consistência em `BillConsistency` (T17, condicional) sem alterar o caminho literal.
- **Ordem do PUT intercala banco e regras:** `UpdateBillRules` expõe passos e o caso de uso os encadeia (padrão da 002).
- **Resumo com `double`:** os fatores `4,33`/`2,16` geram números longos; reproduzir é aritmética `double` idêntica ao Python (IEEE 754); a ordem de soma é fixada (RN-005-13) e a diferença no último dígito é aceita no contrato (Q-005-4). `PyRound` evita o erro de `Math.Round`.
- **Reserva única:** o resumo, a 011 e a 012 só usam `IEmergencyReserveService` (um SQL, uma fórmula). Verificação por busca na T19 garante que não restou outro lugar com os fatores de reserva.
- **`NumericIdFilter` e `/api/bills/summary`:** o literal `summary` vence `{id}`; o 405 de `POST|PUT|DELETE /summary` é tratado no `OtherMethods` (id `summary` só em `/api/bills`).
- **`data_inicio` textual:** o Flask passa o texto ao banco (que aceita formatos como `2026-1-5`, `20260105`); a conversão no servidor (`::date`) preserva isso até P-005-1.
- **Reutilização cruzada de tipos da 002** (`RawBody`, `PyNumber`, `AccountsBodyReader`, `NumericIdFilter`): dependência intencional e temporária.
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
