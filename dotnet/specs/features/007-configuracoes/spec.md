# Spec 007 — Configurações do usuário (`/api/user/*`)

- **Estado:** implementada em 2026-10-05 (aprovada em 2026-10-04; recomendações de foundation/decisoes-onda-2.md; onde a spec diverge, vale esse documento)
- **Onda:** 2
- **Origem (Python):** `app/routes/api.py:2425-3011` (perfil, chaves BYOK, validação, notificações, endereços, `settings` agregado) e o `token_required` de `api.py:34-90`; `app/services/user_address_service.py:22-195` (mesma tabela, via WhatsApp); `app/services/notification_config_service.py:81-137`; `app/routes/api.py:25` (`_fernet`); DDL real em `dotnet/migrations/0000_baseline.sql`
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `IEncryptionService`, `IDbConnectionFactory`, `Envelopes`, `SafeError`), 002 (`PyNumber`, `RawValue`), 008 (tabelas e `ApiProviders`), 010e (`IProviderKeyValidator`)
- **Consumida por:** Angular `/settings` (`core/services/settings.service.ts`); 012 reutiliza `IFavoriteAddressRepository` (§3.8)
- **Desvios aplicáveis (todos aprovados):** 008 (texto fixo nos 500), 019 (já implementado na 010e), 024 (corpo tolerante), 028 (colunas `agenda_diaria_*` inexistentes: a 007 não as usa). Propostas novas: §10 (`P-007-N`).

## Decisões aplicadas (já aprovadas)

| Desvio | Efeito nesta spec |
|---|---|
| 008 | O único 500 que vazava `str(e)` era `PUT /api/user/profile` (`api.py:2514`): passa a texto fixo (§3.2). Os demais 500 do Flask já eram fixos. |
| 019 | `POST /api/user/api-keys/validate` com `weather` valida contra WeatherAPI.com; a lógica está na 010e (`IProviderKeyValidator`), esta feature só a chama (§3.5). |
| 024 | Corpo ausente, inválido ou não-objeto = objeto sem campos (o Flask já fazia `get_json(silent=True) or {}`; a diferença é para corpo JSON **não-objeto** não vazio, que lá dava 500). |
| 028 | `notificationconfigs` não tem `agenda_diaria_ativa/hora`; esta feature só toca `resumo_matinal_*`, `checkin_noturno_*`, `alertas_financeiros_ativos`, `alerta_potes_threshold`. |

## 1. Objetivo

O usuário autenticado vê e altera, em uma tela única do site, o seu perfil, a escolha "chave própria × chave do sistema" para Gemini, clima e rotas (e cadastra a própria chave), o horário das notificações e os endereços favoritos (casa, trabalho, outro). Cada rota vale só para o usuário do token.

## 2. Rotas

| # | Método | Caminho | Auth | Envelope | Seção |
|---|---|---|---|---|---|
| 1 | GET | `/api/user/profile` | JWT (`RequireUser`) | A | 3.1 |
| 2 | PUT | `/api/user/profile` | JWT | A | 3.2 |
| 3 | GET | `/api/user/api-keys` | JWT | A | 3.3 |
| 4 | PUT | `/api/user/api-keys` | JWT | A | 3.4 |
| 5 | POST | `/api/user/api-keys/validate` | JWT | A | 3.5 |
| 6 | GET | `/api/user/notifications` | JWT | A | 3.6 |
| 7 | PUT | `/api/user/notifications` | JWT | A | 3.7 |
| 8 | GET | `/api/user/addresses` | JWT | A | 3.8 |
| 9 | POST | `/api/user/addresses` | JWT | A | 3.9 |
| 10 | DELETE | `/api/user/addresses/<string:label>` | JWT | A | 3.10 |
| 11 | GET | `/api/user/settings` | JWT | A | 3.11 |

Como em todo `/api/*`: sem barra final (404 do Flask, já feito pelo `StrictRoutingMiddleware`); método não permitido dá 405; falha de autenticação: as quatro mensagens 401 do `RequireUser` (spec 001 §4.4) **antes** de qualquer validação. Envelope A: sucesso `{"status":"success","data":...,"message":...}`; erro `{"status":"error","message":"..."}`. `<string:label>` não casa com `/`.

## 3. Contrato por rota

Convenções:
- "Texto" é JSON string. O dono é sempre o `user_id` do token; nenhuma rota lê `usuario_id` do corpo ou da URL.
- Chaves de resposta na ordem escrita aqui (o Flask as ordena alfabeticamente no fio; clientes não dependem; os testes comparam a ordem do .NET).
- Corpo (024): lido com `JsonBody.ReadObjectAsync`; ausente, vazio, inválido ou não-objeto = `{}`. Não exige `Content-Type`. Chaves desconhecidas são ignoradas.
- 500 (desvio 008): `{"status":"error","message":"<texto fixo da rota>"}`; só o log leva `SafeError.Describe`.
- `Erro inesperado` inclui violação de FK/UNIQUE/tamanho do banco e exceção de conversão: o corpo nunca revela a causa.
- Itens marcados **(P-007-N)** têm proposta em §10; sem aprovação vale o texto "sem aprovação".

### 3.1 `GET /api/user/profile`

`SELECT id, nome, email, numero_whatsapp, cidade, estado, fuso_horario, meses_reserva_emergencia FROM usuarios WHERE id = @uid AND ativo = true`.
**200** `{"status":"success","data":{"id":1,"nome":"Rafael","email":null,"numero_whatsapp":"5531...","cidade":"Sao Paulo","estado":"SP","fuso_horario":"America/Sao_Paulo","meses_reserva_emergencia":6}}` (`email`, `cidade`, `estado` e `meses_reserva_emergencia` podem ser `null`; `data` é o registro do banco, sem filtrar).
Sem linha (usuário inexistente ou `ativo = false`): **404** `Usuário não encontrado`. 500: `Erro ao buscar perfil`. *Origem: `api.py:2429-2451`.*

### 3.2 `PUT /api/user/profile`

Entrada: `nome`, `email`, `cidade`, `estado`, `fuso_horario`, `meses_reserva_emergencia`. Só estas seis chaves contam; `id`, `numero_whatsapp`, `senha` etc. são ignoradas (o Angular manda o formulário inteiro).

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | nenhuma das seis chaves presente (corpo vazio/inválido incluído) | 400 `Nenhum campo válido para atualizar` |
| 2 | `meses_reserva_emergencia` presente, **não nulo**, e que o `int()` do Python não converta (RN-007-04) | 400 `Meses de reserva deve ser um número inteiro` |
| 3 | `nome` presente e (nulo, vazio ou menos de 2 caracteres depois do `trim`) | 400 `Nome deve ter pelo menos 2 caracteres` |
| 4 | `estado` presente, **não vazio** e com comprimento ≠ 2 (em pontos de código) | 400 `Estado deve ter 2 caracteres (ex: SP)` |
| 5 | `meses_reserva_emergencia` presente, não nulo, fora de 1 a 36 | 400 `Meses de reserva deve ser entre 1 e 36` |

