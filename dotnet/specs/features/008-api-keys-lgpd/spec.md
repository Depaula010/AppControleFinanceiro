# Spec 008 — API keys e LGPD (`/api-keys/*`)

- **Estado:** aprovada (2026-10-02)
- **Onda:** 1
- **Origem (Python):** `app/routes/api_keys.py:18-847`; `app/services/gerenciador_chaves_api.py:18-286`; `app/services/servico_consentimento_lgpd.py:17-532`; `app/services/encryption_service.py`; DDL em `app/services/finance/setup_service.py:287-418`; schema real em `dotnet/migrations/0000_baseline.sql`
- **Depende de:** 001a (Fernet, `IDbConnectionFactory`, `SafeError`, `Envelopes`)
- **Consumida por:** 010 (Gemini, clima e rotas usam a porta `IApiKeyProvider`, §5); 009 e 012 reutilizam o filtro `x-api-key` (§4.0)
- **Desvios aplicáveis (todos aprovados):** 005 (refinado pelo 052), 006, 008, 024, 028, 041 a 048 e 052; 017 e 029 como referência de leitura Fernet/texto puro. **Rejeitado:** 051 (sem `?mascarar`).

## Decisões aplicadas (Rafael, 2026-10-02: "aprovo as recomendações")

Esta spec descreve o comportamento **final**. Mapeamento das propostas originais:

| Proposta | Desvio | Efeito nesta spec |
|---|---|---|
| P-008-1 | 041 (aprovado) | `POST /lgpd/consentimento` é upsert por (usuário, tipo, versão); `false` grava `revogado_em` (§4.10) |
| P-008-2 | 042 (aprovado) | chave duplicada ⇒ 409 (§4.1) |
| P-008-3 | 043 (aprovado) | usuário inexistente ⇒ 404 em chave, preferência e consentimento (§4.1, 4.5, 4.10) |
| P-008-4 | 044 (aprovado com condições) | `deletar-conta` apaga de fato, em ordem explícita, e limpa o cache Redis do usuário (§4.14) |
| P-008-5 | 045 (aprovado) | `usuario_id` obrigatório em `PUT/DELETE /chave/<id>` (§4.3, 4.4) |
| P-008-6 | 051 (**rejeitado**) | sem `?mascarar`; `exportar` continua devolvendo as chaves decifradas (§4.13) |
| P-008-7 | 046 (aprovado) | limite do plano conta só `tipo_chave = 'sistema'` (§5) |
| P-008-8 | 047 (aprovado) | a porta grava `logacessochaveapi` (§5) |
| P-008-9 | 048 (aprovado) | tipo JSON inesperado em campo do corpo ⇒ 400 "Dados incompletos" da rota |
| G-6 (opção B) | 052 (aprovado) | **todas** as rotas `/api-keys/*` (inclusive `health`) só são registradas com `API_KEYS_ROUTES_ENABLED=true` (padrão **false**; os testes ligam); desligada ⇒ 404 do Flask |
| G-7 | 008 | texto fixo dos 500: `<título>. Tente novamente.` |
| Q4 | — | timezone de produção = UTC (`+00:00`) |
| Q5 | 044 | tokens do Google caem por `CASCADE`; chaves Redis `pending_*`/`nightly_checkin*` são apagadas via `IUserCacheCleaner` |
| Q6 | — | `assinatura.ativo` = `status = 'ativo'` e (`data_fim` nulo ou futura) |
| Q7 | — | token Fernet que não decifra ⇒ falha explícita (500 no export, exceção na porta), nunca o texto cifrado |

Condições do desvio 044: backup completo antes da virada; rota desligada em produção (052); chaves Redis também apagadas.

## 1. Objetivo

Um cliente servidor-a-servidor (autenticado pela `API_SECRET_KEY` no header `X-API-KEY`) gerencia as chaves de API de terceiros de cada usuário (`gemini`, `weather`, `openroute`), as preferências "chave própria × chave do sistema", consulta uso e logs, e exerce os direitos LGPD (consentimentos, exportação, deleção). A feature também entrega a **porta que decide qual chave usar** (`gerenciador_chaves_api`), consumida pela 010.

## 2. Divisão proposta (ver `tasks.md`)

| Entrega | Conteúdo |
|---|---|
| **008-0** (commit separado e primeiro) | Filtro `RequireApiKey` reutilizável em `Api/Http/` (envelope configurável), validador `IApiKeyValidator`, formatador de data estilo Python, porta `IApiKeyProvider` (somente interfaces e tipos de resultado, para a 010 poder começar) |
| **008a** | Chaves, preferências, uso, logs, health, flag `API_KEYS_ROUTES_ENABLED` e a implementação de `IApiKeyProvider` |
| **008b** | LGPD (6 rotas) |

## 3. Rotas

Todas em envelope **C**. **Só existem com `API_KEYS_ROUTES_ENABLED=true` (desvio 052; padrão false); desligada, todas (inclusive `health`) respondem o 404 HTML do Flask.** Autenticação: `X-API-KEY` == `API_SECRET_KEY` (§4.0), exceto `/api-keys/health`. `<int:id>` do Flask só casa dígitos sem sinal: valor não numérico ou negativo ⇒ 404 HTML do Flask (no .NET: constraint `regex(^\d+$)`; ids acima de `int.MaxValue` também dão 404, o Flask daria 500 do banco).

| # | Método | Caminho | Seção |
|---|---|---|---|
| 1 | POST | `/api-keys/usuario/<int:usuario_id>` | 4.1 |
| 2 | GET | `/api-keys/usuario/<int:usuario_id>` | 4.2 |
| 3 | PUT | `/api-keys/chave/<int:chave_id>` | 4.3 |
| 4 | DELETE | `/api-keys/chave/<int:chave_id>` | 4.4 |
| 5 | POST | `/api-keys/preferencias/<int:usuario_id>` | 4.5 |
| 6 | GET | `/api-keys/preferencias/<int:usuario_id>` | 4.6 |
| 7 | DELETE | `/api-keys/preferencias/<int:usuario_id>/<provedor>` | 4.7 |
| 8 | GET | `/api-keys/uso/<int:usuario_id>` | 4.8 |
| 9 | GET | `/api-keys/logs/<int:usuario_id>` | 4.9 |
| 10 | POST | `/api-keys/lgpd/consentimento` | 4.10 |
| 11 | GET | `/api-keys/lgpd/consentimentos/<int:usuario_id>` | 4.11 |
| 12 | GET | `/api-keys/lgpd/consentimentos-iniciais/<int:usuario_id>` | 4.12 |
| 13 | GET | `/api-keys/lgpd/exportar/<int:usuario_id>` | 4.13 |
| 14 | DELETE | `/api-keys/lgpd/deletar-conta/<int:usuario_id>` | 4.14 |
| 15 | GET | `/api-keys/lgpd/verificar-consentimento/<int:usuario_id>/<tipo_consentimento>` | 4.15 |
| 16 | GET | `/api-keys/health` | 4.16 |

`<provedor>` e `<tipo_consentimento>` são texto livre sem barra e **não validados** na rota.

### Estado de cada rota hoje no Flask (produção) × .NET

