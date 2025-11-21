# 🧪 Guia Rápido de Teste - Análise Inteligente

## 📱 Como Testar pelo WhatsApp

### ✅ Pré-requisitos
1. Bot WhatsApp funcionando
2. Pelo menos **alguns gastos registrados** no sistema
3. Transações **consolidadas** (confirmadas)

---

## 🎯 Testes Básicos

### **Teste 1: Análise Inteligente Completa**

**Envie para o bot:**
```
analisar meus gastos
```

**Ou variações:**
- `análise inteligente`
- `quero insights`
- `relatório financeiro`
- `me mostre meus padrões de consumo`

**O que esperar:**
- 📊 Resumo do mês (total gasto, variação %)
- 🔍 Insights sobre categorias principais
- 📅 Padrão de gastos por dia da semana
- ⚠️ Alertas sobre potes e gastos atípicos
- 💡 Sugestões personalizadas de economia

**Exemplo de resposta esperada:**
```
📊 Análise Inteligente de Gastos

📊 Resumo do Mês
• Você gastou R$ 3.450 (↑12% vs mês passado)
• Maior categoria: Alimentação (R$ 850)
• ⚠️ Delivery aumentou 60% - sugestão: cozinhar mais
• 🎯 Você está no limite do pote 'Lazer'

💬 Para análises mais específicas, pergunte:
• "Quanto gastei com [categoria]?"
• "Comparar gastos de [mês]"
```

---

### **Teste 2: Comparação Mensal**

**Envie para o bot:**
```
comparar este mês com o anterior
```

**Ou variações:**
- `comparação mensal`
- `evolução dos gastos`
- `mês atual vs anterior`

**O que esperar:**
- 💰 Total do mês atual
- 💰 Total do mês anterior
- 📈 Variação percentual
- Top 5 categorias com maiores mudanças

**Exemplo de resposta esperada:**
```
📊 Comparação: Novembro/2025 vs Outubro/2025

💰 Novembro/2025: R$ 3.450,00
💰 Outubro/2025: R$ 3.080,00
📈 Variação: +12.0%

Maiores mudanças por categoria:
🔴 Alimentação / Delivery: R$ 850,00 (+60.4%)
🟢 Transporte: R$ 320,00 (-15.0%)
```

---

### **Teste 3: Análise de Categoria Específica**

**Envie para o bot:**
```
quanto gastei com delivery?
```

**Ou outras categorias:**
- `quanto gastei com supermercado?`
- `quanto gastei com transporte?`
- `quanto gastei com lazer?`
- `quanto gastei com alimentação?`

**O que esperar:**
- 📊 Nome da categoria encontrada
- 💰 Total gasto em 3 meses
- 📈 Média mensal
- Detalhamento mês a mês
- Variação percentual (tendência)

**Exemplo de resposta esperada:**
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

## 🔍 Testando Variações de Comando

### Análise Inteligente aceita:
- ✅ "analisar meus gastos"
- ✅ "análise inteligente"
- ✅ "quero insights"
- ✅ "relatório financeiro"
- ✅ "análise dos meus gastos"
- ✅ "padrões de consumo"
- ✅ "me mostre um relatório"

### Comparação Mensal aceita:
- ✅ "comparar este mês com o anterior"
- ✅ "comparação mensal"
- ✅ "evolução mensal"
- ✅ "mês atual vs anterior"

---

## 🐛 Possíveis Problemas e Soluções

### ❌ "Não entendi. Tente 'gastei 50' ou 'meus potes'."

**Causa:** Intent não foi reconhecido corretamente

**Solução:**
- Tente comandos mais diretos: "analisar gastos"
- Evite frases muito longas ou complexas

---

### ❌ "Não consegui gerar a análise no momento."

**Causa:** Pode ser falta de dados ou erro no Gemini

**Soluções:**
1. Verifique se tem transações consolidadas:
   - Envie: `meus potes` (para ver se há gastos)
   - Envie: `quanto gastei este mês?`

