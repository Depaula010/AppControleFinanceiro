# Spec 001 — Auth (cadastro, login, verificação de token)

- **Estado:** em revisão
- **Onda:** 1 (a parte compartilhada, **001a**, precisa entrar antes das demais features)
- **Origem (Python):** `app/routes/auth.py:20-352`; filtro `token_required` em `app/routes/api.py:34-90`; `app/services/encryption_service.py`
- **Depende de:** esqueleto da Fase 1
- **Desvios aplicáveis:** 017 (aprovado), 024 (aprovado), 008 (aprovado), **031 (proposto, novo)**

## 1. Objetivo

O usuário do site cria conta, faz login com WhatsApp e senha e recebe um JWT de 24 h; o front confere o token. Esta feature também entrega as peças que **todas** as rotas `/api/*` reutilizam: emissão e validação do JWT, filtro de autenticação, hash de senha compatível com Werkzeug e criptografia Fernet.

## 2. Divisão em duas entregas

| Entrega | Conteúdo | Quem depende |
|---|---|---|
| **001a — blocos compartilhados** | `JwtTokenService`, filtro `RequireUser` (equivale ao `token_required`), `WerkzeugPasswordHasher`, `FernetEncryptionService`, `IDbConnectionFactory`, suporte do `Envelopes.A` a campos na raiz | Features 002 a 014 (qualquer rota com JWT) |
| **001b — rotas** | `/auth/register`, `/auth/login`, `/auth/verify` | Angular |

A 001a entra em `main` **antes** de a onda 1 abrir em paralelo.

## 3. Rotas

| Método | Caminho | Auth | Envelope |
|---|---|---|---|
| POST | `/auth/register` | — | A, com campos na raiz |
| POST | `/auth/login` | — | A, com campos na raiz |
| POST | `/auth/verify` | — | A, com campos na raiz |

Observação de contrato: nestas três rotas os campos de sucesso ficam **na raiz** (`token`, `user`, `user_id`, `valid`), e não dentro de `data`. `api-contract.md` §1 foi ajustado.

## 4. Contrato por rota

### 4.1 `POST /auth/register`

Entrada (JSON): `nome`, `whatsapp`, `password`, `dia_vencimento`, `dia_fechamento`.

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, inválido ou não-objeto (desvio 024); ou `nome`, `whatsapp` ou `password` vazios depois de `trim` (valor não-texto conta como vazio) | 400 `"Campos obrigatórios: nome, whatsapp, password"` |
| 2 | `password` (já com `trim`) com menos de 6 caracteres | 400 `"Senha deve ter no mínimo 6 caracteres"` |
| 3 | `dia_vencimento` ou `dia_fechamento` ausente ou `null` | 400 `"Campos obrigatórios: dia_vencimento, dia_fechamento"` |
| 4 | `dia_vencimento` fora de 1..31 | 400 `"Dias inválidos: dia_vencimento deve estar entre 1 e 31"` |
| 5 | `dia_fechamento` fora de 1..31 | 400 `"Dias inválidos: dia_fechamento deve estar entre 1 e 31"` |
| 6 | um dos dias não é inteiro (desvio 031) | 400 `"Dias inválidos: dia_vencimento e dia_fechamento devem ser números inteiros"` |
| 7 | `whatsapp` sem nenhum dígito (desvio 031) | 400 `"Campos obrigatórios: nome, whatsapp, password"` |
| 8 | já existe usuário com o mesmo número | 409 `"WhatsApp já cadastrado"` |

Conversão dos dias, como o `int()` do Python: número JSON inteiro; número com parte decimal é truncado (`10.9` → `10`); texto com inteiro, com ou sem espaços (`"10"`), é aceito. Qualquer outra coisa cai na linha 6. A checagem de faixa vem depois da conversão (4 e 5 são avaliadas antes de 6 só quando a conversão tem sucesso).

