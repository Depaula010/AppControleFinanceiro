# Plan 003 — Categorias

- **Spec:** `spec.md` (aprovada em 2026-10-02). G-2 (migration `0020` e reset do fixture) já está no commit base; G-3 (lookups por nome) é entregue aqui.

## 1. Domain (`MeuSecretario.Domain/Categories`)

Funções puras, sem I/O, com teste unitário.

| Tipo | Assinatura | Papel |
|---|---|---|
| `CategoryTypeFilter` | `enum { None, Receita, Despesa }` | filtro do contrato (desvio 001: o nome HTTP é `Receita`) |
| `CategoryTypeFilterParser` | `static CategoryTypeFilter Parse(string? raw)` | `"Receita"`/`"Despesa"` exatos (ordinal, sem `trim`); qualquer outro valor ou nulo vira `None` (RN-003-03) |
| `CategoryRow` | `record(string Grupo, int MacroId, string MacroCategoria, int SubcategoriaId, string SubcategoriaNome)` | linha da consulta |
| `MacroCategory`, `SubCategory` | `record(Grupo, MacroId, MacroCategoria, Subcategorias)`; `record(Id, Nome)` | saída |
| `CategoryTree` | `static IReadOnlyList<MacroCategory> Build(IEnumerable<CategoryRow>)` | agrupa por `(Grupo, MacroId, MacroCategoria)` na ordem da primeira aparição (RN-003-09) |
| `SpecialCategories` | constantes: `ShortTermInvestment = "Investimentos de Curto Prazo"`, `LoanPrincipal = "Quitação de Empréstimos (Principal)"`, `Fallback = "Outros"`, `MetaGroup = "Meta Financeira"`, `ExpenseFallbackMacro = "Despesas Gerais"`, `IncomeFallbackMacro = "Receitas Gerais"` | nomes exigidos (§4.3) |

## 2. Application (`MeuSecretario.Application/Categories`)

**Portas** (no mesmo arquivo `CategoryContracts.cs`):
- `ICategoryRepository { Task<IReadOnlyList<CategoryRow>> ListVisibleAsync(int userId, CategoryTypeFilter filter, CancellationToken ct); }`
- (G-3) `ICategoryLookup { Task<int?> FindShortTermInvestmentAsync(int userId, ct); Task<int?> FindLoanPrincipalAsync(int userId, ct); Task<int?> FindFallbackAsync(bool income, ct); }`

**Caso de uso** `ListCategories` (`ExecuteAsync(int userId, string? rawTipo, ct)`): `Parse` do filtro, `ListVisibleAsync`, `CategoryTree.Build`; devolve `IReadOnlyList<MacroCategory>`. Sem HTTP. Exceções sobem para o endpoint.

Os lookups não têm caso de uso: são portas consumidas pelas features 004 e 011.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Categories`)

`CategoryRepository : ICategoryRepository, ICategoryLookup` (**Dapper**, `IDbConnectionFactory`, como o `UserRepository`; sem `AppDbContext`). Registro como singleton em `Infrastructure/DependencyInjection.cs` seria edição de arquivo compartilhado; **alternativa sem editar**: o `CategoriesModule` (Api) registra o repositório no `AddServices` (`services.AddSingleton<ICategoryRepository, CategoryRepository>()`), o que é permitido porque a Api referencia Infrastructure.

### SQL final

Listagem (um texto constante por filtro; o fragmento de grupo é escolhido por `switch` no enum, nunca por concatenação de texto do usuário):

```sql
SELECT g.nome_grupo AS Grupo, m.id AS MacroId, m.nome_macro AS MacroCategoria,
       s.id AS SubcategoriaId, s.nome_sub AS SubcategoriaNome
FROM subcategoria s
JOIN macrocategoria m ON s.macro_id = m.id
JOIN grupocategoria g ON m.grupo_id = g.id
WHERE (s.usuario_id IS NULL OR s.usuario_id = @userId)
  AND (m.usuario_id IS NULL OR m.usuario_id = @userId)
  /*GRUPO*/
