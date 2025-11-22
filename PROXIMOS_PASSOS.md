# ✅ PRÓXIMOS PASSOS - Resumo Matinal

## 📋 O que você PRECISA fazer

### 1️⃣ Executar Migrations no Banco de Dados

```bash
# No terminal, na pasta do projeto:
python add_location_fields.py
python add_notification_config_fields.py
```

**Resultado esperado:**
```
✅ Migration executada com sucesso!
   - Coluna 'cidade' adicionada
   - Coluna 'estado' adicionada
✅ Migration executada com sucesso!
   - Coluna 'resumo_matinal_ativo' adicionada
   - Coluna 'resumo_matinal_hora' adicionada
```

---

### 2️⃣ Configurar API de Clima

1. **Criar conta grátis:** https://www.weatherapi.com/signup.aspx
2. **Copiar sua API Key** do dashboard
3. **Adicionar ao arquivo `.env`:**

```env
WEATHER_API_KEY=sua_chave_aqui
```

---

### 3️⃣ Adicionar Handlers no Bot do WhatsApp

**Arquivo:** `app/routes/webhooks.py`

**O que fazer:**

1. **Abrir** o arquivo [CODIGO_HANDLER_WHATSAPP.md](CODIGO_HANDLER_WHATSAPP.md)
2. **Copiar** o "Código 1: Handler Configurar Localização"
3. **Colar** na linha ~1322 (antes de "Análise Inteligente")
4. **Copiar** o "Código 2: Handler Configurar Notificações (atualizado)"
5. **Substituir** o handler existente (linhas ~1254-1321)

**OU** se preferir, eu posso fazer isso por você agora!

---

### 4️⃣ Configurar Cron Job

**Adicionar ao crontab:**

```bash
crontab -e
```

**Colar esta linha:**

```bash
# Resumo Matinal - Executar a cada hora
0 * * * * cd /caminho/completo/do/projeto && /caminho/completo/.venv/bin/python processar_resumo_matinal.py >> /var/log/resumo_matinal.log 2>&1
```

**⚠️ Trocar:**
- `/caminho/completo/do/projeto` pelo caminho real
- `/caminho/completo/.venv` pelo caminho do seu ambiente virtual

---

### 5️⃣ Testar

**Via Terminal:**
```bash
python processar_resumo_matinal.py
```

**Via WhatsApp:**
```
Você: Configurar localização: São Paulo, SP
Você: Ativar resumo matinal
Você: Configurar resumo matinal às 7h
```

---

## 🎯 Resumo Ultra-Rápido

| Passo | O que fazer | Tempo estimado |
|-------|-------------|----------------|
| 1️⃣ | Rodar 2 migrations | 1 min |
| 2️⃣ | Criar conta WeatherAPI e adicionar key | 3 min |
| 3️⃣ | Adicionar 2 handlers no bot | 5 min |
| 4️⃣ | Configurar cron job | 2 min |
| 5️⃣ | Testar | 5 min |
| **TOTAL** | | **~15 minutos** |

---

## ❓ Quer que eu faça algo por você?

Posso fazer automaticamente:

- ✅ **Adicionar os handlers no bot** (se quiser)
- ✅ **Gerar o comando cron** com os caminhos corretos
- ✅ **Criar script de teste automatizado**

**É só me avisar!** 🚀

---

## 📚 Documentação Completa

- [CODIGO_HANDLER_WHATSAPP.md](CODIGO_HANDLER_WHATSAPP.md) - Código dos handlers
- [SETUP_RESUMO_MATINAL.md](SETUP_RESUMO_MATINAL.md) - Guia completo de setup
- [RESUMO_MATINAL_README.md](RESUMO_MATINAL_README.md) - Documentação da feature
- [IMPLEMENTACAO_RESUMO_MATINAL.md](IMPLEMENTACAO_RESUMO_MATINAL.md) - Detalhes técnicos
