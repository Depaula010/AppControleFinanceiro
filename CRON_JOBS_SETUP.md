# Configuração de Cron Jobs - Relatórios Mensais Automáticos

Este documento descreve como configurar os cron jobs no servidor Contabo para envio automático de relatórios mensais.

## Pré-requisitos

1. Acesso SSH ao servidor Contabo
2. Container Docker `meu-secretario-api` rodando
3. API Secret Key configurada (`API_SECRET_KEY` no .env)
4. Tabela `MonthlyReportConfigs` criada no banco

## Criar Tabela no Banco

Antes de configurar os cron jobs, execute este endpoint para criar a tabela:

```bash
curl https://SEU-DOMINIO.com/admin/setup-monthly-reports-table
```

## Arquitetura

O sistema possui **2 endpoints** para relatórios mensais:

### 1. Relatórios de Início do Mês
- **Endpoint**: `POST /admin/trigger-monthly-reports-inicio`
- **Quando executar**: Dia 1 de cada mês
- **Relatório**: Refere-se ao **mês anterior**
- **Exemplo**: No dia 1º de Dezembro, envia relatório de Novembro

### 2. Relatórios de Fim do Mês
- **Endpoint**: `POST /admin/trigger-monthly-reports-fim`
- **Quando executar**: Último dia de cada mês (28, 29, 30 ou 31)
- **Relatório**: Refere-se ao **mês atual**
- **Exemplo**: No dia 31 de Dezembro, envia relatório de Dezembro

## Configuração dos Cron Jobs

### Passo 1: Acessar o servidor via SSH

```bash
ssh root@SEU-IP-CONTABO
```

### Passo 2: Editar crontab

```bash
crontab -e
```

### Passo 3: Adicionar as entradas

Adicione as seguintes linhas no arquivo crontab:

```cron
# ========================================
# RELATÓRIOS MENSAIS AUTOMÁTICOS
# ========================================

# Relatórios de INÍCIO DO MÊS (dia 1)
# Executa a cada hora no dia 1 de cada mês
0 * 1 * * docker exec meu-secretario-api curl -X POST \
  -H "x-api-key: ${API_SECRET_KEY}" \
  http://localhost:8000/admin/trigger-monthly-reports-inicio

# Relatórios de FIM DO MÊS (último dia)
# Executa a cada hora e verifica se amanhã é dia 1 (ou seja, hoje é o último dia)
0 * * * * [ $(date -d tomorrow +\%d) -eq 1 ] && docker exec meu-secretario-api curl -X POST \
  -H "x-api-key: ${API_SECRET_KEY}" \
  http://localhost:8000/admin/trigger-monthly-reports-fim
```

### Passo 4: Configurar variável de ambiente (Opcional)

Se preferir não deixar a API key exposta no crontab, adicione ao `.bashrc` ou `.profile`:

```bash
export API_SECRET_KEY="sua-chave-secreta-aqui"
```

E recarregue:

```bash
source ~/.bashrc
```

## Como Funciona

### Fluxo de Execução

1. **Cron Job Executa**: A cada hora nos dias configurados
2. **Endpoint Filtra Usuários**: Busca usuários com configuração ativa + janela de ±5 minutos da hora configurada
3. **Para Cada Usuário Elegível**:
   - Calcula período do relatório (mês anterior ou atual)
   - Gera dados estatísticos (gastos, categorias, potes, contas)
   - Formata mensagem de texto
   - Gera gráfico de pizza
   - Envia via WhatsApp (texto + imagem)
4. **Retorna Resultado**: JSON com estatísticas de envio

### Janela de Tolerância

O sistema usa uma **janela de ±5 minutos** para evitar perder envios:

- Usuário configurado para 08:00 → Envia entre 07:55 e 08:05
- Cron executando a cada hora → Usuário sempre receberá no horário certo

### Exemplo de Resposta do Endpoint

```json
{
  "status": "sucesso",
  "momento_envio": "INICIO_MES",
  "horario_processamento": "2025-01-01 08:00:15",
  "total_usuarios": 3,
  "enviados_sucesso": 3,
  "enviados_erro": 0,
  "duracao_segundos": 4.52,
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

## Testar Manualmente

### Testar Envio para Um Usuário

```bash
# Relatório do mês anterior (INICIO_MES)
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  "https://SEU-DOMINIO.com/admin/test-monthly-report/1?momento=INICIO_MES"

# Relatório do mês atual (FIM_MES)
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  "https://SEU-DOMINIO.com/admin/test-monthly-report/1?momento=FIM_MES"
```

### Testar Trigger de Início do Mês

```bash
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  https://SEU-DOMINIO.com/admin/trigger-monthly-reports-inicio
```

### Testar Trigger de Fim do Mês

```bash
curl -X POST \
  -H "x-api-key: SUA_API_KEY" \
  https://SEU-DOMINIO.com/admin/trigger-monthly-reports-fim
