# Relatório Mensal Automático - Guia de Uso

## 📊 Visão Geral

Sistema de envio automático de relatórios financeiros mensais via WhatsApp, totalmente configurável pelo próprio usuário.

## 🚀 Configuração Inicial

### 1. Criar Tabela no Banco

Execute uma única vez:

```bash
GET https://seu-dominio.com/admin/setup-monthly-reports-table
```

### 2. Configurar Cron Jobs

Siga as instruções em [CRON_JOBS_SETUP.md](./CRON_JOBS_SETUP.md)

## 💬 Como os Usuários Configuram

### Ativar Relatório Mensal

```
📱 WhatsApp: "ativar relatório mensal no início do mês às 8h"

🤖 Bot: ✅ Relatório mensal ativado!

        📅 Momento: início do mês (dia 1)
        🕐 Horário: 08:00

        📊 O que você vai receber:
        • Gastos totais do mês
        • Top 5 categorias
        • Comparação com mês anterior
        • Status dos potes de gastos
        • Contas pagas vs pendentes
        • Gráfico de pizza com categorias
```

### Alterar Configurações

```
📱 WhatsApp: "mudar hora do relatório para 14:00"

🤖 Bot: ✅ Configuração atualizada!

        📅 Momento: início do mês (dia 1)
        🕐 Horário: 14:00
```

### Consultar Configuração Atual

```
📱 WhatsApp: "como está configurado meu relatório?"

🤖 Bot: 📊 CONFIGURAÇÃO DO RELATÓRIO MENSAL

        Status: ✅ Ativo
        Momento: Início do mês (dia 1)
        Horário: 08:00
```

### Desativar Relatório

```
📱 WhatsApp: "desativar relatório mensal"

🤖 Bot: ✅ Relatório mensal desativado com sucesso!
```

## ⚙️ Opções de Configuração

### Momento de Envio

| Opção | Descrição | Relatório Enviado |
|-------|-----------|-------------------|
| **Início do Mês** | Dia 1 de cada mês | Mês **anterior** |
| **Fim do Mês** | Último dia de cada mês | Mês **atual** |

### Horário de Envio

- Qualquer horário entre **00:00** e **23:59**
- Formato: `HH:MM` (ex: 08:00, 14:30, 22:00)
- Janela de tolerância: ±5 minutos

### Exemplos de Comandos

```
✅ "ativar relatório mensal"
✅ "quero receber relatório todo dia 1 às 10h"
✅ "configurar relatório no fim do mês às 08:00"
✅ "mudar para início do mês"
✅ "alterar horário para 14h"
✅ "desativar relatório"
✅ "como está meu relatório?"
```

## 📄 Conteúdo do Relatório

### Mensagem de Texto

```
📊 RELATÓRIO MENSAL - NOVEMBRO/2025
Olá, João!

💰 RESUMO FINANCEIRO
• Receitas: R$ 5.000,00
• Despesas: R$ 3.250,50
• Saldo: R$ 1.749,50
• Transações: 87

📈 COMPARAÇÃO COM MÊS ANTERIOR
• Mês anterior: R$ 2.890,00
• Variação: R$ +360,50 (+12.5%)

🏆 TOP 5 CATEGORIAS
1. Alimentação: R$ 850,00 (26.2%)
2. Transporte: R$ 620,00 (19.1%)
3. Lazer: R$ 480,00 (14.8%)
4. Saúde: R$ 350,00 (10.8%)
5. Educação: R$ 280,00 (8.6%)

🎯 POTES DE GASTOS
✅ Alimentação:
   Usado: R$ 850,00 / R$ 1.000,00
   Saldo: R$ 150,00 (85.0%)

✅ Lazer:
   Usado: R$ 480,00 / R$ 500,00
   Saldo: R$ 20,00 (96.0%)

💳 STATUS DE CONTAS
✅ Pagas: 72 (R$ 2.980,50)
⏳ Pendentes: 15 (R$ 270,00)

📷 Veja o gráfico de pizza anexo para visualização das categorias!
```

### Gráfico de Pizza

![Exemplo de Gráfico](docs/exemplo-grafico-relatorio.png)

- Top 5 categorias visualizadas
- Cores distintas para cada categoria
- Valores e percentuais
- Legenda formatada

## 🔧 Endpoints da API

### Para Administradores

#### Criar Tabela
```bash
GET /admin/setup-monthly-reports-table
```

#### Trigger Manual - Início do Mês
```bash
POST /admin/trigger-monthly-reports-inicio
Header: x-api-key: SUA_API_KEY
```

#### Trigger Manual - Fim do Mês
```bash
POST /admin/trigger-monthly-reports-fim
Header: x-api-key: SUA_API_KEY
```

#### Testar Relatório de Um Usuário
```bash
POST /admin/test-monthly-report/{usuario_id}?momento=INICIO_MES
Header: x-api-key: SUA_API_KEY
```

### Resposta de Sucesso

```json
{
  "status": "sucesso",
  "momento_envio": "INICIO_MES",
  "horario_processamento": "2025-01-01 08:00:15",
  "total_usuarios": 5,
  "enviados_sucesso": 5,
  "enviados_erro": 0,
  "duracao_segundos": 8.42,
  "usuarios_processados": [
    {
      "usuario_id": 1,
      "nome": "João Silva",
      "hora_configurada": "08:00:00",
      "status": "enviado",
      "mensagem": "Relatório enviado com sucesso",
      "grafico_enviado": true
    }
  ]
}
```

