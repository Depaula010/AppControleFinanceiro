# Guia de Testes - Relatório Mensal Automático

## 📋 Checklist de Testes

Use este guia para validar a implementação completa do relatório mensal automático.

## 🔧 Pré-requisitos

- [ ] Aplicação rodando localmente ou em produção
- [ ] Acesso ao banco de dados SQL Server
- [ ] API_SECRET_KEY configurada
- [ ] Bot WhatsApp funcionando
- [ ] Pelo menos 1 usuário cadastrado com transações

## 1️⃣ Teste de Infraestrutura

### 1.1 Criar Tabela no Banco

```bash
curl -X GET http://localhost:8000/admin/setup-monthly-reports-table
```

**Resultado Esperado**:
```json
{
  "status": "sucesso",
  "mensagem": "✅ Tabela MonthlyReportConfigs criada com sucesso!"
}
```

**Validação no Banco**:
```sql
-- Verificar se tabela foi criada
SELECT * FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME = 'MonthlyReportConfigs';

-- Ver estrutura
EXEC sp_help 'MonthlyReportConfigs';
```

### 1.2 Verificar Imports

```bash
# No container ou ambiente Python
docker exec -it meu-secretario-api python -c "
from app.services.monthly_report_config_service import criar_tabela_monthly_report_configs
from app.services.monthly_report_service import generate_monthly_report_data
from app.services.monthly_report_processor_service import processar_relatorios_mensais
print('✅ Todos os imports funcionaram!')
"
```

## 2️⃣ Teste de Configuração via WhatsApp

### 2.1 Testar Detecção de Intenção

Envie via WhatsApp:

```
1. "configurar relatório mensal"
2. "ativar relatório mensal"
3. "como está meu relatório?"
```

**Resultado Esperado**: Bot reconhece a intenção e responde.

### 2.2 Ativar Relatório

```
📱 Enviar: "ativar relatório mensal no início do mês às 10h"
```

**Resposta Esperada**:
```
✅ Relatório mensal ativado!

📅 Momento: início do mês (dia 1)
🕐 Horário: 10:00

📊 O que você vai receber:
• Gastos totais do mês
• Top 5 categorias
• Comparação com mês anterior
• Status dos potes de gastos
• Contas pagas vs pendentes
• Gráfico de pizza com categorias

Você receberá automaticamente no horário configurado!
```

**Validação no Banco**:
```sql
SELECT * FROM MonthlyReportConfigs WHERE usuario_id = 1;
```

Deve mostrar:
- `ativo = 1`
- `momento_envio = 'INICIO_MES'`
- `hora_envio = '10:00:00'`

### 2.3 Consultar Configuração

```
📱 Enviar: "como está configurado meu relatório?"
```

**Resposta Esperada**:
```
📊 CONFIGURAÇÃO DO RELATÓRIO MENSAL

Status: ✅ Ativo
Momento: Início do mês (dia 1)
Horário: 10:00

Para alterar, envie: 'configurar relatório mensal no início do mês às 10h'
```

### 2.4 Alterar Configurações

```
📱 Enviar: "mudar para fim do mês às 14h"
```

**Resposta Esperada**:
```
✅ Configuração atualizada!

📅 Momento: fim do mês (último dia)
🕐 Horário: 14:00

O relatório será enviado automaticamente no horário configurado.
```

### 2.5 Desativar Relatório

```
📱 Enviar: "desativar relatório mensal"
```

**Resposta Esperada**:
```
✅ Relatório mensal desativado com sucesso!

Para reativar, envie: 'ativar relatório mensal'
```

**Validação no Banco**:
```sql
SELECT ativo FROM MonthlyReportConfigs WHERE usuario_id = 1;
-- Deve retornar: 0
```

## 3️⃣ Teste de Geração de Relatório

### 3.1 Preparar Dados de Teste

Execute no banco para garantir dados:

```sql
-- Ver transações do usuário no mês atual
SELECT COUNT(*) AS total_transacoes
FROM Transacoes
WHERE usuario_id = 1
  AND MONTH(data_transacao) = MONTH(GETDATE())
  AND YEAR(data_transacao) = YEAR(GETDATE());

-- Se não houver, inserir transações de teste
INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, data_transacao, tipo_transacao, consolidada)
VALUES
  (1, 1, 5, 'Teste Alimentação', 150.00, GETDATE(), 'Despesa', 1),
  (1, 1, 6, 'Teste Transporte', 80.00, GETDATE(), 'Despesa', 1),
  (1, 1, 7, 'Teste Lazer', 120.00, GETDATE(), 'Despesa', 1),
  (1, 1, 1, 'Teste Salário', 5000.00, GETDATE(), 'Renda', 1);
```

### 3.2 Testar Geração de Dados

Execute em Python:

```python
from app.services.monthly_report_service import generate_monthly_report_data

# Testar relatório de início do mês (mês anterior)
dados_inicio = generate_monthly_report_data(usuario_id=1, momento_envio='INICIO_MES')
print("📊 Dados do relatório (INICIO_MES):")
print(f"Mês: {dados_inicio['mes']}/{dados_inicio['ano']}")
print(f"Total transações: {dados_inicio['totais']['total_transacoes']}")
print(f"Despesas: R$ {dados_inicio['totais']['total_despesas']:.2f}")
print(f"Rendas: R$ {dados_inicio['totais']['total_rendas']:.2f}")

# Testar relatório de fim do mês (mês atual)
dados_fim = generate_monthly_report_data(usuario_id=1, momento_envio='FIM_MES')
print("\n📊 Dados do relatório (FIM_MES):")
print(f"Mês: {dados_fim['mes']}/{dados_fim['ano']}")
```

### 3.3 Testar Geração de Gráfico

```python
from app.services.monthly_report_service import (
    generate_monthly_report_data,
    generate_monthly_report_chart
)

# Gerar dados
dados = generate_monthly_report_data(usuario_id=1, momento_envio='FIM_MES')

# Gerar gráfico
chart_bytes = generate_monthly_report_chart(dados)

# Salvar para visualizar
with open('/tmp/teste_grafico.png', 'wb') as f:
    f.write(chart_bytes)

print(f"✅ Gráfico gerado: {len(chart_bytes)} bytes")
print("📁 Salvo em: /tmp/teste_grafico.png")
```

### 3.4 Testar Formatação de Mensagem

```python
from app.services.monthly_report_service import (
    generate_monthly_report_data,
    format_report_message
)

# Gerar dados
dados = generate_monthly_report_data(usuario_id=1, momento_envio='FIM_MES')

# Formatar mensagem
mensagem = format_report_message(dados, nome_usuario='João Silva')

print("📱 MENSAGEM FORMATADA:")
print("="*60)
print(mensagem)
print("="*60)
```

## 4️⃣ Teste de Envio Manual

### 4.1 Ativar Configuração

Certifique-se de que o relatório está ativo:

```sql
UPDATE MonthlyReportConfigs
SET ativo = 1, momento_envio = 'INICIO_MES', hora_envio = '10:00:00'
WHERE usuario_id = 1;
```

### 4.2 Testar Endpoint de Envio Manual

```bash
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  "http://localhost:8000/admin/test-monthly-report/1?momento=INICIO_MES"
```

**Resultado Esperado**:
```json
{
  "status": "sucesso",
  "sucesso": true,
  "usuario_id": 1,
  "nome": "João Silva",
  "mensagem_enviada": true,
  "grafico_enviado": true,
  "dados": {
    "mes": 11,
    "ano": 2025,
    "totais": {
      "total_despesas": 350.0,
      "total_rendas": 5000.0,
      "saldo_periodo": 4650.0,
      "total_transacoes": 4
    },
    "top_categorias": [...]
  }
}
```

**Validação**: Verificar WhatsApp do usuário recebeu:
1. ✅ Mensagem de texto com estatísticas
2. ✅ Imagem do gráfico de pizza

### 4.3 Testar com Usuário Sem Dados

Criar usuário sem transações:

```bash
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  "http://localhost:8000/admin/test-monthly-report/999?momento=FIM_MES"
```

**Resultado Esperado**:
```json
{
  "status": "erro",
  "sucesso": false,
  "erro": "Nenhuma transação encontrada no período"
}
```

