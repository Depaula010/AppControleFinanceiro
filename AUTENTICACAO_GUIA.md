# Guia de Implementação - Autenticação Web

Este guia detalha a implementação do sistema de autenticação web no backend do "Meu Secretário".

## 📋 Resumo das Alterações

### 1. Banco de Dados
- **Nova coluna**: `senha_hash` na tabela `Usuarios`
- **Tipo**: VARCHAR(255) NULL
- **Propósito**: Armazenar hash bcrypt da senha para login web

### 2. Dependências Adicionadas
- **PyJWT==2.8.0**: Geração e validação de tokens JWT

### 3. Novos Arquivos Criados

#### [app/routes/auth.py](app/routes/auth.py)
Blueprint com 3 endpoints:
- `POST /auth/register` - Cadastro de novos usuários
- `POST /auth/login` - Login e geração de token JWT
- `POST /auth/verify` - Verificação de token

#### [migrations/001_add_password_column.sql](migrations/001_add_password_column.sql)
Script SQL para adicionar a coluna `senha_hash`

#### [migrations/apply_password_migration.py](migrations/apply_password_migration.py)
Script Python para aplicar a migração automaticamente

### 4. Arquivos Modificados

#### [app/__init__.py](app/__init__.py:172)
- Registrado novo blueprint `auth_bp`

#### [app/routes/webhooks.py](app/routes/webhooks.py:488-496)
- Alterado comportamento para usuários não cadastrados
- Agora redireciona para cadastro web em vez de iniciar cadastro conversacional

#### [requirements.txt](requirements.txt:9)
- Adicionado PyJWT==2.8.0

---

## 🚀 Passo a Passo de Implementação

### Etapa 1: Instalar Dependências

```bash
# No ambiente virtual
.venv/Scripts/pip.exe install PyJWT==2.8.0
```

### Etapa 2: Aplicar Migração do Banco de Dados

**Opção A: Via SQL direto (PostgreSQL)**
```bash
psql -h seu-host -U seu-usuario -d seu-database -f migrations/001_add_password_column.sql
```

**Opção B: Via script Python**
```bash
# Certifique-se de que DATABASE_URL está configurada
python -m migrations.apply_password_migration
```

**Opção C: Manualmente via pgAdmin/DBeaver**
```sql
ALTER TABLE Usuarios
ADD COLUMN IF NOT EXISTS senha_hash VARCHAR(255) NULL;
```

### Etapa 3: Reiniciar Aplicação

```bash
# Desenvolvimento local
python app.py

# Produção (Render.com)
# A aplicação será reiniciada automaticamente após o deploy
```

---

## 📡 Documentação dos Endpoints

### POST /auth/register

**Descrição**: Cadastra um novo usuário no sistema

**Request Body**:
```json
{
  "nome": "João Silva",
  "whatsapp": "5511999999999",
  "password": "SenhaSegura123",
  "dia_vencimento": 10,
  "dia_fechamento": 5
}
```

**Response Success (201)**:
```json
{
  "status": "success",
  "message": "Usuário cadastrado com sucesso",
  "user_id": 123
}
```

**Response Error (409 - WhatsApp já existe)**:
```json
{
  "status": "error",
  "message": "WhatsApp já cadastrado"
}
```

**Validações**:
- ✅ Nome, WhatsApp e senha são obrigatórios
- ✅ Senha deve ter mínimo 6 caracteres
- ✅ Dias de vencimento/fechamento entre 1-31
- ✅ WhatsApp não pode estar duplicado

**Comportamento**:
1. Valida campos obrigatórios
2. Verifica se WhatsApp já existe
3. Gera hash da senha (bcrypt)
4. Cria API key criptografada para o bot
5. Cria contas padrão:
   - Carteira (Dinheiro)
   - Conta Corrente
   - Cartão de Crédito (com dias informados)

---

### POST /auth/login

**Descrição**: Autentica usuário e retorna token JWT

**Request Body**:
```json
{
  "whatsapp": "5511999999999",
  "password": "SenhaSegura123"
}
```

**Response Success (200)**:
```json
{
  "status": "success",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 123,
    "nome": "João Silva",
    "whatsapp": "5511999999999"
  }
}
```

**Response Error (401 - Senha incorreta)**:
```json
{
  "status": "error",
  "message": "Senha incorreta"
}
```

**Response Error (404 - WhatsApp não cadastrado)**:
```json
{
  "status": "error",
  "message": "WhatsApp não cadastrado"
}
```

**Token JWT**:
- **Algoritmo**: HS256
- **Validade**: 24 horas
- **Payload**: `{ "user_id": 123, "exp": ..., "iat": ... }`
- **Chave de assinatura**: `API_SECRET_KEY` (do .env)

---

### POST /auth/verify

**Descrição**: Verifica se um token JWT é válido

**Request Body**:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response Success (200)**:
```json
{
  "status": "success",
  "valid": true,
  "user_id": 123
}
```

**Response Error (401)**:
```json
{
  "status": "error",
  "message": "Token inválido ou expirado"
}
```

---

## 🔐 Segurança

### Hash de Senha
- **Algoritmo**: `pbkdf2:sha256` (via werkzeug.security)
- **Salting**: Automático (werkzeug gera salt único)
- **Iterações**: 600.000+ (padrão werkzeug)