Antes de qualquer validação (e portanto antes da 4), `email`, `cidade` e `estado` iguais a `""` viram `null` (só a string vazia; `" "` é mantida); por isso `estado: ""` não cai na validação 4. O `nome` é validado depois do `trim` mas **gravado sem `trim`** (` Ana ` fica assim; P-007-3). `meses_reserva_emergencia: null` é aceito e grava `NULL` (a coluna é anulável; o Angular manda `null` quando o campo numérico fica vazio, `+"" ` ⇒ 0 e `NaN` ⇒ `null`). `estado` não é convertido para maiúsculas.
Efeito: `UPDATE usuarios SET <só as colunas enviadas> WHERE id = @uid AND ativo = true RETURNING id, nome, email, numero_whatsapp, cidade, estado, fuso_horario, meses_reserva_emergencia` (SQL montado com fragmentos fixos, nunca com a entrada).
Sucesso **200** `{"status":"success","data":{...igual a 3.1...},"message":"Perfil atualizado com sucesso"}`. Sem linha (usuário inexistente/inativo): **404** `Usuário não encontrado`.
Erros do banco, que hoje viram 500: `fuso_horario` `null` (NOT NULL), texto maior que a coluna (`nome` 255, `email` 255, `cidade` 100, `fuso_horario` 50), e-mail já usado por outro usuário (UNIQUE) ⇒ **500** `Erro ao atualizar perfil` (o Flask devolvia `Erro ao atualizar perfil: <str(e)>`, que contém o valor duplicado, o e-mail de outra pessoa; desvio 008). Tipos JSON inesperados: `nome`, `email`, `cidade`, `fuso_horario` não-texto ⇒ **sem aprovação**, 500 do mesmo texto (o Flask coagia número para texto no banco ou dava 500); `estado` não-texto truthy ⇒ 500 (o Flask: `len(int)` ⇒ `TypeError`). **(P-007-2, P-007-3)**
*Origem: `api.py:2454-2519`.*

### 3.3 `GET /api/user/api-keys`

```sql
SELECT p.provedor, p.usar_chave_propria, CASE WHEN c.id IS NOT NULL THEN true ELSE false END AS has_key
FROM preferenciaschaveapi p
LEFT JOIN chavesapiusuario c ON c.usuario_id = p.usuario_id AND c.provedor = p.provedor AND c.ativo = true
WHERE p.usuario_id = @uid
```
**200** `{"status":"success","data":[{"provedor":"gemini","usar_chave_propria":false,"has_key":false},{"provedor":"weather",...},{"provedor":"openroute",...}]}`: **sempre três itens, nesta ordem fixa** (`gemini`, `weather`, `openroute`). Provedor com linha em `preferenciaschaveapi` leva a linha (`usar_chave_propria` do banco, `has_key` = existe chave **ativa**); provedor sem linha leva `{"provedor":"<p>","usar_chave_propria":false,"has_key":false}`. Nunca devolve a chave (nem mascarada).
Atenção (P-007-6): como a junção parte das **preferências**, uma chave cadastrada pela API `/api-keys/usuario/<id>` (008) sem preferência aparece com `has_key:false`.
500: `Erro ao buscar chaves API`. *Origem: `api.py:2528-2560`.*

### 3.4 `PUT /api/user/api-keys`

Entrada: `type` (ou `provedor`), `useOwnKey` (**camelCase**), `key`. O Angular envia `{type, useOwnKey, key}` e **omite** `key` quando `useOwnKey` é falso.

Leitura (`api.py:2584-2587`): `provider = type` se verdadeiro, senão `provedor` (`""`, `null` e ausente caem no outro); `use_own` = `useOwnKey` (ausente = `false`; **qualquer valor truthy do Python** vale como verdadeiro: `true`, `1`, texto não vazio, lista/objeto não vazio); `raw_key` = `key.strip()` com padrão `""`.

Validações, nesta ordem:

| # | Condição | Resposta |
|---|---|---|
| 1 | `provider` fora de `gemini`, `weather`, `openroute` (comparação exata, minúsculas; qualquer tipo JSON que não seja um desses textos) | 400 `Provedor inválido. Use: gemini, weather, openroute` |
| 2 | `use_own` verdadeiro e `raw_key` vazio | 400 `Chave API obrigatória quando usar_chave_propria=true` |

A ordem dos provedores na mensagem 1 é **fixa** no .NET; o Flask monta o texto iterando um `set` (ordem varia entre processos pela aleatorização de hash). `key` não-texto (`null`, número, lista) ⇒ o Flask dá `AttributeError` no `.strip()` ⇒ 500: **sem aprovação** 500 `Erro ao salvar chave API` (P-007-2). `provedor` lista/objeto (não hasheável) ⇒ o Flask dá 500; o .NET responde 400 da linha 1 (diferença documentada, borda).

Efeito (uma transação, `api.py:2593-2630`):
1. Upsert da preferência: `INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@uid, @prov, @uso) ON CONFLICT (usuario_id, provedor) DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now()` com `@uso` = `use_own` convertido para booleano pela truthiness do Python.
2. Se `use_own` e `raw_key`: cifra `raw_key` (**já sem `trim`**) com `IEncryptionService` (Fernet, `ENCRYPTION_KEY`) e `INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo) VALUES (...) ON CONFLICT (usuario_id, provedor) DO UPDATE SET chave_api_criptografada = EXCLUDED.chave_api_criptografada, ativo = true, atualizado_em = now()` (reativa e troca a chave).
3. Senão, se `use_own` falso: `UPDATE chavesapiusuario SET ativo = false, atualizado_em = now() WHERE usuario_id = @uid AND provedor = @prov` (desativa, sem apagar; não faz nada se não houver chave).
Sucesso **200** `{"status":"success","message":"Chave API salva com sucesso"}`.
Falhas: usuário que não existe mais (FK) ou qualquer erro do banco/criptografia ⇒ **500** `Erro ao salvar chave API`; **tudo desfeito** (uma transação).
O ramo `Criptografia não configurada no servidor` (500 do Flask quando `ENCRYPTION_KEY` falta) **não existe** no .NET (`AppSettings` falha no boot sem a chave). Nota sobre o Flask: nesse ramo ele devolvia o 500 **depois** de gravar a preferência, e a transação era **confirmada** (o `return` dentro de `with conn.begin()` faz commit); conferido no código (`api.py:2593-2603`) e irrelevante aqui.
Diferenças em relação à 008 (`POST /api-keys/usuario/<id>`): aqui é upsert (a 008 dá 409 se já existe), a chave é `trim`ada, não há consulta de consentimento nem de limite de plano, e o `usuario_id` vem do token. *Origem: `api.py:2574-2634`.*

