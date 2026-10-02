# Plan 003 — Categorias

- **Spec:** `spec.md` (em revisão). Decisões D-1 (migration `0020`) e D-2 (lookups por nome) dependem de aprovação.

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
- (D-2) `ICategoryLookup { Task<int?> FindShortTermInvestmentAsync(int userId, ct); Task<int?> FindLoanPrincipalAsync(int userId, ct); Task<int?> FindFallbackAsync(bool income, ct); }`

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
`/*GRUPO*/` vira: `AND g.nome_grupo = 'Renda'` (Receita), `AND g.nome_grupo != 'Renda'` (Despesa) ou vazio (None). Sem `COLLATE` (pergunta 1 da spec). `NULLS LAST` é o padrão do `ASC`.

Lookups (D-2), todos com `ORDER BY s.id LIMIT 1` (desvio 030):
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

## 5. Migration `0020` e testes de seed

Arquivo `dotnet/migrations/0020_categorias_globais.sql` (o `.csproj` já embute `migrations/*.sql`; o `MigrationRunner` aplica em transação e registra). Conteúdo proposto (dados idênticos a `setup_service.py:80-104`, ordem de inserção fixa por número de linha):

```sql
-- 0020: categorias globais (grupos, macros, subcategorias). Substitui o populate-global-categories do Flask,
-- que apagava tudo antes de inserir. Idempotente: grupos só se faltarem; macros e subcategorias globais
-- só se NÃO existir nenhuma macro nem subcategoria global (em produção não insere nada).
-- Nunca apaga nem altera linhas existentes e nunca toca linhas de usuário (usuario_id não nulo).
DO $$
DECLARE
    precisa_seed boolean;
BEGIN
    INSERT INTO grupocategoria (nome_grupo)
    SELECT v.nome
    FROM (VALUES (1, 'Renda'), (2, 'Despesa Essencial'), (3, 'Meta Financeira'),
                 (4, 'Despesa Discricionária'), (5, 'Geral')) AS v(n, nome)
    WHERE NOT EXISTS (SELECT 1 FROM grupocategoria g WHERE g.nome_grupo = v.nome)
    ORDER BY v.n;

    SELECT NOT EXISTS (SELECT 1 FROM macrocategoria WHERE usuario_id IS NULL)
       AND NOT EXISTS (SELECT 1 FROM subcategoria WHERE usuario_id IS NULL)
      INTO precisa_seed;

    IF precisa_seed THEN
        INSERT INTO macrocategoria (grupo_id, nome_macro, ordem_macro)
        SELECT g.id, v.macro, v.ordem
        FROM (VALUES
            (1, 'Renda', 'Renda Principal', 1),
            (2, 'Renda', 'Renda Extra e Passiva', 2),
            (3, 'Renda', 'Investimentos e Reembolsos', 3),
            (4, 'Renda', 'Outras Entradas', 4),
            (5, 'Despesa Essencial', 'Moradia e Contas', 1),
            (6, 'Despesa Essencial', 'Alimentação Essencial', 2),
            (7, 'Despesa Essencial', 'Saúde e Bem-Estar Básico', 3),
            (8, 'Despesa Essencial', 'Educação Essencial', 4),
            (9, 'Despesa Essencial', 'Transporte Essencial', 5),
            (10, 'Meta Financeira', 'Construção de Riqueza', 1),
            (11, 'Meta Financeira', 'Gerenciamento de Dívidas', 2),
            (12, 'Meta Financeira', 'Educação de Alto Custo', 3),
            (13, 'Despesa Discricionária', 'Lazer e Entretenimento', 1),
            (14, 'Despesa Discricionária', 'Assinaturas e Serviços', 2),
            (15, 'Despesa Discricionária', 'Cuidados Pessoais', 3),
            (16, 'Despesa Discricionária', 'Compras Pessoais', 4),
            (17, 'Geral', 'Despesas Gerais', NULL),
            (18, 'Geral', 'Receitas Gerais', NULL)
        ) AS v(n, grupo, macro, ordem)
        JOIN grupocategoria g ON g.nome_grupo = v.grupo
        ORDER BY v.n;

        INSERT INTO subcategoria (macro_id, nome_sub)
        SELECT m.id, v.sub
        FROM (VALUES
            (1, 'Renda Principal', 'Salário Fixo / Pró-labore'),
            (2, 'Renda Principal', 'Remuneração Variável / Comissões'),
            (3, 'Renda Extra e Passiva', 'Aluguéis Recebidos'),
            (4, 'Renda Extra e Passiva', 'Freelances / Projetos Avulsos'),
            (5, 'Investimentos e Reembolsos', 'Dividendos / Juros (Rendimentos)'),
            (6, 'Investimentos e Reembolsos', 'Restituições / Reembolsos'),
            (7, 'Outras Entradas', 'Presentes em Dinheiro / Doações'),
            (8, 'Outras Entradas', 'Venda de Bens (Pontual)'),
            (9, 'Moradia e Contas', 'Aluguel / Financiamento'),
            (10, 'Moradia e Contas', 'Condomínio / IPTU / Taxas'),
            (11, 'Moradia e Contas', 'Eletricidade / Água / Gás'),
            (12, 'Moradia e Contas', 'Internet / Telefone Fixo'),
            (13, 'Moradia e Contas', 'Manutenção da Casa'),
            (14, 'Alimentação Essencial', 'Supermercado / Mercearia'),
            (15, 'Alimentação Essencial', 'Feira / Hortifrúti'),
            (16, 'Alimentação Essencial', 'Suplementos e Itens Básicos'),
            (17, 'Saúde e Bem-Estar Básico', 'Plano de Saúde / Convênio'),
            (18, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo'),
            (19, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)'),
            (20, 'Educação Essencial', 'Mensalidades Escolares / Faculdade'),
            (21, 'Educação Essencial', 'Material Didático e Livros'),
            (22, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)'),
            (23, 'Transporte Essencial', 'Transporte Público (Bilhetes/Passe)'),
            (24, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)'),
            (25, 'Transporte Essencial', 'IPVA / Seguro do Veículo'),
            (26, 'Construção de Riqueza', 'Aporte à Reserva de Emergência'),
            (27, 'Construção de Riqueza', 'Investimentos para Aposentadoria'),
            (28, 'Construção de Riqueza', 'Investimentos de Curto Prazo'),
            (29, 'Gerenciamento de Dívidas', 'Quitação de Empréstimos (Principal)'),
            (30, 'Gerenciamento de Dívidas', 'Juros e Multas (Dívidas)'),
            (31, 'Educação de Alto Custo', 'Pós-graduação, Intercâmbio'),
            (32, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery'),
            (33, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais'),
            (34, 'Lazer e Entretenimento', 'Viagens e Hospedagem'),
            (35, 'Assinaturas e Serviços', 'Streaming (Netflix, Spotify, etc.)'),
            (36, 'Assinaturas e Serviços', 'Softwares / Apps Pagos'),
            (37, 'Assinaturas e Serviços', 'Academia / Clube'),
            (38, 'Cuidados Pessoais', 'Salão de Beleza / Barbearia'),
            (39, 'Cuidados Pessoais', 'Cosméticos e Higiene (não básica)'),
            (40, 'Compras Pessoais', 'Roupas, Calçados e Acessórios'),
            (41, 'Compras Pessoais', 'Eletrônicos e Gadgets'),
            (42, 'Compras Pessoais', 'Presentes (Aniversários, Datas)')
        ) AS v(n, macro, sub)
        JOIN macrocategoria m ON m.nome_macro = v.macro AND m.usuario_id IS NULL
        ORDER BY v.n;

        INSERT INTO subcategoria (macro_id, nome_sub)
        SELECT m.id, 'Outros'
        FROM macrocategoria m
        WHERE m.usuario_id IS NULL
        ORDER BY m.id;
    END IF;
END
$$;
```

