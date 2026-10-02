# Spec 003 — Categorias (listagem e seed das categorias globais)

- **Estado:** em revisão
- **Onda:** 1
- **Origem (Python):** `app/routes/api.py:1702-1788` (`_get_categories_impl` e as duas rotas); `app/services/finance/category_service.py:13-69`; `app/services/queries/category_queries.py:25-107`; seed em `app/services/finance/setup_service.py:72-114` (cópia idêntica em `app/services/finance_service.py:135-167`); consumo no Angular em `meusecretariofront/src/app/features/finances/`
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes.A`), 001b (`MigrationRunner`, baseline `0000`)
- **Desvios aplicáveis:** 001 (aprovado), 008 (aprovado), 024 (aprovado), 030 (aprovado), 002 (aprovado, só no seed)

## 1. Objetivo

O usuário autenticado lista as categorias que pode usar ao lançar transações e contas mensais (3 níveis: grupo, macro, subcategoria), globais mais as próprias, opcionalmente filtradas por `Receita` ou `Despesa`. A feature também define como as categorias globais passam a existir num banco novo (homol, testes, ambiente local) sem duplicar nada em produção.

## 2. Rotas

| Método | Caminho | Auth | Envelope |
|---|---|---|---|
| GET | `/api/categories` | JWT (`RequireUser`) | A |
| GET | `/api/categorias` | JWT (`RequireUser`) | A |

As duas rotas chamam a mesma implementação (`api.py:1776`, `1788`). Não existe criação, edição nem exclusão de categoria por API (RN-003-14).

## 3. Contrato

### 3.1 Entrada

Sem corpo. Query opcional `tipo`. Só `Receita` e `Despesa` (exatos, com maiúscula inicial, sem `trim`) filtram. Qualquer outro valor (`receita`, `Renda`, `""`, `" Receita"`, texto qualquer) é **ignorado** e a listagem sai sem filtro. Com `tipo` repetido vale o primeiro valor (`request.args.get`). *Origem: `api.py:1708-1715`.*

### 3.2 Mapeamento `tipo` para grupos

| `tipo` | Filtro de grupo | Observação |
|---|---|---|
| `Receita` | `g.nome_grupo = 'Renda'` | O contrato HTTP usa `Receita` e o banco usa `Renda` (desvio 001); aqui só o literal do filtro muda, nada é devolvido convertido. |
| `Despesa` | `g.nome_grupo != 'Renda'` | Inclui `Despesa Essencial`, `Despesa Discricionária`, `Meta Financeira` e **todo o grupo `Geral`**, inclusive a macro `Receitas Gerais` (ver P-003-1). |
| ausente ou outro | nenhum | Devolve também `Renda`. |

### 3.3 Sucesso

**200**, envelope A com `data` (sem `message`):

```json
{"status":"success","data":[
  {"grupo":"Despesa Discricionária","macro_id":13,"macro_categoria":"Lazer e Entretenimento",
   "subcategorias":[{"id":55,"nome":"Outros"},{"id":33,"nome":"Passeios e Eventos Culturais"}]}
]}
```

- Item de `data`: `grupo` (texto), `macro_id` (inteiro), `macro_categoria` (texto), `subcategorias` (lista de `{id` inteiro, `nome` texto`}`), nessa ordem. Sem campos extras.
- Nenhuma categoria visível: **200** `{"status":"success","data":[]}` (a chave `data` aparece mesmo vazia).
- O Flask serializa as chaves em ordem alfabética e com `\uXXXX`; o .NET usa a ordem acima e UTF-8 direto. Equivalente para qualquer cliente JSON.
- Sem paginação, sem limite, sem cache.

### 3.4 Erros

| Condição | Resposta |
|---|---|
| Header `Authorization` ausente, mal formado, token inválido/expirado, sem `user_id` | 401, as 4 mensagens do `RequireUser` (spec 001 §4.4) |
| Falha ao consultar o banco ou qualquer exceção | **500** `{"status":"error","message":"Erro ao carregar categorias"}` (mensagem fixa; detalhe só no log, desvio 008) |
| Caminho com barra final (`/api/categories/`) | 404 HTML do Flask (`StrictRoutingMiddleware`) |

O 503 `"Banco de dados não configurado"` (`api.py:1704-1705`) **não é reproduzido**: no .NET a falta de `DATABASE_URL` derruba o boot (falha rápida, `AppSettings`). Banco fora do ar em tempo de execução dá o 500 acima, igual ao Flask.

`tipo` nunca causa 500 (desvio 024): é lido como texto, comparado por igualdade e jamais concatenado em SQL.

### 3.5 Consulta e ordenação

Visibilidade, junção e ordem (RN-003-07 a 09): `subcategoria` JOIN `macrocategoria` JOIN `grupocategoria`; linhas em que `s.usuario_id` e `m.usuario_id` são nulos (global) ou iguais ao `user_id` do token; ordem `g.nome_grupo, m.ordem_macro, m.nome_macro, s.nome_sub` (mais `s.id` como último desempate). Ordem resultante com o seed: grupos `Despesa Discricionária`, `Despesa Essencial`, `Geral`, `Meta Financeira`, `Renda`; macros por `ordem_macro` (NULL por último, como o `ASC` do PostgreSQL); subcategorias por nome. A comparação de texto usa a collation do banco, sem `COLLATE` explícito (ver pergunta 1).

## 4. Seed das categorias globais

### 4.1 Situação

- Não há CRUD de categoria no Flask. As globais nascem da rota admin `GET /admin/populate-global-categories`, que executa `DELETE FROM SubCategoria/MacroCategoria WHERE usuario_id IS NULL; DELETE FROM GrupoCategoria;` e reinsere tudo (`setup_service.py:79-104`). É destrutiva (falha por FK `RESTRICT` quando há transações) e, pelo desvio 002, passa a ser no-op na feature 009. **A feature 009 nunca pode executar esse DELETE.**
- `0000_baseline.sql` não traz dados. Um banco novo fica sem categorias, e transferência, pagamento de fatura e o bot falham ("Subcategoria ... não encontrada").
- Produção já tem tudo: 5 grupos, 18 macros, 61 subcategorias (60 do seed mais o `Outros` duplicado do desvio 030). Os ids do seed (macros 1 a 18 na ordem abaixo) explicam o `macro_id = 17` do desvio 030: é `Despesas Gerais`. Isso é inferência dos números, confirmar (pergunta 2).

### 4.2 Conteúdo do seed (idêntico ao Python)

Grupos, na ordem de inserção: `Renda`, `Despesa Essencial`, `Meta Financeira`, `Despesa Discricionária`, `Geral`. Toda macro recebe uma subcategoria `Outros` (inserida depois das demais, ids 43 a 60). Todas com `usuario_id` nulo.

| Macro id | Grupo | Macro (`ordem_macro`) | Subcategorias (id) |
|---|---|---|---|
| 1 | Renda | Renda Principal (`1`) | Salário Fixo / Pró-labore (1); Remuneração Variável / Comissões (2); Outros (43) |
| 2 | Renda | Renda Extra e Passiva (`2`) | Aluguéis Recebidos (3); Freelances / Projetos Avulsos (4); Outros (44) |
| 3 | Renda | Investimentos e Reembolsos (`3`) | Dividendos / Juros (Rendimentos) (5); Restituições / Reembolsos (6); Outros (45) |
| 4 | Renda | Outras Entradas (`4`) | Presentes em Dinheiro / Doações (7); Venda de Bens (Pontual) (8); Outros (46) |
| 5 | Despesa Essencial | Moradia e Contas (`1`) | Aluguel / Financiamento (9); Condomínio / IPTU / Taxas (10); Eletricidade / Água / Gás (11); Internet / Telefone Fixo (12); Manutenção da Casa (13); Outros (47) |
| 6 | Despesa Essencial | Alimentação Essencial (`2`) | Supermercado / Mercearia (14); Feira / Hortifrúti (15); Suplementos e Itens Básicos (16); Outros (48) |
| 7 | Despesa Essencial | Saúde e Bem-Estar Básico (`3`) | Plano de Saúde / Convênio (17); Medicamentos de Uso Contínuo (18); Consultas e Exames (não cobertos) (19); Outros (49) |
| 8 | Despesa Essencial | Educação Essencial (`4`) | Mensalidades Escolares / Faculdade (20); Material Didático e Livros (21); Outros (50) |
| 9 | Despesa Essencial | Transporte Essencial (`5`) | Combustível / Recarga (Carro/Moto) (22); Transporte Público (Bilhetes/Passe) (23); Aplicativos de Transporte (Necessidade) (24); IPVA / Seguro do Veículo (25); Outros (51) |
| 10 | Meta Financeira | Construção de Riqueza (`1`) | Aporte à Reserva de Emergência (26); Investimentos para Aposentadoria (27); Investimentos de Curto Prazo (28); Outros (52) |
| 11 | Meta Financeira | Gerenciamento de Dívidas (`2`) | Quitação de Empréstimos (Principal) (29); Juros e Multas (Dívidas) (30); Outros (53) |
| 12 | Meta Financeira | Educação de Alto Custo (`3`) | Pós-graduação, Intercâmbio (31); Outros (54) |
| 13 | Despesa Discricionária | Lazer e Entretenimento (`1`) | Restaurantes, Bares e Delivery (32); Passeios e Eventos Culturais (33); Viagens e Hospedagem (34); Outros (55) |
| 14 | Despesa Discricionária | Assinaturas e Serviços (`2`) | Streaming (Netflix, Spotify, etc.) (35); Softwares / Apps Pagos (36); Academia / Clube (37); Outros (56) |
| 15 | Despesa Discricionária | Cuidados Pessoais (`3`) | Salão de Beleza / Barbearia (38); Cosméticos e Higiene (não básica) (39); Outros (57) |
| 16 | Despesa Discricionária | Compras Pessoais (`4`) | Roupas, Calçados e Acessórios (40); Eletrônicos e Gadgets (41); Presentes (Aniversários, Datas) (42); Outros (58) |
| 17 | Geral | Despesas Gerais (`NULL`) | Outros (59) |
| 18 | Geral | Receitas Gerais (`NULL`) | Outros (60) |

Total: 5 grupos, 18 macros, 60 subcategorias. Contagem por filtro: `Receita` = 4 macros e 12 subs; `Despesa` = 14 macros e 48 subs; sem filtro = 18 e 60.

### 4.3 Nomes exigidos por outras features (o seed precisa conter)

| Nome | Onde | Quem usa |
|---|---|---|
| `Investimentos de Curto Prazo` | grupo `Meta Financeira`, macro `Construção de Riqueza` | transferência (`transaction_service.py:97-108`), feature 004 |
| `Quitação de Empréstimos (Principal)` | grupo `Meta Financeira`, macro `Gerenciamento de Dívidas` | pagamento de fatura (`transaction_service.py:190-205`), feature 004 |
| `Outros` | macro `Despesas Gerais` (fallback de despesa) e macro `Receitas Gerais` (fallback de renda), globais | `category_service.py:49-69`, bot e Gemini (features 010, 011, 012) |

### 4.4 Estratégia: migration de dados x fixture de teste

| Critério | Migration `0020` (idempotente, com guarda) | Fixture só nos testes |
|---|---|---|
| Banco novo (homol, local, recuperação) | Fica pronto sozinho | Continua vazio fora dos testes |
| Produção | Roda e não insere nada (guarda); risco baixo, verificável antes da virada | Nenhum |
| Ids iguais entre ambientes | Sim, no banco novo (ordem fixa) | Depende do teste |
| Uma só definição do seed (constituição §5) | Sim, o mesmo arquivo serve a testes e ambientes | Duas cópias (fixture e eventual script de homol) |
| Custo | Pede adendo ao ADR 0001 (migration com dados de referência) | Nenhum |

**Recomendação: migration `dotnet/migrations/0020_categorias_globais.sql`.** Regras:
1. Grupos: inserir só os que faltam por nome (`WHERE NOT EXISTS`, sem `ON CONFLICT` para não gastar a sequence).
2. Macros e subcategorias globais: inserir **somente se não existir nenhuma macro global nem subcategoria global** (tudo ou nada). Escolhido em vez de "linha a linha por nome" porque, em produção, um nome editado à mão faria a migration criar uma segunda estrutura inteira; com a guarda o pior caso é não completar um banco parcialmente populado, o que se corrige à mão.
3. Nunca `DELETE`, nunca toca linhas com `usuario_id` não nulo, nunca apaga a duplicata de `Outros`.
4. Antes da virada, rodar em produção a consulta de conferência do plan.md §6 e comparar com §4.2.

Os testes de integração herdam o seed da migration. Atenção: `PostgresFixture.ResetAsync` faz `TRUNCATE usuarios ... CASCADE`, que também esvazia `macrocategoria` e `subcategoria` (FKs para `usuarios`) e reinicia as sequences; os testes desta feature reaplicam o script depois do reset (plan.md §5).

## 5. Regras de negócio

- **RN-003-01** As duas rotas exigem JWT e usam o `user_id` do token; nenhum `usuario_id` vem do cliente. Não se verifica se o usuário existe nem `usuarios.ativo` (token de usuário inexistente recebe só as globais). *Origem: `api.py:1772-1773`, `1780-1781`, `34-90`.*
- **RN-003-02** `/api/categories` e `/api/categorias` respondem exatamente o mesmo. *Origem: `api.py:1776`, `1788`.*
- **RN-003-03** `tipo` filtra apenas com `Receita` ou `Despesa` exatos; qualquer outro valor equivale a ausente (primeiro valor se repetido). *Origem: `api.py:1708-1715`.*
- **RN-003-04** `Receita` mostra só o grupo `Renda`. *Origem: `api.py:1712-1713`; desvio 001.*
- **RN-003-05** `Despesa` mostra todo grupo diferente de `Renda` (hoje inclui `Receitas Gerais`; P-003-1). *Origem: `api.py:1714-1715`.*
- **RN-003-06** Uma subcategoria é visível se `s.usuario_id IS NULL OR = :uid` **e** sua macro é visível pelo mesmo critério (`m.usuario_id`). `grupocategoria` não tem dono. Categorias de outro usuário nunca aparecem. *Origem: `api.py:1728-1729`.*
- **RN-003-07** A junção é interna: macro sem nenhuma subcategoria visível não aparece. *Origem: `api.py:1725-1727`.*
- **RN-003-08** Ordem: `g.nome_grupo`, `m.ordem_macro` (NULL por último), `m.nome_macro`, `s.nome_sub`; desempate final por `s.id` (acréscimo: só decide linhas idênticas em todas as chaves, que o Flask deixava indefinidas, como as duas `Outros` do desvio 030). *Origem: `api.py:1731`.*
- **RN-003-09** Agrupamento por `(grupo, macro_id, macro_categoria)` na ordem da primeira aparição; as subcategorias de cada macro seguem a ordem da consulta. *Origem: `api.py:1736-1754`.*
- **RN-003-10** Resposta e erro como em §3.3 e §3.4; 500 com mensagem fixa e log sem detalhe interno (desvio 008). *Origem: `api.py:1756-1769`.*
- **RN-003-11** Somente leitura: categorias próprias de usuário só existem se inseridas direto no banco. Esta feature não cria rota de escrita. *Origem: ausência de INSERT/UPDATE/DELETE em `api.py` e `category_service.py`.*
- **RN-003-12** O seed contém exatamente §4.2 e os nomes de §4.3, é idempotente e não destrutivo (§4.4). *Origem: `setup_service.py:78-104`.*
- **RN-003-13** Toda busca de subcategoria por nome (transferência, pagamento de fatura, fallback `Outros`) ordena por `id` e pega a primeira (desvio 030). Filtros: `Meta Financeira` + `(s.usuario_id IS NULL OR = :uid)` para as duas de meta; macro `Receitas Gerais` (renda) ou `Despesas Gerais` (demais) com `s.usuario_id IS NULL` para o fallback. *Origem: `category_queries.py:25-107`, `transaction_service.py:97-108`, `190-205`, `category_service.py:62-69`.* Entregue como complemento (plan.md §2, decisão D-2).
- **RN-003-14** `category_service.get_user_categories` (bot/Gemini, `category_service.py:32-41`) tem filtro e ordem próprios (4 grupos em `IN`, sem checar `m.usuario_id`, sem `ORDER BY`). Equivale a `!= 'Renda'` enquanto só existirem os 5 grupos. Fora desta feature (010/012); anotado para não divergirem sem querer.

## 6. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","003-NN")]`). "Banco semeado" = migrations aplicadas e seed carregado.