### JWT
- **Assinatura**: HMAC-SHA256
- **Chave secreta**: `API_SECRET_KEY` do ambiente
- **Expiração**: 24 horas
- **Claims**: `user_id`, `exp`, `iat`

### API Key do Bot
- **Geração**: `secrets.token_urlsafe(32)` (256 bits)
- **Armazenamento**: Criptografada via `encryption_service`
- **Uso**: Automação WhatsApp (Automate.io)

---

## 🧪 Testando os Endpoints

### Teste 1: Registrar Usuário

```bash
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "nome": "Maria Santos",
    "whatsapp": "5511988887777",
    "password": "senha123",
    "dia_vencimento": 15,
    "dia_fechamento": 10
  }'
```

### Teste 2: Fazer Login

```bash
curl -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "whatsapp": "5511988887777",
    "password": "senha123"
  }'
```

**Salve o token retornado!**

### Teste 3: Verificar Token

```bash
curl -X POST http://localhost:5000/auth/verify \
  -H "Content-Type: application/json" \
  -d '{
    "token": "SEU_TOKEN_AQUI"
  }'
```

---

## 🔄 Comportamento do Bot WhatsApp

### Antes (Cadastro Conversacional)
```
Usuário não cadastrado envia mensagem
    ↓
Bot: "Qual seu nome?"
    ↓
Bot: "Qual dia de vencimento?"
    ↓
Bot: "Qual dia de fechamento?"
    ↓
Cadastro concluído
```

### Agora (Redirecionamento Web)
```
Usuário não cadastrado envia mensagem
    ↓
Bot: "Olá! 👋 Parece que você ainda não tem cadastro no Meu Secretário.
      Para começar a usar, crie sua conta em nosso site:
      https://app.meusecretario.com/register"
```

---

## 🎯 Próximos Passos

### No Frontend Angular
1. Criar página de registro (`/register`)
2. Criar página de login (`/login`)
3. Implementar guard de autenticação
4. Armazenar token JWT no localStorage
5. Adicionar interceptor HTTP para incluir token
6. Implementar logout (limpar token)

### Exemplo de Guard (Angular)

```typescript
// auth.guard.ts
canActivate(): boolean {
  const token = localStorage.getItem('jwt_token');

  if (!token) {
    this.router.navigate(['/login']);
    return false;
  }

  // Verificar token com backend
  return this.authService.verifyToken(token);
}
```

### Exemplo de Interceptor (Angular)

```typescript
// auth.interceptor.ts
intercept(req: HttpRequest<any>, next: HttpHandler) {
  const token = localStorage.getItem('jwt_token');

  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next.handle(req);
}
```

---

## 🐛 Troubleshooting

### Erro: "Package PyJWT not found"
```bash
.venv/Scripts/pip.exe install PyJWT==2.8.0
```

### Erro: "Column senha_hash does not exist"
Execute a migração:
```bash
python -m migrations.apply_password_migration
```

### Erro: "Token inválido"
- Verifique se `API_SECRET_KEY` está configurada no `.env`
- Certifique-se de que o token não expirou (24h)
- Verifique se o token está sendo enviado corretamente

### Erro: "WhatsApp já cadastrado"
- O WhatsApp informado já existe no banco
- Use outro número ou faça login com esse WhatsApp

---

## 📊 Estrutura de Dados

### Tabela Usuarios (Atualizada)

| Coluna            | Tipo          | Nullable | Descrição                           |
|-------------------|---------------|----------|-------------------------------------|
| id                | SERIAL        | NOT NULL | Chave primária                      |
| nome              | VARCHAR(255)  | NOT NULL | Nome do usuário                     |
| numero_whatsapp   | VARCHAR(50)   | NOT NULL | Número do WhatsApp (único)          |
| api_key_automate  | VARCHAR(100)  | NULL     | Chave criptografada para bot        |
| senha_hash        | VARCHAR(255)  | NULL     | Hash bcrypt da senha (NOVO)         |
| created_at        | TIMESTAMP     | NOT NULL | Data de criação                     |

---

## 💡 Boas Práticas

### Senhas
- ✅ Nunca armazene senhas em texto puro
- ✅ Use bcrypt/pbkdf2 para hash
- ✅ Exija mínimo 6-8 caracteres
- ✅ Considere adicionar validação de força de senha

### Tokens JWT
- ✅ Use HTTPS em produção
- ✅ Configure expiração adequada (24h é razoável)
- ✅ Implemente refresh tokens para sessões longas
- ✅ Nunca armazene informações sensíveis no payload

### API Keys
- ✅ Gere keys com `secrets` (criptograficamente seguro)
- ✅ Criptografe antes de salvar no banco
- ✅ Use chaves longas (256 bits mínimo)

---

## 📞 Contato e Suporte

Para dúvidas ou problemas na implementação, consulte:
- Documentação do Flask: https://flask.palletsprojects.com
- Documentação do PyJWT: https://pyjwt.readthedocs.io
- Werkzeug Security: https://werkzeug.palletsprojects.com/security/

---

**Implementado em**: 2025-12-11
**Versão**: 1.0
**Status**: ✅ Pronto para produção
