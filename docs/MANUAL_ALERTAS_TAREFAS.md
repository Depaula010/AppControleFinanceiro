# Manual de Alertas de Tarefas do Google Calendar

## Visão Geral

O sistema de **Alertas de Tarefas** permite que você receba notificações no WhatsApp antes dos seus compromissos agendados no Google Calendar. Você pode configurar quantos minutos antes deseja ser alertado e ativar/desativar os alertas a qualquer momento.

---

## Pré-requisitos

Antes de usar os alertas de tarefas, você precisa:

1. ✅ **Ter o Google Calendar conectado** ao sistema
   - Se ainda não conectou, siga as instruções em `/conectar-calendar` no WhatsApp

---

## Como Usar

### 1. Configuração Inicial (Setup da Tabela)

**Apenas para administradores do sistema**

Antes de usar pela primeira vez, o administrador deve criar a tabela de configurações:

```bash
GET https://seu-dominio.com/admin/setup-calendar-alert-table
```

Retorno esperado:
```
✅ Tabela CalendarAlertConfigs criada!
```

---

### 2. Ativar Alertas de Tarefas

#### Via API REST:

**Endpoint:** `POST /calendar-alerts/ativar/{usuario_id}`

**Corpo (opcional):**
```json
{
  "minutos_antes": 5
}
```

**Exemplo de Resposta:**
```json
{
  "status": "sucesso",
  "mensagem": "Alertas de tarefas ativados e alerta configurado para 5 minuto(s) antes",
  "config": {
    "alertas_tarefas_ativo": true,
    "minutos_antes": 5
  }
}
```

**Exemplos de uso:**

```bash
# Ativar com 1 minuto de antecedência (padrão)
curl -X POST https://seu-dominio.com/calendar-alerts/ativar/1

# Ativar com 10 minutos de antecedência
curl -X POST https://seu-dominio.com/calendar-alerts/ativar/1 \
  -H "Content-Type: application/json" \
  -d '{"minutos_antes": 10}'
```

---

### 3. Desativar Alertas de Tarefas

**Endpoint:** `POST /calendar-alerts/desativar/{usuario_id}`

**Exemplo:**
```bash
curl -X POST https://seu-dominio.com/calendar-alerts/desativar/1
```

**Resposta:**
```json
{
  "status": "sucesso",
  "mensagem": "Alertas de tarefas desativados",
  "config": {
    "alertas_tarefas_ativo": false,
    "minutos_antes": 5
  }
}
```

---

### 4. Consultar Configuração Atual

**Endpoint:** `GET /calendar-alerts/config/{usuario_id}`

**Exemplo:**
```bash
curl https://seu-dominio.com/calendar-alerts/config/1
```

**Resposta:**
```json
{
  "status": "sucesso",
  "config": {
    "alertas_tarefas_ativo": true,
    "minutos_antes": 5
  }
}
```

---

### 5. Atualizar Configuração (Forma Avançada)

**Endpoint:** `POST /calendar-alerts/config/{usuario_id}`

**Corpo:**
```json
{
  "ativo": true,
  "minutos_antes": 15
}
```

**Resposta:**
```json
{
  "status": "sucesso",
  "mensagem": "Alertas de tarefas ativados e alerta configurado para 15 minuto(s) antes",
  "config": {
    "alertas_tarefas_ativo": true,
    "minutos_antes": 15
  }
}
```

---

## Como Funcionam os Alertas

### Processamento Automático

O sistema executa um **cronjob a cada 1 minuto** que:

1. Busca todos os usuários com alertas ativos
2. Para cada usuário:
   - Verifica eventos que começarão em X minutos (conforme configurado)
   - Envia notificação via WhatsApp para cada evento encontrado

### Exemplo de Notificação

```
⏰ *Alerta de Tarefa*

📅 *Reunião com Cliente*
🕐 Horário: *14:30*
⚠️ Começa em *5 minutos*!
📍 Local: Escritório Central, Sala 201

📝 Apresentar proposta do projeto X e discutir próximos passos

📆 Calendário: Trabalho
```

---

## Configurações Disponíveis

### Parâmetro `minutos_antes`

- **Valor mínimo:** 1 minuto
- **Valor máximo:** 60 minutos
- **Valor padrão:** 1 minuto