## 5️⃣ Teste de Processamento em Lote

### 5.1 Preparar Múltiplos Usuários

```sql
-- Ativar relatório para vários usuários
INSERT INTO MonthlyReportConfigs (usuario_id, ativo, momento_envio, hora_envio)
VALUES
  (1, 1, 'INICIO_MES', '10:00:00'),
  (2, 1, 'INICIO_MES', '10:05:00'),
  (3, 1, 'FIM_MES', '14:00:00');
```

### 5.2 Simular Horário e Testar

```bash
# Testar processamento de INÍCIO DO MÊS às 10h
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  http://localhost:8000/admin/trigger-monthly-reports-inicio
```

**Resultado Esperado** (se hora atual for ~10:00):
```json
{
  "status": "sucesso",
  "momento_envio": "INICIO_MES",
  "horario_processamento": "2025-01-01 10:00:15",
  "total_usuarios": 2,
  "enviados_sucesso": 2,
  "enviados_erro": 0,
  "duracao_segundos": 5.23,
  "usuarios_processados": [
    {
      "usuario_id": 1,
      "nome": "João Silva",
      "hora_configurada": "10:00:00",
      "status": "enviado",
      "mensagem": "Relatório enviado com sucesso",
      "grafico_enviado": true
    },
    {
      "usuario_id": 2,
      "nome": "Maria Santos",
      "hora_configurada": "10:05:00",
      "status": "enviado",
      "mensagem": "Relatório enviado com sucesso",
      "grafico_enviado": true
    }
  ]
}
```

### 5.3 Testar Fora da Janela de Horário

Se hora atual for 15:00, o mesmo endpoint deve retornar:

```json
{
  "status": "sucesso",
  "momento_envio": "INICIO_MES",
  "total_usuarios": 0,
  "enviados_sucesso": 0,
  "enviados_erro": 0
}
```

## 6️⃣ Teste de Detecção de Erros

### 6.1 Testar Validação de Hora Inválida

Via WhatsApp:
```
📱 Enviar: "configurar relatório às 25:00"
```

**Resposta Esperada**:
```
❌ hora_envio deve estar no formato 'HH:MM' ou 'HH:MM:SS'

Exemplos válidos:
• Momento: 'início do mês' ou 'fim do mês'
• Horário: '08:00' ou '14:30'
```

### 6.2 Testar Momento Inválido

```bash
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  "http://localhost:8000/admin/test-monthly-report/1?momento=MEIO_MES"
```

**Resposta Esperada**:
```json
{
  "status": "erro",
  "mensagem": "Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'"
}
```

## 7️⃣ Teste de Cálculo de Período

### 7.1 Verificar Lógica de Mês

Execute em Python:

```python
from app.services.monthly_report_service import calcular_periodo_relatorio
from datetime import datetime

# Simular: Hoje é 1º de Janeiro
# INICIO_MES deve retornar Dezembro
mes, ano, inicio, fim = calcular_periodo_relatorio('INICIO_MES')
print(f"INICIO_MES: {mes}/{ano} ({inicio} até {fim})")
# Esperado: 12/2024 (2024-12-01 até 2024-12-31)

# FIM_MES deve retornar Janeiro
mes, ano, inicio, fim = calcular_periodo_relatorio('FIM_MES')
print(f"FIM_MES: {mes}/{ano} ({inicio} até {fim})")
# Esperado: 1/2025 (2025-01-01 até 2025-01-31)
```

## 8️⃣ Teste de Integração Completa

### Checklist Final

- [ ] Tabela criada com sucesso
- [ ] Configuração via WhatsApp funciona
- [ ] Consulta de configuração funciona
- [ ] Alteração de momento funciona
- [ ] Alteração de horário funciona
- [ ] Desativação funciona
- [ ] Reativação funciona
- [ ] Geração de dados funciona (INICIO_MES)
- [ ] Geração de dados funciona (FIM_MES)
- [ ] Geração de gráfico funciona
- [ ] Formatação de mensagem funciona
- [ ] Envio manual funciona
- [ ] Processamento em lote funciona
- [ ] Filtro de janela de horário funciona
- [ ] Validações de erro funcionam
- [ ] Logs estão sendo gerados

