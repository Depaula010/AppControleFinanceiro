# 🚀 Guia Rápido: Configuração do Resumo Matinal

## Passo 1: Executar Migrations

```bash
# 1. Adicionar campos de localização (cidade, estado)
python add_location_fields.py

# 2. Adicionar campos de resumo matinal
python add_notification_config_fields.py
```

**Resultado esperado:**
```
✅ Migration executada com sucesso!
   - Coluna 'cidade' adicionada (padrão: 'São Paulo')
   - Coluna 'estado' adicionada (padrão: 'SP')

✅ Migration executada com sucesso!
   - Coluna 'resumo_matinal_ativo' adicionada (padrão: TRUE)
   - Coluna 'resumo_matinal_hora' adicionada (padrão: '07:00:00')
```

---

## Passo 2: Configurar API de Clima

### 2.1. Criar Conta no WeatherAPI

1. Acesse: https://www.weatherapi.com/signup.aspx
2. Crie uma conta gratuita (1M chamadas/mês)
3. Acesse o Dashboard e copie sua **API Key**

### 2.2. Adicionar ao .env

Edite seu arquivo `.env`:

```env
# ... outras variáveis ...

# WeatherAPI (Resumo Matinal)
WEATHER_API_KEY=sua_chave_aqui_cole_a_chave_copiada
```

---

## Passo 3: Configurar Cron Job

### 3.1. Editar Crontab

```bash
crontab -e
```

### 3.2. Adicionar Linha

**Opção 1: Executar a cada hora (recomendado)**
```bash
# Resumo Matinal - Executar a cada hora
0 * * * * cd /caminho/completo/do/projeto && /caminho/completo/.venv/bin/python processar_resumo_matinal.py >> /var/log/resumo_matinal.log 2>&1
```

**Opção 2: Executar apenas em horários específicos (mais eficiente)**
```bash
# Resumo Matinal - Executar às 6h, 7h, 8h e 9h
0 6,7,8,9 * * * cd /caminho/completo/do/projeto && /caminho/completo/.venv/bin/python processar_resumo_matinal.py >> /var/log/resumo_matinal.log 2>&1
```

### 3.3. Verificar Cron

```bash
# Listar cron jobs ativos
crontab -l

# Verificar se cron está rodando
sudo service cron status
```

---

## Passo 4: Testar Funcionalidade

### 4.1. Testar Processador Manualmente

```bash
# Ativar ambiente virtual
source .venv/bin/activate

# Executar processador
python processar_resumo_matinal.py
```

**Saída esperada:**
```
[RESUMO-MATINAL] Início do processamento - 2025-11-22 07:00:00
[RESUMO-MATINAL] Buscando usuários para notificar às 07:00
[RESUMO-MATINAL] 2 usuário(s) encontrado(s)
[RESUMO-MATINAL] Processando usuário 1...
[WEATHER] ✅ Clima obtido para São Paulo,SP: 24°C, Ensolarado ☀️
[GEMINI-BRIEFING] Resumo gerado com sucesso (342 chars)
[RESUMO-MATINAL] ✅ Resumo enviado para usuário 1
[RESUMO-MATINAL] Processamento finalizado - 2025-11-22 07:00:15
```

### 4.2. Testar via WhatsApp

**Configurar localização:**
```
Você: "Configurar localização: São Paulo, SP"
Bot: "📍 Localização configurada: São Paulo, SP"
```

**Configurar horário (para receber em breve):**
```
Você: "Configurar resumo matinal às 14:30"
Bot: "✅ Resumo matinal ativado e horário configurado para 14:30"
```

**Aguardar horário configurado** e verificar se recebe o resumo.

---

## Passo 5: Monitorar Logs

### 5.1. Criar Arquivo de Log

```bash
sudo touch /var/log/resumo_matinal.log
sudo chmod 666 /var/log/resumo_matinal.log
```

### 5.2. Visualizar Logs em Tempo Real

```bash
tail -f /var/log/resumo_matinal.log
```

---

## ✅ Checklist de Verificação

- [ ] Migrations executadas com sucesso
- [ ] `WEATHER_API_KEY` configurada no `.env`
- [ ] Cron job adicionado ao crontab
- [ ] Teste manual do processador funcionou
- [ ] Localização configurada via WhatsApp
- [ ] Resumo recebido no horário configurado

---

## 🐛 Troubleshooting

### Problema: "WEATHER_API_KEY não configurada"

**Solução:**
1. Verifique se a variável está no `.env`
2. Reinicie a aplicação Flask
3. Teste: `echo $WEATHER_API_KEY`

### Problema: "Nenhum usuário configurado para este horário"

**Solução:**
Verifique no banco se a configuração está correta:

```sql
SELECT u.nome, nc.resumo_matinal_ativo, nc.resumo_matinal_hora
FROM NotificationConfigs nc
JOIN Usuarios u ON nc.usuario_id = u.id;
```

### Problema: Cron não está executando

**Solução:**
1. Verifique se o cron está rodando:
```bash
sudo service cron status
```

2. Verifique se há erros no syslog:
```bash
grep CRON /var/log/syslog | tail -20
```

3. Teste o comando manualmente antes de adicionar ao cron

---

## 📋 Variáveis de Ambiente Necessárias

```env
# Banco de Dados
DATABASE_URL=postgresql://usuario:senha@host:porta/database

# Gemini AI
GEMINI_API_KEY=sua_chave_gemini

# WhatsApp Bot
BOT_WHATSAPP_URL=http://seu-bot:porta/sendMessage
API_SECRET_KEY=sua_chave_secreta

# WeatherAPI (NOVO)
WEATHER_API_KEY=sua_chave_weatherapi
```

---

## 🎯 Próximos Passos

Após configuração:

1. **Adicionar handler no WhatsApp** para processar intent "Configurar Localização"
2. **Testar com múltiplos usuários** em horários diferentes
3. **Monitorar uso da API** do WeatherAPI (1M chamadas/mês)
4. **Ajustar prompts** do Gemini conforme feedback dos usuários

---

## 📞 Suporte

Documentação completa: [RESUMO_MATINAL_README.md](RESUMO_MATINAL_README.md)