### 3.5 `POST /api/user/api-keys/validate`

Entrada: `type` (ou `provedor`), `key`. Leitura como em 3.4 (`provider = type or provedor`; `raw_key = key.strip()`, padrão `""`).

| # | Condição | Resposta |
|---|---|---|
| 1 | `provider` falso (ausente, nulo, `""`) **ou** `raw_key` vazio | 400 `Campos 'type' e 'key' são obrigatórios` |
| 2 | `provider` diferente de `gemini`, `weather`, `openroute` (qualquer texto ou tipo) | 400 `Provedor não suportado para validação` |
| 3 | provedor externo não respondeu (timeout de 5 s, erro de rede, DNS) | **503** `Não foi possível validar (timeout ou erro de rede)` |

A linha 2 é avaliada **antes** de qualquer chamada de rede (no Flask, o `else` do `if/elif`). `key` não-texto ⇒ **sem aprovação** 500 `Erro ao validar chave API` (P-007-2).
Validação: `IProviderKeyValidator.ValidateAsync(provider, key, ct)` (010e): `Valid` ⇒ `is_valid=true`; `Invalid` ⇒ `false`; `NotSupported` ⇒ linha 2; `ProviderUnavailableException` ⇒ linha 3 (a mensagem da exceção **não** vai ao corpo nem ao log além do `SafeError.Describe`). Regras de "válida" por provedor, URLs e timeouts são da 010e (RN-010-59; `weather` contra WeatherAPI.com, desvio 019).
Sucesso **200** `{"status":"success","data":{"is_valid":true}}`.
A chave **nunca** é gravada, logada nem devolvida; o `GET` do Gemini leva a chave na URL (`?key=`) e **não pode** aparecer em log de requisição HTTP de saída (a 010e remove a query do log). 500: `Erro ao validar chave API`. *Origem: `api.py:2637-2680`.*

### 3.6 `GET /api/user/notifications`

```sql
SELECT resumo_matinal_ativo, resumo_matinal_hora::text AS resumo_matinal_hora,
       checkin_noturno_ativo, checkin_noturno_hora::text AS checkin_noturno_hora,
       alertas_financeiros_ativos, dias_antes_vencimento
FROM notificationconfigs WHERE usuario_id = @uid
```
**200** `{"status":"success","data":{"morningBriefing":{"enabled":true,"time":"07:00"},"eveningCheckIn":{"enabled":true,"time":"19:00"},"financialAlerts":{"enabled":true,"daysBeforeDue":3}}}` (camelCase).
- **Sem linha**: exatamente o exemplo acima (padrões do código, que coincidem com os defaults do banco: `07:00`, `19:00`; a linha **não** é criada pelo GET).
- **Com linha**: `enabled` de cada bloco = coluna (`morningBriefing` ⇐ `resumo_matinal_ativo`, `eveningCheckIn` ⇐ `checkin_noturno_ativo`, `financialAlerts` ⇐ `alertas_financeiros_ativos` com `NULL` ⇒ `false`); `time` = os **5 primeiros caracteres** do texto da hora (`07:00:00` ⇒ `07:00`; `19:30:00.5` ⇒ `19:30`); `daysBeforeDue` = `dias_antes_vencimento` **ou 3 se for 0** (desvio 250: coluna própria, default 3) (zero vira o padrão; o banco guarda 0 como padrão "sempre mostrar").
Desvio 250 (aprovado e implementado em 2026-10-09, reabrindo a P-007-1): `daysBeforeDue` tem coluna própria, `dias_antes_vencimento` (inteiro, `NOT NULL DEFAULT 3`, migration `0070`). O Flask o gravava em `alerta_potes_threshold` (limiar de % do pote: 0 = sempre, 50/70/90), que agora **nunca** é lido nem escrito por esta feature.
500: `Erro ao buscar notificações`. *Origem: `api.py:2683-2727`.*

### 3.7 `PUT /api/user/notifications`

Entrada (camelCase): `morningBriefing{enabled,time}`, `eveningCheckIn{enabled,time}`, `financialAlerts{enabled,daysBeforeDue}`. Ausência de um bloco = bloco `{}`; ausência de campo = padrão (`enabled` `true`, `morningBriefing.time` `"07:00"`, `eveningCheckIn.time` `"19:00"`, `daysBeforeDue` `3`). **Não é parcial**: o `PUT` grava os seis valores (um campo ausente volta ao padrão, não ao valor anterior). O Angular sempre manda os três blocos completos.

Validações, nesta ordem:

| # | Condição | Resposta |
|---|---|---|
| 1 | `eveningCheckIn.time` não é texto no formato `<inteiro>:<inteiro>` (o `split(":")` e `map(int, ...)` do Python: exatamente 2 partes, cada uma aceita pelo `int()`; texto, nulo ou número ⇒ erro) | 400 `Formato de horário inválido (use HH:MM)` |
| 2 | hora (1ª parte) fora de 18 a 23 | 400 `Horário do check-in noturno deve ser entre 18:00 e 23:00` |

Só a **hora** é validada (`18 <= h <= 23`): `23:30` e `18:75` passam na validação; `23:30` é gravado (o banco de produção **não** tem CHECK de horário: `chk_checkin_hora` do DDL antigo não existe no baseline) e `18:75` falha no banco (500). `morningBriefing.time` **não é validado** além do que o PostgreSQL aceitar como `time` (`07:00`, `7:30`, `07:00:15`, `7:00 pm` ...): o valor é enviado como texto e convertido pelo próprio banco (`CAST(@x AS time)`), para aceitar e rejeitar exatamente o mesmo que o Flask. Erro de conversão (`25:00`, `abc`, `null`, número) ⇒ 500 (P-007-3).
Efeito: um `INSERT ... ON CONFLICT (usuario_id) DO UPDATE` (upsert por usuário) gravando `resumo_matinal_ativo`, `resumo_matinal_hora`, `checkin_noturno_ativo`, `checkin_noturno_hora`, `alertas_financeiros_ativos`, `alerta_potes_threshold` (= `daysBeforeDue`, sem validação de faixa: 0, negativos e até o limite do `int4` são gravados; o Angular só deixa 1 a 30) e `updated_at = now()` no conflito. Não toca `alerta_potes_ativo`.
Tipos: `enabled` precisa ser booleano JSON (outro tipo ⇒ 500 **sem aprovação**; o Flask repassava ao banco, que aceita `"yes"` e rejeita `1`); `daysBeforeDue` inteiro JSON ou texto de inteiro aceito pelo `int()` (fração é arredondada pelo banco; `null`, booleano, lista ⇒ 500); bloco que não é objeto (`"morningBriefing": null`, lista, texto) ⇒ 500 (o Flask: `AttributeError`). Tudo isso é **P-007-2** (proposta: 400).
Sucesso **200** `{"status":"success","message":"Notificações atualizadas com sucesso"}`. 500: `Erro ao salvar notificações`. *Origem: `api.py:2730-2792`.*