Produção: `consentimentousuario` 0 linhas, `logacessochaveapi` 0, `chavesapiusuario` 3 (Fernet), `rastreamentousoapi` 11, `planos` 3, `assinaturasusuario` 0. Todas as tabelas de chaves têm FK `ON DELETE CASCADE` para `usuarios`.

| Rota | Flask hoje | Causa | .NET |
|---|---|---|---|
| 1 | Funciona; **500** se o usuário não existe (FK) ou se já há chave do provedor (UNIQUE) | `INSERT` sem upsert (`api_keys.py:73-89`) | 404 usuário inexistente (043), 409 chave duplicada (042) |
| 2, 3, 4, 5, 6, 7, 8 | Funcionam | colunas existem | Igual |
| 9 | Funciona, **sempre vazio** | usa `criado_em` (existe); nada grava em `logacessochaveapi` (`_registrar_log_acesso` não tem chamador) | Passa a ter dados: a porta grava o log (047) |
| 10 | **500 sempre** | `INSERT` com `versao_termos`, `ip_endereco`, `user_agent` (`servico_consentimento_lgpd.py:71-79`); no banco: `versao_consentimento`, `ip_consentimento`, sem user_agent | Funciona (desvio 028). Segundo registro do mesmo tipo e versão atualiza (upsert, 041) |
| 11 | **200 vazio sempre** | `SELECT versao_termos` falha e o `except` devolve `[]` (`:213-215`) | Funciona (desvio 028) |
| 12 | Funciona | não usa banco | Igual |
| 13 | **500 sempre** | `data_acesso` (real: `timestamp_acesso`), `p.nome` (real: `nome_plano`), `p.limite_mensal_*` (real: `limite_*`), `a.ativo` (não existe; há `status`) | Funciona (desvio 028), §4.13 |
| 14 | Funciona, mas só apaga 6 tabelas e **não apaga o usuário** | `:440-473` | Apaga a conta de fato (044) |
| 15 | **200 `false` sempre** | tabela vazia (colunas usadas existem) | Funciona |
| 16 | Funciona | — | Igual |

### Consequência dos desvios 005, 006 e 052 para `x-api-key` e IDOR

- Desvio 005 derivaria a whitelist das rotas registradas, expondo as 16 rotas. O desvio 052 refina isso: as rotas só existem com `API_KEYS_ROUTES_ENABLED=true`, desligada em produção. Com a flag ligada, **a única proteção é o `X-API-KEY`**. Nenhum cliente conhecido chama estas rotas.
- A `API_SECRET_KEY` é também a chave de assinatura do JWT (desvio 006, separação adiada). Quem a tem lê, grava e **apaga** dados de qualquer usuário (com 044, a conta inteira) e forja tokens; por isso a flag fica desligada até haver cliente legítimo.
- IDOR: `usuario_id` do caminho vale para qualquer usuário (credencial administrativa). O desvio 045 fecha o caso em que o caminho não tem usuário: `usuario_id` é obrigatório em `/chave/<id>`, §4.3 e §4.4.

## 4. Contrato por rota

Convenções:
- Erro de autenticação (todas, exceto health): **401** `{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}`. Header ausente, vazio ou diferente da `API_SECRET_KEY`. A autenticação roda antes de qualquer leitura de corpo.
- 500 (todos): `{"erro":"<título da rota>","mensagem":"<texto fixo da rota>"}` (desvio 008). O Flask devolve `str(e)`. Texto fixo adotado: o próprio título seguido de `. Tente novamente.` (ex.: `"Erro ao cadastrar chave. Tente novamente."`). Só o log leva `SafeError.Describe`.
- Corpo ausente, não-JSON ou que não seja objeto (desvio 024): tratado como objeto sem campos, o que cai na validação "dados incompletos" da rota (400). Não exige `Content-Type`.
- Ordem das chaves nos exemplos = ordem do código Python (o Flask ordena alfabeticamente no fio; clientes não dependem disso; os testes comparam a ordem do .NET).
- Timestamps `timestamptz`: Python `isoformat()` com o fuso da sessão do banco: `2026-10-02T13:45:10.123456+00:00`, sem fração quando os microssegundos são 0, `null` se nulo. O .NET usa `+00:00` (ver pergunta 4).
- Valores JSON não esperados (desvio 048): campos de texto só aceitam texto, booleanos só booleano JSON e `usuario_id` do corpo só inteiro JSON; qualquer outro tipo (número onde se espera texto, `"true"`, `"5"`) responde **400** com a mensagem "Dados incompletos" da própria rota (a primeira validação 400 da rota). Valor vazio/nulo segue a mesma validação, como o `not x` do Python. O Flask repassava ao banco ou dava 500. Fora do corpo, `limit` (§4.9) mantém o `int()` do Python (inválido ⇒ 500).
- Usuário inexistente (desvio 043): onde a rota grava com `usuario_id` (§4.1, 4.5, 4.10), a violação de FK `23503` vira 404 `{"erro":"Usuário não encontrado","mensagem":"Nenhum usuário com ID <id>"}`.

### 4.0 Filtro `RequireApiKey`

Compara o header `X-API-KEY` (primeiro valor) com `API_SECRET_KEY` em **tempo constante** (hash SHA-256 dos dois e `CryptographicOperations.FixedTimeEquals`; o Flask usa `!=`). O envelope do 401 é parâmetro do filtro (aqui C; 009 e 012 passam B e E). *Origem: `api_keys.py:18-31`; `integrations.md` §1.*

### 4.1 `POST /api-keys/usuario/<usuario_id>`

Entrada: `provedor`, `chave_api`.

| # | Condição | Resposta |
|---|---|---|
| 1 | `provedor` ou `chave_api` vazios/ausentes/nulos/falsos (corpo inválido incluído) | 400 `{"erro":"Dados incompletos","mensagem":"Provedor e chave_api são obrigatórios"}` |
| 2 | `provedor` fora de `gemini`, `weather`, `openroute` (comparação exata, minúsculas) | 400 `{"erro":"Provedor inválido","mensagem":"Provedor deve ser um de: gemini, weather, openroute"}` |
| 3 | `provedor` ou `chave_api` com tipo JSON diferente de texto (048) | 400 igual ao da linha 1 |

Efeito: cifra `chave_api` com Fernet (sem `trim`, valor como veio) e `INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em) VALUES (..., TRUE, now, now) RETURNING id`. `usuario_id` do caminho é validado pela FK (404 se não existir).

Sucesso **201** `{"mensagem":"Chave cadastrada com sucesso","id":<int>,"provedor":"<p>","usuario_id":<int>}`.
Erros: **404** usuário inexistente (FK `23503`, desvio 043); **409** `{"erro":"Chave já cadastrada","mensagem":"Já existe uma chave para este provedor. Use PUT /api-keys/chave/<id>."}` quando o par (usuário, provedor) já existe (UNIQUE `23505`, inclusive chave desativada: a reativação é por `PUT`; desvio 042); demais falhas: 500 `{"erro":"Erro ao cadastrar chave","mensagem":"Erro ao cadastrar chave. Tente novamente."}`. *Origem: `api_keys.py:38-105`.*