```

## Configuração pelo WhatsApp

Os usuários podem configurar suas preferências diretamente pelo WhatsApp:

### Ativar Relatório

```
Usuário: "ativar relatório mensal no início do mês às 8h"
Bot: ✅ Relatório mensal ativado!
     📅 Momento: início do mês (dia 1)
     🕐 Horário: 08:00
```

### Alterar Configurações

```
Usuário: "mudar relatório para fim do mês às 10h"
Bot: ✅ Configuração atualizada!
     📅 Momento: fim do mês (último dia)
     🕐 Horário: 10:00
```

### Consultar Configuração

```
Usuário: "como está configurado meu relatório?"
Bot: 📊 CONFIGURAÇÃO DO RELATÓRIO MENSAL
     Status: ✅ Ativo
     Momento: Início do mês (dia 1)
     Horário: 08:00
```

### Desativar Relatório

```
Usuário: "desativar relatório mensal"
Bot: ✅ Relatório mensal desativado com sucesso!
```

## Conteúdo do Relatório

Cada relatório inclui:

### Mensagem de Texto:
- 💰 **Resumo Financeiro**: Receitas, Despesas, Saldo
- 📈 **Comparação com Mês Anterior**: Variação em R$ e %
- 🏆 **Top 5 Categorias**: Maiores gastos por categoria
- 🎯 **Potes de Gastos**: Utilização vs Limite
- 💳 **Status de Contas**: Pagas vs Pendentes

### Gráfico de Pizza:
- Top 5 categorias com maior gasto
- Valores absolutos e percentuais
- Cores distintas para cada categoria
- Legenda formatada

## Monitoramento

### Verificar Logs do Cron

```bash
# Ver últimas execuções do cron
grep CRON /var/log/syslog | tail -20

# Logs do container
docker logs meu-secretario-api | grep MONTHLY-REPORT
```

### Verificar Configurações dos Usuários

Execute no banco de dados:

```sql
SELECT
    u.id,
    u.nome,
    mrc.ativo,
    mrc.momento_envio,
    mrc.hora_envio
FROM Usuarios u
LEFT JOIN MonthlyReportConfigs mrc ON u.id = mrc.usuario_id
ORDER BY u.id;
```

## Troubleshooting

### Relatório não foi enviado

1. **Verificar se usuário está ativo**:
   ```sql
   SELECT * FROM MonthlyReportConfigs WHERE usuario_id = 1;
   ```

2. **Verificar horário configurado**: Janela de ±5min
3. **Verificar logs**: `docker logs meu-secretario-api`
4. **Testar manualmente**: Usar endpoint de teste

### Erro ao criar tabela

Se o endpoint `/admin/setup-monthly-reports-table` falhar, execute diretamente no PostgreSQL:

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

### Cron não está executando

1. **Verificar serviço cron**: `systemctl status cron`
2. **Reiniciar cron**: `systemctl restart cron`
3. **Verificar sintaxe**: `crontab -l`

## Alternativa: GitHub Actions (Opcional)

Se preferir usar GitHub Actions em vez de cron local:

```yaml
name: Monthly Reports Scheduler

on:
  schedule:
    # Início do mês - todo dia 1 a cada hora
    - cron: '0 * 1 * *'
    # Fim do mês - últimos dias 28-31 a cada hora
    - cron: '0 * 28-31 * *'

jobs:
  trigger-reports:
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Monthly Reports - Inicio
        if: github.event.schedule == '0 * 1 * *'
        run: |
          curl -X POST \
            -H "x-api-key: ${{ secrets.API_SECRET_KEY }}" \
            https://SEU-DOMINIO.com/admin/trigger-monthly-reports-inicio

      - name: Trigger Monthly Reports - Fim
        if: github.event.schedule == '0 * 28-31 * *'
        run: |
          # Só executa se amanhã for dia 1 (hoje é último dia do mês)
          if [ $(date -d tomorrow +%d) -eq 1 ]; then
            curl -X POST \
              -H "x-api-key: ${{ secrets.API_SECRET_KEY }}" \
              https://SEU-DOMINIO.com/admin/trigger-monthly-reports-fim
          fi
```

## Segurança

⚠️ **IMPORTANTE**:
- Nunca commite a `API_SECRET_KEY` no código
- Use variáveis de ambiente
- Mantenha a chave segura
- Rotacione periodicamente

## Suporte

Para dúvidas ou problemas:
1. Verificar logs: `docker logs meu-secretario-api`
2. Testar endpoints manualmente
3. Verificar configuração do usuário no banco
4. Verificar sintaxe do crontab: `crontab -l`