### 3.8 `GET /api/user/addresses`

`SELECT id, label, endereco_completo AS address, latitude, longitude FROM enderecosfavoritos WHERE usuario_id = @uid ORDER BY label`.
**200** `{"status":"success","data":[{"id":1,"label":"casa","address":"Av Paulista 1000","coordinates":{"lat":-23.56,"lng":-46.65}}]}`. `coordinates` existe **só se `latitude` e `longitude` forem ambos não nulos** (senão a chave é omitida); `lat` e `lng` saem como número JSON. Ordem pelo banco: `casa`, `outro`, `trabalho` (alfabética por bytes; **diferente** da ordem `casa`, `trabalho`, `outro` da mensagem de WhatsApp). Sem endereços: `data: []`. `id` é inteiro.
500: `Erro ao buscar endereços`. *Origem: `api.py:2801-2827`.*

### 3.9 `POST /api/user/addresses`

Entrada: `label`, `address`, `coordinates{lat,lng}` (opcional). O Angular envia só `{label, address}`.
Leitura: `label = (label ou "").strip().lower()`; `address = (address ou "").strip()`; `coordinates = coordinates ou {}`.

| # | Condição | Resposta |
|---|---|---|
| 1 | `label` fora de `casa`, `trabalho`, `outro` (depois de `trim` e minúsculas) | 400 `Label inválido. Use: casa, outro, trabalho` (lista em ordem alfabética, como o `sorted` do Flask) |
| 2 | `address` vazio depois do `trim` | 400 `Endereço é obrigatório` |

`label` ou `address` não-texto truthy (número, lista) ⇒ o Flask dá `AttributeError` ⇒ 500: **sem aprovação** 500 `Erro ao salvar endereço` (P-007-2); `label`/`address` `null`, ausente ou `""` ⇒ validação 1 ou 2 (o `or ""`). `coordinates` que não seja objeto (e truthy) ⇒ 500.
Efeito (upsert por `(usuario_id, label)`): `INSERT INTO enderecosfavoritos (usuario_id, label, endereco_completo, latitude, longitude) VALUES (@uid, @label, @addr, @lat, @lng) ON CONFLICT (usuario_id, label) DO UPDATE SET endereco_completo = EXCLUDED.endereco_completo, latitude = EXCLUDED.latitude, longitude = EXCLUDED.longitude, updated_at = now()`. `lat`/`lng` são os valores de `coordinates` repassados ao banco: ausentes/`null` ⇒ `NULL`; sem validação de faixa (P-007-5); `NUMERIC(10,8)` e `NUMERIC(11,8)` estouram acima de 99,99999999 e 999,99999999 ⇒ 500; texto numérico (`"-23.5"`) é aceito; não numérico ⇒ 500. **Sem `coordinates`, o `UPDATE` apaga as coordenadas que já existiam** (P-007-4).
Sucesso **201** `{"status":"success","message":"Endereço salvo com sucesso"}` (também ao atualizar). 500: `Erro ao salvar endereço`. *Origem: `api.py:2830-2866`.*

### 3.10 `DELETE /api/user/addresses/<label>`

`label_lower = label.lower()` (o parâmetro do caminho, já decodificado).

| # | Condição | Resposta |
|---|---|---|
| 1 | `label_lower` fora de `casa`, `trabalho`, `outro` | 400 `Label inválido. Use: casa, outro, trabalho` |
| 2 | nenhuma linha apagada | 404 `Endereço não encontrado` |

Efeito: `DELETE FROM enderecosfavoritos WHERE usuario_id = @uid AND label = @label`. Sucesso **200** `{"status":"success","message":"Endereço removido com sucesso"}`. 500: `Erro ao remover endereço`. *Origem: `api.py:2869-2898`.*

### 3.11 `GET /api/user/settings`

Uma resposta com o que as rotas 1, 3, 6 e 8 devolvem, lidos com a **mesma** lógica e a mesma conexão:
**200** `{"status":"success","data":{"profile":{...3.1...},"apiKeys":[...3.3...],"notifications":{...3.6...},"addresses":[...3.8...]}}`.
- Usuário inexistente/inativo (perfil sem linha): **404** `Usuário não encontrado` (as outras três leituras nem rodam).
- Qualquer erro: **500** `Erro ao buscar configurações`.
- As quatro leituras compartilham o código dos `GET` individuais (`ProfileReader`, `ApiKeySettingsReader`, `NotificationSettingsReader`, `AddressReader`); um teste garante que `settings` e as quatro rotas devolvem o mesmo JSON para o mesmo usuário (AC-007-70).
*Origem: `api.py:2901-3011`.*

## 4. Regras de negócio

- **RN-007-01** Toda rota vale só para o `user_id` do token; o `usuario_id` nunca vem do cliente. *Origem: `api.py:2429-3011`.*
- **RN-007-02** Provedores válidos: `gemini`, `weather`, `openroute`; comparação exata; a ordem fixa nas listas e mensagens é essa. *Origem: `api.py:2543, 2589, 2670-2674`.*
- **RN-007-03** A chave de API do usuário é guardada cifrada em Fernet (`ENCRYPTION_KEY`) em `chave_api_criptografada`, **sem `trim` além do da própria rota**; nunca em resposta, log ou erro. *Origem: `api.py:2604-2606`; integrations.md §1.*
- **RN-007-04** `meses_reserva_emergencia` é convertido como o `int()` do Python: inteiro JSON; número com parte decimal (truncado: `3.9` ⇒ 3); texto inteiro com espaços e sinal (`" 5 "`, `"+5"`); booleano (`true` ⇒ 1); texto não inteiro (`"5.5"`, `"abc"`) e lista/objeto ⇒ inválido. Reaproveita `PyNumber.TryInt` da 002. *Origem: `api.py:2476-2481`; spec 002 RN-002-09.*
- **RN-007-05** `useOwnKey` verdadeiro **com chave** grava preferência `true` e (re)ativa a chave; `useOwnKey` falso grava preferência `false` e **desativa** (não apaga) a chave; `useOwnKey` verdadeiro **sem chave** é 400 (não grava nada). *Origem: `api.py:2591-2630`.*
- **RN-007-06** `usar_chave_propria` e a chave são gravados na **mesma transação** (tudo ou nada). *Origem: `api.py:2593-2630`.*
- **RN-007-07** `GET` de notificações sem linha devolve os padrões sem criar a linha; `PUT` cria ou atualiza a linha inteira. *Origem: `api.py:2693-2706, 2757-2780`.*
- **RN-007-08** `daysBeforeDue` 0 (ou ausente de linha) é lido como 3. *Origem: `api.py:2720`.*
- **RN-007-09** Hora do check-in noturno: validada só a hora (18 a 23) e o formato `H:M`; minutos não validados. A hora do resumo matinal não é validada pela rota. *Origem: `api.py:2737-2750`.*
- **RN-007-10** Rótulos de endereço: `casa`, `trabalho`, `outro` (também o CHECK do banco); entrada normalizada com `trim` + minúsculas; `address` com `trim`; um endereço por (usuário, rótulo), `POST` é upsert (201 nos dois casos). *Origem: `api.py:2810, 2836-2866`; DDL `enderecosfavoritos_label_check`.*
- **RN-007-11** `coordinates` só sai na resposta se latitude **e** longitude existem; `lat`/`lng` como número. *Origem: `api.py:2814-2821`.*
- **RN-007-12** `settings` reaproveita a mesma lógica dos `GET` individuais. *Origem: `api.py:2901-3011`.*
- **RN-007-13** Mensagens e textos da API em português, exatamente os desta spec (lista em `SettingsMessages`). *Origem: constituição regra 7.*
- **RN-007-14** Fuso `America/Sao_Paulo` e cultura `pt-BR` explícitos: nenhuma data aparece nestas rotas (`updated_at` usa o relógio do banco, como o Flask); nenhuma formatação depende do container. *Origem: constituição regra 8.*
- **RN-007-15** `has_key` considera só chave `ativo = true` (`ativo` `NULL` conta como inativa). *Origem: `api.py:2536-2540`.*