**Listagem**
- **AC-003-01** Dado banco semeado e usuário sem categorias próprias, quando `GET /api/categories` sem `tipo`, então 200, `status` `success`, 18 itens em `data` e 60 subcategorias no total; grupos na ordem `Despesa Discricionária`, `Despesa Essencial`, `Geral`, `Meta Financeira`, `Renda`.
- **AC-003-02** Dado o mesmo cenário, quando `GET /api/categorias`, então corpo idêntico ao de `/api/categories` (texto exato).
- **AC-003-03** Dado `?tipo=Receita`, então só grupo `Renda`: 4 macros (`Renda Principal`, `Renda Extra e Passiva`, `Investimentos e Reembolsos`, `Outras Entradas`) e 12 subcategorias; em `Renda Principal` a ordem é `Outros` (43), `Remuneração Variável / Comissões` (2), `Salário Fixo / Pró-labore` (1).
- **AC-003-04** Dado `?tipo=Despesa`, então 14 macros e 48 subcategorias, nenhum item com grupo `Renda`, e a macro `Receitas Gerais` presente (reprodução do Flask; P-003-1).
- **AC-003-05** Dado `tipo` = `receita`, `RECEITA`, `Renda`, vazio, `" Receita"` ou `xyz`, então 200 com a listagem completa (18 macros). Dado `?tipo=Despesa&tipo=Receita`, vale `Despesa`.
- **AC-003-06** Cada item tem exatamente `grupo`, `macro_id`, `macro_categoria`, `subcategorias` e cada subcategoria exatamente `id` e `nome`, nessa ordem e com esses tipos JSON; o corpo tem `status` e `data` e não tem `message`.
- **AC-003-07** Dada a listagem completa, então macros do mesmo grupo seguem `ordem_macro` e `Despesas Gerais` (ordem NULL) vem antes de `Receitas Gerais`; subcategorias seguem o nome em ordem de bytes (sem `COLLATE`), por exemplo `Aluguéis Recebidos`, `Freelances / Projetos Avulsos`, `Outros` em `Renda Extra e Passiva`.
- **AC-003-08** Dada uma macro global sem nenhuma subcategoria, então ela não aparece; cada macro aparece uma só vez com todas as suas subcategorias.
- **AC-003-09** Dado banco sem categorias, então 200 `{"status":"success","data":[]}`.

