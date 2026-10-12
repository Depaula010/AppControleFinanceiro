# Tasks 002 — Contas

Regra: cada tarefa termina com testes verdes e um commit. Ordem importa. Testes primeiro (vermelhos), depois a implementação. Pré-requisito: spec aprovada (feito). A 001 já está na base.

- [x] **T0 — Decisões.** Spec aprovada em 2026-10-02; resultado dos P-002-1 a P-002-9 em `spec.md` §11 (desvios 032 a 038 aprovados, 039 só documentado, 049 rejeitado); arquivos de desvio já existem em `parity-deviations/`.

## Domain

- [x] **T1 — Valores brutos e conversões.** `RawValue`, `RawBody`, `PyNumber.TryFloat/TryInt` com tabela de casos (`"12.5"`, `" 7 "`, `true`, `"1,5"`, `[]`, `10.9`, `"10"`, não finitos, fora de `numeric(15,2)`, arredondamento `10.005` ⇒ `10.01`). Testes `PyNumberTests` (AC-002-17, 18).
- [x] **T2 — Tipos e nome.** `AccountTypes` (6 valores, mensagem) e `AccountName` (`trim`, corte em 100 pontos de código). Testes (AC-002-15, 16, 43).
- [x] **T3 — Regras de validação.** `CreateRules.Validate`, `UpdateRules.ReadName/ReadFields/CheckConsistency`, `AccountError`, `NewAccountData`, `AccountChanges`/`Change<T>`; defaults só com chave ausente, `null` explícito, ordem da spec §3.2 e §3.3, `NothingToUpdate`. Testes `AccountRulesTests` (AC-002-12 a 19, 24, 28, 29, 43 a 52, 54 a 56).

## Application

- [x] **T4 — Porta, DTOs e casos de uso de leitura e remoção.** `IAccountRepository`, `AccountListItem`, `AccountDetails`, resultados; `ListAccounts` e `DeleteAccount` com repositório falso (NSubstitute). Testes (AC-002-01, 05, 70 a 73).
- [x] **T5 — `CreateAccount` e `UpdateAccount`.** Ordem completa (PUT: corpo ⇒ 404 ⇒ nome/duplicidade ⇒ campos ⇒ gravação); `NameTakenAsync` sem filtro de `ativa` e com `exceptId` no PUT; repositório intocado em erro de validação; `NameTaken` vindo da gravação (corrida). Testes (AC-002-10 a 14, 20 a 23, 40 a 44, 50, 51).

## Infrastructure

- [x] **T6 — `AccountRepository` + `AddAccountsInfrastructure`.** Dapper com o SQL do plan §3; helper `AccountsTestData` (inclui agendamento) (usuário, conta, transação com categoria mínima). Testes de integração em Testcontainers: saldo só com transações consolidadas (desvio 007), contas inativas fora do GET, nome em uso sem filtro de `ativa` (desvio 016), UNIQUE ⇒ `NameTaken` (constraint `contas_usuario_id_nome_conta_key` validada contra o baseline), `chk_cartao_credito` propaga, `UPDATE` parcial só dos campos `IsSet`, `FOR UPDATE` no remover com transação concorrente (AC-002-03, 04, 21 a 23, 41, 44, 70 a 77).

## Api

- [x] **T7 — Leitor de corpo, mensagens, respostas e log.** `AccountsBodyReader` (corpo ausente/vazio/inválido/não-objeto ⇒ `null`, `RawValue` por tipo JSON), `AccountsMessages`, `AccountsResponses` (corpo exato, ordem das chaves, `limite_credito` 0 ⇒ `null`, `data: []`), `AccountsLog` (EventIds 2001–2004, só tipo do erro). Testes unitários com `ResultRunner` (AC-002-06, 12, 85).
- [x] **T8 — `GET` com `RequireUser` (primeira prova em rota real).** `AccountsModule` registrando serviços e `GET /api/accounts` + `/api/contas`; testes de integração: 401 nas 4 mensagens, corpo exato, isolamento entre usuários (AC-002-01 a 06, 80, 81, 82 para GET).
- [x] **T9 — `POST`.** Endpoint + testes de integração por linha da §3.2, incluindo 500 reproduzidos (CHECK dos dias, `cor_hex` longo, nome não texto), duplicidade ativa/inativa/corrida e usuário do corpo ignorado (AC-002-10 a 29).
- [x] **T10 — `PUT`.** Endpoint com a regex de id e a conversão de id grande; testes por linha da §3.3, ordem com dois erros, corpo do Angular (AC-002-54), conta de outro usuário (AC-002-40 a 56, 86).
- [x] **T11 — `DELETE`.** Endpoint + testes (AC-002-70 a 77, 86).
- [x] **T12 — Roteamento e isolamento.** `AccountsRoutingTests` (alias, barra final, `abc`/`-1`/`1.5` ⇒ 404 HTML sem token, 405) e `AccountsTenantIsolationTests` com dois usuários cobrindo as 8 operações (AC-002-04, 22, 41, 53, 72, 82 a 84).

## Fechamento

- [x] **T13 — Contrato e desvios.** Revisar cada resposta contra o código Python lido (corpo exato dos testes de integração); desvios 007, 008, 016, 024 e 032 a 038 marcados como `implementado` neste escopo; avisar a feature 006 para reutilizar o saldo (RN-002-05) e as specs 004/011/012 sobre P-002-8. Spec atualizada se a implementação revelou algo (parar e pedir aprovação antes de seguir, conforme `specs/README.md`). Cobertura: Domain ≥ 90%, Application ≥ 80%. PR aberto com CI verde.

Notas de execução (2026-10-02): os testes de integração de T8 a T12 foram escritos em arquivos por rota (`AccountsAuthAndGetTests`, `AccountsPostEndpointsTests`, `AccountsPutEndpointsTests`, `AccountsDeleteEndpointsTests`, `AccountsRoutingIsolationAndFailureTests`). T13: falta só a revisão final contra o Flask em homologação (Fase 3) e o aviso às specs 004, 006, 011 e 012 (feito na resposta de entrega); o PR fica para o Rafael.

Definição de pronto: constitution.md §10.
