# Tasks 003 — Categorias

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro. Ordem importa. Spec aprovada em 2026-10-02. Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`.

- [x] **T0 — Spec aprovada.** Decisões aplicadas (desvio 040 aprovado, 050 rejeitado, G-2 feito, G-3 aprovado).
- [ ] **T1 — Domain.** Testes vermelhos de `CategoryTypeFilterParser` (AC-003-05, 31) e `CategoryTree` (AC-003-06, 08, 32) e implementação de `CategoryTypeFilter`, `CategoryRow`, `MacroCategory`, `SubCategory`, `CategoryTree`, `SpecialCategories`.
- [ ] **T2 — Caso de uso.** `ICategoryRepository` e `ICategoryLookup` em `Application/Categories/CategoryContracts.cs`; testes de `ListCategories` com NSubstitute e implementação.
- [x] **T3 — Migration `0020` e testes de seed.** Já entregue no commit base (G-2). Falta só `Categories/CategorySeedGapsTests` com as lacunas de AC-003-41/44/45.
- [ ] **T4 — `CategoryRepository` (Dapper).** Testes de integração de isolamento e ordem primeiro (AC-003-03, 04, 07, 08, 10 a 15, 32) e `ListVisibleAsync` com o SQL final (plan §3); filtro por `switch` no enum.
- [ ] **T5 — Lookups por nome (G-3).** `CategoryLookupTests` (AC-003-50 a 53) e os três métodos com `ORDER BY s.id LIMIT 1`.
- [ ] **T6 — Endpoints.** `CategoriesModule`, `CategoriesMessages`, `CategoriesLog` (EventId 3001), registro do repositório no `AddServices`; testes do endpoint (AC-003-02, 09, 20, 21, 30) e de integração ponta a ponta (AC-003-01, 05, 06, 31) com corpo comparado como texto exato.
- [ ] **T7 — Verificações finais.** `dotnet build` (warnings como erro) e `dotnet test dotnet/MeuSecretario.slnx`; testes de arquitetura; nenhum log contém `userId` de outro usuário nem a exceção inteira.
- [ ] **T8 — Fechamento (Rafael).** Conferência do plan §6 em produção (já confirmada pelo script 05); nota no `cutover-runbook.md` sobre a `0020` (no-op em produção); avisar a 009 que `/admin/populate-global-categories` não pode executar `DELETE`; PR aberto.

Definição de pronto: `constitution.md` §10.