**Isolamento entre usuários**
- **AC-003-10** Dado o usuário A com uma subcategoria própria numa macro global, então ela aparece para A e não para B.
- **AC-003-11** Dado o usuário A com uma macro própria e subcategoria própria, então ambas aparecem só para A.
- **AC-003-12** Dada uma subcategoria global dentro de uma macro de A, então ela não aparece para B e aparece para A.
- **AC-003-13** Dada uma subcategoria de B dentro de uma macro de A, então não aparece nem para A nem para B.
- **AC-003-14** Dados dois usuários com categorias próprias, então nenhum id próprio de B está no corpo de A e vice-versa (varredura do JSON), com e sem `tipo`.
- **AC-003-15** Dado token válido de `user_id` inexistente, então 200 apenas com as globais.

**Autenticação e rotas**
- **AC-003-20** Dado header ausente, `Token abc`, token adulterado/expirado ou token sem `user_id`, então 401 com a mensagem exata do `RequireUser`, nas duas rotas.
- **AC-003-21** Dado `GET /api/categories/` (barra final), então 404 com o HTML do Flask.

**Erros e entradas hostis**
- **AC-003-30** Dado repositório que lança exceção com texto sensível, então 500 `{"status":"error","message":"Erro ao carregar categorias"}` sem o texto da exceção, e o log registra só o tipo e o `SqlState` (desvio 008).
- **AC-003-31** Dado `tipo` com `' OR 1=1 --`, texto de 10 000 caracteres, Unicode ou caracteres de controle, então 200 com a listagem completa, nunca 500 (desvio 024).

