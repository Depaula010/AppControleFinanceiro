# Tasks 003 — Categorias

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro. Ordem importa. Depende da 001a/001b em `main` e da spec aprovada (decisões D-1 e D-2). Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`.

- [ ] **T1 — Domain.** Testes vermelhos de `CategoryTypeFilterParser` (AC-003-05, 31) e `CategoryTree` (AC-003-06, 08, 32) ⇒ implementar `CategoryTypeFilter`, `CategoryRow`, `MacroCategory`, `SubCategory`, `CategoryTree`, `SpecialCategories`.
- [ ] **T2 — Caso de uso.** `ICategoryRepository` (e `ICategoryLookup` se D-2 aprovada) em `Application/Categories/CategoryContracts.cs`; testes de `ListCategories` com NSubstitute ⇒ implementação.
- [ ] **T3 — Migration `0020` + testes de seed.** Escrever `CategorySeedMigrationTests` (AC-003-40 a 45) com o auxiliar `CategorySeed.ReapplyAsync`; criar `dotnet/migrations/0020_categorias_globais.sql` (plan §5). Provar que o `DO $$` roda no `MigrationRunner`, a reaplicação é neutra, o estado de produção simulado (61 subs, duplicata) não recebe inserções e a sequence não anda.
- [ ] **T4 — `CategoryRepository` (Dapper).** Testes de integração de isolamento e ordem primeiro (AC-003-03, 04, 07, 08, 10 a 15, 32) ⇒ `ListVisibleAsync` com o SQL final (plan §3); filtro por `switch` no enum.
- [ ] **T5 — Lookups por nome (D-2).** `CategoryLookupTests` (AC-003-50 a 53) ⇒ três métodos com `ORDER BY s.id LIMIT 1`. Pular se o Rafael recusar D-2 (a 004 faz o seu).
- [ ] **T6 — Endpoints.** `CategoriesModule`, `CategoriesMessages`, `CategoriesLog` (EventId 3001), registro do repositório no `AddServices`; testes do endpoint (AC-003-02, 09, 20, 21, 30) e de integração ponta a ponta (AC-003-01, 05, 06, 31) com corpo comparado como texto exato.
- [ ] **T7 — Verificações finais.** Rodar `dotnet build` (warnings como erro) e `dotnet test dotnet/MeuSecretario.sln`; conferir os testes de arquitetura; conferir que nenhum log contém `userId` de outro usuário nem a exceção inteira.
- [ ] **T8 — Fechamento.** Rodar a conferência do plan §6 em produção (Rafael) e anotar o resultado em "Perguntas em aberto"; nota no `cutover-runbook.md` sobre a migration `0020` (no-op em produção); adendo ao ADR 0001 (dados de referência); marcar desvios 030 (parte da 003) e, se aprovados, P-003-1 e P-003-2 como arquivos de desvio; avisar a feature 009 que `/admin/populate-global-categories` não pode executar `DELETE`; spec ajustada se a implementação revelar algo; PR aberto.

Definição de pronto: `constitution.md` §10.