Efeito (uma transação; tudo ou nada):
1. `usuarios`: `nome` (com `trim`), `numero_whatsapp` (só dígitos), `senha_hash` (pbkdf2:sha256 do Werkzeug, senha com `trim`), `api_key_automate` (token de 32 bytes em base64 url-safe, **cifrado com Fernet**; desvio 017).
2. `contas`, três linhas:
   - `Carteira`, tipo `Dinheiro`, sem dias;
   - `Conta Corrente`, tipo `Conta Corrente`, sem dias;
   - `Cartão de Crédito`, tipo `Cartão de Crédito`, `dia_vencimento` e `dia_fechamento` informados.

Sucesso: **201** `{"status":"success","message":"Usuário cadastrado com sucesso","user_id":<int>}`.

Erro inesperado: **500** `{"status":"error","message":"Erro ao cadastrar usuário. Tente novamente."}`. Violação de unicidade no número, vinda de cadastro concorrente (desvio 031): 409 como na linha 8.

### 4.2 `POST /auth/login`

Entrada: `whatsapp`, `password`.

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo inválido (024), ou `whatsapp`/`password` vazios depois de `trim` | 400 `"Campos obrigatórios: whatsapp, password"` |
| 2 | nenhum usuário com o número (só dígitos) | 404 `"WhatsApp não cadastrado"` |
| 3 | usuário sem `senha_hash` (nulo ou vazio) | 401 `"Usuário sem senha cadastrada. Cadastre-se pelo site."` |
| 4 | senha (com `trim`) não confere com o hash, ou o hash tem formato não reconhecido | 401 `"Senha incorreta"` |

Sucesso: **200**
```json
{"status":"success","token":"<jwt>","user":{"id":1,"nome":"...","whatsapp":"<numero_whatsapp do banco>"}}
```
Erro inesperado: **500** `"Erro ao fazer login. Tente novamente."`

Não se grava `ultimo_acesso` (o Flask não grava). Não se verifica `usuarios.ativo` (o Flask não verifica).

### 4.3 `POST /auth/verify`

Entrada: `token`.

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo inválido (024) ou `token` vazio depois de `trim` | 400 `"Token não fornecido"` |
| 2 | assinatura inválida, expirado ou malformado | 401 `"Token inválido ou expirado"` |
| 3 | token válido sem o claim `user_id` | 500 `"Erro ao verificar token"` |

Sucesso: **200** `{"status":"success","valid":true,"user_id":<int>}`.

### 4.4 Filtro `RequireUser` (equivale ao `token_required`)

Aplicado às rotas `/api/*` com JWT. Lê o header `Authorization`:

| # | Condição | Resposta |
|---|---|---|
| 1 | header ausente ou vazio | 401 `"Token de autenticação não fornecido"` |
| 2 | não tem exatamente duas partes separadas por espaço, ou a primeira não é `Bearer` (sem diferenciar maiúsculas) | 401 `"Formato de autenticação inválido. Use: Bearer <token>"` |
| 3 | token inválido, expirado ou malformado | 401 `"Token inválido ou expirado"` |
| 4 | claim `user_id` ausente, nulo ou zero | 401 `"Token não contém user_id"` |

Todas em envelope A (`{"status":"error","message":...}`). Em sucesso o `user_id` fica disponível ao endpoint (`ICurrentUser`).

## 5. Regras de negócio