**Desvio 030**
- **AC-003-32** Dado `Outros` global duplicado na macro 17 (inserido no teste), então as duas linhas aparecem em `Despesas Gerais`, a de menor `id` primeiro (reprodução do Flask; P-003-2).

**Seed**
- **AC-003-40** Dado banco vazio após as migrations, então 5 grupos, 18 macros e 60 subcategorias globais, com nomes, grupos e `ordem_macro` de §4.2, ids de macro 1 a 18 na ordem da tabela e `Outros` com ids 43 a 60.
- **AC-003-41** Dado seed já aplicado, quando o script `0020` roda de novo, então nenhuma linha é criada, alterada ou removida e os valores das sequences não mudam.
- **AC-003-42** Dado banco no estado de produção (5 grupos, 18 macros, 61 subcategorias com `Outros` duplicado, ids de produção), então o script não insere nada e a duplicata continua.
- **AC-003-43** Dado banco semeado, então existem `Investimentos de Curto Prazo` e `Quitação de Empréstimos (Principal)` no grupo `Meta Financeira` e `Outros` globais em `Despesas Gerais` e `Receitas Gerais`.
- **AC-003-44** O seed não cria linha com `usuario_id` não nulo e não altera linhas existentes de usuário.
- **AC-003-45** O `MigrationRunner` registra `0020_categorias_globais` em `dotnet_migrations`.

