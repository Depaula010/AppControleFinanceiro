# Tasks 004 — Transações

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro (vermelhos), depois a implementação. Ordem importa. Pré-requisito: spec aprovada (**estado atual: em revisão**) e as decisões P-004-1 a P-004-7. Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`. Comandos: `dotnet build dotnet/MeuSecretario.slnx` e `dotnet test dotnet/MeuSecretario.slnx`.

- [ ] **T0 — Decisões.** Spec aprovada; resultado dos P-004-1 a P-004-7 registrado em `spec.md` §10; arquivos de desvio criados em `parity-deviations/` (próximos números livres) com o estado decidido; resposta às perguntas Q-004-1 a Q-004-5 (principalmente Q-004-1 e Q-004-2).

## Migration

- [ ] **T1 — `0050_transacoes_observacoes_origem.sql`.** Teste de integração `Migration0050Tests` primeiro (colunas existem, `text` e `varchar(20)`, segunda execução não falha, linha antiga sem as colunas lê NULL), depois o arquivo (spec §6.1). Verificar que o `MigrationRunner` embute o arquivo novo (padrão `migrations/*.sql`).

## Domain

- [ ] **T2 — Tipos, sinal, datas.** `TransactionTypes`, `SignedAmount`, `MonthDays`, `FlaskDate`, `PostingDescriptions`, `InstallmentSchedule` com tabelas (`Renda`⇒`Receita`; `-0.0`; `2026-1-5` válido, `2026-02-30`/`05/01/2026`/`2026-12-11T10:00` inválidos; `31/01 + 1 mês`; `29/12/2027 + 1 mês`) (AC-004-06, 11, 18, 43, 44, 134).
- [ ] **T3 — `InvoiceCycle`.** Gerar `invoice-cycle-fixtures.json` com o script Python (fórmula literal); `InvoiceCycleTests` com a tabela de 12 linhas da spec e as ~200 combinações (31/02, bissexto, virada de ano, `V < F`, `V = F`). **Decisão do P-004-1 antes de começar**: com aprovação, só `ForPurchase` corrigida (as fixtures literais viram teste de que as duas só divergem em `D > F` e `V ≥ F`); sem, `ForPurchase` literal + `CurrentFor` (AC-004-90 a 94, 96).
- [ ] **T4 — Regras de entrada.** `ListQueryParser` (ordem limit, offset, tipo, datas; `limit` gigante ⇒ erro; texto ⇒ padrão), `CreateTransactionRules` (linhas 1 a 9; `0` e `false` em `valor`; falso em `data`/ids; corpo nulo ⇒ erro de `descricao`; eco de `DateText`), `UpdateTransactionRules` (`ReadHead`, `ReadReferences`, `ReadTail`, sinal por tipo atual/`Renda`, ordem valor antes de tipo, `descricao` falsa ignorada, `NoValidField`) (AC-004-02 a 06, 12 a 22, 40 a 53).

## Application

- [ ] **T5 — Contratos e portas.** `TransactionPostingContracts.cs` (comandos, resultados, `PostingError`, `IInvoiceService`, `IInvoiceSchedulePopulator`) exatamente como a spec §7.1; `ITransactionRepository`, `ITransactionsUnitOfWork`, `ITransactionsSession` e os records de linha. Só compilação + teste de arquitetura (Application não referencia Infrastructure).
- [ ] **T6 — `TransactionPosting`.** Sessão falsa; testes primeiro dos 4 métodos: ordem valor/conta/subcategoria, fatura só depois das checagens, `Commit` só no sucesso, nada gravado em erro, sinal, tipos, descrições, pares, desvio 010 (duas `Fechada`, nenhuma `Fechada`, cartão sem dias), `NotACreditCard`, `SameAccount`, `RequiredCategoryMissing`, parcelamento (`parcelas_executadas = 1`, `data_inicio`) (AC-004-110 a 134).
- [ ] **T7 — `InvoiceService.EnsureCurrentAsync` e populador.** `IClock` falso; cria fatura do ciclo de hoje, não recria `Aberta` existente, ignora cartão inativo/alheio, populador só depois do commit, erro do populador engolido, `NullInvoiceSchedulePopulator` (AC-004-95, 140, 141).
- [ ] **T8 — `ListTransactions`, `CreateTransaction`, `UpdateTransaction`, `DeleteTransaction`.** NSubstitute; ordem completa do PUT (corpo ⇒ 404 ⇒ valor/tipo/data ⇒ subcategoria ⇒ conta ⇒ observações/consolidada ⇒ vazio); repositório intocado em erro de validação; `NotFound` do `UpdateAsync` (AC-004-13, 20, 40 a 53, 70 a 72).

## Infrastructure

- [ ] **T9 — `TransactionRepository`.** Testes de integração primeiro (SQL real): filtros e ordem (desempate por `id DESC`), paginação, `total` com filtros, `Renda` aparece em `Receita`, isolamento, `UpdateAsync` só dos campos `IsSet` e `updated_at` avança, `DeleteWithPairAsync` (par próprio, par de outro usuário, rollback quando a segunda `DELETE` falha) (AC-004-01 a 09, 42, 70 a 77). Verificar o tratamento de `DateOnly` no Dapper (plan §7).
- [ ] **T10 — `TransactionsUnitOfWork`/`TransactionsSession`.** Integração: `InsertInvoiceIfMissing` com corrida (`Task.WhenAll` de 2 sessões ⇒ uma linha em `faturas`), `FindOldestClosedInvoice` (ordem e desempate), `InsertTransaction` com `origem`/`observacoes`/`agendamento_id`, `LinkPair` nos dois sentidos, rollback sem `Commit`, falha injetada no meio dos 4 serviços deixando o banco intacto (AC-004-25, 26, 75, 117, 132).
- [ ] **T11 — Serviços de lançamento ponta a ponta no banco.** `TransactionPostingTests` com `ICategoryLookup` real (seed da `0020`): simples, transferência, pagamento (desvio 010), parcelada; tenant (contas de outro usuário, faturas de outro cartão); `Outros` duplicado não interfere (AC-004-110 a 134).

## Api

- [ ] **T12 — Respostas, mensagens e log.** `TransactionsResponses`, `TransactionsMessages`, `TransactionsLog` (EventIds 4001–4004 e 4101–4103) com testes unitários (`ResultRunner`): corpo exato, ordem das chaves, `valor` e `data` do eco, `tipo` mapeado, 500 fixo sem trecho da exceção (AC-004-01, 10, 16, 18, 83).
- [ ] **T13 — `GET` com `RequireUser`.** `TransactionsModule` registrando serviços e `MapGet`; integração: 401 nas 4 mensagens, corpo exato, todos os filtros e erros, isolamento (AC-004-01 a 09, 10a, 80, 85).
- [ ] **T14 — `POST`.** Endpoint + integração por linha da §3.2, fatura criada e reutilizada, rollback de fatura, corrida, corpo do Angular, usuário do corpo ignorado (AC-004-10 a 29).
- [ ] **T15 — `PUT`.** Endpoint com `NumericIdFilter` e id grande; integração por linha da §3.3, ordem com dois erros, sinal em `Renda` do bot, `fatura_id` intacto (RN-004-14), corpo do Angular (AC-004-40 a 56).
- [ ] **T16 — `DELETE`.** Endpoint + integração: par de transferência e de pagamento, par de outro usuário, `fatura` intacta (AC-004-70 a 77, 84).
- [ ] **T17 — Roteamento e isolamento.** `TransactionsRoutingTests` (barra final, `abc`/`-1`/`1.5` ⇒ 404 HTML sem token, 405, `/recent` ⇒ 405) e `TransactionsTenantIsolationTests` com dois usuários nas 4 rotas e nos 4 serviços (AC-004-81, 82, 85).

## Condicionais (só entram se o Rafael aprovar o respectivo P-004-N)

- [ ] **T18 — P-004-2** Entradas que viram 500: mensagens novas, tipos JSON, limites; ampliar `CreateTransactionRules`/`UpdateTransactionRules` e os testes de §3.5.
- [ ] **T19 — P-004-3** Campos extras no GET (`conta_id`, `subcategoria_id`, `observacoes`, `data`, `conta_bancaria`, `conta_bancaria_id`) no SQL do plan §3 e em `TransactionsResponses`.
- [ ] **T20 — P-004-4** `NotEditable` no PUT de `Transferência`/`Pagamento Fatura` (após o 404), mensagem e testes.
- [ ] **T21 — P-004-5** Recalcular `fatura_id` no PUT (conta, data ou tipo) com `ITransactionsSession`, na mesma transação; testes de mover para conta não cartão, trocar de ciclo, trocar para `Receita`.
- [ ] **T22 — P-004-6** Reabrir a fatura ao apagar `Pagamento Fatura` (`Fechada`/`Aberta`), testes com um e com dois pagamentos na fatura.
- [ ] **T23 — P-004-7** `NotACreditCard` em `PostInvoicePaymentAsync` (já previsto no contrato; se rejeitado, remover o membro e reproduzir o Flask: par sem fatura).

## Fechamento

- [ ] **T24 — Contrato e desvios.** Revisar cada resposta contra o código Python (corpo exato dos testes de integração); desvios 001, 008, 010, 024, 027, 030, 039 marcados como `implementado` neste escopo; avisar 006 (reutilizar `TransactionTypes.ToApi` e o sinal), 011 e 012 (usar `ITransactionPosting`, `IInvoiceService`; `Origin` null por padrão; filtrar `ativa = true` ao resolver conta) e 013 (registrar o `IInvoiceSchedulePopulator` real, usar `InvoiceCycle`); nota no `cutover-runbook.md` sobre a `0050`. Spec atualizada se a implementação revelou algo (parar e pedir aprovação, conforme `specs/README.md`). Cobertura: Domain ≥ 90%, Application ≥ 80%. PR aberto com CI verde.

Definição de pronto: `constitution.md` §10.
