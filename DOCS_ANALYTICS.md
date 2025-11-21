# 📊 Análise Inteligente de Gastos com IA

## 🎯 Visão Geral

O sistema de **Análise Inteligente** usa o Google Gemini para gerar insights personalizados sobre seus padrões de consumo, identificar oportunidades de economia e fornecer relatórios financeiros detalhados.

---

## ✨ Funcionalidades Implementadas

### 1. **Análise Inteligente Completa**
- Analisa os últimos 3 meses de gastos
- Identifica padrões de consumo
- Compara mês atual vs anterior
- Analisa gastos por categoria
- Verifica status dos potes
- Identifica dias da semana com mais gastos
- Gera sugestões de economia personalizadas

### 2. **Comparação Mensal**
- Compara mês atual com mês anterior
- Mostra variação percentual
- Destaca as 5 categorias com maiores mudanças
- Identifica tendências de aumento ou redução

### 3. **Análise por Categoria Específica**
- Compara uma categoria ao longo do tempo
- Mostra tendências de crescimento/redução
- Calcula média mensal

---

## 🗣️ Como Usar pelo WhatsApp

### **Análise Inteligente Completa**

Envie qualquer uma dessas mensagens para o bot:

```
analisar meus gastos
análise inteligente
quero insights
me mostre um relatório financeiro
análise dos meus gastos
padrões de consumo
```

#### Exemplo de Resposta:

```
📊 Análise Inteligente de Gastos

📊 Resumo do Mês
Você gastou R$ 3.450,00 este mês, um aumento de 12% em relação
ao mês anterior (R$ 3.080,00).

🔍 Principais Insights
• Seu maior gasto foi com Alimentação / Delivery (R$ 850,00),
  representando 24% do total
• Você gasta mais nas sextas-feiras (R$ 520,00) e sábados (R$ 480,00)
• Delivery aumentou 60% este mês comparado ao anterior
• Transporte teve uma redução de 15%, economizando R$ 120,00

⚠️ Alertas
• Pote "Lazer" está em 95% (R$ 475 de R$ 500)
• Categoria "Delivery" teve aumento significativo de 60%
• Seu maior gasto individual foi R$ 230,00 no dia 15/11

💡 Sugestões de Economia
• Reduzir delivery em 30% pode economizar ~R$ 255/mês
• Seus gastos fixos (contas) são R$ 1.200 - considere renegociar
  internet e celular
• Substituir 2 deliveries por semana por cozinhar pode economizar
  R$ 400/mês
• Controlar gastos de fim de semana pode gerar economia de ~R$ 200/mês

💬 Para análises mais específicas, pergunte:
• "Quanto gastei com [categoria]?"
• "Comparar gastos de [mês]"
```

---

### **Comparação Mensal**

Envie qualquer uma dessas mensagens:

```
comparar este mês com o anterior
comparação mensal
evolução dos gastos
mês atual vs anterior
```

#### Exemplo de Resposta:

```
📊 Comparação: Novembro/2025 vs Outubro/2025

💰 Novembro/2025: R$ 3.450,00
💰 Outubro/2025: R$ 3.080,00
📈 Variação: +12.0%

Maiores mudanças por categoria:
🔴 Alimentação / Delivery: R$ 850,00 (+60.4%)
🔴 Transporte / Combustível: R$ 320,00 (+15.2%)
🟢 Saúde / Academia: R$ 150,00 (-25.0%)
⚪ Moradia / Aluguel: R$ 1.200,00 (+0.0%)
⚪ Assinaturas / Streaming: R$ 85,00 (+6.3%)
```

---

### **Análise por Categoria Específica**

Envie mensagens como:

```
quanto gastei com supermercado?
quanto gastei com alimentação?
quanto gastei com delivery?
quanto gastei com transporte?
```

#### Exemplo de Resposta:

```
📊 Análise: Alimentação / Delivery

💰 Total em 3 meses: R$ 2.100,00
📈 Média mensal: R$ 700,00

Detalhamento:
• Nov/2025: R$ 850,00 (12x)
• Out/2025: R$ 530,00 (8x)
• Set/2025: R$ 720,00 (10x)

📈 Variação: +60.4% (primeiro vs último mês)
```

---

## 🔧 Arquitetura Técnica

### **Arquivo Principal: `app/services/analytics_service.py`**

#### Funções Implementadas:

1. **`get_spending_analysis(usuario_id, meses_analise=3)`**
   - Coleta dados estruturados dos últimos N meses
   - Retorna: gastos mensais, por categoria, por dia da semana, potes, etc.

2. **`generate_ai_insights(usuario_id)`**
   - Usa os dados coletados para gerar insights com Gemini
   - Retorna: relatório formatado com resumo, insights, alertas e sugestões

3. **`get_category_comparison(usuario_id, categoria_nome, meses=3)`**
   - Compara gastos de uma categoria ao longo do tempo
   - Retorna: relatório de evolução da categoria

4. **`get_monthly_comparison(usuario_id, mes_referencia=None)`**
   - Compara mês atual (ou especificado) com mês anterior
   - Retorna: comparação detalhada por categoria

### **Integrações:**

#### **1. Gemini Service (`gemini_service.py`)**

Novos intents adicionados:
- `"Análise Inteligente"` → Análise completa com IA
- `"Comparação Mensal"` → Comparação entre meses