**Buscas por nome (complemento, D-2)**
- **AC-003-50** Dado banco semeado, então os 3 lookups devolvem um id (investimento de curto prazo, quitação de empréstimos, `Outros` de despesa e de renda).
- **AC-003-51** Dado `Outros` duplicado em `Despesas Gerais`, então o lookup de fallback devolve o de menor `id`.
- **AC-003-52** Dado nome inexistente (categoria removida no teste), então o lookup devolve nulo (quem chama decide o erro).
- **AC-003-53** Dado uma subcategoria `Investimentos de Curto Prazo` de outro usuário, então ela nunca é devolvida.

## 7. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `grupocategoria` | SELECT; INSERT só na migration | UNIQUE `nome_grupo`; sem tenant |
| `macrocategoria` | SELECT; INSERT só na migration | `usuario_id` NULL = global; FK `grupo_id` RESTRICT; índice `idx_macro_usuario_id` |
| `subcategoria` | SELECT; INSERT só na migration | UNIQUE `(macro_id, nome_sub, usuario_id)` não impede duplicata global (desvio 030); índice `idx_sub_usuario_id` |

Tenant: `usuario_id` do JWT, comparado em `subcategoria` e `macrocategoria`. Nenhuma tabela é escrita em tempo de requisição. Volume em produção: 18 macros e 61 subcategorias.