### 4.2 `GET /api-keys/usuario/<usuario_id>`

`SELECT id, provedor, ativo, ultimo_uso_em, criado_em, atualizado_em FROM chavesapiusuario WHERE usuario_id = @id ORDER BY provedor, criado_em DESC`. Nunca devolve a chave.
**200** `{"usuario_id":<id>,"total":<n>,"chaves":[{"id","provedor","ativo","ultimo_uso_em","criado_em","atualizado_em"}]}`. `ativo` pode ser `null` (coluna anulável). Usuário inexistente ⇒ 200 com `total` 0.
500: `Erro ao listar chaves`. *Origem: `:108-155`.*

### 4.3 `PUT /api-keys/chave/<chave_id>`

Entrada: `chave_api` (opcional), `ativo` (opcional, booleano), `usuario_id` (**obrigatório**, inteiro JSON, desvios 006 e 045).

| # | Condição | Resposta |
|---|---|---|
| 1 | `chave_api` vazia/ausente **e** `ativo` ausente/nulo (corpo inválido incluído), ou `chave_api`/`ativo` com tipo errado (048) | 400 `{"erro":"Dados incompletos","mensagem":"Forneça pelo menos 'chave_api' ou 'ativo'"}` |
| 2 | `usuario_id` ausente, nulo, 0 ou de tipo diferente de inteiro (045, 048) | 400 `{"erro":"Dados incompletos","mensagem":"usuario_id é obrigatório"}` |
| 3 | nenhuma chave com o id, **ou** `usuario_id` diferente do dono (desvio 006, mesma resposta para não revelar a existência) | 404 `{"erro":"Chave não encontrada","mensagem":"Nenhuma chave com ID <chave_id>"}` |

Efeito: `UPDATE chavesapiusuario SET [chave_api_criptografada = Fernet(chave_api)] [, ativo = @ativo], atualizado_em = now WHERE id = @id AND usuario_id = @uid RETURNING provedor, usuario_id`. Chave vazia com `ativo` informado atualiza só o `ativo`. `ativo: false` é valor válido.
Sucesso **200** `{"mensagem":"Chave atualizada com sucesso","id":<chave_id>,"provedor":"<p>","usuario_id":<dono>}`. 500: `Erro ao atualizar chave`. *Origem: `:158-234`.*

### 4.4 `DELETE /api-keys/chave/<chave_id>`

Parâmetro **obrigatório** `?usuario_id=<int>` (desvios 006 e 045): ausente ou vazio ⇒ 400 `{"erro":"Dados incompletos","mensagem":"usuario_id é obrigatório"}`; valor não inteiro ⇒ tratado como diferente do dono ⇒ 404.
`UPDATE chavesapiusuario SET ativo = FALSE, atualizado_em = now WHERE id = @id AND usuario_id = @uid RETURNING provedor, usuario_id` (soft delete; repetir devolve 200).
Sucesso **200** `{"mensagem":"Chave desativada com sucesso","id":<id>,"provedor":"<p>"}`. Sem linha: 404 igual ao de 4.3. 500: `Erro ao deletar chave`. *Origem: `:237-283`.*

### 4.5 `POST /api-keys/preferencias/<usuario_id>`

Entrada: `provedor`, `usar_chave_propria` (booleano).

| # | Condição | Resposta |
|---|---|---|
| 1 | `provedor` vazio/ausente, ou `usar_chave_propria` ausente/nulo (`false` é válido) | 400 `{"erro":"Dados incompletos","mensagem":"Provedor e usar_chave_propria são obrigatórios"}` |
| 2 | provedor inválido | 400 `{"erro":"Provedor inválido","mensagem":"Provedor deve ser um de: gemini, weather, openroute"}` |
| 3 | `provedor` não é texto ou `usar_chave_propria` não é booleano (048) | 400 igual ao da linha 1 |

Efeito: `INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria, atualizado_em) VALUES (...) ON CONFLICT (usuario_id, provedor) DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now RETURNING id`. Não verifica se existe chave cadastrada; usuário inexistente (FK) ⇒ 404 (043).
Sucesso **201** (também na atualização) `{"mensagem":"Preferência configurada com sucesso","id":<int>,"usuario_id":<id>,"provedor":"<p>","usar_chave_propria":<bool>,"tipo":"própria (grátis)" | "sistema (cobrado)"}`. 500: `Erro ao configurar preferência`. *Origem: `:290-365`.*

### 4.6 `GET /api-keys/preferencias/<usuario_id>`

`SELECT id, provedor, usar_chave_propria, atualizado_em FROM preferenciaschaveapi WHERE usuario_id = @id ORDER BY provedor`.
**200** `{"usuario_id":<id>,"total":<n>,"preferencias":[{"id","provedor","usar_chave_propria","tipo","atualizado_em"}]}` (`tipo` como em 4.5). 500: `Erro ao listar preferências`. *Origem: `:368-411`.*

### 4.7 `DELETE /api-keys/preferencias/<usuario_id>/<provedor>`

`DELETE FROM preferenciaschaveapi WHERE usuario_id = @id AND provedor = @provedor RETURNING id`. Sem linha: 404 `{"erro":"Preferência não encontrada","mensagem":"Nenhuma preferência para <provedor>"}` (o `provedor` do caminho, decodificado). Sucesso **200** `{"mensagem":"Preferência removida com sucesso","usuario_id":<id>,"provedor":"<provedor>"}`. 500: `Erro ao deletar preferência`. *Origem: `:414-459`.*

### 4.8 `GET /api-keys/uso/<usuario_id>`

Query: `mes_ano` (padrão: mês atual `yyyy-MM` em São Paulo, `IClock.Now`; **qualquer texto é aceito**, string vazia conta como ausente), `provedor` (opcional, não validado; vazio conta como ausente).
`SELECT provedor, tipo_chave, SUM(quantidade_chamadas) AS total_chamadas, mes_ano, MAX(atualizado_em) AS ultima_atualizacao FROM rastreamentousoapi WHERE usuario_id = @id AND mes_ano = @mes [AND provedor = @p] GROUP BY provedor, tipo_chave, mes_ano ORDER BY provedor, tipo_chave`.
**200** `{"usuario_id":<id>,"mes_ano":"<mes>","resumo":{"total_chamadas":<n>,"chamadas_chave_propria":<n>,"chamadas_chave_sistema":<n>},"detalhes":[{"provedor","tipo_chave","total_chamadas","mes_ano","ultima_atualizacao"}]}`. `chamadas_chave_propria` soma `tipo_chave = 'propria'`; `chamadas_chave_sistema` soma `'sistema'`; `total_chamadas` do resumo = soma dos dois (outros valores de `tipo_chave` não entram). 500: `Erro ao consultar uso`. *Origem: `:466-539`.*

### 4.9 `GET /api-keys/logs/<usuario_id>`

