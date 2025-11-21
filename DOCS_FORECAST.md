# 📈 Previsão de Gastos Futuros - Documentação Técnica

## 📋 Índice

1. [Visão Geral](#visão-geral)
2. [Arquitetura](#arquitetura)
3. [Fluxo de Funcionamento](#fluxo-de-funcionamento)
4. [Algoritmos de Previsão](#algoritmos-de-previsão)
5. [Endpoints e Integrações](#endpoints-e-integrações)
6. [Exemplos de Uso](#exemplos-de-uso)
7. [Métricas e Cálculos](#métricas-e-cálculos)

---

## 🎯 Visão Geral

A feature de **Previsão de Gastos Futuros** projeta quanto o usuário gastará em períodos futuros baseado em:

- **Histórico de gastos** (média móvel dos últimos 6 meses)
- **Contas fixas agendadas** (agendamentos mensais pendentes)
- **Padrão de consumo** (taxa de gastos por dia do mês)
- **Análise com IA** (Gemini identifica tendências e gera insights)

### Principais Funcionalidades

1. **Projeção do Mês Atual**
   - Gastos até hoje
   - Projeção para final do mês
   - Contas pendentes
   - Comparação com média histórica

2. **Análise de Tendências**
   - Crescimento/redução de gastos
   - Categorias com maior variação
   - Alertas de gastos acima do esperado

3. **Recomendações Personalizadas**
   - Sugestões de economia baseadas em dados reais
   - Metas de gastos até fim do mês
   - Categorias para controlar

---

## 🏗️ Arquitetura

### Estrutura de Arquivos

```
app/
├── services/
│   ├── forecast_service.py        # Serviço principal de previsão
│   ├── gemini_service.py          # Adicionado intent "Previsão de Gastos"
│   └── analytics_service.py       # Serviço de análise (referência)
├── routes/
│   └── webhooks.py                # Integração via WhatsApp
└── __init__.py                    # Configuração do Gemini
```

### Dependências

- **SQLAlchemy** - Queries ao banco PostgreSQL
- **Google Gemini Flash** - Análise de IA e geração de insights
- **Python datetime/calendar** - Manipulação de datas
- **Flask** - API REST

---

## 🔄 Fluxo de Funcionamento

### 1. Entrada via WhatsApp

```
Usuário: "quanto vou gastar este mês"
         ↓
gemini_service.get_message_intent()
         ↓
Intent: "Previsão de Gastos"
         ↓
webhooks.py (linha 850-865)
         ↓
forecast_service.generate_forecast_insights(usuario_id)
```

### 2. Coleta de Dados

**Função: `get_forecast_data(usuario_id)`**

Coleta em paralelo:

1. **Histórico de Categorias** (últimos 6 meses)
   ```sql
   SELECT mes, categoria, subcategoria, SUM(valor), COUNT(*)
   FROM Transacoes + SubCategoria + MacroCategoria
   WHERE tipo_transacao = 'Despesa' AND consolidada = true
   GROUP BY mes, categoria, subcategoria
   ```

2. **Gastos Mensais Totais**
   ```sql
   SELECT mes, SUM(valor)
   FROM Transacoes
   WHERE tipo_transacao = 'Despesa'
   GROUP BY mes
   ```

3. **Contas Fixas Ativas**
   ```sql
   SELECT descricao, valor_previsto, periodicidade, dia_execucao
   FROM Agendamentos
   WHERE ativo = true AND tipo_agendamento IN ('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')
   ```

4. **Gastos do Mês Atual (até hoje)**
   ```sql
   SELECT SUM(valor), COUNT(*)
   FROM Transacoes
   WHERE tipo_transacao = 'Despesa' AND mes_atual
   ```

5. **Contas Pendentes do Mês**
   ```sql
   SELECT a.descricao, a.valor_previsto, a.dia_execucao
   FROM Agendamentos a
   WHERE NOT EXISTS (transação correspondente no mês atual)
   ```

6. **Padrão de Gastos por Dia do Mês**
   ```sql
   SELECT dia_do_mes, AVG(valor), COUNT(*)
   FROM Transacoes (últimos 6 meses)
   GROUP BY dia_do_mes
   ```

### 3. Cálculo da Projeção

**Função: `calculate_simple_forecast(dados)`**

#### Algoritmo de Projeção

```python
# 1. Média Móvel Histórica
media_historica = sum(gastos_ultimos_6_meses) / 6

# 2. Projeção Linear (baseado no ritmo atual)
taxa_diaria = gasto_ate_hoje / dia_atual
projecao_linear = taxa_diaria * dias_no_mes

# 3. Projeção com Contas Pendentes
projecao_pendentes = gasto_ate_hoje + sum(contas_pendentes)

# 4. Projeção Final (maior valor entre as opções, mas pelo menos 85% da média)
projecao_final = max(
    projecao_linear,
    projecao_pendentes,
    media_historica * 0.85
)
```

#### Exemplo Prático

```
Cenário:
- Dia atual: 20 de novembro (30 dias no mês)
- Gasto até hoje: R$ 1.300
- Contas pendentes: R$ 250 (Luz + Internet)
- Média histórica: R$ 2.000

Cálculo:
- Taxa diária: 1.300 / 20 = R$ 65/dia
- Projeção linear: 65 * 30 = R$ 1.950
- Projeção + pendentes: 1.300 + 250 = R$ 1.550
- 85% da média: 2.000 * 0.85 = R$ 1.700

Projeção Final: R$ 1.950 (maior valor)
```

### 4. Geração de Insights com IA

**Função: `generate_forecast_insights(usuario_id)`**

Envia para o Gemini:
- Dados estruturados (histórico, projeção, pendentes)
- Prompt personalizado com instruções específicas

**Prompt estruturado:**

```markdown
**DADOS DO USUÁRIO:**
- Data atual: 2024-11-20 (dia 20 de 30)
- Gastos até hoje: R$ 1.300
- Taxa média diária: R$ 65
- Contas pendentes: R$ 250

**Histórico (últimos 6 meses):**
- 2024-10: R$ 2.100
- 2024-09: R$ 1.950
- Média mensal: R$ 2.000

**Projeção Calculada:**
- Projeção final Novembro: R$ 1.950

**INSTRUÇÕES:**
Gere relatório com:
1. Projeção [mês] (gastos até agora, projeção final, pendentes, base)
2. Análise de Tendências (comparação com média, padrão)
3. Alertas (se projeção acima da média, contas grandes pendentes)
4. Recomendações (categorias a controlar, meta até fim do mês)
```

**Resposta do Gemini** (exemplo):

```markdown
📈 Projeção Novembro
• Gastos até agora: R$ 1.300 (dia 20)
• Projeção final: ~R$ 1.950
• Faltam contas: Luz (R$ 150), Internet (R$ 100)
• Baseado em: média últimos 6 meses

🔍 Análise de Tendências
• Você está gastando menos que a média (R$ 2.000)
• Taxa diária atual: R$ 65/dia está controlada
• Tendência de redução de 2,5% vs média

💡 Recomendações
• Continue no ritmo atual para fechar abaixo da média
• Atenção às contas pendentes de R$ 250
• Meta: gastar no máximo R$ 650 nos próximos 10 dias
```

### 5. Retorno ao Usuário

```
Resposta via WhatsApp:
📈 *Previsão de Gastos*

[Insights gerados pelo Gemini]

💬 _Baseado em: histórico de 6 meses + contas fixas_
```

---

## 🧮 Algoritmos de Previsão

### 1. Média Móvel Simples

**Quando usar:** Gastos estáveis sem grandes variações

```python
media = sum(gastos_ultimos_N_meses) / N
```

**Vantagens:**
- Simples e rápido
- Bom para padrões estáveis

**Desvantagens:**
- Não detecta tendências
- Não considera sazonalidade

### 2. Projeção Linear (Implementado)

**Quando usar:** Estimar fim do mês baseado no ritmo atual

```python
taxa_diaria = gasto_ate_hoje / dias_passados
projecao = taxa_diaria * dias_totais_mes
```

**Vantagens:**
- Reflete ritmo atual de gastos
- Dinâmico (atualiza conforme mês avança)

**Desvantagens:**
- Assume ritmo constante
- Não considera eventos futuros

### 3. Projeção com Contas Fixas (Implementado)

**Quando usar:** Incluir agendamentos conhecidos

```python
projecao = gasto_ate_hoje + sum(contas_pendentes)
```

**Vantagens:**
- Considera compromissos futuros
- Mais preciso para contas previsíveis

**Desvantagens:**
- Não captura gastos variáveis

### 4. Média Móvel Ponderada (Futuro)

**Quando usar:** Dar mais peso aos meses recentes

```python
pesos = [3, 2, 1]  # últimos 3 meses
media_ponderada = sum(gasto[i] * peso[i]) / sum(pesos)
```

### 5. Tendência Linear (Futuro)

**Quando usar:** Detectar crescimento/redução contínua

```python
# Regressão linear simples
tendencia = (ultimo_mes - primeiro_mes) / num_meses
projecao = media + tendencia
```

---

## 🔌 Endpoints e Integrações

### WhatsApp Webhook

**Endpoint:** `POST /webhook-whatsapp`

**Intent:** `"Previsão de Gastos"`

**Frases que ativam:**
- "quanto vou gastar este mês"
- "qual a projeção de gastos"
- "estimativa de gastos próximo mês"
- "previsão financeira"
- "orçamento futuro"

**Payload de entrada:**

```json
{
  "message": "quanto vou gastar este mês",
  "user_api_key": "...",
  "phone_number": "+5511999999999"
}
```

**Resposta:**

```json
{
  "status": "sucesso",
  "resposta": "📈 *Previsão de Gastos*\n\n[insights]"
}
```

### Funções Exportadas

#### `forecast_service.py`

```python
# Principal (com IA)
generate_forecast_insights(usuario_id) -> str

# Coleta de dados
get_forecast_data(usuario_id, meses_historico=6, meses_projecao=3) -> dict

# Cálculo simples
calculate_simple_forecast(dados) -> dict

# Previsão sem IA (fallback)
generate_simple_forecast_text(usuario_id) -> str

# Previsão por categoria
get_category_forecast(usuario_id, categoria_nome, meses=3) -> str
```

---

## 💡 Exemplos de Uso

### Exemplo 1: Projeção do Mês Atual

**Input (WhatsApp):**
```
Usuário: "quanto vou gastar este mês"
```

**Output:**
```
📈 *Previsão de Gastos*

📈 Projeção Novembro
• Gastos até agora: R$ 980 (dia 20)
• Projeção final: ~R$ 2.100
• Faltam contas: Luz (R$ 150), Internet (R$ 100)
• Baseado em: média últimos 6 meses + ritmo atual

🔍 Análise de Tendências
• Você está gastando acima da média histórica (R$ 1.850)
• Aumento de 13,5% comparado aos últimos 6 meses
• Taxa diária atual: R$ 49/dia (normal: R$ 42/dia)

⚠️ Alertas
• Projeção está 13,5% acima da média histórica
• Contas grandes pendentes: R$ 250 total
• Delivery teve aumento de 45% este mês

💡 Recomendações
• Controle gastos com Delivery nos próximos 10 dias
• Meta: gastar no máximo R$ 1.000 até fim do mês
• Evite compras grandes até fechar o mês

💬 _Baseado em: histórico de 6 meses + contas fixas_
```

### Exemplo 2: Previsão por Categoria (Futuro)

**Input:**
```python
from app.services.forecast_service import get_category_forecast
resultado = get_category_forecast(usuario_id=1, categoria_nome="Delivery", meses=3)
print(resultado)
```

**Output:**
```
📈 **Projeção: Delivery**

💰 Média mensal: R$ 420,00
📊 Projeção 3 meses: R$ 1.260,00

**Histórico recente:**
• Nov/2024: R$ 480,00
• Out/2024: R$ 390,00
• Set/2024: R$ 350,00
```

### Exemplo 3: Dados Brutos (API Interna)

```python
from app.services.forecast_service import get_forecast_data, calculate_simple_forecast

# Coletar dados
dados = get_forecast_data(usuario_id=1)
print(dados)
# {
#   "usuario_id": 1,
#   "data_atual": "2024-11-20",
#   "dia_atual": 20,
#   "dias_no_mes": 30,
#   "mes_atual": "2024-11",
#   "gastos_mensais": [
#     {"mes": "2024-11", "total": 1300.0},
#     {"mes": "2024-10", "total": 2100.0},
#     ...
#   ],
#   "contas_pendentes": [
#     {"descricao": "Luz", "valor": 150.0, "dia": 25},
#     ...
#   ],
#   ...
# }

# Calcular projeção
projecao = calculate_simple_forecast(dados)
print(projecao)
# {
#   "projecao_mes_atual": 1950.0,
#   "media_historica": 2000.0,
#   "contas_pendentes_total": 250.0,
#   "gasto_ate_hoje": 1300.0,
#   "taxa_diaria_atual": 65.0
# }
```

---

## 📊 Métricas e Cálculos

### Métricas Principais

| Métrica | Fórmula | Descrição |
|---------|---------|-----------|
| **Média Histórica** | `sum(gastos_N_meses) / N` | Média de gastos mensais |
| **Taxa Diária** | `gasto_ate_hoje / dia_atual` | Quanto gasta por dia em média |
| **Projeção Linear** | `taxa_diaria * dias_mes` | Estimativa baseada no ritmo |
| **Variação %** | `((projecao - media) / media) * 100` | Quanto está acima/abaixo da média |
| **Contas Pendentes** | `sum(agendamentos_nao_executados)` | Total de contas fixas restantes |

### Exemplo de Cálculo Completo

**Cenário:**
- Data: 20/11/2024 (30 dias no mês)
- Gasto até hoje: R$ 1.300
- Histórico últimos 6 meses: [2.100, 1.950, 2.050, 1.900, 2.000, 1.800]
- Contas pendentes: Luz (R$ 150), Internet (R$ 100)

**Passo 1: Média Histórica**
```
media = (2.100 + 1.950 + 2.050 + 1.900 + 2.000 + 1.800) / 6
media = 11.800 / 6 = R$ 1.966,67
```

**Passo 2: Taxa Diária**
```
taxa_diaria = 1.300 / 20 = R$ 65/dia
```

**Passo 3: Projeção Linear**
```
projecao_linear = 65 * 30 = R$ 1.950
```

**Passo 4: Projeção + Pendentes**
```
projecao_pendentes = 1.300 + 150 + 100 = R$ 1.550
```

**Passo 5: Mínimo (85% da média)**
```
minimo = 1.966,67 * 0.85 = R$ 1.671,67
```

**Passo 6: Projeção Final**
```
projecao_final = max(1.950, 1.550, 1.671,67) = R$ 1.950
```

**Passo 7: Variação**
```
variacao = ((1.950 - 1.966,67) / 1.966,67) * 100 = -0,85%
```

**Interpretação:**
- Projeção: R$ 1.950
- Variação: -0,85% (ligeiramente abaixo da média)
- Status: ✅ Dentro do esperado

---

## 🔧 Configuração e Requisitos

### Variáveis de Ambiente

```env
GEMINI_API_KEY=your_api_key_here
DATABASE_URL=postgresql://user:pass@host:port/db
```

### Dependências (requirements.txt)

```
Flask==3.0.0
SQLAlchemy==2.0.30
google-generativeai==0.5.4
psycopg2-binary==2.9.9
```

---

## 🚀 Melhorias Futuras

### 1. Sazonalidade
Detectar padrões mensais (ex: dezembro tem gastos 30% maiores)

### 2. Categorização da Projeção
Projetar cada categoria individualmente e somar

### 3. Machine Learning
Usar scikit-learn para modelos mais sofisticados (ARIMA, Prophet)

### 4. Alertas Proativos
Enviar notificações quando projeção ultrapassar limites

### 5. Comparação com Metas
Integrar com potes para comparar projeção vs meta

### 6. Projeção Multi-mês
Projetar 2-3 meses à frente com contas parceladas

---

## 📝 Notas Técnicas

### Performance

- **Queries otimizadas**: Uso de índices em `data_transacao`, `usuario_id`
- **Cache**: Considerar Redis para projeções recalculadas
- **Timeout Gemini**: 30 segundos (ajustável)

### Tratamento de Erros

```python
try:
    previsao = generate_forecast_insights(usuario_id)
except Exception as e:
    print(f"[FORECAST-ERRO] {e}")
    # Fallback: usar projeção simples sem IA
    previsao = generate_simple_forecast_text(usuario_id)
```

### Limites

- **Histórico mínimo**: 1 mês (ideal: 6 meses)
- **Precisão**: ±15% em média
- **Melhor para**: Usuários com gastos regulares
- **Menos preciso para**: Gastos muito irregulares

---

## 📞 Suporte

Para dúvidas ou melhorias, consulte:
- [GUIA_TESTE_FORECAST.md](./GUIA_TESTE_FORECAST.md) - Guia de testes
- [DOCS_ANALYTICS.md](./DOCS_ANALYTICS.md) - Feature relacionada (análise)
- Código fonte: `app/services/forecast_service.py`
