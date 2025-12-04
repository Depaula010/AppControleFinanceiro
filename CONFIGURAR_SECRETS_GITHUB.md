# 🔐 Como Configurar GitHub Secrets

## ⚠️ IMPORTANTE: Nunca commit arquivos .env no repositório!

As chaves de segurança devem ser armazenadas como **GitHub Secrets** para deploy automático.

---

## 📋 Passo a Passo

### 1️⃣ Acessar GitHub Secrets

1. Vá para o repositório no GitHub
2. Clique em **Settings** (Configurações)
3. No menu lateral esquerdo, clique em **Secrets and variables** → **Actions**
4. Clique em **New repository secret**

---

### 2️⃣ Chaves que VOCÊ PRECISA ADICIONAR

Adicione os seguintes secrets (se ainda não tiver):

#### 🔑 Novas Chaves de Segurança (OBRIGATÓRIAS)

**Nome do Secret**: `ENCRYPTION_KEY`
**Valor**:
```
jj5aVOCgECuD7_FEc3fLUD2XAK_M76_g45OqoCMGojc=
```
**Finalidade**: Criptografa dados sensíveis no banco de dados (API keys dos usuários, tokens OAuth)

---

**Nome do Secret**: `WEBHOOK_SIGNATURE_KEY`
**Valor**:
```
kuYW0XCI_IdQmaMZtaIACwOmzh-Euw3RieNaQ-K1iEQ=
```
**Finalidade**: Valida assinaturas HMAC de webhooks

---

**Nome do Secret**: `API_SECRET_KEY` *(se não tiver)*
**Valor**:
```
p9LSbMIOF_J6EuN5cVxJLJsGlStO4UtYVqc-1Tg9l3c=
```
**Finalidade**: Autenticação de requisições do bot/automações

---

### 3️⃣ Verificar Secrets Existentes

Certifique-se de que já tem estes secrets configurados:

- ✅ `CONTABO_HOST`
- ✅ `CONTABO_USER`
- ✅ `CONTABO_SSH_KEY`
- ✅ `GEMINI_API_KEY`
- ✅ `DATABASE_URL`
- ✅ `BOT_WHATSAPP_URL`
- ✅ `GOOGLE_CLIENT_ID` (opcional)
- ✅ `GOOGLE_CLIENT_SECRET` (opcional)
- ✅ `GOOGLE_REDIRECT_URI` (opcional)
- ✅ `WEATHER_API_KEY` (opcional)
- ✅ `OPENROUTE_API_KEY` (opcional)

---

## 🚀 Após Configurar

1. **Faça commit** das alterações do `docker-compose.yml` e `deploy.yml`
2. **Push para main/master** - o deploy automático vai rodar
3. O GitHub Actions vai criar o `.env` no servidor com as chaves dos secrets

---

## 🔒 Segurança

### ✅ Vantagens de usar GitHub Secrets:

- Chaves nunca aparecem no código
- Não são expostas em logs públicos
- Apenas colaboradores com permissão podem ver
- Deploy automático sem expor credenciais

### ❌ NUNCA faça:

- Commit do arquivo `.env` no repositório
- Compartilhe as chaves em chat/email
- Hardcode chaves no código

---

## 🔄 Se Precisar Gerar Novas Chaves

Se por algum motivo precisar gerar novas chaves seguras:

```bash
# ENCRYPTION_KEY e WEBHOOK_SIGNATURE_KEY (formato Fernet)
python -c "import base64; import os; print(base64.urlsafe_b64encode(os.urandom(32)).decode())"

# API_SECRET_KEY (formato URL-safe)
python -c "import secrets; print(secrets.token_urlsafe(32))"
```

---

## 📝 Notas Importantes

### ENCRYPTION_KEY:
- **CRÍTICA**: Se mudar, dados já criptografados no banco ficarão ILEGÍVEIS
- Guarde um backup seguro desta chave (ex: gerenciador de senhas)
- Use a MESMA chave em todos os ambientes que acessam o mesmo banco

### WEBHOOK_SIGNATURE_KEY:
- Pode ser diferente por ambiente
- Não afeta dados já armazenados
- Usada apenas para validação de webhooks

---

## ✅ Checklist Final

- [ ] Adicionei `ENCRYPTION_KEY` nos GitHub Secrets
- [ ] Adicionei `WEBHOOK_SIGNATURE_KEY` nos GitHub Secrets
- [ ] Adicionei `API_SECRET_KEY` nos GitHub Secrets
- [ ] Verifiquei que o `.env` está no `.gitignore`
- [ ] Fiz commit do `docker-compose.yml` atualizado
- [ ] Fiz commit do `deploy.yml` atualizado
- [ ] Testei o deploy automático
- [ ] Endpoint `/api/transacao` está funcionando no servidor

---

**Pronto!** 🎉 Agora seu sistema está seguro e o deploy é automático!