- **RN-001-01** O JWT é HS256 assinado com `API_SECRET_KEY`. Claims: `user_id` (inteiro), `exp` (agora UTC + 24 h) e `iat`, ambos em segundos desde 1970. Sem `iss`, `aud`, `sub` nem `jti`. *Origem: `auth.py:20-39`.*
- **RN-001-02** Validação do token: assinatura, algoritmo HS256 e `exp` (sem tolerância de relógio). O claim lido é `user_id`, **não** `sub` (por isso `MapInboundClaims = false`). Token sem `exp` é rejeitado (o PyJWT aceitaria; os tokens do Flask sempre têm `exp`). *Origem: `auth.py:42-58`.*
- **RN-001-03** Compatibilidade entre implementações: token emitido pelo Flask é aceito pelo .NET e o inverso. Ninguém é deslogado na virada.
- **RN-001-04** Senha: `trim` antes de gerar e de verificar o hash. Hash novo: `pbkdf2:sha256:<iterações>$<salt>$<hex>` (ver `integrations.md` §1; salt de 16 caracteres alfanuméricos usado como bytes UTF-8). Verificação aceita `pbkdf2:sha256` (iterações lidas da própria string) e `scrypt:N:r:p` (salt UTF-8, `dklen` 64). Comparação em tempo constante. *Origem: `auth.py:91, 144, 236, 271`.*
- **RN-001-05** WhatsApp: guardado e buscado só com dígitos (`"+55 (11) 99999-9999"` → `5511999999999`). *Origem: `auth.py:130, 245`.*
- **RN-001-06** `api_key_automate`: `secrets.token_urlsafe(32)` equivalente (32 bytes aleatórios, base64 url-safe sem padding, 43 caracteres), cifrada com Fernet (`ENCRYPTION_KEY`). A coluna `varchar(100)` não comporta o token cifrado: exige a migration do desvio 017 (alargar para `varchar(255)`). A leitura continua aceitando chaves em texto puro (a do usuário existente). *Origem: `auth.py:147-152`; desvio 017.*
- **RN-001-07** As três contas padrão são criadas na mesma transação do usuário. Se qualquer insert falhar, nada é gravado. *Origem: `auth.py:155-185`.*
- **RN-001-08** Logs nunca contêm senha, hash, token, `api_key_automate` nem o número de WhatsApp completo; registram só o `user_id`. (O Flask imprime nome e WhatsApp.) *Desvio 008.*

## 6. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","001-NN")]`).

**Registro**
- **AC-001-01** Dado um corpo válido, quando `POST /auth/register`, então 201 com `user_id`, 1 usuário e 3 contas criadas (nomes, tipos e dias como em §4.1).
- **AC-001-02** Dado WhatsApp formatado `"+55 (11) 99999-9999"`, então o banco guarda `5511999999999`.
- **AC-001-03** Dado `nome` vazio, ou só espaços, ou `whatsapp` ausente, ou `password` ausente, então 400 `"Campos obrigatórios: nome, whatsapp, password"`.
- **AC-001-04** Dado `password` de 5 caracteres, então 400 `"Senha deve ter no mínimo 6 caracteres"`; com 6 caracteres passa; com `"  abc  "` (3 depois do `trim`) falha.
- **AC-001-05** Dado `dia_vencimento` ou `dia_fechamento` ausente ou `null`, então 400 `"Campos obrigatórios: dia_vencimento, dia_fechamento"`.
- **AC-001-06** Dado `dia_vencimento` 0 ou 32, então 400 com a mensagem de faixa de vencimento; idem para `dia_fechamento`. Dados ambos inválidos, vale a mensagem do vencimento.
- **AC-001-07** Dado `dia_vencimento: "10"` ou `10.9`, então aceito como 10; dado `"abc"` ou `true`, então 400 da linha 6 (desvio 031).
- **AC-001-08** Dado WhatsApp já cadastrado (mesmo com formatação diferente), então 409 `"WhatsApp já cadastrado"` e nada é gravado.
- **AC-001-09** Dado `whatsapp: "abc"` (sem dígitos), então 400 e nada é gravado (desvio 031).
- **AC-001-10** Dado corpo vazio, sem `Content-Type` ou JSON inválido, então 400 com a mensagem de campos obrigatórios (desvio 024), não 500.
- **AC-001-11** Dado falha ao inserir a 3ª conta, então o usuário também não é gravado (transação).
- **AC-001-12** Dados dois cadastros simultâneos do mesmo número, então um responde 201 e o outro 409 (desvio 031).
- **AC-001-13** A senha é gravada como `pbkdf2:sha256:...` verificável pelo Werkzeug (fixture gerada em Python) e a `api_key_automate` é um token Fernet válido decifrável com a `ENCRYPTION_KEY` (desvio 017).