## 5. Critérios de aceite

- **AC-007-01** — **Dado** sem `Authorization` (e com `Bearer` inválido) **Quando** qualquer das 11 rotas **Então** 401 com as mensagens do `RequireUser`, antes de validar corpo.
- **AC-007-02** — **Dado** usuário ativo **Quando** `GET /api/user/profile` **Então** 200 com as 8 chaves na ordem de 3.1 e tipos corretos (`id` inteiro; `email`/`cidade`/`estado` `null` quando nulos).
- **AC-007-03** — **Dado** usuário `ativo = false` ou apagado **Quando** `GET`/`PUT /api/user/profile` e `GET /api/user/settings` **Então** 404 `Usuário não encontrado`.
- **AC-007-10** — **Dado** `PUT /api/user/profile` **Quando** corpo `{}`, `[]`, `"x"`, corpo vazio ou só chaves desconhecidas **Então** 400 `Nenhum campo válido para atualizar`.
- **AC-007-11** — **Dado** `{"nome":"A"}`, `{"nome":""}`, `{"nome":null}`, `{"nome":"  A "}` **Então** 400 `Nome deve ter pelo menos 2 caracteres`; `{"nome":"Ana"}` ⇒ 200 e `data.nome:"Ana"`; `{"nome":" Ana "}` ⇒ gravado como ` Ana ` (sem `trim`; P-007-3).
- **AC-007-12** — **Dado** `{"estado":"SPX"}` ou `{"estado":"S"}` **Então** 400 `Estado deve ter 2 caracteres (ex: SP)`; `{"estado":""}` ⇒ 200 e `estado:null`; `{"estado":"sp"}` ⇒ gravado `sp`.
- **AC-007-13** — **Dado** `meses_reserva_emergencia` `"x"`, `"5.5"`, `[]` **Então** 400 `Meses de reserva deve ser um número inteiro`; `0`, `37`, `"40"` ⇒ 400 `Meses de reserva deve ser entre 1 e 36`; `"12"`, `" 12 "`, `12.9`, `true` ⇒ 200 com 12, 12, 12, 1; `null` ⇒ 200 e `null` gravado.
- **AC-007-14** — **Dado** `{"nome":"A","meses_reserva_emergencia":"x"}` **Então** a mensagem de `meses` (conversão vem antes do nome); `{"nome":"A","estado":"S"}` ⇒ mensagem do nome (nome antes de estado).
- **AC-007-15** — **Dado** `{"email":""}` e `{"cidade":""}` **Então** viram `null`; `{"cidade":" "}` fica `" "`.
- **AC-007-16** — **Dado** o corpo completo do Angular (inclui `id`, `numero_whatsapp`, `fuso_horario`) **Então** 200 e `numero_whatsapp`/`id` inalterados no banco.
- **AC-007-17** — **Dado** e-mail já usado por outro usuário, `fuso_horario: null`, `nome` com 300 caracteres **Então** 500 `Erro ao atualizar perfil` (sem `str(e)`, sem o e-mail alheio no corpo nem no log); nada gravado (P-007-3 propõe 409/400).
- **AC-007-20** — **Dado** usuário sem preferências **Quando** `GET /api/user/api-keys` **Então** três itens `gemini`, `weather`, `openroute`, todos `{"usar_chave_propria":false,"has_key":false}`.
- **AC-007-21** — **Dado** preferência `weather` própria com chave ativa, `gemini` própria com chave **desativada**, `openroute` sem linha **Então** `weather.has_key:true`, `gemini.has_key:false`, `openroute` padrão; a ordem é fixa e nenhuma chave aparece no corpo.
- **AC-007-22** — **Dado** `PUT /api/user/api-keys` `{"type":"gemini","useOwnKey":true,"key":"  AIza-x  "}` **Então** 200; `preferenciaschaveapi.usar_chave_propria=true`; `chavesapiusuario` com a chave **cifrada** (decifra para `AIza-x`, sem os espaços), `ativo=true`; repetir com outra chave troca a mesma linha (sem 409).
- **AC-007-23** — **Dado** chave existente **Quando** `{"type":"gemini","useOwnKey":false}` **Então** 200; preferência `false`; chave `ativo=false` e o valor cifrado preservado; sem chave existente, só a preferência.
- **AC-007-24** — **Dado** `{"type":"gemini","useOwnKey":true}` (sem chave ou `"key":"   "`) **Então** 400 `Chave API obrigatória quando usar_chave_propria=true` e **nada** gravado (nem preferência).
- **AC-007-25** — **Dado** `type` `"openai"`, ausente, `null`, `5`, `"GEMINI"` **Então** 400 `Provedor inválido. Use: gemini, weather, openroute`; `{"provedor":"weather","useOwnKey":false}` (sem `type`) é aceito; `type` `""` com `provedor` válido usa o `provedor`.
- **AC-007-26** — **Dado** `useOwnKey` ausente **Então** tratado como falso; `useOwnKey` `"false"` (texto) é tratado como verdadeiro e exige chave (truthiness do Python; P-007-2).
- **AC-007-27** — **Dado** falha injetada no segundo `INSERT` (chave) **Então** 500 `Erro ao salvar chave API` e a preferência **também** não foi gravada (uma transação; RN-007-06).
- **AC-007-28** — **Dado** `key: null` (ou número) **Então** 500 `Erro ao salvar chave API` (sem aprovação da P-007-2).
- **AC-007-30** — **Dado** `POST /api/user/api-keys/validate` com `{"type":"gemini","key":"x"}` e validador `Valid`/`Invalid` **Então** 200 `{"status":"success","data":{"is_valid":true|false}}`.
- **AC-007-31** — **Dado** `type` ou `key` ausentes, vazios ou só espaços **Então** 400 `Campos 'type' e 'key' são obrigatórios`; `type:"openai"` ⇒ 400 `Provedor não suportado para validação` **sem** chamar o validador de rede.
- **AC-007-32** — **Dado** `ProviderUnavailableException` **Então** 503 `Não foi possível validar (timeout ou erro de rede)`, corpo e log sem a mensagem da exceção nem a chave.
- **AC-007-33** — **Dado** chave sentinela **Quando** qualquer resultado **Então** a chave não aparece no corpo, nos logs capturados nem no banco (a rota não grava).
- **AC-007-40** — **Dado** usuário sem linha em `notificationconfigs` **Quando** `GET /api/user/notifications` **Então** os padrões de 3.6 e **nenhuma** linha criada.
- **AC-007-41** — **Dado** linha com `resumo_matinal_hora = 08:30`, `checkin_noturno_hora = 21:15`, `alertas_financeiros_ativos = NULL`, `alerta_potes_threshold = 0` **Então** `time` `"08:30"` e `"21:15"`, `financialAlerts.enabled:false`, `daysBeforeDue:3`; com `alerta_potes_threshold = 7` ⇒ `7`.
- **AC-007-42** — **Dado** `PUT /api/user/notifications` com os três blocos do Angular **Então** 200 e a linha tem os seis valores; `GET` devolve o mesmo; repetir não cria segunda linha (UNIQUE) e atualiza `updated_at`.
- **AC-007-43** — **Dado** `eveningCheckIn.time` `"17:59"`, `"24:00"`, `"7:00"` **Então** 400 `Horário do check-in noturno deve ser entre 18:00 e 23:00`; `"abc"`, `"19"`, `"19:00:00"`, `null`, `1900` ⇒ 400 `Formato de horário inválido (use HH:MM)`; `"18:00"` e `"23:30"` passam (gravados).
- **AC-007-44** — **Dado** corpo `{}` **Então** 200 e grava os padrões (`true`/`07:00`, `true`/`19:00`, `true`/`3`): não preserva o que havia (PUT total).
- **AC-007-45** — **Dado** `morningBriefing.time` `"25:00"`, `"abc"`, `null` ou `"18:75"` no check-in **Então** 500 `Erro ao salvar notificações`, nada gravado; `"7:30"` e `"07:00:15"` são aceitos como o PostgreSQL aceita.
- **AC-007-46** — **Dado** `financialAlerts.daysBeforeDue` `0`, `-5`, `45` **Então** gravados como estão (sem faixa) em `dias_antes_vencimento` e o `GET` mostra `3`, `-5`, `45`.
- **AC-007-48** (desvio 250) — **Dado** linha com `alerta_potes_threshold = 70` **Quando** `PUT` com `daysBeforeDue: 7` **Então** `dias_antes_vencimento = 7` e `alerta_potes_threshold` continua 70; **Dado** linha antiga só com `alerta_potes_threshold = 1` **Então** o `GET` mostra `daysBeforeDue: 3`; a migration `0070` é idempotente, dá 3 às linhas existentes e não copia o limiar.
- **AC-007-47** — **Dado** `enabled: "x"`, bloco `"financialAlerts": null`, `daysBeforeDue: null` **Então** 500 `Erro ao salvar notificações` (sem aprovação da P-007-2).
- **AC-007-50** — **Dado** dois usuários com endereços **Quando** `GET /api/user/addresses` **Então** só os do token, ordenados `casa`, `outro`, `trabalho`; `coordinates` só quando lat e lng existem; sem endereços `data:[]`.
- **AC-007-51** — **Dado** `POST /api/user/addresses` `{"label":" CASA ","address":" Rua A, 10 "}` **Então** 201, linha com `label:"casa"` e `endereco_completo:"Rua A, 10"`; repetir com outro texto atualiza a mesma linha (201, sem duplicar).
- **AC-007-52** — **Dado** `label` `"escritorio"`, ausente, `""`, `null` **Então** 400 `Label inválido. Use: casa, outro, trabalho`; `label` válido e `address` ausente/vazio/`"  "` ⇒ 400 `Endereço é obrigatório` (rótulo antes de endereço).
- **AC-007-53** — **Dado** `coordinates:{"lat":-23.5,"lng":-46.6}` **Então** gravadas; `GET` devolve `coordinates` com os mesmos números; só `lat` ⇒ gravada só a latitude e o `GET` **omite** `coordinates`; sem `coordinates` num endereço que já tinha ⇒ coordenadas viram `NULL` (P-007-4).
- **AC-007-54** — **Dado** `coordinates:{"lat":123456,"lng":0}` ou `lat:"abc"` **Então** 500 `Erro ao salvar endereço` (sem aprovação da P-007-5; o banco rejeita o estouro).
- **AC-007-55** — **Dado** `DELETE /api/user/addresses/Casa` **Então** 200 e a linha `casa` do usuário some; de outro usuário permanece; repetir ⇒ 404 `Endereço não encontrado`; `DELETE /api/user/addresses/xyz` ⇒ 400 `Label inválido. Use: casa, outro, trabalho`; `PUT /api/user/addresses/casa` ⇒ 405.
- **AC-007-60** — **Dado** `GET /api/user/settings` **Então** `data` com `profile`, `apiKeys` (3 itens), `notifications`, `addresses` e **cada bloco igual** ao da rota individual (AC-007-70).
- **AC-007-61** — **Dado** usuário com preferências, notificações e endereços **Então** os quatro blocos populados; sem linhas nas três tabelas ⇒ `apiKeys` padrão, `notifications` padrão, `addresses:[]`.
- **AC-007-70** — **Dado** o mesmo usuário **Quando** se comparam `settings.profile`/`apiKeys`/`notifications`/`addresses` com `GET /profile`, `/api-keys`, `/notifications`, `/addresses` **Então** o JSON é idêntico (teste de paridade interna).
- **AC-007-80** — **Dado** erro inesperado em qualquer rota (falha injetada com mensagem sentinela) **Então** 500 com o texto fixo da rota; corpo e log sem a sentinela.
- **AC-007-81** — **Dado** dois usuários **Quando** qualquer escrita (PUT/POST/DELETE) **Então** dados do outro permanecem intactos (isolamento de tenant).
- **AC-007-82** — **Dado** `GET /api/user/profile/` (barra final) ou `DELETE /api/user/addresses/` **Então** 404 HTML do Flask.

