# Feature: Feedback Financeiro em Tempo Real

## 📋 Resumo

Feature implementada com sucesso que enriquece a mensagem de confirmação de transações com:
- **Status do pote** relacionado à categoria (saldo restante + semáforo 🟢🟡🔴)
- **Valor da fatura atual** (se cartão de crédito) OU **saldo da conta** (se débito/pix/dinheiro)
- **Alertas configuráveis** por threshold (sempre mostrar ou apenas em 50%/70%/90%)

## 🗂️ Arquivos Criados/Modificados

### **Arquivos Criados:**

1. **`app/services/transaction_feedback_service.py`**
   - Service completo com todas as funções de cálculo e formatação
   - Funções principais:
     - `calcular_status_pote()` - Busca status do pote por categoria
     - `verificar_tipo_conta()` - Identifica se é crédito ou corrente
     - `calcular_fatura_atual()` - Calcula valor da fatura em aberto
     - `calcular_saldo_conta()` - Calcula saldo disponível
     - `deve_exibir_alerta()` - Verifica configuração de threshold
     - `get_emoji_status()` - Retorna emoji do semáforo
     - `gerar_feedback_transacao()` - Função orquestradora principal

### **Arquivos Modificados:**

1. **`app/routes/webhooks.py`**
   - Adicionado import: `from app.services.transaction_feedback_service import gerar_feedback_transacao`
   - Modificado fluxo de confirmação (linha ~540-576):
     - Captura ID da transação após `create_transaction()`
     - Chama `gerar_feedback_transacao()` após commit
     - Substitui mensagem simples por feedback enriquecido

2. **`app/services/finance_service.py`**
   - Modificada função `create_transaction()` (linha 368-380):
     - Agora retorna o ID da transação criada
     - Adicionado `RETURNING id` na query SQL

3. **`app/routes/admin.py`**
   - Adicionado endpoint `/admin/setup-potes-alerts` (linha 779-922):
     - Cria tabela `NotificationConfigs` se não existir
     - Adiciona colunas de configuração de alertas
     - Insere configurações padrão
   - Modificado endpoint `/admin/get-notification-config/<usuario_id>`:
     - Agora retorna também `alerta_potes_ativo` e `alerta_potes_threshold`

## 🎨 Exemplos de Output

### Exemplo 1: Crédito + Pote Saudável (🟢)

```
✅ Transação Salva!
📝 Uber
💵 R$ 25,00 (Nubank)

🎯 Pote Transporte (Semanal):
Restam: R$ 120,00 🟢

💳 Fatura Nubank:
R$ 1.250,00 (Fecha dia 15)
```

### Exemplo 2: Débito + Pote Crítico (🔴)

```
✅ Transação Salva!
📝 Padaria
💵 R$ 10,00 (Itaú)

🎯 Pote Alimentação (Semanal):
Restam: R$ 15,00 🔴

🏦 Saldo Itaú:
R$ 890,50 (Disponível)
```

### Exemplo 3: Sem Pote Configurado

```
✅ Transação Salva!
📝 Cinema
💵 R$ 40,00 (Dinheiro)

🏦 Saldo Carteira:
R$ 120,00 (Disponível)
```

## 🚀 Como Ativar a Feature

### Via Endpoint Admin (Recomendado)

Acesse o endpoint de setup no seu navegador ou via curl:

```bash
# Navegador
http://seu-backend.com/admin/setup-potes-alerts

# Ou via curl
curl http://seu-backend.com/admin/setup-potes-alerts
```

O endpoint irá:
1. ✅ Criar tabela `NotificationConfigs` (se não existir)
2. ✅ Adicionar colunas `alerta_potes_ativo` e `alerta_potes_threshold`
3. ✅ Verificar campo `periodicidade` em `PotesDeGastos`
4. ✅ Inserir configurações padrão para usuários existentes

### Verificar Configurações

```bash
# Ver configurações de um usuário específico
http://seu-backend.com/admin/get-notification-config/1
```

## ⚙️ Configurações Disponíveis

