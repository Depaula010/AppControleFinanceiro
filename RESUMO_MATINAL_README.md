# 📅 Resumo Inteligente de Compromissos (Daily Briefing)

## Visão Geral

O **Resumo Matinal** é uma feature inteligente que envia automaticamente um resumo humanizado da sua agenda diária, incluindo informações de clima, análise de intervalos livres e sugestões personalizadas.

---

## ✨ Funcionalidades

### 🎯 O que o sistema faz automaticamente:

1. **Busca eventos do dia** no seu Google Calendar
2. **Obtém informações de clima** da sua cidade configurada
3. **Detecta localizações** em eventos (viagens, compromissos em outras cidades)
4. **Calcula intervalos livres** entre eventos
5. **Identifica tipo de evento** (remoto via Meet/Zoom ou presencial)
6. **Gera resumo humanizado** com IA (Gemini)
7. **Envia via WhatsApp** no horário configurado

---

## 📋 Exemplo de Resumo

```
☀️ Bom dia!

Seu dia hoje está movimentado com 3 compromissos:

• 9h - Reunião de Alinhamento (1h) [remoto]
• 14h - Academia (1h30) - leve roupa de treino
• 19h - Jantar com João em Restaurante Italiano - 15 min do escritório

💡 Você tem 3h livres entre 10h-13h - ótimo período para trabalho focado!

🌡️ Clima: 24°C, Parcialmente nublado ⛅
☔ Possível chuva à tarde (40%) - leve guarda-chuva

Tenha um ótimo dia! 🚀
```

---

## 🚀 Como Usar

### 1. **Configurar Localização (Necessário para clima)**

```
Você: "Configurar localização: São Paulo, SP"
Bot: "📍 Localização configurada: São Paulo, SP"
```

**Comandos alternativos:**
- "Minha cidade é Campinas"
- "Mudar localização para Rio de Janeiro, RJ"
- "Onde estou: Curitiba, PR"

**Consultar localização atual:**
```
Você: "Qual minha localização?"
Bot: "📍 Sua localização atual: São Paulo, SP"
```

### 2. **Configurar Horário do Resumo (Opcional)**

Por padrão, o resumo é enviado às **07:00**.

Para alterar:
```
Você: "Configurar resumo matinal às 8h"
Bot: "✅ Resumo matinal ativado e horário configurado para 08:00"
```

### 3. **Ativar/Desativar Resumo**

```
Você: "Desativar resumo matinal"
Bot: "✅ Resumo matinal desativado"

Você: "Ativar resumo matinal"
Bot: "✅ Resumo matinal ativado"
```

---

## 🛠️ Instalação e Configuração

### 1. **Executar Migrations**

```bash
# Migration 1: Adicionar campos de localização
python add_location_fields.py

# Migration 2: Adicionar campos de resumo matinal
python add_notification_config_fields.py
```

### 2. **Configurar API de Clima (WeatherAPI)**