## 6. Tabelas e dados tocados

| Tabela | Leitura | Escrita | Constraints / observações |
|---|---|---|---|
| `usuarios` | perfil | `UPDATE` das 6 colunas permitidas | `ativo` NOT NULL; `email` UNIQUE; `nome` varchar(255), `email` varchar(255), `cidade` varchar(100), `estado` varchar(2), `fuso_horario` varchar(50) NOT NULL (padrão `America/Sao_Paulo`), `meses_reserva_emergencia` integer NULL (padrão 6) |
| `notificationconfigs` | `GET` | upsert por `usuario_id` | UNIQUE (`usuario_id`), FK `ON DELETE CASCADE`; `resumo_matinal_hora` time (padrão 07:00), `checkin_noturno_hora` time (padrão 19:00), **sem CHECK de horário**; `alertas_financeiros_ativos` anulável; `alerta_potes_threshold` integer NOT NULL (padrão 0); **sem** `agenda_diaria_*` (desvio 028) |
| `enderecosfavoritos` | `GET` | upsert/`DELETE` | UNIQUE (`usuario_id`, `label`), CHECK `label IN ('casa','trabalho','outro')`, `latitude` numeric(10,8), `longitude` numeric(11,8), FK `ON DELETE CASCADE` |
| `preferenciaschaveapi` | `GET` | upsert | UNIQUE (`usuario_id`, `provedor`) |
| `chavesapiusuario` | `GET` (`has_key`) | upsert / desativação | UNIQUE (`usuario_id`, `provedor`); `chave_api_criptografada` TEXT; `ativo` anulável |