#### **2. Webhooks (`webhooks.py`)**

Rotas adicionadas (linhas 814-846):
```python
elif intent == 'Análise Inteligente':
    insights = generate_ai_insights(usuario_id)
    resposta = f"📊 *Análise Inteligente de Gastos*\n\n{insights}"

elif intent == 'Comparação Mensal':
    comparacao = get_monthly_comparison(usuario_id)
    resposta = comparacao
```

---

## 📊 Dados Analisados

O sistema analisa:

| Dado | Descrição |
|------|-----------|
| **Gastos Mensais** | Total de despesas por mês (últimos 3 meses) |
| **Gastos por Categoria** | Top 10 categorias com mais gastos |
| **Gastos por Dia da Semana** | Padrão de consumo ao longo da semana |
| **Comparação Mensal** | Variação percentual entre meses |
| **Potes de Gastos** | Utilização dos limites configurados |
| **Maiores Gastos** | Top 5 transações individuais |
| **Gastos Delivery** | Evolução específica de delivery |
| **Contas Fixas** | Resumo das contas recorrentes |

---

## 🎯 Exemplos de Insights Gerados pela IA

### **Resumo do Mês**
```
Você gastou R$ 3.450,00 este mês, representando um aumento de 12%
em relação ao mês passado. Seus principais gastos foram concentrados
em Alimentação e Transporte.
```

### **Principais Insights**
```
• Delivery representa 24% dos seus gastos mensais
• Você tende a gastar mais nos fins de semana (sexta e sábado)
• Categoria "Saúde" teve redução de 25% - parabéns!
• Seu maior gasto fixo é Aluguel (R$ 1.200/mês)
```

### **Alertas**
```
⚠️ Pote "Lazer" está em 95% do limite
⚠️ Delivery aumentou 60% este mês
⚠️ Você teve um gasto atípico de R$ 230 no dia 15/11
```

### **Sugestões de Economia**
```
💡 Reduzir delivery em 30% = ~R$ 255 de economia/mês
💡 Cozinhar 2x/semana em vez de pedir = ~R$ 400/mês
💡 Renegociar internet + celular = possível economia de R$ 80/mês
💡 Controlar gastos de fim de semana = ~R$ 200/mês
```

---

## 🧪 Como Testar

### **Pré-requisitos:**
1. Ter transações consolidadas no sistema
2. Bot WhatsApp configurado e funcionando
3. Usuário autenticado

### **Passo a Passo:**

#### **1. Testar Análise Inteligente**
```
Você → "analisar meus gastos"

Bot → [Relatório completo com insights, alertas e sugestões]
```

#### **2. Testar Comparação Mensal**
```
Você → "comparar este mês com o anterior"

Bot → [Comparação detalhada com variações por categoria]
```

#### **3. Testar Análise de Categoria**
```
Você → "quanto gastei com delivery?"

Bot → [Evolução dos gastos com delivery nos últimos 3 meses]
```

#### **4. Testar Variações de Comando**
```
Você → "quero insights"
Você → "análise financeira"
Você → "relatório dos meus gastos"
Você → "padrões de consumo"

Bot → [Todos devem acionar a Análise Inteligente]
```

---

## 🚀 Próximas Melhorrias (Sugestões)

- [ ] **Gráficos Visuais:** Gerar gráficos de pizza e linha com matplotlib
- [ ] **Análise Preditiva:** Prever gastos do próximo mês baseado no histórico
- [ ] **Alertas Proativos:** Enviar notificação quando gasto de categoria ultrapassar média
- [ ] **Benchmark Social:** Comparar com média de gastos de outros usuários (anônimo)
- [ ] **Metas Financeiras:** Acompanhar progresso de objetivos de economia
- [ ] **Exportar PDF:** Gerar relatório mensal em PDF

---

## 🐛 Troubleshooting

### **"Não consegui gerar a análise"**
- Verifique se há transações consolidadas no sistema
- Confirme que o Gemini API está funcionando
- Verifique logs no servidor para erros específicos

### **"Não encontrei gastos para comparar"**
- Certifique-se de ter transações nos últimos 2 meses
- Verifique se as transações estão marcadas como `consolidada = true`

### **Intent não reconhecido**
- Tente frases mais diretas: "analisar gastos" ou "comparar meses"
- Verifique logs do Gemini Service para ver como foi interpretado

---

## 📝 Changelog

### **v1.0.0 - 2025-11-21**
- ✅ Implementado `analytics_service.py` completo
- ✅ Adicionado reconhecimento de intent "Análise Inteligente"
- ✅ Adicionado reconhecimento de intent "Comparação Mensal"
- ✅ Integração com Gemini para geração de insights
- ✅ Análise de padrões de gastos (categoria, dia da semana, potes)
- ✅ Comparação mensal detalhada
- ✅ Análise por categoria específica
- ✅ Sugestões personalizadas de economia

---

## 👨‍💻 Desenvolvedor

Para dúvidas ou sugestões sobre a funcionalidade de Analytics:
- Arquivo principal: `app/services/analytics_service.py`
- Rotas: `app/routes/webhooks.py` (linhas 814-846)
- Intents: `app/services/gemini_service.py` (linhas 102-117)

---

**🎉 Análise Inteligente implementada com sucesso!**