1. Crie uma conta gratuita em [WeatherAPI.com](https://www.weatherapi.com/)
2. Obtenha sua API Key (1M chamadas/mês grátis)
3. Adicione ao `.env`:

```env
WEATHER_API_KEY=sua_chave_aqui
```

### 3. **Configurar Cron Job**

Adicione ao crontab para executar a cada hora:

```bash
# Resumo Matinal (executado a cada hora, verifica se há usuários para notificar)
0 * * * * cd /caminho/do/projeto && /caminho/do/.venv/bin/python processar_resumo_matinal.py >> /var/log/resumo_matinal.log 2>&1
```

**Exemplo para horários específicos (mais eficiente):**

```bash
# Executar apenas às 6h, 7h, 8h e 9h (horários comuns)
0 6,7,8,9 * * * cd /caminho/do/projeto && /caminho/do/.venv/bin/python processar_resumo_matinal.py >> /var/log/resumo_matinal.log 2>&1
```

---

## 📊 Arquitetura

### **Fluxo de Dados:**

```
1. Cron Job (horário configurado)
   ↓
2. NotificationConfigService.get_users_with_resumo_matinal_active()
   ↓
3. DailyBriefingService.prepare_briefing_data()
   ├── GoogleCalendarOAuthService (buscar eventos)
   ├── WeatherService (buscar clima)
   └── Análise de gaps, tipos de eventos, etc.
   ↓
4. GeminiService.generate_daily_briefing()
   ↓
5. NotificationService.enviar_notificacao_whatsapp()
```

### **Arquivos Criados/Modificados:**

**Novos Arquivos:**
- `app/services/daily_briefing_service.py` - Serviço principal
- `app/services/weather_service.py` - Integração com clima
- `app/services/location_service.py` - Gerenciamento de localização
- `processar_resumo_matinal.py` - Processador cron job
- `add_location_fields.py` - Migration localização
- `add_notification_config_fields.py` - Migration resumo matinal

**Arquivos Modificados:**
- `app/services/gemini_service.py`:
  - `generate_daily_briefing()` - Gera resumo humanizado
  - `extract_location_config()` - Extrai cidade/estado
  - Atualizado `get_message_intent()` com "Configurar Localização"

- `app/services/notification_config_service.py`:
  - `update_resumo_matinal_config()` - Atualiza configuração
  - `get_users_with_resumo_matinal_active()` - Busca usuários

---

## 🎨 Recursos Inteligentes

### **1. Detecção Automática de Clima em Múltiplas Cidades**

Se você tem eventos em cidades diferentes da sua localização padrão, o sistema detecta automaticamente e inclui o clima de lá também:

```
📍 Clima em São Paulo: 24°C, sol ☀️
📍 Clima em Campinas: 22°C, nublado ☁️ (você tem evento lá)
```

### **2. Análise de Intervalos Livres**

O sistema calcula automaticamente horários livres entre compromissos e sugere o melhor uso:

```
💡 Horários livres:
• 10:00-14:00 (4h) - ideal para trabalho focado
• 15:30-18:00 (2h30) - tempo para almoço e descanso
```

### **3. Detecção de Tipo de Evento**

Identifica automaticamente se o evento é:
- **Remoto**: Meet, Zoom, Teams, etc.
- **Presencial**: Baseado em localização física

### **4. Sugestões Contextuais**

A IA analisa seu dia e oferece dicas úteis:
- "Saia cedo - trânsito pesado nesse horário"
- "Leve guarda-chuva - alta chance de chuva"
- "Leve roupa de treino" (se detectar academia)

---

## 🔧 Configurações Avançadas

### **Tabela: Usuarios**

Novos campos:
- `cidade` VARCHAR(100) - Cidade do usuário
- `estado` VARCHAR(2) - Estado (sigla)

### **Tabela: NotificationConfigs**

Novos campos:
- `resumo_matinal_ativo` BOOLEAN - Ativar/desativar resumo
- `resumo_matinal_hora` TIME - Horário de envio

---

## 🧪 Testes

### **Testar Manualmente:**

```bash
# Executar processador diretamente
python processar_resumo_matinal.py
```

### **Simular Horário Específico:**

Edite temporariamente `processar_resumo_matinal.py`:

```python
# Linha: hora_atual = datetime.now().time()...
hora_atual = time(7, 0)  # Simular 07:00
```

---

## 📖 Comandos WhatsApp

| Comando | Descrição |
|---------|-----------|
| `Configurar localização: [Cidade], [Estado]` | Define sua cidade |
| `Qual minha localização?` | Consulta localização atual |
| `Configurar resumo matinal às [hora]` | Define horário |
| `Ativar resumo matinal` | Ativa notificação |
| `Desativar resumo matinal` | Desativa notificação |

---

## 🌐 API Externa Utilizada

**WeatherAPI** ([weatherapi.com](https://www.weatherapi.com/))

- **Plano Gratuito**: 1.000.000 chamadas/mês
- **Endpoint usado**: `/current.json` (clima atual)
- **Dados retornados**: Temperatura, condição, umidade, sensação térmica

---

## 🔒 Segurança e Privacidade

- ✅ Localização armazenada localmente no banco de dados
- ✅ API Key de clima não exposta ao usuário
- ✅ Cada usuário controla suas próprias configurações
- ✅ Dados de calendário buscados via OAuth (sem armazenamento)

---

## 🐛 Troubleshooting

### **Problema: Resumo não está sendo enviado**

1. Verifique se o cron job está rodando:
```bash
grep CRON /var/log/syslog
```

2. Verifique logs do processador:
```bash
tail -f /var/log/resumo_matinal.log
```

3. Verifique configuração do usuário no banco:
```sql
SELECT resumo_matinal_ativo, resumo_matinal_hora
FROM NotificationConfigs
WHERE usuario_id = 1;
```

### **Problema: Clima não aparece**

1. Verifique se `WEATHER_API_KEY` está configurada:
```bash
echo $WEATHER_API_KEY
```

2. Verifique se localização está configurada:
```sql
SELECT cidade, estado FROM Usuarios WHERE id = 1;
```

3. Teste a API manualmente:
```bash
curl "http://api.weatherapi.com/v1/current.json?key=SUA_KEY&q=São Paulo&lang=pt"
```

### **Problema: IA gerando resumos estranhos**

Isso pode acontecer se os dados estiverem inconsistentes. Verifique:
- Eventos do Google Calendar têm horários válidos
- Descrições dos eventos não estão muito longas
- Gemini API está respondendo corretamente

---

## 🚀 Próximas Melhorias (Futuro)

- [ ] Sugestão de rota mais rápida (integração com Google Maps)
- [ ] Previsão de clima para próximos dias
- [ ] Resumo semanal (domingo à noite)
- [ ] Integração com tráfego em tempo real
- [ ] Sugestões de preparação (ex: "reserve mesa no restaurante")
- [ ] Análise de produtividade semanal

---

## 📞 Suporte

Para dúvidas ou problemas, consulte:
- [README principal](README.md)
- [Implementação Completa](IMPLEMENTACAO_COMPLETA.md)
- [Configuração de Cron Jobs](CRON_JOBS_SETUP.md)

---

**Desenvolvido com ❤️ usando:**
- Python + Flask
- Google Gemini AI
- Google Calendar API
- WeatherAPI
- PostgreSQL

---

## 📝 Changelog

### v1.0.0 (2025-11-22)
- ✅ Implementação inicial
- ✅ Integração com WeatherAPI
- ✅ Geração de resumo com IA
- ✅ Detecção inteligente de eventos
- ✅ Cálculo de intervalos livres
- ✅ Suporte a múltiplas cidades