**Exemplos de uso:**

- `1 minuto`: Para compromissos que você precisa sair imediatamente
- `5 minutos`: Tempo para se preparar e se deslocar
- `15 minutos`: Para compromissos que precisam de preparação
- `30 minutos`: Para eventos importantes ou que exigem deslocamento
- `60 minutos`: Para compromissos que precisam de muita preparação

---

## Tipos de Eventos Alertados

✅ **Eventos com horário específico** (ex: "Reunião às 14:30")
❌ **Eventos de dia inteiro** (ex: "Aniversário") - Não são alertados

---

## Múltiplos Calendários

Os alertas funcionam para **todos os calendários conectados** à sua conta Google, incluindo:

- Calendário principal
- Calendários compartilhados
- Calendários de outras pessoas (que você tenha acesso)

**Nota:** O sistema respeita a configuração de "calendários selecionados" do Google Calendar.

---

## Resolução de Problemas

### Não estou recebendo alertas

1. ✅ Verifique se os alertas estão ativados:
   ```bash
   GET /calendar-alerts/config/{seu_usuario_id}
   ```

2. ✅ Confirme se o Google Calendar está conectado:
   - Teste a conexão via `/admin/debug-calendar`

3. ✅ Verifique se você tem eventos próximos:
   - O evento deve estar dentro da janela de tempo configurada
   - Eventos de dia inteiro não geram alertas

4. ✅ Verifique se o cronjob está rodando:
   - No Docker, use: `docker logs meu-secretario-cron`

### Recebendo alertas duplicados

- Cada evento gera apenas **1 alerta** na janela de tempo configurada
- Se você receber duplicados, pode haver problema no cronjob

### Quero mudar o tempo de antecedência

```bash
curl -X POST https://seu-dominio.com/calendar-alerts/config/1 \
  -H "Content-Type: application/json" \
  -d '{"minutos_antes": 10}'
```

---

## Arquitetura Técnica

### Componentes

1. **CalendarAlertConfigs** (Tabela no PostgreSQL)
   - Armazena configurações de cada usuário

2. **CalendarAlertConfigService** (Serviço)
   - Gerencia configurações CRUD

3. **CalendarAlertService** (Serviço)
   - Busca eventos próximos
   - Formata e envia notificações

4. **processar_alertas_tarefas.py** (Cronjob)
   - Executado a cada 1 minuto pelo Ofelia
   - Processa alertas de todos os usuários ativos

5. **calendar_alerts_bp** (Blueprint Flask)
   - API REST para gerenciar configurações

### Fluxo de Execução

```
[Ofelia Scheduler]
    ↓ (a cada 1 minuto)
[processar_alertas_tarefas.py]
    ↓
[CalendarAlertConfigService.get_users_with_alerts_active()]
    ↓
[CalendarAlertService.process_alerts_for_user()]
    ↓ (para cada usuário)
[GoogleCalendarOAuthService.get_calendar_service()]
    ↓
[Busca eventos próximos no Google Calendar]
    ↓
[CalendarAlertService.send_event_alert()]
    ↓
[notification_service.enviar_notificacao_whatsapp()]
    ↓
[Bot WhatsApp envia mensagem]
```

---

## Perguntas Frequentes

### Posso ter configurações diferentes para cada calendário?

Não. A configuração de `minutos_antes` é global para todos os seus calendários.

### Os alertas funcionam para eventos recorrentes?

Sim! Cada ocorrência de um evento recorrente gera um alerta separado.

### Posso receber alertas múltiplos para o mesmo evento?

Não. O sistema envia apenas **1 alerta** por evento, dentro da janela de tempo configurada.

### Os alertas funcionam se eu estiver offline?

Sim! Os alertas são enviados pelo servidor. Você só precisa estar online no momento de receber a notificação no WhatsApp.

### Quantos eventos podem ser alertados simultaneamente?

Não há limite. Se você tiver 5 eventos começando ao mesmo tempo, receberá 5 alertas.

---

## Suporte

Em caso de problemas, verifique:

1. Logs do container: `docker logs meu-secretario-api`
2. Logs do cronjob: `docker logs meu-secretario-cron`
3. Status da API: `GET https://seu-dominio.com/`

---

**Última atualização:** 2025-12-10