## 8. Desvios de paridade

| Desvio | Estado | Efeito nesta feature |
|---|---|---|
| 001 Receita x Renda | aprovado | O contrato continua `?tipo=Receita`; o filtro vira `grupo = 'Renda'`. Nenhum valor é convertido na resposta. |
| 008 vazamento de erros | aprovado | 500 com mensagem fixa já é o comportamento do Flask aqui; o log não leva a exceção inteira. |
| 024 corpo/params inválidos | aprovado | Sem corpo; `tipo` hostil nunca dá 500. |
| 030 `Outros` duplicado | aprovado | **Afeta**: a listagem mostra as duas linhas (P-003-2 propõe esconder) e os lookups por nome ordenam por `id`. A duplicata não é apagada. |
| 002 setup sem auth | aprovado | `populate-global-categories` vira no-op na 009; o seed passa a ser a migration `0020`. |

## 9. Consumo pelo Angular

Fonte: `finances.service.ts:253-270` e `finances.model.ts:136-163`.
- O front chama só `/api/categories` (nunca o alias), sempre com `tipo` `Receita` ou `Despesa` (padrão `Despesa`) a partir do modal de transação e do modal de conta mensal. O formato esperado (`Category`: `grupo`, `macro_id`, `macro_categoria`, `subcategorias[{id, nome}]`; `CategoriesResponse`: `status`, `data`, `message`) é exatamente o que o Flask devolve: **sem divergência de contrato**.
- Usa `macro_id` como valor do select e como `track`; `grupo` não é exibido; mantém a ordem recebida (por isso a ordenação é parte do contrato).
- Trata `data: []` como sucesso (lista vazia é truthy no `response.data`), por isso a chave `data` precisa existir mesmo vazia.
- Efeitos visíveis dos pontos P-003-1 e P-003-2: em `Despesa` o usuário vê a macro `Receitas Gerais`; em `Receita` não existe o `Outros` de `Receitas Gerais` (só os `Outros` de cada macro de `Renda`); em `Despesas Gerais` aparecem dois `Outros`.