A feature adiciona duas novas colunas na tabela `NotificationConfigs`:

### `alerta_potes_ativo` (BOOLEAN)
- **Padrão**: `TRUE`
- **Descrição**: Liga/desliga os alertas de potes
- **Uso futuro**: Permitir usuário desativar via WhatsApp ou frontend

### `alerta_potes_threshold` (INT)
- **Padrão**: `0` (sempre mostrar)
- **Valores possíveis**:
  - `0` = Sempre mostrar status do pote
  - `50` = Mostrar apenas se usar >= 50% do limite
  - `70` = Mostrar apenas se usar >= 70% do limite
  - `90` = Mostrar apenas se usar >= 90% do limite
- **Uso futuro**: Permitir usuário configurar via WhatsApp ou frontend

## 🎯 Lógica de Negócio

### Regras de Exibição

#### Bloco do Pote (🎯)
- **Quando mostrar**: Se existir pote relacionado à categoria E threshold permitir
- **Cálculo**: Somatória dos gastos do período (semanal/mensal) vs limite
- **Emoji de status**:
  - 🟢 Verde: < 70% usado
  - 🟡 Amarelo: 70-90% usado
  - 🔴 Vermelho: >= 90% usado
- **Periodicidade**: Calcula gastos desde início do período (semana ou mês)

#### Bloco de Rodapé (Condicional)

**Se cartão de crédito (💳 Fatura)**:
- Mostra valor total da fatura em aberto
- Dia de fechamento
- Inclui TODAS despesas da fatura atual

**Se conta corrente/pix/dinheiro (🏦 Saldo)**:
- Mostra saldo disponível (Receitas - Despesas)
- Ajuda prevenir saldo negativo

### Múltiplos Potes
- Se uma categoria estiver em múltiplos potes, usa o primeiro encontrado
- Recomendação: manter categorias em apenas um pote para clareza

## 🧪 Como Testar

### 1. Criar um Pote de Gastos (via WhatsApp ou diretamente no banco)

```sql
-- Exemplo: Criar pote de alimentação semanal
INSERT INTO PotesDeGastos (usuario_id, nome_pote, valor_limite, periodicidade, ativo)
VALUES (1, 'Alimentação', 500.00, 'SEMANAL', TRUE);

-- Vincular categoria ao pote
INSERT INTO PoteSubCategorias (pote_id, subcategoria_id)
VALUES (
    (SELECT id FROM PotesDeGastos WHERE nome_pote = 'Alimentação' LIMIT 1),
    (SELECT id FROM SubCategoria WHERE nome_sub = 'Supermercado / Mercearia' LIMIT 1)
);
```

### 2. Registrar uma Despesa via WhatsApp

```
Você: gastei 50 no mercado
Bot: [Mensagem de confirmação]
Você: OK
Bot: [Mensagem de feedback enriquecida com status do pote]
```

### 3. Verificar Comportamento

- ✅ Mensagem mostra status do pote
- ✅ Emoji correto aparece (🟢🟡🔴)
- ✅ Se crédito: mostra fatura
- ✅ Se débito: mostra saldo
- ✅ Se sem pote: não mostra bloco do pote

## 📊 Estrutura do Banco de Dados

### Tabela `NotificationConfigs` (estendida)

```sql
CREATE TABLE NotificationConfigs (
    id SERIAL PRIMARY KEY,
    usuario_id INT NOT NULL REFERENCES Usuarios(id),

    -- Configurações existentes...
    agenda_diaria_ativa BOOLEAN DEFAULT TRUE,
    -- ...

    -- NOVAS COLUNAS
    alerta_potes_ativo BOOLEAN NOT NULL DEFAULT TRUE,
    alerta_potes_threshold INT NOT NULL DEFAULT 0,

    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(usuario_id)
);
```

### Tabela `PotesDeGastos` (já existente, garantindo `periodicidade`)