**Login**
- **AC-001-20** Dado usuário e senha corretos, então 200 com `token` e `user` `{id, nome, whatsapp}`.
- **AC-001-21** Dado WhatsApp formatado diferente do cadastro (mesmos dígitos), então o login funciona.
- **AC-001-22** Dado campo vazio ou corpo inválido, então 400 `"Campos obrigatórios: whatsapp, password"`.
- **AC-001-23** Dado número desconhecido, então 404 `"WhatsApp não cadastrado"`.
- **AC-001-24** Dado usuário com `senha_hash` nulo ou vazio, então 401 `"Usuário sem senha cadastrada. Cadastre-se pelo site."`.
- **AC-001-25** Dada senha errada, então 401 `"Senha incorreta"`; dado hash de formato desconhecido, também 401 `"Senha incorreta"`.
- **AC-001-26** Dado hash `scrypt:32768:8:1$...` gerado pelo Python (o formato do usuário real), então login com a senha correta funciona (fixture).
- **AC-001-27** Dado hash `pbkdf2:sha256:...` gerado pelo Python, então login funciona (fixture).
- **AC-001-28** O token do login tem `user_id` inteiro, `exp` = emissão + 24 h e `iat`; é aceito pelo PyJWT do Flask com a mesma chave (fixture cruzada).

**Verify**
- **AC-001-30** Dado token válido, então 200 `{"status":"success","valid":true,"user_id":N}`.
- **AC-001-31** Dado `token` vazio ou corpo inválido, então 400 `"Token não fornecido"`.
- **AC-001-32** Dado token adulterado, expirado ou com `alg` diferente de HS256 (inclusive `none`), então 401 `"Token inválido ou expirado"`.
- **AC-001-33** Dado token válido sem `user_id`, então 500 `"Erro ao verificar token"`.
- **AC-001-34** Dado token emitido pelo Flask (fixture gerada com PyJWT), então 200.

**Filtro `RequireUser`**
- **AC-001-40** Sem header: 401 `"Token de autenticação não fornecido"`.
- **AC-001-41** `Authorization: Token abc`, `Bearer` sozinho ou `Bearer a b`: 401 `"Formato de autenticação inválido. Use: Bearer <token>"`; `bearer abc` (minúsculo) é aceito como esquema.
- **AC-001-42** Token inválido ou expirado: 401 `"Token inválido ou expirado"`.
- **AC-001-43** Token válido sem `user_id`, ou com `user_id` 0: 401 `"Token não contém user_id"`.
- **AC-001-44** Token válido: o endpoint recebe o `user_id` em `ICurrentUser`.

**Compatibilidade criptográfica**
- **AC-001-50** O hasher verifica os hashes das fixtures Werkzeug (pbkdf2 e scrypt) e gera hashes que o Werkzeug verifica.
- **AC-001-51** O serviço Fernet passa nos vetores oficiais da especificação e decifra um token gerado pela biblioteca `cryptography` do Python.
- **AC-001-52** Leitura de chave de API aceita Fernet e texto puro (token que não começa com `gAAAAA` ou tem menos de 40 caracteres).

## 7. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `usuarios` | INSERT, SELECT | UNIQUE em `numero_whatsapp`, `api_key_automate`, `email`; trigger de `updated_at` |
| `contas` | INSERT | UNIQUE `(usuario_id, nome_conta)`; CHECK `chk_cartao_credito` (dias só no cartão); `ativa` e `saldo_inicial` pelos defaults |

Migration necessária (desvio 017): `ALTER TABLE usuarios ALTER COLUMN api_key_automate TYPE varchar(255)` — alargamento, sem perda. Processo em `adr/0001-migrations.md`.

## 8. Fora de escopo

- Reset e troca de senha (não existem hoje).
- Refresh e revogação de token (não existem hoje).
- Rate limit de login (desvio 023, feature 009).
- Busca de usuário por `user_api_key` (feature 011).
- Login social.

## 9. Perguntas em aberto

1. Usuário com `ativo = false` pode logar? O Flask permite. **Padrão desta spec: manter (permite).** Altere se quiser bloquear.
2. O cadastro hoje provavelmente falha com 500 em produção (a chave cifrada não cabe em `varchar(100)`). Se alguém já tentou se cadastrar pelo site, o usuário não existe; confirme se isso é conhecido.
