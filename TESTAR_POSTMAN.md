# 🧪 Como Testar via Postman (Estilo que você gosta!)

## ✅ **PASSO 1: Criar as Colunas no Banco**

### **Request no Postman:**

```
GET http://212.47.65.37:8000/admin/setup-resumo-matinal
```

**Sem headers, sem body, só dar GET!**

### **Resposta esperada:**

```
============================================================
SETUP: Resumo Matinal (Daily Briefing)
============================================================

[1/2] Adicionando campos de localizacao na tabela Usuarios...
OK - Campos 'cidade' e 'estado' adicionados!

[2/2] Adicionando campos de resumo matinal na tabela NotificationConfigs...
OK - Campos 'resumo_matinal_ativo' e 'resumo_matinal_hora' adicionados!

============================================================
SUCESSO! Resumo Matinal configurado
============================================================

Proximos passos:
1. Configurar WEATHER_API_KEY no .env (opcional)
2. Testar via WhatsApp: 'Configurar localizacao: Sao Paulo, SP'
3. Testar via WhatsApp: 'Ativar resumo matinal'
4. Configurar cron job para /admin/trigger-daily-briefing
```

✅ **Pronto! Tabelas criadas!**

---

## ✅ **PASSO 2: Testar Configurar Localização via WhatsApp**

### **Request no Postman:**

```
POST http://212.47.65.37:8000/webhook-whatsapp
```

**Headers:**
```json
Content-Type: application/json
```

**Body (JSON):**
```json
{
  "texto": "Configurar localização: São Paulo, SP",
  "numero": "553194001072"
}
```

### **Resposta esperada:**

```json
{
  "status": "sucesso",
  "resposta": "✅ Localização configurada: São Paulo, SP\n\nAgora você receberá informações de clima nos resumos matinais!"
}
```

---

## ✅ **PASSO 3: Testar Ativar Resumo Matinal via WhatsApp**

### **Request no Postman:**

```
POST http://212.47.65.37:8000/webhook-whatsapp
```

**Body (JSON):**
```json
{
  "texto": "Ativar resumo matinal",
  "numero": "553194001072"
}
```

### **Resposta esperada:**

```json
{
  "status": "sucesso",
  "resposta": "✅ Resumo matinal ativado\n\n📱 Resumo Matinal - Status atual:\n• Ativo: Sim\n• Horário: 07:00\n\n💡 Configure sua localização para receber informações de clima:\n\"Configurar localização: [Cidade], [Estado]\""
}
```

---

## ✅ **PASSO 4: Testar Configurar Horário**

### **Request no Postman:**

```
POST http://212.47.65.37:8000/webhook-whatsapp
```

**Body (JSON):**
```json
{
  "texto": "Configurar resumo matinal às 8h",
  "numero": "553194001072"
}
```

### **Resposta esperada:**

```json
{
  "status": "sucesso",
  "resposta": "✅ Resumo matinal ativado e horário configurado para 08:00\n\n📱 Resumo Matinal - Status atual:\n• Ativo: Sim\n• Horário: 08:00..."
}
```

---

## ✅ **PASSO 5: Testar Trigger Manual (Enviar Resumo Agora)**

### **Request no Postman:**

```
POST http://212.47.65.37:8000/admin/trigger-daily-briefing
```

**Headers:**
```
x-api-key: SUA_API_SECRET_KEY
```

**Sem body!**

### **Resposta esperada:**

```json
{
  "status": "sucesso",
  "usuarios_processados": 1,
  "enviados_sucesso": 1,
  "erros": 0,
  "horario": "14:30"
}
```

**⚠️ Nota:** Se retornar `usuarios_processados: 0`, é porque nenhum usuário tem o resumo configurado para o horário atual. Normal!

---

## 📋 **Resumo Ultra-Rápido**

| Passo | Método | URL | Body/Headers |
|-------|--------|-----|--------------|
| 1️⃣ Criar tabelas | `GET` | `/admin/setup-resumo-matinal` | Nada |
| 2️⃣ Configurar localização | `POST` | `/webhook-whatsapp` | `{"texto": "Configurar localização: São Paulo, SP", "numero": "553194001072"}` |
| 3️⃣ Ativar resumo | `POST` | `/webhook-whatsapp` | `{"texto": "Ativar resumo matinal", "numero": "553194001072"}` |
| 4️⃣ Configurar horário | `POST` | `/webhook-whatsapp` | `{"texto": "Configurar resumo matinal às 8h", "numero": "553194001072"}` |
| 5️⃣ Testar trigger | `POST` | `/admin/trigger-daily-briefing` | Header: `x-api-key: SUA_KEY` |

---

## 🔧 **BONUS: Configurar WEATHER_API_KEY (Opcional)**

Se quiser que o resumo tenha informações de clima:

1. **Criar conta:** https://www.weatherapi.com/signup.aspx
2. **Copiar API Key**
3. **Adicionar no `.env`:**

```env
WEATHER_API_KEY=sua_chave_aqui
```

4. **Reiniciar Flask**

**⚠️ Se não configurar:** Resumo funciona normalmente, mas **sem clima**.

---

## 🎯 **Configurar Cron Job no UptimeRobot**

Depois de testar, configure o UptimeRobot para chamar automaticamente:

```
POST http://212.47.65.37:8000/admin/trigger-daily-briefing
Header: x-api-key: SUA_SECRET_KEY
```

**Frequência:** A cada hora (o sistema verifica se há usuários para aquele horário)

---

## ✅ **Pronto!**

Agora você pode:
1. ✅ Criar tabelas via Postman
2. ✅ Testar via WhatsApp (Postman simulando)
3. ✅ Disparar resumos manualmente
4. ✅ Configurar cron job automático

**Tudo do jeito que você gosta!** 🚀