ORDER BY g.nome_grupo, m.ordem_macro, m.nome_macro, s.nome_sub, s.id
```
`/*GRUPO*/` vira (desvio 040): `AND (g.nome_grupo = 'Renda' OR (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais'))` (Receita), `AND g.nome_grupo != 'Renda' AND NOT (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais')` (Despesa) ou vazio (None). Sem `COLLATE` (pergunta 1 da spec). `NULLS LAST` é o padrão do `ASC`.

Lookups (G-3), todos com `ORDER BY s.id LIMIT 1` (desvio 030):
```sql
-- investimento de curto prazo e quitação (troca só o nome em @nome)
SELECT s.id FROM subcategoria s
JOIN macrocategoria m ON s.macro_id = m.id
JOIN grupocategoria g ON m.grupo_id = g.id
WHERE g.nome_grupo = 'Meta Financeira' AND s.nome_sub = @nome
  AND (s.usuario_id IS NULL OR s.usuario_id = @userId)
ORDER BY s.id LIMIT 1;

-- fallback Outros (@macro = 'Receitas Gerais' para renda, senão 'Despesas Gerais')
SELECT s.id FROM subcategoria s
JOIN macrocategoria m ON s.macro_id = m.id
WHERE m.nome_macro = @macro AND s.nome_sub = 'Outros' AND s.usuario_id IS NULL
ORDER BY s.id LIMIT 1;
```

## 4. Api (`MeuSecretario.Api/Categories/`)

- `CategoriesModule : IFeatureModule` (não edita `Program.cs`). `MapEndpoints`: um handler único mapeado em `/api/categories` e `/api/categorias`, ambos com `.RequireUser()`.
- Handler: `ListCategories.ExecuteAsync(currentUser.UserId, request.Query["tipo"].FirstOrDefault(), ct)`; sucesso `Envelopes.A.Success(data)` com a lista mapeada para dicionários/DTOs na ordem `grupo, macro_id, macro_categoria, subcategorias[{id, nome}]` (`JsonDefaults`, sem camelCase). Lista vazia deve sair como `"data":[]` (a lista vazia não é nula, então `Success` inclui a chave; coberto por teste).
- `catch (Exception ex) when (ex is not OperationCanceledException)`: `CategoriesLog.ListFailed(logger, SafeError.Describe(ex))` e `Envelopes.A.Error(CategoriesMessages.LoadFailed, 500)`.
- `CategoriesMessages.LoadFailed = "Erro ao carregar categorias"`.
- `CategoriesLog` (`[LoggerMessage]`, EventId `3001`, como `AuthLog`): `"Erro ao listar categorias (usuário {UserId}): {Error}"`; nunca a exceção inteira.
- Barra final: já coberta pelo `StrictRoutingMiddleware`.

## 5. Migration `0020` e reset dos testes (já entregues, G-2/G-4)

`dotnet/migrations/0020_categorias_globais.sql` e a reaplicação do seed em `PostgresFixture.ResetAsync` já estão no commit base, com testes em `tests/MeuSecretario.IntegrationTests/Database/ReferenceDataMigrationTests.cs`. Esta feature não os recria nem os edita. Lacunas de AC (registro em `dotnet_migrations`, sequences inalteradas, sem linha de usuário criada) são cobertas em `Categories/CategorySeedGapsTests`.

## 6. Conferência em produção (antes da virada, somente leitura)

```sql
SELECT (SELECT count(*) FROM grupocategoria) AS grupos,
       (SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL) AS macros_globais,
       (SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL) AS subs_globais,
       (SELECT count(*) FROM macrocategoria WHERE usuario_id IS NOT NULL) AS macros_de_usuario,
       (SELECT count(*) FROM subcategoria WHERE usuario_id IS NOT NULL) AS subs_de_usuario;
SELECT macro_id, nome_sub, count(*) FROM subcategoria WHERE usuario_id IS NULL
GROUP BY macro_id, nome_sub HAVING count(*) > 1;
SELECT id, nome_macro, ordem_macro FROM macrocategoria WHERE usuario_id IS NULL ORDER BY id;
SELECT datname, datcollate, datctype FROM pg_database WHERE datname = current_database();
```
Esperado: 5, 18, 61, 0, 0; um duplicado (`macro_id = 17`, `Outros`, 2); macros 1 a 18 como na spec §4.2. Qualquer diferença: parar e rever a guarda antes de aplicar `0020` (a nota entra no `cutover-runbook.md`).

## 7. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário | `Domain/Categories/CategoryTypeFilterParserTests` | AC-003-05, 31 (valores exatos, vazio, `" Receita"`, hostil, nulo) |
| Unitário | `Domain/Categories/CategoryTreeTests` | AC-003-06 (formato), 08, 32 (ordem de primeira aparição, macros intercaladas, subs na ordem recebida) |
| Unitário | `Application/Categories/ListCategoriesTests` (NSubstitute) | repositório recebe `userId` e filtro certos; resultado agrupado; exceção sobe |
| Integração sem Docker | `IntegrationTests/Categories/CategoriesEndpointTests` (WebApplicationFactory com fake de repositório; `TestHost` só existe no projeto de integração) | AC-003-20, 21, 30 (500 sem texto da exceção), `data: []` (AC-003-09), mesmo corpo nos dois caminhos (AC-003-02) |
| Integração (Docker) | `Categories/CategoriesEndpointsTests` (`PostgresFixture`, token via `JwtTokenService.Issue`, usuários e categorias próprias inseridos por SQL) | AC-003-01 a 08, 10 a 15, 31, 32; corpo comparado como texto exato |
| Integração | `Database/ReferenceDataMigrationTests` (existente) + `Categories/CategorySeedGapsTests` | AC-003-40 a 45 |
| Integração | `Categories/CategoryLookupTests` (G-3) | AC-003-50 a 53 |
| Arquitetura | já existentes | Domain/Application sem Npgsql/Dapper; Api sem Dapper |

Todos de integração com `[Collection(PostgresTestGroup.Name)]` e `[Trait("Category","Docker")]`; nomes de teste citam o AC (`[Trait("AC","003-NN")]`). Relógio não é usado nesta feature.

## 8. Riscos

- **Collation**: ordem de nomes depende do banco (pergunta 1); testes assumem a Alpine.
- **Guarda tudo-ou-nada**: não completa banco parcialmente populado; escolha consciente para nunca duplicar em produção.
- **Ids diferem entre ambientes** (produção tem os seus): nenhum cliente depende de valor fixo; testes de lookup e seed usam os ids do banco novo.
- **`DO $$` no runner**: validado por T3.