Query: `limit` (padrão 50, teto 500), `provedor` (vazio = ausente), `sucesso` (presente, mesmo vazio: `true` sem diferenciar maiúsculas ⇒ `sucesso = true`; qualquer outro texto ⇒ `sucesso = false`).
`limit` segue o `int()` do Python (espaços e sinal aceitos); não inteiro ⇒ 500 `Erro ao consultar logs`; negativo ⇒ o PostgreSQL rejeita `LIMIT` negativo ⇒ 500; `0` ⇒ lista vazia.
`SELECT id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, criado_em FROM logacessochaveapi WHERE usuario_id = @id [AND provedor = @p] [AND sucesso = @s] ORDER BY criado_em DESC LIMIT @limit` (usa `criado_em`, que existe; `timestamp_acesso` é outra coluna).
**200** `{"usuario_id":<id>,"total":<n>,"logs":[{"id","provedor","tipo_chave","operacao","sucesso","mensagem_erro","criado_em"}]}`. *Origem: `:542-613`.*

### 4.10 `POST /api-keys/lgpd/consentimento`

Entrada: `usuario_id`, `tipo_consentimento`, `consentimento_dado` (booleano), `versao_termos` (opcional).

| # | Condição | Resposta |
|---|---|---|
| 1 | `usuario_id` vazio/0, `tipo_consentimento` vazio, ou `consentimento_dado` ausente/nulo (`false` é válido) | 400 `{"erro":"Dados incompletos","mensagem":"usuario_id, tipo_consentimento e consentimento_dado são obrigatórios"}` |
| 2 | `tipo_consentimento` fora de `uso_dados_pessoais`, `armazenamento_chaves_api`, `rastreamento_uso`, `comunicacao_whatsapp`, `compartilhamento_terceiros` | 400 `{"erro":"Validação","mensagem":"Tipo de consentimento inválido: <tipo>. Válidos: ['uso_dados_pessoais', 'armazenamento_chaves_api', 'rastreamento_uso', 'comunicacao_whatsapp', 'compartilhamento_terceiros']"}` (a lista no formato `repr` do Python, aspas simples) |

`usuario_id` precisa ser inteiro JSON, `tipo_consentimento`/`versao_termos` texto e `consentimento_dado` booleano; outro tipo ⇒ 400 igual ao da linha 1 (048; o Flask aceitava `"5"` por coerção do banco).
Efeito (desvio 041, upsert): `INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, consentimento_dado, versao_consentimento, data_consentimento, ip_consentimento) VALUES (@u, @t, @c, @v, now, NULL) ON CONFLICT (usuario_id, tipo_consentimento, versao_consentimento) DO UPDATE SET consentimento_dado = EXCLUDED.consentimento_dado, data_consentimento = now, revogado_em = CASE WHEN EXCLUDED.consentimento_dado THEN NULL ELSE now END RETURNING id`, com `@v = versao_termos` ou `"1.0"` se vazio. Repetir o mesmo (usuário, tipo, versão) atualiza a mesma linha (mesmo `id`) e também devolve 201; revogar (`false`) grava `revogado_em`, reconceder (`true`) o zera. `ip_consentimento` fica nulo (o Flask também não captura o IP); `user_agent` não existe no banco e deixa de existir.
Sucesso **201** `{"status":"sucesso","consentimento_id":<int>,"mensagem":"Consentimento registrado com sucesso","tipo":"<tipo>","consentimento_dado":<bool>}` (o `status` aqui é do serviço, não do envelope; preservado).
Erros: 404 usuário inexistente (043); 500 `Erro ao registrar consentimento` para `versao_termos` com mais de 20 caracteres e demais falhas. *Origem: `api_keys.py:620-669`; `servico_consentimento_lgpd.py:35-117`.*

### 4.11 `GET /api-keys/lgpd/consentimentos/<usuario_id>`

`SELECT id, tipo_consentimento, consentimento_dado, versao_consentimento, data_consentimento FROM consentimentousuario WHERE usuario_id = @id ORDER BY data_consentimento DESC`.
**200** `{"usuario_id":<id>,"total":<n>,"consentimentos":[{"id","tipo_consentimento","descricao","consentimento_dado","versao_termos","data_consentimento"}]}`. `versao_termos` vem de `versao_consentimento`; `descricao` é a do catálogo (§5, RN-008-12) ou `"Descrição não disponível"` para tipo fora do catálogo. Erro de banco: o serviço devolve lista vazia e a rota responde 200 com `total` 0 (reproduzido; é isso que esconde o bug de coluna hoje). *Origem: `:672-694`; serviço `:160-215`.*

### 4.12 `GET /api-keys/lgpd/consentimentos-iniciais/<usuario_id>`

Sem banco, sem verificar o usuário. **200**
`{"usuario_id":<id>,"consentimentos_necessarios":[{"tipo","descricao","obrigatorio","versao_termos":"1.0"}, ...],"mensagem":"Para continuar, você precisa consentir com os termos abaixo"}`, cinco itens na ordem do catálogo; `obrigatorio` verdadeiro só para `uso_dados_pessoais` e `armazenamento_chaves_api`. 500: `Erro ao solicitar consentimentos`. *Origem: `:697-717`; serviço `:494-532`.*

### 4.13 `GET /api-keys/lgpd/exportar/<usuario_id>`

**200**
```
{"usuario_id":<id>,"data_exportacao":"<IClock.Now, yyyy-MM-ddTHH:mm:ss.ffffff, sem fuso>","dados":{
  "chaves_api":[{"id","provedor","chave_api","ativo","ultimo_uso_em","criado_em","atualizado_em"}],
  "preferencias":[{"id","provedor","usar_chave_propria","atualizado_em"}],
  "uso_mensal":[{"provedor","tipo_chave","quantidade_chamadas","mes_ano"}],
  "logs_acesso":[{"provedor","tipo_chave","sucesso","mensagem_erro","data_acesso"}],
  "consentimentos":[ ...igual a 4.11... ],
  "assinatura": null | {"id","plano","preco_mensal","limites":{"gemini","weather","openroute"},"data_inicio","data_fim","ativo"}}}
```
- `chaves_api`: sem `ORDER BY` no Flask; o .NET ordena por `provedor, id` (determinístico). `chave_api` é a chave **decifrada** (Fernet ou texto puro legado; token com formato Fernet que não decifra ⇒ falha explícita, Q7). O Flask usa `decrypt` estrito e daria 500 para texto puro.
- `uso_mensal`: `ORDER BY mes_ano DESC` (+ `provedor, tipo_chave` para desempate).
- `logs_acesso`: `SELECT provedor, tipo_chave, sucesso, mensagem_erro, timestamp_acesso ... ORDER BY timestamp_acesso DESC LIMIT 1000`; a chave de saída continua `data_acesso`.
- `assinatura`: `SELECT a.id, p.nome_plano, p.preco_mensal, p.limite_gemini, p.limite_weather, p.limite_openroute, a.data_inicio, a.data_fim, a.status FROM assinaturasusuario a JOIN planos p ON a.plano_id = p.id WHERE a.usuario_id = @id ORDER BY a.data_inicio DESC LIMIT 1`. `plano` = `nome_plano`. `preco_mensal` = número (`0` inteiro quando o valor é zero, como `float(x) if x else 0`; `29.9` para 29.90). `limites` são inteiros ou `null` (ilimitado). `ativo` (coluna inexistente) = `status = 'ativo'` **e** (`data_fim` nulo ou futuro), igual ao critério de `_verificar_limite_plano` (decisão Q6). Sem linha: `null` (hoje sempre, `assinaturasusuario` tem 0 linhas).
- Usuário inexistente: 200 com listas vazias e `assinatura: null` (o Flask, se funcionasse, faria o mesmo).
- **Checagens do desvio 006** (mantendo o contrato): resposta com `Cache-Control: no-store`; as chaves nunca vão ao log; `x-api-key` obrigatório; nenhum caminho devolve texto cifrado como se fosse chave (token com formato Fernet que falha ao decifrar ⇒ 500 `Erro ao exportar dados`, não o texto cifrado). Mascarar a chave ou exigir o JWT do próprio usuário muda o contrato: proposta rejeitada (desvio 051); o export só é alcançável com a flag 052 ligada e `X-API-KEY` válida.
500: `Erro ao exportar dados`. *Origem: `api_keys.py:720-757`; serviço `:217-399`; desvios 006 e 028.*