## 9️⃣ Teste de Produção

### Antes de Deploy

1. **Backup do Banco**
   ```sql
   BACKUP DATABASE SeuBanco
   TO DISK = '/backup/pre-relatorio-mensal.bak';
   ```

2. **Testar em Staging**
   - Executar todos os testes acima
   - Validar envio real via WhatsApp
   - Verificar logs

3. **Configurar Cron Jobs**
   - Seguir [CRON_JOBS_SETUP.md](./CRON_JOBS_SETUP.md)
   - Testar execução manual
   - Validar variáveis de ambiente

### Pós-Deploy

1. **Dia 1 do Mês** (INICIO_MES)
   - Monitorar logs às 07:00, 08:00, 09:00, 10:00
   - Validar envios para usuários configurados
   - Verificar erros

2. **Último Dia do Mês** (FIM_MES)
   - Monitorar logs nos horários configurados
   - Validar envios
   - Verificar erros

3. **Logs de Monitoramento**
   ```bash
   # Ver logs em tempo real
   docker logs -f meu-secretario-api | grep MONTHLY-REPORT

   # Ver últimos 100 logs
   docker logs meu-secretario-api --tail 100 | grep MONTHLY-REPORT
   ```

## 🐛 Troubleshooting de Testes

### Erro: "Tabela já existe"
**Solução**: Normal se executou setup múltiplas vezes. Ignore ou delete e recrie.

### Erro: "Módulo não encontrado"
**Solução**: Verificar imports no topo dos arquivos. Reiniciar container.

### Erro: "Usuário não encontrado"
**Solução**: Criar usuário de teste ou usar ID existente.

### Gráfico não gera
**Solução**: Verificar se há dados. Usar `generate_monthly_report_data()` primeiro.

### WhatsApp não recebe
**Solução**:
1. Verificar BOT_WHATSAPP_URL
2. Verificar API_SECRET_KEY
3. Testar endpoint de notificação diretamente

## ✅ Critérios de Sucesso

O sistema está pronto para produção quando:

- ✅ Todos os 9 testes acima passam sem erros
- ✅ Envio manual funciona para múltiplos usuários
- ✅ Processamento em lote filtra corretamente por horário
- ✅ Validações de erro retornam mensagens claras
- ✅ Logs são gerados e compreensíveis
- ✅ Documentação está completa (README + CRON_JOBS)
- ✅ Cron jobs estão configurados no servidor
- ✅ Backup do banco foi feito
- ✅ Usuários foram instruídos sobre comandos

## 📊 Relatório de Teste (Template)

```
RELATÓRIO DE TESTES - RELATÓRIO MENSAL AUTOMÁTICO
Data: _______________
Testador: _______________

[ ] 1. Infraestrutura
    [ ] Tabela criada
    [ ] Imports funcionam

[ ] 2. Configuração via WhatsApp
    [ ] Ativar
    [ ] Consultar
    [ ] Alterar
    [ ] Desativar

[ ] 3. Geração de Relatório
    [ ] Dados gerados (INICIO_MES)
    [ ] Dados gerados (FIM_MES)
    [ ] Gráfico gerado
    [ ] Mensagem formatada

[ ] 4. Envio Manual
    [ ] Envio com dados
    [ ] Envio sem dados
    [ ] WhatsApp recebido

[ ] 5. Processamento em Lote
    [ ] Múltiplos usuários
    [ ] Filtro de horário
    [ ] Fora da janela

[ ] 6. Validações
    [ ] Hora inválida
    [ ] Momento inválido
    [ ] Erros tratados

[ ] 7. Cálculo de Período
    [ ] INICIO_MES correto
    [ ] FIM_MES correto

[ ] 8. Integração Completa
    [ ] Fluxo end-to-end

[ ] 9. Produção
    [ ] Cron configurado
    [ ] Backup feito
    [ ] Logs funcionando

OBSERVAÇÕES:
_________________________________________________
_________________________________________________
_________________________________________________

APROVADO PARA PRODUÇÃO: [ ] SIM  [ ] NÃO
```