## 10. Fora de escopo

- CRUD de categorias de usuário (não existe hoje).
- `get_user_categories` do bot/Gemini, categorização automática e `get_category_spending` (010, 011, 012).
- Respostas 405 de método não suportado e HEAD/OPTIONS automáticos do Flask (decisão geral do contrato, `api-contract.md` §4).
- Limpeza da duplicata `Outros` e índice único parcial (opcional do desvio 030; decisão do Rafael).

## 11. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask.

### P-003-1 — `Despesa` inclui `Receitas Gerais` e `Receita` não inclui o `Outros` de renda
- **Atual:** `tipo=Despesa` usa `grupo != 'Renda'` (`api.py:1714-1715`), então a macro `Receitas Gerais` (grupo `Geral`) aparece como categoria de despesa. `tipo=Receita` usa `grupo = 'Renda'` (`api.py:1712-1713`), então `Receitas Gerais` > `Outros` (o fallback de renda do bot, `category_service.py:62`) nunca é oferecido ao lançar uma receita.
- **Proposta:** `Receita` = grupo `Renda` mais a macro `Receitas Gerais`; `Despesa` = grupos diferentes de `Renda` menos a macro `Receitas Gerais`.
- **Impacto:** `Receita` passa de 4 para 5 macros (13 subs); `Despesa` de 14 para 13 macros (47 subs). O front só mostra mais ou menos uma opção; nenhum campo muda.
- **Recomendação:** aprovar (corrige uma categoria de receita listada como despesa e vice-versa). Baixo risco.

### P-003-2 — `Outros` global duplicado aparece duas vezes na listagem
- **Atual:** com a duplicata do desvio 030, `Despesas Gerais` mostra dois `Outros` com ids diferentes.
- **Proposta:** a listagem esconde o de maior `id` quando há duas subcategorias globais com o mesmo nome na mesma macro (a transação que aponte para o escondido continua válida).
- **Impacto:** dropdown de despesa perde uma opção repetida; ids antigos continuam aceitos pelo POST/PUT de transações.
- **Recomendação:** preferível resolver no dado (Rafael escolhe qual manter e reaponta, conforme desvio 030) e não esconder na API; se não for limpar, aprovar este desvio.

## 12. Perguntas em aberto

1. **Collation de produção.** O Flask usa a collation padrão do banco. A produção é PostgreSQL 15 Alpine (`lc_time = C`, musl), em que a ordem de texto é por bytes (maiúsculas antes de minúsculas, acentuadas depois); o container de teste usa a mesma imagem. Rodar `SELECT datname, datcollate, datctype FROM pg_database;` e comparar a ordem real de `GET /api/categories` com §4.2. Se for glibc `en_US`, a ordem dos nomes muda e a spec precisa de `COLLATE` explícito.
2. **O seed de produção é o do Python?** Rodar a consulta de conferência (plan.md §6): esperado 5 / 18 / 61, macro 17 = `Despesas Gerais`, as duas `Outros` duplicadas em `macro_id = 17`, e nenhuma linha com `usuario_id` não nulo.
3. **Aprovar D-1** (migration `0020` com guarda tudo-ou-nada e adendo ao ADR 0001 para dados de referência) e **D-2** (lookups por nome na 003, para a 004 e a 011 não reescreverem o SQL).
4. **Reset dos testes.** `PostgresFixture.ResetAsync` apaga o seed. Esta feature reaplica o script no seu próprio teste; promover isso ao `ResetAsync` pede um PR de apoio separado (arquivo compartilhado), que beneficia 002, 004, 011.
5. **HEAD/OPTIONS e 405** nas rotas GET (Flask responde HEAD e 405 em HTML; ASP.NET `MapGet` não responde HEAD). Decisão geral do contrato, não só desta rota.
