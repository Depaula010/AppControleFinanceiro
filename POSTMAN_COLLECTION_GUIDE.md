# 📬 Guia da Postman Collection - Meu Secretário Admin API

## 📥 Como Importar no Postman

### **Passo 1: Abrir Postman**
- Abra o Postman (desktop ou web)

### **Passo 2: Importar Collection**
1. Clique em **"Import"** (canto superior esquerdo)
2. Arraste o arquivo `Meu_Secretario_Admin_API.postman_collection.json`
3. Ou clique em **"Choose Files"** e selecione o arquivo
4. Clique em **"Import"**

✅ **Pronto!** A collection aparecerá na sidebar com 20 endpoints organizados em 5 pastas.

---

## 🔑 Configurar Variáveis de Ambiente

Após importar, configure as variáveis:

### **Opção 1: Editar Variáveis da Collection (RECOMENDADO)**

1. Clique com botão direito na collection **"Meu Secretário - Admin API"**
2. Selecione **"Edit"**
3. Vá na aba **"Variables"**
4. Edite as variáveis:

| Variável | Valor | Descrição |
|----------|-------|-----------|
| `BASE_URL` | `http://212.47.65.37` | URL base da API (já configurado) |
| `API_SECRET_KEY` | `SUA_CHAVE_AQUI` | ⚠️ **COLE SUA API_SECRET_KEY REAL** |

5. Clique em **"Save"**

---

### **Opção 2: Criar Environment (Opcional)**

Se preferir ter múltiplos ambientes (dev, prod):

1. Clique no **"Environments"** (sidebar)
2. Clique em **"+"** → **"Create Environment"**
3. Nome: `Meu Secretário - Prod`
4. Adicione variáveis:
   ```
   BASE_URL = http://212.47.65.37
   API_SECRET_KEY = sua-api-key-aqui
   ```
5. Selecione o environment no dropdown (canto superior direito)

---

## 🚀 Testando os Endpoints

### **1. Primeiro Teste - Health Check**

Antes de testar `/admin`, verifique se API está online:

**URL:** `http://212.47.65.37/`
**Method:** GET
**Headers:** Nenhum necessário
**Esperado:**
```json
{
  "status": "online",
  "service": "Meu Secretário API",
  "database": "✅ Conectado",
  "timestamp": "2025-11-23T..."
}
```

---

### **2. Testar Endpoint Admin**

Vamos testar o **Security Stats**:

1. Na collection, abra: **Configurações & Info** → **Security Stats**
2. Verifique se o header `x-api-key` está preenchido com `{{API_SECRET_KEY}}`
3. Clique em **"Send"**

**Esperado:**
```json
{
  "blocked_ips": [],
  "blocked_ips_count": 0,
  "suspicious_attempts": [],
  "suspicious_count": 0,
  "timestamp": "2025-11-23T..."
}
```

Se retornar **401 Unauthorized** → API key incorreta
Se retornar **200 OK** → ✅ Tudo funcionando!

---

## 📂 Estrutura da Collection

### **1. Setup & Configuração** (7 endpoints)
Endpoints para configurar o sistema na primeira vez:
- `Setup Database` - Cria tabelas
- `Populate Global Categories` - Popula categorias
- `Setup User Data` - Configura usuário
- `Setup Calendar Table` - Tabela de agendamentos
- `Setup Monthly Reports Table` - Tabela de relatórios
- `Setup Resumo Matinal` - Configura daily briefing
- `Setup Potes Alerts` - Configura alertas de potes

**Quando usar:** Primeira instalação ou resetar sistema

---

### **2. Triggers & Agendamentos** (6 endpoints)
Endpoints para disparar processos automatizados:
- `Run Motor Agendamentos` - Processa agendamentos
- `Trigger Agenda Notifications` - Envia notifs de agenda
- `Trigger Bills Notifications` - Envia notifs de contas
- `Trigger Daily Briefing` - Envia resumo matinal
- `Trigger Monthly Reports (Início)` - Relatório início do mês
- `Trigger Monthly Reports (Fim)` - Relatório fim do mês

**Quando usar:** Testar triggers ou executar manualmente

---

### **3. Testes & Debug** (3 endpoints)
Endpoints para testar funcionalidades:
- `Test Notification` - Testa envio de notificações
- `Test Monthly Report` - Testa relatório mensal
- `Debug Calendar` - Debug do Google Calendar

**Quando usar:** Desenvolvimento e troubleshooting

---

### **4. Configurações & Info** (3 endpoints)
Endpoints para consultar configurações:
- `Get Notification Config` - Config de notificações do usuário
- `OAuth Config Check` - Verifica OAuth do Google
- `Security Stats` - Estatísticas de segurança

**Quando usar:** Consultar estado do sistema

---

### **5. Utilidades** (1 endpoint)
Endpoints auxiliares:
- `Clear Bot Session` - Limpa sessão do chatbot

**Quando usar:** Resetar conversa do bot

---

## 🔧 Exemplos de Uso

### **Exemplo 1: Configuração Inicial do Sistema**