```sql
CREATE TABLE PotesDeGastos (
    id SERIAL PRIMARY KEY,
    usuario_id INT NOT NULL REFERENCES Usuarios(id),
    nome_pote VARCHAR(100) NOT NULL,
    valor_limite NUMERIC(15, 2) NOT NULL,
    periodicidade VARCHAR(20) NOT NULL DEFAULT 'MENSAL'
        CHECK (periodicidade IN ('SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL')),
    data_inicio DATE NOT NULL DEFAULT CURRENT_DATE,
    ativo BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE(usuario_id, nome_pote)
);
```

## 🔧 Performance e Otimizações

### Índices Recomendados (já criados pela migration)

```sql
CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
ON NotificationConfigs(usuario_id);

-- Índices existentes importantes:
-- idx_transacoes_usuario_id
-- idx_pote_subcat_pote_id
-- idx_pote_subcat_subcat_id
```

### Queries Otimizadas

Todas as queries usam:
- `LEFT JOIN` para evitar perda de dados
- `COALESCE` para tratar NULLs
- `date_trunc` para cálculo eficiente de períodos
- Índices em colunas de join

## 🐛 Troubleshooting

### Erro: "Transação não encontrada"
- **Causa**: `create_transaction()` não está retornando ID
- **Solução**: Verificar se migration do `finance_service.py` foi aplicada

### Mensagem simples aparece ao invés do feedback
- **Causa**: Erro silencioso no `transaction_feedback_service.py`
- **Solução**: Verificar logs do servidor para exceções
- **Fallback**: Service retorna mensagem simples em caso de erro

### Pote não aparece na mensagem
- **Possíveis causas**:
  1. Categoria não vinculada a nenhum pote
  2. Pote está inativo (`ativo = FALSE`)
  3. Threshold está configurado e percentual ainda baixo
  4. Periodicidade não tem gastos no período

### Valor da fatura está errado
- **Causa**: Faturas antigas não fechadas
- **Solução**: Verificar status das faturas no banco:

```sql
SELECT * FROM Faturas WHERE conta_id = X AND status = 'Aberta';
```

## 📚 Próximos Passos (Features Futuras)

### 1. Comandos via WhatsApp

```
Usuário: configurar alertas de potes
Bot: Como você quer receber alertas?
     1. Sempre mostrar
     2. Apenas quando usar 50%
     3. Apenas quando usar 70%
     4. Apenas quando usar 90%
     5. Desativar alertas

Usuário: desativar alertas de potes
Bot: ✅ Alertas de potes desativados
```

### 2. Frontend Web

- Dashboard com configurações visuais
- Toggles para ativar/desativar
- Slider para ajustar threshold
- Preview da mensagem de feedback

### 3. Resumo Diário Opcional

```
Bot (20h): 📊 Resumo dos Potes - 23/11

🛒 Alimentação Semanal
   Gasto hoje: R$ 125,00
   Total: R$ 425/R$ 500
   Restam: R$ 75 (2 dias até resetar)
```

### 4. Alertas Proativos

- Notificação quando atingir 80% do pote
- Alerta quando ultrapassar 100%
- Sugestão de ajuste de limite

## ✅ Checklist de Validação

- [x] Migration SQL criada
- [x] Service de feedback implementado
- [x] Integração no fluxo de confirmação
- [x] `create_transaction()` retorna ID
- [ ] Migration executada no banco de produção
- [ ] Testes manuais via WhatsApp
- [ ] Criar potes de teste
- [ ] Validar todos cenários (crédito/débito/sem pote)
- [ ] Documentação completa

## 📝 Notas Técnicas

### Compatibilidade
- PostgreSQL 12+
- Python 3.10+
- SQLAlchemy 2.x
- Flask

### Segurança
- Queries parametrizadas (proteção contra SQL injection)
- Validações de usuário_id em todas queries
- Fallback em caso de erro (não quebra o fluxo)

### Manutenibilidade
- Código modular e testável
- Funções pequenas e focadas
- Documentação inline
- Logging em pontos críticos

---

**Implementado por**: Claude Code (Anthropic)
**Data**: 23/01/2025
**Versão**: 1.0.0