### 4.14 `DELETE /api-keys/lgpd/deletar-conta/<usuario_id>`

Entrada: `confirmacao`.

| # | Condição | Resposta |
|---|---|---|
| 1 | `confirmacao` ausente, corpo inválido (024) ou diferente de `"CONFIRMO_DELECAO"` (exato, com caixa; valor não-texto também) | 400 `{"erro":"Confirmação inválida","mensagem":"Confirmação inválida. Para deletar a conta, você deve passar confirmacao='CONFIRMO_DELECAO' explicitamente."}` |
| 2 | usuário não existe (desvio 044) | 404 `{"erro":"Usuário não encontrado","mensagem":"Nenhum usuário com ID <id>"}`; nada é apagado |

Efeito (desvio 044, **uma transação**; o Flask só apagava as 6 primeiras e deixava `usuarios` e os dados financeiros): `DELETE ... WHERE usuario_id = @id` contando as linhas, nesta ordem: `chavesapiusuario`, `preferenciaschaveapi`, `logacessochaveapi`, `rastreamentousoapi`, `consentimentousuario`, `assinaturasusuario` (ordem do Flask), depois, por causa dos `RESTRICT` de `transacoes.conta_id`/`subcategoria_id` e `agendamentos.subcategoria_id`: `transacoes`, `agendamentos`, `contas` (leva `faturas` por cascata), `potesdegastos` (leva `potesubcategorias`), `subcategoria` e `macrocategoria` do usuário (`usuario_id = @id`; as globais ficam) e por fim a linha de `usuarios` (o resto cai por `CASCADE`: `googlecalendartokens`, `calendaralertconfigs`, `notificationconfigs`, `monthlyreportconfigs`, `enderecosfavoritos`). Qualquer falha desfaz tudo.
Depois do `COMMIT`, o caso de uso chama `IUserCacheCleaner.ClearAsync(userId)`, que apaga as chaves Redis `pending_*` e `nightly_checkin*` do usuário (decisão Q5). Falha ao limpar o cache é logada (`SafeError.Describe`) e **não** desfaz a deleção nem vira 500 (os dados já foram apagados). Enquanto a 010a (cliente Redis) não existir, a implementação registrada é nula (não faz nada).
Sucesso **200** `{"status":"sucesso","usuario_id":<id>,"registros_deletados":{"chaves_api","preferencias","logs","uso","consentimentos","assinaturas","transacoes","agendamentos","contas","potes","subcategorias","macrocategorias","usuarios"},"total_registros":<soma dos 13 contadores>,"mensagem":"Conta e todos os dados do usuário <id> foram deletados com sucesso"}`. Linhas apagadas só por cascata (`faturas`, `potesubcategorias`, tokens e configurações) não entram nos contadores.
500: `Erro ao deletar conta`. *Origem: `api_keys.py:760-798`; serviço `:401-492`; desvio 044.*

### 4.15 `GET /api-keys/lgpd/verificar-consentimento/<usuario_id>/<tipo_consentimento>`

`SELECT consentimento_dado FROM consentimentousuario WHERE usuario_id = @id AND tipo_consentimento = @tipo ORDER BY data_consentimento DESC LIMIT 1`. Sem linha ⇒ `false`; `revogado_em` é ignorado (como o Flask). Qualquer erro de banco ⇒ `false` (reproduzido, fail-closed; logado).
**200** `{"usuario_id":<id>,"tipo_consentimento":"<tipo>","consentimento_ativo":<bool>}`. `consentimento_dado` nulo não ocorre (NOT NULL). *Origem: `:801-833`; serviço `:119-157`.*

### 4.16 `GET /api-keys/health`

Sem autenticação (mas só existe com a flag 052 ligada). **200** `{"status":"ok","servico":"API Keys Management","versao":"1.0.0"}`. *Origem: `:840-847`.*

## 5. Regras de negócio

- **RN-008-01** Provedores válidos: `gemini`, `weather`, `openroute`; comparação exata. *Origem: `api_keys.py:62, 318`; `gerenciador_chaves_api.py:24`.*
- **RN-008-02** Chaves de usuário são guardadas cifradas em Fernet (`ENCRYPTION_KEY`) em `chave_api_criptografada`; a leitura aceita Fernet e texto puro legado (`DecryptOrPlain`; AC-001-52). Chave nunca em log, erro ou resposta, exceto `exportar` (RN-008-10). *Origem: `api_keys.py:70, 186`; `integrations.md` §1; desvios 017/029.*
- **RN-008-03** Uma chave por (usuário, provedor) (UNIQUE); `DELETE` é soft delete (`ativo = false`); reativar e trocar a chave é por `PUT`. *Origem: `setup_service.py:287-300`; `api_keys.py:246-251`.*
- **RN-008-04** Preferência é escolha explícita: `usar_chave_propria = true` ⇒ chave do usuário; `false` ⇒ chave do sistema; **sem preferência não há fallback automático**. *Origem: `gerenciador_chaves_api.py:1-10, 48-62`; `business-rules.md` R13.*
- **RN-008-05** `tipo` na resposta de preferência: `"própria (grátis)"` se verdadeiro, senão `"sistema (cobrado)"`. *Origem: `api_keys.py:348, 391`.*
- **RN-008-06** Desvios 006 e 045: `PUT/DELETE /chave/<id>` exigem `usuario_id` e só atuam se ele for o dono; caso contrário 404 idêntico ao de chave inexistente. *Origem: `api_keys.py:196-201, 246-251` (sem filtro de dono); desvios 006 e 045.*
- **RN-008-07** Mês de uso/cobrança: `yyyy-MM` do `IClock.Now` (São Paulo), não do relógio do container. *Origem: `api_keys.py:481-483`; `gerenciador_chaves_api.py:162, 238`.*
- **RN-008-08** Consentimento: o tipo precisa estar no catálogo de 5 tipos (descrições literais abaixo); versão padrão `"1.0"`; IP não capturado. *Origem: `servico_consentimento_lgpd.py:23-32, 60-66`.*

  | tipo | descrição | obrigatório |
  |---|---|---|
  | `uso_dados_pessoais` | Uso de dados pessoais para funcionamento do sistema | sim |
  | `armazenamento_chaves_api` | Armazenamento de chaves de API criptografadas | sim |
  | `rastreamento_uso` | Rastreamento de uso para billing e analytics | não |
  | `comunicacao_whatsapp` | Envio de notificações via WhatsApp | não |
  | `compartilhamento_terceiros` | Compartilhamento com APIs de terceiros (Gemini, Weather, etc) | não |