## 🗄️ Estrutura do Banco

### Tabela: MonthlyReportConfigs

```sql
CREATE TABLE IF NOT EXISTS MonthlyReportConfigs (
    usuario_id INT PRIMARY KEY,
    ativo BOOLEAN DEFAULT TRUE,
    momento_envio VARCHAR(20) DEFAULT 'INICIO_MES'
        CHECK (momento_envio IN ('INICIO_MES', 'FIM_MES')),
    hora_envio TIME DEFAULT '08:00:00',
    created_at TIMESTAMP DEFAULT NOW(),
    updated_at TIMESTAMP DEFAULT NOW(),
    FOREIGN KEY (usuario_id) REFERENCES Usuarios(id) ON DELETE CASCADE
);
```

### Consultar Configurações

```sql
SELECT
    u.nome,
    u.numero_whatsapp,
    mrc.ativo AS relatorio_ativo,
    mrc.momento_envio,
    mrc.hora_envio
FROM Usuarios u
LEFT JOIN MonthlyReportConfigs mrc ON u.id = mrc.usuario_id;
```

## 📋 Arquivos Criados

```
app/
├── services/
│   ├── monthly_report_config_service.py      # Gerencia configurações
│   ├── monthly_report_service.py             # Gera dados e relatórios
│   └── monthly_report_processor_service.py   # Processa envios
├── routes/
│   ├── admin.py                              # Endpoints admin (modificado)
│   └── webhooks.py                           # Handler WhatsApp (modificado)

docs/
├── CRON_JOBS_SETUP.md                        # Configuração detalhada de cron
└── RELATORIO_MENSAL_README.md                # Este arquivo
```

## 🔍 Logs e Monitoramento

### Ver Logs do Container

```bash
docker logs meu-secretario-api | grep MONTHLY-REPORT
```

### Exemplo de Log

```
[MONTHLY-REPORT] 📊 Processando relatórios de INICIO DO MES...
[MONTHLY-REPORT-CONFIG] Ação: ativar, Momento: INICIO_MES, Hora: 08:00
[MONTHLY-REPORT] ✅ Processamento concluído: 3 enviados
```

## ❓ FAQ

### 1. Qual a diferença entre início e fim do mês?

- **Início do mês (dia 1)**: Relatório do **mês anterior** (ex: dia 1/12 → relatório de Novembro)
- **Fim do mês (último dia)**: Relatório do **mês atual** (ex: dia 31/12 → relatório de Dezembro)

### 2. Posso receber nos dois momentos?

Não. Cada usuário pode escolher apenas **um momento** (início OU fim do mês).

### 3. E se eu não tiver transações no mês?

O relatório não será enviado. O sistema detecta quando não há dados suficientes.

### 4. Posso testar antes de ativar?

Sim! Use o endpoint de teste:
```bash
POST /admin/test-monthly-report/SEU_ID?momento=INICIO_MES
```

### 5. O que acontece se eu mudar a hora no dia do envio?

A nova hora será aplicada imediatamente. Se já passou da janela de envio, receberá no próximo mês.

### 6. Como desativar temporariamente?

```
📱 WhatsApp: "desativar relatório mensal"
```

Para reativar:
```
📱 WhatsApp: "ativar relatório mensal"
```

## 🛠️ Troubleshooting

### Relatório não chegou

1. ✅ Verificar se está ativo: `"como está meu relatório?"`
2. ✅ Verificar horário configurado (janela de ±5min)
3. ✅ Verificar se há transações no período
4. ✅ Testar manualmente via endpoint de teste

### Erro de configuração

```
❌ Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'
```

Use apenas:
- "início do mês" / "dia 1" / "começo do mês"
- "fim do mês" / "último dia" / "final do mês"

### Gráfico não foi enviado

O sistema prioriza a mensagem de texto. Se o gráfico falhar, a mensagem ainda é enviada.

## 📞 Suporte

Para problemas técnicos:
1. Verificar logs do container
2. Consultar [CRON_JOBS_SETUP.md](./CRON_JOBS_SETUP.md)
3. Testar endpoints manualmente
4. Verificar banco de dados

## 🎯 Próximos Passos

Após implementar:

1. ✅ Criar tabela: `GET /admin/setup-monthly-reports-table`
2. ✅ Configurar cron jobs no servidor
3. ✅ Testar com seu usuário: `POST /admin/test-monthly-report/1`
4. ✅ Instruir usuários sobre comandos via WhatsApp
5. ✅ Monitorar logs nos primeiros dias

## 📝 Notas de Versão

### v1.0.0 - Lançamento Inicial

**Funcionalidades**:
- ✅ Configuração via WhatsApp
- ✅ Escolha de momento (início/fim do mês)
- ✅ Escolha de horário personalizado
- ✅ Relatório completo com estatísticas
- ✅ Gráfico de pizza automático
- ✅ Comparação com mês anterior
- ✅ Status de potes de gastos
- ✅ Contas pagas vs pendentes
- ✅ Cron jobs automatizados
- ✅ Endpoints de teste e administração

**Tecnologias**:
- Python 3.11 + Flask
- SQLAlchemy + SQL Server
- Matplotlib (gráficos)
- Gemini AI (NLP)
- WhatsApp Bot (Baileys)
- Docker + Cron