Execute nesta ordem:

1. ✅ `Setup Database`
2. ✅ `Populate Global Categories`
3. ✅ `Setup User Data`
4. ✅ `Setup Calendar Table`
5. ✅ `Setup Monthly Reports Table`
6. ✅ `Setup Resumo Matinal`
7. ✅ `Setup Potes Alerts`

**Resultado:** Sistema 100% configurado e pronto para uso!

---

### **Exemplo 2: Testar Notificações**

1. ✅ `Test Notification` (Body: `{"tipo": "agenda"}`)
2. ✅ Verificar WhatsApp do usuário
3. ✅ Se recebeu → Sistema funcionando!

---

### **Exemplo 3: Disparar Relatório Mensal Manualmente**

1. ✅ `Test Monthly Report` com `usuario_id=1` e `momento=INICIO_MES`
2. ✅ Verificar WhatsApp
3. ✅ PDF do relatório será enviado!

---

## ⚠️ Troubleshooting

### **Erro 401 Unauthorized**
**Causa:** API key incorreta ou ausente

**Solução:**
1. Verificar se variável `{{API_SECRET_KEY}}` está preenchida
2. Verificar se header `x-api-key` está presente
3. Confirmar que a API key é a mesma do servidor (`.env`)

---

### **Erro 403 Forbidden**
**Causa:** IP bloqueado (não deveria mais ocorrer, removemos whitelist)

**Solução:**
1. Verificar logs de segurança: `GET /admin/security-stats`
2. Se IP estiver bloqueado, desbloquear no servidor:
   ```bash
   docker exec meu-secretario-redis redis-cli DEL "blocked_ip:SEU_IP"
   ```

---

### **Erro 429 Too Many Requests**
**Causa:** Rate limiting ativo (5 req/s no /admin)

**Solução:**
1. Aguardar alguns segundos
2. Fazer requisições mais devagar
3. Normal durante testes intensivos

---

### **Erro 500 Internal Server Error**
**Causa:** Erro no servidor (banco de dados, etc)

**Solução:**
1. Verificar logs do servidor:
   ```bash
   docker logs meu-secretario-api --tail=50
   ```
2. Verificar se banco de dados está conectado
3. Verificar variáveis de ambiente no servidor

---

### **Erro 0 / Connection Timeout**
**Causa:** Servidor offline ou firewall bloqueando

**Solução:**
1. Verificar se servidor está online:
   ```bash
   curl http://212.47.65.37/
   ```
2. Verificar se containers estão rodando:
   ```bash
   docker compose ps
   ```

---

## 🔐 Segurança

### **Proteja sua API Key!**

⚠️ **NUNCA:**
- Commite a API key no Git
- Compartilhe a collection com a key preenchida
- Exponha a key publicamente

✅ **SEMPRE:**
- Use variáveis de ambiente
- Guarde a key em local seguro
- Rotacione a key periodicamente (trocar no GitHub Secrets + servidor)

---

### **Como Trocar a API Key**

Se a key vazar, troque imediatamente:

1. **Gerar nova key:**
   ```bash
   openssl rand -hex 32
   ```

2. **Atualizar GitHub Secrets:**
   - Vá em: `Settings` → `Secrets` → `API_SECRET_KEY`
   - Cole a nova key

3. **Fazer deploy:**
   ```bash
   git commit --allow-empty -m "chore: Rotação de API key"
   git push origin main
   ```

4. **Atualizar Postman:**
   - Editar variável `API_SECRET_KEY` na collection

---

## 📊 Endpoints Mais Usados

### **Durante Desenvolvimento:**
1. `Security Stats` - Ver estado de segurança
2. `Test Notification` - Testar notificações
3. `Debug Calendar` - Debug do Calendar
4. `OAuth Config Check` - Verificar OAuth

### **Em Produção (via Cron/GitHub Actions):**
1. `Trigger Daily Briefing` - Todo dia 7h
2. `Trigger Agenda Notifications` - Todo dia 7h
3. `Trigger Bills Notifications` - Todo dia 7h
4. `Trigger Monthly Reports (Início)` - Dia 1 do mês
5. `Trigger Monthly Reports (Fim)` - Último dia do mês

---

## 🎯 Próximos Passos

Após importar e testar:

1. ✅ Salve a collection como favorita (⭐)
2. ✅ Organize em pastas no Postman
3. ✅ Crie testes automatizados (aba Tests)
4. ✅ Configure monitors (Postman Cloud)
5. ✅ Documente respostas de exemplo (Save Response)

---

## 📞 Suporte

**Dúvidas sobre endpoints?**
- Consulte: `app/routes/admin.py` (código-fonte)
- Veja logs: `docker logs meu-secretario-api`

**Dúvidas sobre segurança?**
- Consulte: `SECURITY_GUIDE.md`

**Problemas no deploy?**
- Consulte: `DEPLOY_INSTRUCTIONS.md`

---

**Arquivo criado em:** 2025-11-23
**Versão da API:** v99
**Total de Endpoints:** 20 (todos com autenticação x-api-key)