Tenant: toda leitura e escrita filtra por `usuario_id = <user_id do token>`. **Migration `0070_notificationconfigs_dias_antes_vencimento`** (desvio 250): `ALTER TABLE notificationconfigs ADD COLUMN IF NOT EXISTS dias_antes_vencimento integer NOT NULL DEFAULT 3`, idempotente; o valor atual de `alerta_potes_threshold` **não** é copiado (em produção é 1, lixo do bug); linhas existentes ficam em 3.

## 7. O que o Angular espera × o que o Flask devolve

Fonte: `meusecretariofront/src/app/core/services/settings.service.ts`, `core/models/settings.model.ts`, `features/settings/settings.component.{ts,html}`.

- O Angular usa **só** `GET /profile`, `PUT /profile`, `GET /settings`, `PUT /api-keys`, `POST /api-keys/validate`, `PUT /notifications`, `POST /addresses`, `DELETE /addresses/<label>`. **Não chama** `GET /api-keys`, `GET /notifications` e `GET /addresses` (contrato preservado, sem cliente conhecido).
- **Mensagens de erro do servidor nunca chegam ao usuário:** em 4xx/5xx o `HttpClient` lança e o `catchError` do serviço devolve `err.message` (texto genérico do `HttpErrorResponse`), não o `message` do corpo. O que o usuário vê são os toasts locais ("Erro ao salvar perfil"...). As mensagens desta spec valem para outros clientes e para o log do navegador.
- `validateApiKey` transforma **qualquer** falha (inclusive o 503) em `false` ⇒ toast "Chave inválida ou sem permissão"; um timeout do provedor é indistinguível de chave errada. Divergência do Angular, não do backend.
- O Angular envia `key` só quando `useOwnKey`; manda o formulário de perfil inteiro (inclui `id` e `numero_whatsapp`, ignorados); `meses_reserva_emergencia` por `+$event` (campo vazio ⇒ `0` ⇒ 400 de faixa; `NaN` ⇒ `null`).
- `saveAddress` envia `{label, address}` **sem `coordinates`** ⇒ toda edição pela tela apaga as coordenadas guardadas (inclusive as geocodificadas pelo WhatsApp). Ver P-007-4.
- `daysBeforeDue` (campo 1 a 30 na tela) vai para `dias_antes_vencimento` (desvio 250); a hora do check-in tem `min="18:00" max="23:00"` só no HTML.
- O modelo do Angular lê `k.provedor ?? k.type` e `k.usar_chave_propria ?? k.useOwnKey`: a resposta de `settings` usa `provedor`/`usar_chave_propria`/`has_key` (snake_case) e o `PUT` usa camelCase; o Angular já faz esse mapeamento (`_mapServerSettings`); **não** padronizar.
- Em `saveApiKey` o Angular só marca `hasKey` localmente, não reconsulta; depois de um erro 500 a tela fica inconsistente com o servidor até recarregar.

## 8. Decisões de desenho aplicadas

- `PUT /api/user/api-keys` roda numa **transação própria** que grava preferência e chave. Reutilizar os repositórios da 008 (`IPreferenceRepository`, `IApiKeyRepository`) exigiria compartilhar a transação entre dois repositórios e, mesmo assim, o upsert de chave da 007 (`ON CONFLICT DO UPDATE ... ativo = true`) não existe na 008 (lá é `INSERT` simples + `UPDATE` por id). Por isso a 007 tem o seu repositório (`ApiKeySettingsRepository`), com o SQL do §3.4; o **único** SQL repetido é o upsert de `preferenciaschaveapi` (uma instrução). Ver Q-007-1 se preferirem estender a 008.
- `IFavoriteAddressRepository` (Application/Settings) é a porta de `enderecosfavoritos` e nasce nesta feature para a 012 reaproveitar o `UserAddressService` (WhatsApp; geocodifica e grava com o **mesmo** upsert, ordena `casa`, `trabalho`, `outro` em código).
- Leituras compartilhadas entre as rotas individuais e `settings`: um leitor por bloco (§3.11), evitando as duas cópias de SQL e de montagem que o Flask tem.

## 9. Fora de escopo

- Geocodificar o endereço na API REST (o Flask não faz; só o WhatsApp faz, 012).
- Consentimento LGPD e limite de plano na gravação da chave (a rota do Angular nunca os consultou).
- Trocar senha, número de WhatsApp ou e-mail com confirmação (não existe no Flask).
- Criar a linha de `notificationconfigs` no `GET` (Flask não cria).
- Consumo de `alerta_potes_threshold` pelos alertas de pote (013/012).

## 10. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask; onde a reprodução exata não é possível (o Flask depende da coerção do banco), o texto de 3.x diz o que acontece.

