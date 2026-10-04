# Tasks 005 — Contas mensais (agendamentos)

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro (vermelhos), depois a implementação. Ordem importa. Pré-requisito: spec aprovada e as decisões P-005-1 a P-005-5 (todas aprovadas em 2026-10-04; ver spec §10.1). T0 e T15 a T18 entregues; T19 pendente só do PR e do aviso às features 011, 012, 013 e 006. Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`. Comandos: `dotnet build dotnet/MeuSecretario.slnx` e `dotnet test dotnet/MeuSecretario.slnx`. Sem migration.

- [x] **T0 — Decisões.** Spec aprovada; resultado dos P-005-1 a P-005-5 registrado em `spec.md` §10; arquivos de desvio criados em `parity-deviations/` (próximos números livres) com o estado decidido; resposta às perguntas Q-005-1 a Q-005-6.

## Domain

- [x] **T1 — Constantes e arredondamento.** `BillTypes`, `Periodicities`, `PyRound.Round2` com `bills-fixtures.json` gerado pelo script Python (`0.125 ⇒ 0.12`, `2.675 ⇒ 2.67`, `1.115 ⇒ 1.11`, negativos, grandes) (AC-005-97).
- [x] **T2 — Reserva.** `ReserveFactor` (tabela `N` de 1 a 36; `round(50 × 4,33) = 216`) e `EmergencyReserve.Calculate`/`Impact` contra o `get_reserva_status` do Python (fixtures) (AC-005-93, 100, 103, 104).
- [x] **T3 — Resumo.** `BillsSummaryCalculator` (fatores, `ANUAL` de receita fora, fallback, `meses ≤ 0`, ordem de soma fixa) (AC-005-90 a 98).
- [x] **T4 — Regras do POST.** `CreateBillRules` linha a linha da §3.2, `trim` só no POST, corte em 255 pontos de código, `LEMBRETE_VARIAVEL` com valor inválido ⇒ NULL, `total_parcelas` ignorado fora de `PARCELADO`, `mes_execucao` só em `ANUAL`, padrões (`notificar 3`, `incluir false`, `data_inicio` hoje), corpo vazio/`{}` ⇒ `DataMissing` (AC-005-10 a 23).
- [x] **T5 — Regras do PUT.** `UpdateBillRules` (`ReadHead`, referências, `ReadTail`; `str()`/`strip`/corte; tipo sem `trim`; `valor_previsto` sem sinal; `PARCELADO` sem `total_parcelas`; `notificar` inválido ⇒ ramo "500"; `NothingToUpdate`) (AC-005-40 a 55, 57).

## Application

- [x] **T6 — Portas e contratos.** `IBillRepository`, `IEmergencyReserveService` e records exatamente como spec §7 e plan §2; teste de arquitetura (Application não referencia Infrastructure).
- [x] **T7 — Casos de uso de leitura e remoção.** `ListBills`, `DeleteBill`, `GetBillsSummary`, `EmergencyReserveService` com repositório falso (resumo e reserva usam a mesma consulta de meses) (AC-005-01, 05, 70 a 72, 90 a 104).
- [x] **T8 — `CreateBill` e `UpdateBill`.** Ordem completa (POST: validações ⇒ conta; PUT: corpo ⇒ 404 ⇒ campos ⇒ conta ⇒ …); repositório intocado em erro de validação; `AccountNotFound`/`NotFound` (AC-005-11, 12, 24, 40, 41, 47, 55).

## Infrastructure

- [x] **T9 — `BillRepository`.** Testes de integração primeiro (SQL real): GET (ordem, `ativo`, `LEFT JOIN`, tenant), `CreateAsync` + releitura, `UpdateAsync` só dos campos `IsSet` e `updated_at` avança, `DeactivateAsync`, `CHECK` propagam (`ANUAL` sem mês, `chk_parcelado`), reserva e resumo com os dados da spec (AC-005-90 a 99), `meses_reserva_emergencia` NULL/sem linha ⇒ 6; `(double)decimal` igual ao `float(Decimal)` do Python (AC-005-01 a 08, 20, 56, 70 a 72).

## Api

- [x] **T10 — Respostas, mensagens e log.** `BillsResponses`, `BillsMessages`, `BillsLog` (EventIds 5001–5005) com testes unitários (`ResultRunner`): corpo exato, ordem das chaves, POST/PUT sem `message`, `tipo_transacao`, `meses_configurados` inteiro, 500 fixo sem trecho da exceção (AC-005-01, 10, 84, 98).
- [x] **T11 — `GET` e `summary` com `RequireUser`.** `BillsModule` registrando serviços e rotas; integração: 401 nas 4 mensagens, corpo exato, alias, isolamento (AC-005-01 a 08, 80, 81, 90 a 99).
- [x] **T12 — `POST`.** Endpoint + integração por linha da §3.2, `IClock` falso em `data_inicio`, 500 reproduzido (`ANUAL` sem mês, `subcategoria_id` inexistente), corpo do Angular (AC-005-10 a 28).
- [x] **T13 — `PUT` e `DELETE`.** Endpoints com `NumericIdFilter` e id grande; integração por linha da §3.3 e §3.4, ordem com dois erros, `notificar_antes_dias` inválido ⇒ 500, corpo do Angular, agendamento de outro usuário (AC-005-40 a 59, 70 a 72, 76, 85).
- [x] **T14 — Roteamento e isolamento.** `BillsRoutingTests` (barra final, `abc`/`-1`/`1.5` ⇒ 404 HTML sem token, 405, `summary` em POST/PUT/DELETE ⇒ 405, `/contas-mensais/summary` ⇒ 404) e `BillsTenantIsolationTests` com dois usuários nas 9 operações (AC-005-81 a 86).

## Condicionais (só entram se o Rafael aprovar o respectivo P-005-N)

- [x] **T15 — P-005-1** Tipos JSON, `notificar_antes_dias` ignorado, `valor_previsto` não finito, `data_inicio` e `incluir_na_reserva` validados; duas mensagens novas; testes das linhas "muda com P-005-1".
- [x] **T16 — P-005-2** `SubcategoryVisibleAsync` no POST (depois da conta) e no PUT (entre `valor_previsto` e `conta_id`), 404 `"Subcategoria não encontrada"`; testes de subcategoria de outro usuário e inexistente.
- [x] **T17 — P-005-3** `BillConsistency` + `GetCurrentAsync` + ajustes automáticos no `UPDATE` (`mes_execucao`, `total_parcelas`, `parcelas_executadas`); três mensagens novas; testes do corpo do Angular (AC-005-56, 58) e dos casos `ANUAL`⇄`MENSAL`, sair de `PARCELADO`, `valor_previsto` ≤ 0/`null`.
- [x] **T18 — P-005-4 e P-005-5** Predicado de receita com `Receitas Gerais` no GET e no resumo; exclusão de receita da reserva (`LEFT JOIN` de grupo no SQL da reserva).

## Fechamento

- [ ] **T19 — Contrato e desvios.** Revisar cada resposta contra o código Python lido (corpo exato dos testes de integração); desvios 008, 014 e 024 marcados como `implementado` neste escopo; verificar por busca que o fator de reserva (`4,33`, `2,16`, `30`) só existe em `Domain/Bills` e que 011/012 usam `IEmergencyReserveService`; avisar 011 e 012 (usar a porta; sem SQL de reserva), 013 (invariantes de `PARCELADO`, semântica de `parcelas_executadas`, predicado de receita de P-005-4, reutilizar `BillTypes`/`Periodicities`) e a 006 (se o dashboard usar o total de contas mensais). Spec atualizada se a implementação revelou algo (parar e pedir aprovação, conforme `specs/README.md`). Cobertura: Domain ≥ 90%, Application ≥ 80%. PR aberto com CI verde.

Definição de pronto: `constitution.md` §10.