- **RN-008-09** Consentimento ativo = o registro mais recente (por `data_consentimento`) do tipo tem `consentimento_dado = true`. *Origem: `servico_consentimento_lgpd.py:134-153`.*
- **RN-008-10** `exportar` devolve as chaves decifradas (direito de portabilidade) com as checagens do §4.13 (desvio 051 rejeitado: sem máscara). *Origem: `:264-268`; desvio 006.*
- **RN-008-11** `deletar-conta` exige a confirmação exata e roda numa transação que apaga a conta de fato (§4.14, desvio 044); `total_registros` = soma dos 13 contadores. *Origem: `:430-488`; desvio 044.*
- **RN-008-12** Erros 500 devolvem texto fixo por rota (`<título>. Tente novamente.`); logs só com `SafeError.Describe` e ids (nunca chave, corpo ou `x-api-key`). *Desvio 008.*
- **RN-008-14** Rotas `/api-keys/*` só são registradas com `API_KEYS_ROUTES_ENABLED=true` (padrão false; desvio 052). A porta `IApiKeyProvider` e o validador ficam registrados **sempre** (a 010 não depende da flag).
- **RN-008-13** Todas as colunas usadas são as do banco: `versao_consentimento`, `ip_consentimento`, `timestamp_acesso`, `nome_plano`, `limite_gemini/weather/openroute`, `assinaturasusuario.status`. *Desvio 028.*

### Porta para a feature 010: `IApiKeyProvider` (equivale a `GerenciadorChavesApi.obter_chave_api`)

`ResolveAsync(int userId, string provider, CancellationToken)` (`MeuSecretario.Application.Abstractions`) devolve `Resolved(Key, KeyType)` (`KeyType` = `propria` | `sistema`) ou `Failed(Reason, Message)`. `Message` é o texto literal do Flask, que a 010 mostra ao usuário (os consumidores atuais re-lançam a exceção e o WhatsApp exibe `str(e)`). Quem chama sem `usuario_id` usa a chave global sem passar pela porta (decisão da 010).

Algoritmo e mensagens (*origem: `gerenciador_chaves_api.py:33-139`*):

| # | Condição | Resultado |
|---|---|---|
| 1 | provedor fora da lista | `InvalidProvider`: `Provedor inválido: <provedor>` |
| 2 | sem linha em `preferenciaschaveapi` (ou erro de banco ao ler, que o Flask engole) | `NoPreference`: `⚙️ Você ainda não configurou suas preferências para <p>.\nAcesse o dashboard e escolha:\n• Usar sua própria chave (grátis)\n• Usar chave do sistema (com custo adicional)` |
| 3 | `usar_chave_propria = true` e não há chave `ativo = true` do provedor | `OwnKeyMissing`: `🔑 Você configurou para usar sua chave de <p>, mas ainda não cadastrou.\nAcesse o dashboard para cadastrar sua chave.\nOu responda: *Cadastrar chave de <p>*` |
| 4 | `usar_chave_propria = true` e há chave | decifra (`DecryptOrPlain`); `UPDATE chavesapiusuario SET ultimo_uso_em = now, atualizado_em = now WHERE usuario_id AND provedor`; registra uso `propria`; devolve `(chave, propria)` |
| 5 | `usar_chave_propria = false` e a chave do sistema do provedor (`GEMINI_API_KEY`, `WEATHER_API_KEY`, `OPENROUTE_API_KEY` em `AppSettings`) está vazia | `SystemKeyUnavailable`: `❌ Chave do sistema para <p> indisponível no momento.\nEntre em contato com o suporte ou configure sua própria chave.` |
| 6 | `usar_chave_propria = false`: verifica o limite do plano (abaixo; só conta uso `sistema`, desvio 046) | `LimitReached`: `🚫 Limite mensal atingido para <p>!\nSeu plano <nome_plano> permite <limite> chamadas/mês.\nVocê já usou <usado> chamadas.\n\n💡 Opções:\n• Faça upgrade do plano\n• Use sua própria chave (sem limites)` |
| 7 | passou | registra uso `sistema`; devolve `(chave do sistema, sistema)` |

- **Limite do plano:** plano = `assinaturasusuario a JOIN planos p ON a.plano_id = p.id WHERE a.usuario_id = @u AND a.status = 'ativo' AND (a.data_fim IS NULL OR a.data_fim > now)` (`ORDER BY a.data_inicio DESC LIMIT 1`; o Flask não ordena). Sem plano ativo ⇒ sem limite (hoje vale para todos: `assinaturasusuario` está vazia e **nenhuma rota cria assinatura**). Limite do provedor `NULL` ⇒ ilimitado. Usado = `COALESCE(SUM(quantidade_chamadas),0)` de `rastreamentousoapi` no mês para o provedor, **somando só `tipo_chave = 'sistema'`** (desvio 046; o Flask somava também `propria`). Excedido quando `usado >= limite`. Qualquer erro ao verificar (que não seja o limite) é engolido e a chamada passa (fail-open, logado).
- **Registro de uso:** `INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano, atualizado_em) VALUES (..., 1, @mes, now) ON CONFLICT (usuario_id, provedor, tipo_chave, mes_ano) DO UPDATE SET quantidade_chamadas = rastreamentousoapi.quantidade_chamadas + 1, atualizado_em = now`. Erro ⇒ engolido e logado (a chave ainda é devolvida). Conta a tentativa de obter a chave, não o sucesso da chamada externa.
- `ultimo_uso_em` e uso: mesmos engolimentos de erro.
- **Log de acesso (desvio 047):** a cada resolução (sucesso ou falha) a porta grava `INSERT INTO logacessochaveapi (usuario_id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, endereco_ip) VALUES (..., @tipo, 'obter_chave', @sucesso, @erro, NULL)`; `tipo_chave` = `propria`/`sistema` na escolha feita, ou `n/a` quando não houve escolha (provedor inválido, sem preferência); `mensagem_erro` = nome do motivo da falha (`NoPreference`, `OwnKeyMissing`...; nunca a chave, nunca texto do banco), nulo no sucesso. Provedor inválido não grava (a coluna não é validada, mas não há usuário/provedor confiável). Erro ao gravar é engolido e logado (a resolução não muda). Falha de FK (usuário inexistente) é engolida da mesma forma.

## 6. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","008-NN")]`).