| Id | Atual (arquivo:linha) | Proposta | Impacto | Recomendação |
|---|---|---|---|---|
| **P-007-1** (reaberta: desvio 250, implementado) | `daysBeforeDue` ("dias antes do vencimento", 1 a 30 na tela) é gravado em `alerta_potes_threshold` e lido com `or 3` (`api.py:2720, 2773`); a coluna é o limiar de % do pote (0 = sempre, 50/70/90; `feature_migrations.py:284`). Nenhum consumidor da coluna foi achado hoje (`grep` em `app/`), e os alertas financeiros "sempre verificam hoje e amanhã" (`notification_config_service.py:166-169`): o campo da tela não faz nada | manter como está; **não** criar coluna nova enquanto a 013 não decidir se haverá janela configurável | nenhum | **Rejeitar** a mudança (manter paridade); registrar o risco: quando os alertas de pote (013/012) passarem a ler a coluna, valores 1 a 30 virarão limiares de % |
| **P-007-2** | Tipos JSON inesperados dão 500 (ou coerção do banco): `key` não-texto (`api.py:2587`), `label`/`address` não-texto (`:2834-2835`), bloco de notificação não-objeto (`:2731-2733`), `enabled`/`daysBeforeDue` fora do tipo, `coordinates` não-objeto (`:2837`), campos de perfil não-texto (`:2487, 2496`) | 400 no lugar do 500: com a mensagem **da própria rota** quando existe uma que descreve o erro (`key` não-texto ⇒ `Chave API obrigatória quando usar_chave_propria=true` no PUT e `Campos 'type' e 'key' são obrigatórios` no validate; `label` não-texto ⇒ `Label inválido. Use: casa, outro, trabalho`; `address` não-texto ⇒ `Endereço é obrigatório`; `eveningCheckIn.time` já tem 400) e com a mensagem **nova** `Dados inválidos` para os demais (bloco de notificação não-objeto, `enabled`/`daysBeforeDue` de tipo errado, `coordinates` não-objeto, campos de perfil não-texto, `useOwnKey` presente que não seja booleano nem nulo; ausente e `null` continuam valendo falso) | clientes que mandavam tipo errado recebem 400 (nenhum cliente conhecido; o Angular manda os tipos certos); `useOwnKey: "false"` deixa de ser tratado como verdadeiro | Aprovar |
| **P-007-3** | Perfil: e-mail repetido e textos longos viram 500 e o `str(e)` do Flask incluía o valor duplicado (`api.py:2514`); `nome` gravado sem `trim` (`:2486-2489, 2503`); `fuso_horario` aceita qualquer texto (nenhum consumidor), `null` dá 500; `morningBriefing.time` sem validação (500 para `25:00`) e minutos do check-in sem validação | (a) e-mail já existente (outro usuário) ⇒ **409** `E-mail já cadastrado`; (b) textos acima dos limites das colunas ⇒ 400 `<Campo> deve ter no máximo <n> caracteres` (nome 255, email 255, cidade 100, fuso 50); (c) `fuso_horario` precisa ser um id de fuso conhecido (`TimeZoneInfo.FindSystemTimeZoneById`) ⇒ 400 `Fuso horário inválido`, e `null` ⇒ o mesmo 400; (d) `nome` gravado com `trim`; (e) `morningBriefing.time` e minutos do check-in validados (`HH:MM`, 00 a 59) com `Formato de horário inválido (use HH:MM)` | muda 500 por 400/409; e-mail com espaços ou fuso inexistente passam a ser recusados | Aprovar (a) e (c) a (e); (b) opcional |
| **P-007-4** | `POST /api/user/addresses` sem `coordinates` grava `NULL` em `latitude`/`longitude` (`api.py:2840-2858`); o Angular nunca manda coordenadas ⇒ toda edição pelo site apaga as coordenadas geocodificadas pelo WhatsApp (`user_address_service.py:60-72`) | se a chave `coordinates` **não vier** (ou vier vazia): manter as coordenadas já gravadas **quando o texto do endereço não mudou**, e gravar `NULL` quando mudou (a coordenada deixaria de valer); com `coordinates` presente, comporta-se como hoje | só afeta quem edita pelo site | Aprovar |
| **P-007-5** | `lat`/`lng` sem faixa: aceita 99, rejeita 100 (estouro do `numeric`) com 500 (`api.py:2840-2858`) | 400 `Coordenadas inválidas` se `lat` fora de [-90, 90], `lng` fora de [-180, 180] ou não numéricos | nenhum cliente envia | Aprovar |
| **P-007-6** | `has_key` só existe para provedor com linha de **preferência** (`api.py:2531-2540`); a chave cadastrada pela API da 008 sem preferência aparece `false`. Em produção há 3 chaves em `chavesapiusuario` | a consulta parte de uma lista fixa dos três provedores e faz `LEFT JOIN` nas duas tabelas (`has_key` reflete a chave ativa mesmo sem preferência; `usar_chave_propria` segue `false` sem preferência) | só muda o caso "chave sem preferência" | Aprovar |

## 11. Perguntas em aberto

- **Q-007-1** Aceita a 007 ter repositório próprio para o `PUT /api-keys` (§8), ou prefere estender os repositórios da 008 com um método de upsert e uma sobrecarga com transação? A segunda muda arquivos da 008 (já implementada).
- **Q-007-2** Há usuários `ativo = false` em produção com JWT ainda válido? As rotas de perfil respondem 404 para eles, mas as demais (chaves, notificações, endereços) respondem normalmente (o Flask não checa `ativo` nelas). Reproduzido; confirmar que é aceitável.
- **Q-007-3** `GET /api/user/api-keys`, `GET /api/user/notifications` e `GET /api/user/addresses` não têm cliente conhecido; manter as três (contrato) ou registrar para remoção futura?
- **Q-007-4** O desvio 028 e o roadmap citam `agenda_diaria_*`; confirmar que o Angular não envia nem espera esses campos em `notifications` (conferido: não envia).


## 12. Registro da implementação (2026-10-05)

- P-007-1 **reaberta e aprovada como desvio 250** (2026-10-09, após a consulta 10 do `06-conferencia-onda-2.sql`): `daysBeforeDue` em coluna própria, `alerta_potes_threshold` intocado. Rafael deve acertar `alerta_potes_threshold` direto no banco se quiser outro valor que não 1.
- Desvios 084 a 088 implementados; onde §3 diz sem aprovação/500, vale o 400 do desvio 084 (Dados inválidos ou a mensagem da própria rota).
- Desvio 085: ordem após as 5 validações da §3.2: tipos, limites (nome 255, e-mail 255, cidade 100), fuso IANA (Fuso horário inválido, 
ull incluído); 409 E-mail já cadastrado; resumo matinal e minutos do check-in exigem HH:MM (hora 0-23, minuto 0-59, senão Formato de horário inválido (use HH:MM)); horas gravadas em forma canônica HH:MM.
- Desvio 086: IFavoriteAddressRepository.UpsertAsync com coordinates nulo preserva lat/lng se o texto não mudou; coordinates vazio ({}, 
ull, []) conta como não enviado. A 012 deve tratar lat/lng nulos.