2. Se não tiver gastos, adicione alguns:
   - Envie: `gastei 50 no mercado`
   - Confirme: `sim`
   - Repita algumas vezes

3. Verifique logs do servidor (se tiver acesso)

---

### ❌ "Não encontrei gastos para comparar em [mês]."

**Causa:** Não há transações suficientes nos últimos 2 meses

**Solução:**
- Aguarde ter pelo menos alguns gastos em 2 meses diferentes
- Ou adicione gastos manualmente de meses anteriores

---

### ❌ "Não encontrei gastos com '[categoria]' nos últimos 3 meses."

**Causa:** Categoria não existe ou não tem gastos

**Solução:**
- Tente categorias genéricas: "alimentação", "transporte", "lazer"
- Verifique suas categorias enviando: `meus potes` ou `quanto gastei este mês?`

---

## 📊 Cenários de Teste Completos

### Cenário 1: Usuário Novo (Poucos Dados)
```
1. Adicione alguns gastos:
   Você → "gastei 100 no supermercado"
   Bot → [Confirmação]
   Você → "sim"

   Você → "gastei 50 na farmácia"
   Bot → [Confirmação]
   Você → "sim"

2. Peça análise:
   Você → "analisar meus gastos"
   Bot → [Insights básicos com os dados disponíveis]
```

### Cenário 2: Usuário com Histórico
```
1. Peça análise completa:
   Você → "análise inteligente"
   Bot → [Relatório completo com insights ricos]

2. Compare meses:
   Você → "comparar meses"
   Bot → [Comparação detalhada]

3. Analise categoria específica:
   Você → "quanto gastei com delivery?"
   Bot → [Evolução de delivery]
```

### Cenário 3: Verificar Potes
```
1. Configure um pote (se não tiver):
   [Via interface web ou comandos específicos]

2. Peça análise:
   Você → "analisar gastos"
   Bot → [Deve mostrar status dos potes na seção de alertas]
```

---

## 💡 Dicas para Melhores Resultados

1. **Tenha dados suficientes:**
   - Pelo menos 5-10 transações consolidadas
   - Transações em pelo menos 2 meses diferentes

2. **Use comandos diretos:**
   - "analisar gastos" é melhor que "você pode me mostrar uma análise dos meus gastos por favor?"

3. **Teste todas as variações:**
   - Análise completa
   - Comparação mensal
   - Categoria específica

4. **Verifique alertas:**
   - Potes próximos do limite
   - Categorias com aumento significativo
   - Gastos atípicos

---

## ✅ Checklist de Teste

- [ ] Enviar "analisar meus gastos" → Recebe relatório completo
- [ ] Enviar "comparar meses" → Recebe comparação
- [ ] Enviar "quanto gastei com [categoria]?" → Recebe análise da categoria
- [ ] Testar variações: "insights", "relatório", "análise"
- [ ] Verificar se insights são personalizados (não genéricos)
- [ ] Confirmar que valores estão corretos
- [ ] Verificar se sugestões de economia fazem sentido
- [ ] Testar com poucos dados (ver comportamento)
- [ ] Testar com muitos dados (ver insights ricos)

---

## 🎯 Resultados Esperados

### ✅ Sucesso:
- Respostas personalizadas com valores reais
- Insights específicos sobre seus gastos
- Sugestões acionáveis de economia
- Comparações com dados históricos
- Alertas sobre potes e categorias

### ❌ Falha:
- Respostas genéricas sem valores
- Mensagens de erro sem contexto
- Dados incorretos ou desatualizados
- Intent não reconhecido para comandos válidos

---

## 📞 Suporte

Se encontrar problemas:
1. Verifique se o bot está online
2. Confirme que suas transações estão consolidadas
3. Tente comandos mais simples
4. Verifique logs do servidor (se tiver acesso)
5. Consulte a documentação completa em `DOCS_ANALYTICS.md`

---

**🚀 Pronto para testar! Envie "analisar meus gastos" para o bot e veja a mágica acontecer!**