**Autenticação e rotas**
- **AC-008-01** Dado header `X-API-KEY` ausente, vazio ou errado, quando qualquer rota exceto health, então 401 `{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}` e nada é lido nem gravado (corpo e banco intactos).
- **AC-008-02** Dado a chave correta, então a rota executa; a comparação usa `FixedTimeEquals` sobre hashes (teste unitário do validador, inclusive chaves de tamanhos diferentes).
- **AC-008-03** `GET /api-keys/health` sem header ⇒ 200 `{"status":"ok","servico":"API Keys Management","versao":"1.0.0"}`.
- **AC-008-04** `/api-keys/usuario/abc`, `/api-keys/usuario/-1` e `/api-keys/usuario/1/` ⇒ 404. Divergência de corpo (aceita): só a barra final e ids acima de `int.MaxValue` devolvem o HTML do Flask; os ids não numéricos/negativos caem na rota inexistente do ASP.NET (404 de corpo vazio). Nenhum cliente depende do corpo.
- **AC-008-05** (desvio 052) Com `API_KEYS_ROUTES_ENABLED` ausente ou `false`, todas as 16 rotas (inclusive `health`) ⇒ 404 (corpo vazio do ASP.NET, ver AC-008-04), mesmo com `X-API-KEY` correta; com `true`, existem. `AppSettings.ApiKeysRoutesEnabled` lê a variável (padrão false; `true`/`TRUE` ligam).

**Chaves**
- **AC-008-10** Dado corpo válido, `POST /usuario/<id>` ⇒ 201 com `mensagem`, `id`, `provedor`, `usuario_id`; o banco tem a chave em token Fernet decifrável pelo Python (fixture) e `ativo = true`.
- **AC-008-11** Dado `provedor` ou `chave_api` vazios, ausentes ou corpo inválido/sem `Content-Type` (024) ⇒ 400 "Provedor e chave_api são obrigatórios".
- **AC-008-12** Dado `provedor: "openai"` ⇒ 400 "Provedor deve ser um de: gemini, weather, openroute"; `"Gemini"` também.
- **AC-008-13** (042, 043) Dado usuário inexistente ⇒ 404 `Usuário não encontrado`/`Nenhum usuário com ID N`; chave já cadastrada para o provedor (inclusive inativa) ⇒ 409 `Chave já cadastrada`. Nenhuma resposta contém texto do PostgreSQL, nome de constraint ou valor da chave.
- **AC-008-14** `GET /usuario/<id>` lista ordenado por `provedor, criado_em DESC`, sem campo de chave; `total` coerente; timestamps no formato Python; usuário sem chaves ⇒ `total` 0.
- **AC-008-15** `PUT /chave/<id>` com `chave_api` regrava o token Fernet e atualiza `atualizado_em`; com só `ativo: false` não mexe na chave; com `ativo: false` e chave vazia atualiza só o `ativo`.
- **AC-008-16** `PUT` sem `chave_api` e sem `ativo` (ou `ativo: null`) ⇒ 400 "Forneça pelo menos 'chave_api' ou 'ativo'"; id inexistente ⇒ 404 "Nenhuma chave com ID N".
- **AC-008-17** (IDOR, desvios 006 e 045) Dada a chave do usuário A, `PUT` com `usuario_id` de B (corpo) ⇒ 404 e a chave de A continua intacta; `DELETE ?usuario_id=B` ⇒ 404 e continua ativa; com `usuario_id` de A ⇒ sucesso; **sem `usuario_id`** (corpo/query ausente, nulo, 0 ou de tipo errado) ⇒ 400 `usuario_id é obrigatório` e nada muda.
- **AC-008-18** `DELETE /chave/<id>` ⇒ 200 `{"mensagem":"Chave desativada com sucesso","id","provedor"}`; `ativo = false`; repetir também 200; id inexistente ⇒ 404.
- **AC-008-19** Chave legada em texto puro e chave Fernet (fixture do Python) são lidas por `DecryptOrPlain` na porta e no export.

**Preferências**
- **AC-008-20** `POST /preferencias/<id>` ⇒ 201 com `tipo` correto para `true` e `false`; repetir atualiza a mesma linha (mesmo `id`) e também devolve 201.
- **AC-008-21** `usar_chave_propria` ausente/nulo ou não booleano (048) ⇒ 400; `false` é aceito; provedor inválido ⇒ 400; usuário inexistente ⇒ 404 (043).
- **AC-008-23** (048) Tipos JSON errados (`provedor: 1`, `chave_api: 5`, `ativo: "true"`, `usuario_id: "5"`) ⇒ 400 com a mensagem "Dados incompletos" da rota, nunca 500.
- **AC-008-22** `GET /preferencias/<id>` ordenado por `provedor`; `DELETE /preferencias/<id>/<provedor>` ⇒ 200 e depois 404 "Nenhuma preferência para <provedor>".

**Uso e logs**
- **AC-008-30** `GET /uso/<id>` sem parâmetros usa o mês atual do `FakeTimeProvider` em São Paulo (virada de mês às 00:00 de lá, não de UTC); com `mes_ano` e `provedor` filtra; `resumo` soma por `tipo_chave`; sem dados ⇒ zeros e `detalhes` vazio.
- **AC-008-31** `GET /logs/<id>`: padrão 50, teto 500, `limit` não inteiro ou negativo ⇒ 500 de texto fixo, `sucesso=true|TRUE` filtra verdadeiros, qualquer outro texto filtra falsos, ordenado por `criado_em DESC`.

**LGPD**
- **AC-008-40** `POST /lgpd/consentimento` válido ⇒ 201 com `status`, `consentimento_id`, `mensagem`, `tipo`, `consentimento_dado`; grava `versao_consentimento` ("1.0" por padrão ou a informada) e `ip_consentimento` nulo.
- **AC-008-41** Campos obrigatórios ausentes (corpo inválido incluído) ⇒ 400 "usuario_id, tipo_consentimento e consentimento_dado são obrigatórios"; `consentimento_dado: false` é aceito.
- **AC-008-42** Tipo fora do catálogo ⇒ 400 `Validação` com a mensagem e a lista no formato do Python.
- **AC-008-43** (041) Segundo registro do mesmo (usuário, tipo, versão) ⇒ 201 com o mesmo `consentimento_id`; `false` grava `revogado_em`, depois `true` o zera e atualiza `data_consentimento`; versão nova cria outra linha; usuário inexistente ⇒ 404; `versao_termos` com mais de 20 caracteres ⇒ 500 de texto fixo.
- **AC-008-44** `GET /consentimentos/<id>` ⇒ itens com `descricao` do catálogo e `versao_termos` vinda de `versao_consentimento`, mais recente primeiro.
- **AC-008-45** `GET /consentimentos-iniciais/<id>` ⇒ os 5 itens na ordem, `obrigatorio` só nos dois primeiros, `versao_termos` "1.0", mensagem literal.
- **AC-008-46** `verificar-consentimento`: sem registro ⇒ `false`; último registro `true` ⇒ `true`; último `false` ⇒ `false`; tipo desconhecido ⇒ `false`.
- **AC-008-47** `exportar` com dados em todas as tabelas ⇒ 200 com as seis seções nos formatos do §4.13, chaves decifradas (Fernet e texto puro), `logs_acesso[].data_acesso` vindo de `timestamp_acesso`, `assinatura.ativo` derivado do `status`/`data_fim`, `preco_mensal` `0` inteiro para plano zero; sem assinatura ⇒ `null`; `Cache-Control: no-store`; token Fernet corrompido ⇒ 500 sem vazar o token.
- **AC-008-48** `deletar-conta` com `confirmacao` errada, ausente ou corpo inválido ⇒ 400 "Confirmação inválida" e **nada é apagado**.
- **AC-008-49** (044) `deletar-conta` correto, para um usuário completo (contas com fatura, transações, agendamentos, subcategoria e macro próprias, pote, chaves, preferências, uso, logs, consentimentos, assinatura, token do Google) ⇒ 200 com os 13 contadores e `total_registros` = soma; o usuário e todas as suas linhas somem (inclusive por cascata); categorias globais e todos os dados de outro usuário permanecem; usuário inexistente ⇒ 404 sem apagar nada; falha injetada em qualquer delete desfaz tudo; `IUserCacheCleaner.ClearAsync` é chamado uma vez com o id **depois** do commit e uma falha dele não muda a resposta 200.