Riscos e verificações: (a) o `DO $$` precisa passar pelo `NpgsqlCommand` do runner (o baseline já roda vários comandos num texto só; o teste T3 prova o `DO`); (b) `ordem_macro` com `NULL` dentro de `VALUES` resolve para inteiro porque a coluna tem inteiros; (c) a ordem dos `INSERT ... ORDER BY` fixa os ids 1 a 18 e 1 a 60 num banco vazio (AC-003-40). Exige adendo ao ADR 0001 (migration com dados de referência, sempre idempotente e protegida por guarda).

**Teste com o seed apagado pelo reset.** `PostgresFixture.ResetAsync` (`TRUNCATE usuarios RESTART IDENTITY CASCADE`) esvazia `macrocategoria` e `subcategoria`. Os testes da 003 usam o auxiliar `CategorySeed.ReapplyAsync(PostgresFixture)` (em `tests/MeuSecretario.IntegrationTests/Categories/`) que lê o recurso embutido `migrations/0020_categorias_globais.sql` e o executa; chamado em `InitializeAsync` depois do `ResetAsync`. Não edita arquivo compartilhado; promover ao `ResetAsync` fica para um PR de apoio.

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
| Unitário | `Api/Categories/CategoriesEndpointTests` (host mínimo com fake de repositório e `RequireUser`) | AC-003-20, 21, 30 (500 sem texto da exceção), `data: []` (AC-003-09), mesmo corpo nos dois caminhos (AC-003-02) |
| Integração (Docker) | `Categories/CategoriesEndpointsTests` (`PostgresFixture`, token via `JwtTokenService.Issue`, usuários e categorias próprias inseridos por SQL) | AC-003-01 a 08, 10 a 15, 31, 32; corpo comparado como texto exato |
| Integração | `Categories/CategorySeedMigrationTests` | AC-003-40 a 45 (banco vazio, reaplicação, estado de produção simulado com 61 subs e duplicata, nomes exigidos, sem linhas de usuário) |
| Integração | `Categories/CategoryLookupTests` (D-2) | AC-003-50 a 53 |
| Arquitetura | já existentes | Domain/Application sem Npgsql/Dapper; Api sem Dapper |

Todos de integração com `[Collection(PostgresTestGroup.Name)]` e `[Trait("Category","Docker")]`; nomes de teste citam o AC (`[Trait("AC","003-NN")]`). Relógio não é usado nesta feature.

## 8. Riscos

- **Reset apaga o seed**: mitigado por `CategorySeed.ReapplyAsync`; risco para as outras features até o PR de apoio.
- **Collation**: ordem de nomes depende do banco (pergunta 1); testes assumem a Alpine.
- **Guarda tudo-ou-nada**: não completa banco parcialmente populado; escolha consciente para nunca duplicar em produção.
- **Ids diferem entre ambientes** (produção tem os seus): nenhum cliente depende de valor fixo; testes de lookup e seed usam os ids do banco novo.
- **`DO $$` no runner**: validado por T3.