**Erros e logs**
- **AC-008-50** Em qualquer 500 simulado (repositório que lança exceção com texto sensível), o corpo contém só o texto fixo da rota e o log contém só `SafeError.Describe`, sem chave, `x-api-key`, corpo ou `Authorization`.

**Porta `IApiKeyProvider`**
- **AC-008-60** Provedor inválido ⇒ `InvalidProvider` com a mensagem literal.
- **AC-008-61** Sem preferência ⇒ `NoPreference` (mensagem literal com o provedor).
- **AC-008-62** Preferência própria sem chave ativa (ou chave inativa) ⇒ `OwnKeyMissing`.
- **AC-008-63** Preferência própria com chave ⇒ `Resolved(chave decifrada, propria)`; `ultimo_uso_em` atualizado; uso `propria` +1 no mês de São Paulo (Fernet e texto puro).
- **AC-008-64** Preferência sistema com chave do sistema vazia ⇒ `SystemKeyUnavailable`; nenhum uso registrado.
- **AC-008-65** Preferência sistema sem assinatura ativa ⇒ `Resolved(chave do sistema, sistema)`; uso `sistema` +1 (a linha é criada na 1ª vez e incrementada depois).
- **AC-008-69** (047) Cada resolução grava uma linha em `logacessochaveapi` (`operacao = 'obter_chave'`; sucesso com `tipo_chave` `propria`/`sistema`; falha com `sucesso = false`, `tipo_chave = 'n/a'` ou o escolhido e `mensagem_erro` = nome do motivo); a linha aparece em `GET /logs` e em `exportar`; erro ao gravar não impede o retorno.
- **AC-008-66** Plano com limite 100 e uso do mês 99 ⇒ passa; uso 100 ⇒ `LimitReached` com plano, limite e usado na mensagem; limite `NULL` ⇒ nunca bloqueia; assinatura expirada (`data_fim` passada) ou `status` diferente de `ativo` ⇒ sem limite; uso de outro mês ou outro provedor não conta; uso `propria` **não** conta (046).
- **AC-008-67** Erro de banco ao registrar uso, atualizar `ultimo_uso_em` ou verificar o limite não impede o retorno da chave (fail-open, logado); erro ao ler a preferência ⇒ `NoPreference`.
- **AC-008-68** `Resolved.ToString()` e os logs da porta nunca contêm a chave.

## 7. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `chavesapiusuario` | INSERT, SELECT, UPDATE, DELETE | UNIQUE `(usuario_id, provedor)`; FK `usuarios` CASCADE; índice parcial `idx_chaves_api_ativo` |
| `preferenciaschaveapi` | upsert, SELECT, DELETE | UNIQUE `(usuario_id, provedor)` |
| `rastreamentousoapi` | SELECT, upsert, DELETE | UNIQUE `(usuario_id, provedor, tipo_chave, mes_ano)`; `valor_cobrado` nunca atualizado |
| `logacessochaveapi` | INSERT (porta, 047), SELECT, DELETE | `timestamp_acesso` e `criado_em` coexistem (ambos com default `now()`) |
| `consentimentousuario` | upsert, SELECT, DELETE | UNIQUE `(usuario_id, tipo_consentimento, versao_consentimento)`; `revogado_em` gravado pela revogação (041); `texto_consentimento` não usado |
| `planos` | SELECT | 3 linhas (Bronze 100/50/10, Prata 500/200/50, Ouro ilimitado) |
| `assinaturasusuario` | SELECT, DELETE | 0 linhas; nenhuma rota cria assinatura; FK `plano_id` RESTRICT |
| `usuarios`, `contas`, `faturas`, `transacoes`, `agendamentos`, `potesdegastos`, `subcategoria`, `macrocategoria` | DELETE (só `deletar-conta`, 044) | ordem explícita por causa dos `RESTRICT`; demais tabelas do usuário por cascata |

Tenant: `usuario_id` do caminho/corpo em toda query; não há isolamento por credencial (chave global). Sem migration (RN-008-13 usa colunas que já existem; nenhuma alteração de schema é necessária).

## 8. Fora de escopo

- `/api/user/api-keys*` (feature 007, JWT, camelCase) e a validação de chaves em provedores (desvio 019).
- Criação de assinaturas e cobrança (`valor_cobrado`).
- Separar a chave do JWT (desvio 006, depois da virada).
- Middleware de segurança e bloqueio de IP (009).
- Cliente Redis (010a): aqui só a porta `IUserCacheCleaner` e uma implementação nula; a 010a a implementa. Tokens do Google caem por `CASCADE`.

## 9. Desvios de paridade

| Desvio | Estado | Aplicação |
|---|---|---|
| 005 | aprovado, refinado pelo 052 | §3 |
| 006 | aprovado (checagem de dono, via 045) | §4.3, 4.4, 4.13, RN-008-06 |
| 008 | aprovado | todos os 500 |
| 024 | aprovado | corpo inválido ⇒ 400 da rota |
| 028 | aprovado | colunas reais, RN-008-13 |
| 041 | aprovado | §4.10, AC-008-43 |
| 042 | aprovado | §4.1, AC-008-13 |
| 043 | aprovado | §4.1, 4.5, 4.10, 4.14 |
| 044 | aprovado com condições | §4.14, AC-008-49 |
| 045 | aprovado | §4.3, 4.4, AC-008-17 |
| 046 | aprovado | §5 (limite do plano), AC-008-66 |
| 047 | aprovado | §5 (log de acesso), AC-008-69 |
| 048 | aprovado | §4 (convenções), AC-008-23 |
| 051 | **rejeitado** | §4.13 (sem `?mascarar`) |
| 052 | aprovado | §3, RN-008-14, AC-008-05 |
| 017 / 029 | referência | leitura Fernet/texto puro |

## 10. Perguntas

Todas respondidas (ver "Decisões aplicadas"): Q1 = flag 052; Q2 = `usuario_id` obrigatório (045); Q3 = `<título>. Tente novamente.`; Q4 = UTC; Q5 = Redis apagado via `IUserCacheCleaner`, tokens do Google por `CASCADE`; Q6 = derivação confirmada; Q7 = falha explícita confirmada.
