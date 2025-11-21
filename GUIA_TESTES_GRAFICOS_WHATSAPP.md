# 📊 Guia de Testes - Gráficos via WhatsApp

## 📋 Índice
1. [Visão Geral](#visão-geral)
2. [Pré-requisitos](#pré-requisitos)
3. [Tipos de Gráficos Disponíveis](#tipos-de-gráficos-disponíveis)
4. [Como Testar](#como-testar)
5. [Exemplos de Mensagens](#exemplos-de-mensagens)
6. [Solução de Problemas](#solução-de-problemas)
7. [Verificação de Logs](#verificação-de-logs)

---

## 🎯 Visão Geral

A funcionalidade de gráficos permite que os usuários solicitem visualizações de seus dados financeiros diretamente pelo WhatsApp. O sistema gera gráficos em formato PNG e os envia como imagem na conversa.

### Funcionalidades Implementadas

- ✅ **Gráfico de Pizza**: Gastos por categoria
- ✅ **Gráfico de Barras**: Evolução mensal (Despesas vs Rendas)
- ✅ **Gráfico de Linha**: Evolução do saldo ao longo do tempo

---

## ⚙️ Pré-requisitos

### 1. Configuração do Bot WhatsApp

O bot WhatsApp precisa ter o endpoint `/enviar-imagem` implementado que aceita:

```json
{
  "numero": "5531999999999",
  "imagem": "base64_encoded_image",
  "legenda": "Título do gráfico"
}
```

**Headers necessários:**
```
x-api-key: sua_api_key_aqui
```

### 2. Variáveis de Ambiente

Certifique-se de que as seguintes variáveis estão configuradas:

```env
BOT_WHATSAPP_URL=https://seu-bot.onrender.com
API_SECRET_KEY=sua_chave_secreta
DATABASE_URL=postgresql://...
REDIS_URL=redis://...
```

### 3. Dependências

Verifique se o `matplotlib` está instalado:

```bash
pip install matplotlib==3.8.2
```

### 4. Dados de Teste

Para testes completos, certifique-se de ter:
- ✅ Transações de despesas registradas
- ✅ Transações de rendas registradas
- ✅ Dados de múltiplas categorias
- ✅ Dados de múltiplos meses (ideal: 6+ meses)

---

## 📊 Tipos de Gráficos Disponíveis

### 1. Gráfico de Pizza 🍕

**O que mostra:**
- Distribuição percentual de gastos por categoria
- Período padrão: últimos 30 dias

**Quando usar:**
- Para visualizar quais categorias consomem mais do orçamento
- Identificar padrões de gastos por categoria

**SQL utilizada:**
```sql
SELECT
    COALESCE(mc.nome, 'Outros') as categoria,
    SUM(t.valor) as total
FROM Transacoes t
LEFT JOIN SubCategoria sc ON t.id_subcategoria = sc.id
LEFT JOIN MacroCategoria mc ON sc.id_macrocategoria = mc.id
WHERE t.usuario_id = :uid
    AND t.tipo_fluxo = 'Despesa'
    AND t.data_transacao >= :data_inicio
GROUP BY mc.nome
```

---

### 2. Gráfico de Barras 📊

**O que mostra:**
- Comparação mensal entre Despesas e Rendas
- Período padrão: últimos 6 meses

**Quando usar:**
- Para comparar entradas e saídas mensais
- Identificar tendências de gastos ao longo do tempo
- Ver meses com déficit ou superávit

**SQL utilizada:**
```sql
SELECT
    DATE_TRUNC('month', data_transacao) as mes,
    SUM(CASE WHEN tipo_fluxo = 'Despesa' THEN valor ELSE 0 END) as despesas,
    SUM(CASE WHEN tipo_fluxo = 'Renda' THEN valor ELSE 0 END) as rendas
FROM Transacoes
WHERE usuario_id = :uid
    AND data_transacao >= CURRENT_DATE - INTERVAL ':months months'
GROUP BY mes
```

---

### 3. Gráfico de Linha 📈

**O que mostra:**
- Evolução do saldo acumulado ao longo do tempo
- Período padrão: últimos 6 meses

**Quando usar:**
- Para visualizar crescimento ou declínio patrimonial
- Identificar momentos críticos de saldo baixo
- Acompanhar tendência geral das finanças

**SQL utilizada:**
```sql
SELECT
    data_transacao,
    SUM(CASE WHEN tipo_fluxo = 'Renda' THEN valor ELSE -valor END)
        OVER (ORDER BY data_transacao) as saldo_acumulado
FROM Transacoes
WHERE usuario_id = :uid
    AND data_transacao >= :data_inicio
ORDER BY data_transacao
```

---

## 🧪 Como Testar

### Preparação do Ambiente de Teste

1. **Certifique-se que o servidor está rodando:**

```bash
# Se usando Docker
docker-compose up -d

# Ou localmente
python run.py
```

2. **Verifique se o bot WhatsApp está online:**

```bash
curl -X GET https://seu-bot.onrender.com/status \
  -H "x-api-key: sua_api_key"
```

3. **Prepare dados de teste** (se necessário):

```python
# Script para popular dados de teste
python populate_test_data.py
```

---

### Fluxo de Teste Completo

#### 🟢 Cenário 1: Gráfico de Pizza (Padrão)

**Objetivo:** Testar geração de gráfico de pizza com período padrão

1. **Envie a mensagem:**
   ```
   Gráfico de gastos
   ```

2. **Comportamento esperado:**
   - ✅ Sistema identifica intent "Gráfico de Gastos"
   - ✅ Extrai tipo "pizza" com 30 dias
   - ✅ Gera gráfico com categorias e percentuais
   - ✅ Envia imagem PNG via WhatsApp
   - ✅ Usuário recebe mensagem: "✅ 📊 Gastos por Categoria - Últimos 30 dias"

3. **Verificações:**
   - [ ] Imagem foi recebida?
   - [ ] Cores são distintas entre categorias?
   - [ ] Percentuais somam 100%?
   - [ ] Legenda mostra valores em R$?

---

#### 🟠 Cenário 2: Gráfico de Pizza Personalizado

**Objetivo:** Testar período customizado

1. **Envie a mensagem:**
   ```
   Gráfico de pizza dos últimos 7 dias
   ```

2. **Comportamento esperado:**
   - ✅ Sistema extrai período de 7 dias
   - ✅ Gera gráfico apenas com dados da última semana
   - ✅ Mensagem: "✅ 📊 Gastos por Categoria - Últimos 7 dias"

---

#### 🔵 Cenário 3: Gráfico de Barras (Padrão)

**Objetivo:** Testar evolução mensal

1. **Envie a mensagem:**
   ```
   Gráfico de evolução mensal
   ```

2. **Comportamento esperado:**
   - ✅ Sistema identifica tipo "barras"
   - ✅ Gera gráfico com 6 meses
   - ✅ Mostra barras de Despesas (vermelho) e Rendas (verde)
   - ✅ Valores aparecem no topo de cada barra
   - ✅ Mensagem: "✅ 📊 Evolução Mensal - Últimos 6 meses"

3. **Verificações:**
   - [ ] Duas barras por mês (Despesas e Rendas)?
   - [ ] Valores formatados em R$?
   - [ ] Meses no formato "Jan/25", "Fev/25"?
   - [ ] Grid no eixo Y para facilitar leitura?

---

#### 🟣 Cenário 4: Gráfico de Barras Personalizado

**Objetivo:** Testar período customizado

1. **Envie a mensagem:**
   ```
   Gráfico de barras dos últimos 3 meses
   ```

2. **Comportamento esperado:**
   - ✅ Sistema extrai 3 meses
   - ✅ Gera gráfico apenas com últimos 3 meses
   - ✅ Mensagem: "✅ 📊 Evolução Mensal - Últimos 3 meses"

---

#### 🟡 Cenário 5: Gráfico de Linha (Padrão)

**Objetivo:** Testar evolução do saldo

1. **Envie a mensagem:**
   ```
   Gráfico de saldo
   ```

2. **Comportamento esperado:**
   - ✅ Sistema identifica tipo "linha"
   - ✅ Gera gráfico com linha azul mostrando saldo acumulado
   - ✅ Área abaixo da linha preenchida
   - ✅ Linha tracejada vermelha no zero (referência)
   - ✅ Mensagem: "✅ 📈 Evolução do Saldo - Últimos 6 meses"

3. **Verificações:**
   - [ ] Linha está suave e conectada?
   - [ ] Área preenchida visível?
   - [ ] Eixo Y mostra valores em R$?
   - [ ] Datas no eixo X estão legíveis?

---

#### 🔴 Cenário 6: Sem Dados Suficientes

**Objetivo:** Testar comportamento quando não há dados

1. **Preparação:**
   - Use usuário sem transações OU
   - Solicite período sem dados

2. **Envie a mensagem:**
   ```
   Gráfico dos últimos 365 dias
   ```

3. **Comportamento esperado:**
   - ✅ Sistema tenta gerar gráfico
   - ✅ Detecta ausência de dados
   - ✅ Retorna mensagem: "❌ Não há dados suficientes para gerar o gráfico no período solicitado."
   - ✅ Nenhuma imagem é enviada

---

#### ⚫ Cenário 7: Variações de Linguagem Natural

**Objetivo:** Testar robustez da compreensão de texto

Teste as seguintes variações:

```
1. "mostrar gráfico"
2. "quero ver gráficos"
3. "me manda um gráfico de gastos"
4. "visualizar meus gastos"
5. "gráfico por categoria"
6. "evolução do saldo"
7. "gráfico de linha"
```

**Comportamento esperado:**
- ✅ Todas devem ser identificadas como "Gráfico de Gastos"
- ✅ Sistema extrai o tipo correto (pizza/barras/linha)
- ✅ Gráfico apropriado é gerado

---

## 💬 Exemplos de Mensagens

### Gráfico de Pizza

```
✅ Funcionam:
- "gráfico de gastos"
- "gráfico de pizza"
- "mostrar gráfico por categoria"
- "quero ver meus gastos em gráfico"
- "gráfico dos últimos 15 dias"

❌ Não funcionam (intents diferentes):
- "quanto gastei" → Intent: Consulta Período
- "análise de gastos" → Intent: Análise Inteligente
```

### Gráfico de Barras

```
✅ Funcionam:
- "gráfico de evolução mensal"
- "gráfico de barras"
- "comparar rendas e despesas"
- "gráfico de barras dos últimos 4 meses"

❌ Não funcionam:
- "comparar com mês anterior" → Intent: Comparação Mensal
```

### Gráfico de Linha

```
✅ Funcionam:
- "gráfico de saldo"
- "evolução do saldo"
- "gráfico de linha"
- "ver meu saldo ao longo do tempo"
- "gráfico de linha dos últimos 12 meses"
```

---

## 🔧 Solução de Problemas

### Problema 1: "❌ Não consegui enviar o gráfico"

**Possíveis causas:**
1. Bot WhatsApp offline
2. Endpoint `/enviar-imagem` não implementado
3. API key incorreta
4. Timeout na requisição

**Como investigar:**

```bash
# 1. Testar endpoint manualmente
curl -X POST https://seu-bot.onrender.com/enviar-imagem \
  -H "Content-Type: application/json" \
  -H "x-api-key: sua_api_key" \
  -d '{
    "numero": "5531999999999",
    "imagem": "iVBORw0KGgo...",
    "legenda": "Teste"
  }'

# 2. Verificar logs do bot
heroku logs --tail -a seu-bot

# 3. Verificar variáveis de ambiente
echo $BOT_WHATSAPP_URL
echo $API_SECRET_KEY
```

**Solução:**
- Verifique se o bot está rodando
- Implemente o endpoint `/enviar-imagem` se não existir
- Confirme que a API key está correta

---

### Problema 2: "❌ Não há dados suficientes"

**Possíveis causas:**
1. Usuário sem transações no período
2. Query SQL retornando vazio
3. Período solicitado muito longo/curto

**Como investigar:**

```sql
-- Verificar transações do usuário
SELECT COUNT(*), MIN(data_transacao), MAX(data_transacao)
FROM Transacoes
WHERE usuario_id = 'seu_usuario_id';

-- Verificar categorias
SELECT mc.nome, COUNT(*)
FROM Transacoes t
JOIN SubCategoria sc ON t.id_subcategoria = sc.id
JOIN MacroCategoria mc ON sc.id_macrocategoria = mc.id
WHERE t.usuario_id = 'seu_usuario_id'
GROUP BY mc.nome;
```

**Solução:**
- Adicione transações de teste
- Ajuste o período solicitado
- Verifique se há transações categorizadas

---

### Problema 3: Gráfico Gerado mas com Erro Visual

**Possíveis causas:**
1. Fontes não instaladas no servidor
2. Backend Matplotlib incorreto
3. Dependências faltando

**Como investigar:**

```python
# Teste local de geração
from app.services import chart_service

# Gerar gráfico de teste
chart_bytes = chart_service.generate_pie_chart(usuario_id=1, period_days=30)

if chart_bytes:
    with open('test_chart.png', 'wb') as f:
        f.write(chart_bytes)
    print("Gráfico gerado com sucesso!")
else:
    print("Erro ao gerar gráfico")
```

**Solução:**
```bash
# Instalar fontes (Ubuntu/Debian)
sudo apt-get install fonts-dejavu-core

# Limpar cache do Matplotlib
rm -rf ~/.cache/matplotlib
```

---

### Problema 4: "❌ Não consegui gerar o gráfico. Erro: ..."

**Possíveis causas:**
1. Erro na query SQL
2. Tipo de dado incompatível
3. Memória insuficiente

**Como investigar:**

Verifique os logs do servidor:

```bash
# Logs do Docker
docker-compose logs -f app

# Logs locais
tail -f logs/app.log
```

Procure por:
```
[CHART] Erro ao gerar gráfico: ...
```

**Soluções comuns:**

1. **PostgreSQL INTERVAL syntax:**
```python
# ❌ Errado
AND data_transacao >= CURRENT_DATE - INTERVAL ':months months'

# ✅ Correto
data_inicio = datetime.now() - timedelta(days=months*30)
AND data_transacao >= :data_inicio
```

2. **Conversão de tipos:**
```python
# Garantir que valores são float
valores = [float(row.total) for row in result]
```

3. **Memória:**
```python
# Fechar figura após uso
plt.close()
```

---

## 📋 Verificação de Logs

### Logs Esperados (Sucesso)

```
[WHATSAPP] Intenção de Gráfico de Gastos detectada
[GEMINI-CHART] Tipo de gráfico extraído: {"tipo_grafico": "pizza", "periodo_dias": 30}
[CHART] Gerando gráfico tipo: pizza
[NOTIF-IMG] ✅ Imagem enviada para 5531999999999
```

### Logs de Erro Comuns

```
# Problema: Bot offline
[NOTIF-IMG] ❌ Erro 503 para 5531999999999

# Problema: Sem dados
[CHART] Gráfico não gerado: Nenhum resultado da query

# Problema: Matplotlib
[CHART] Erro ao gerar gráfico: No module named 'matplotlib'

# Problema: PostgreSQL
[CHART] Erro ao gerar gráfico: syntax error at or near "INTERVAL"
```

---

## ✅ Checklist Final de Testes

### Testes Funcionais

- [ ] Gráfico de pizza padrão (30 dias)
- [ ] Gráfico de pizza customizado (7, 15, 60 dias)
- [ ] Gráfico de barras padrão (6 meses)
- [ ] Gráfico de barras customizado (3, 12 meses)
- [ ] Gráfico de linha padrão (6 meses)
- [ ] Gráfico de linha customizado (3, 12 meses)
- [ ] Mensagem sem dados suficientes
- [ ] Variações de linguagem natural

### Testes Visuais

- [ ] Cores distintas e agradáveis
- [ ] Texto legível (sem sobreposição)
- [ ] Valores formatados corretamente (R$)
- [ ] Legendas visíveis
- [ ] Títulos descritivos
- [ ] Eixos com labels claros
- [ ] Imagem em alta resolução (150 DPI)

### Testes de Performance

- [ ] Tempo de resposta < 10 segundos
- [ ] Imagem < 1MB (para envio rápido)
- [ ] Sem vazamento de memória
- [ ] Arquivos temporários são removidos

### Testes de Erro

- [ ] Usuário sem transações
- [ ] Bot WhatsApp offline
- [ ] API key inválida
- [ ] Erro no Gemini
- [ ] Erro no PostgreSQL
- [ ] Período inválido

---

## 📞 Suporte

### Reportar Problemas

Se encontrar problemas, forneça:

1. **Mensagem enviada:** "gráfico de gastos"
2. **Resposta recebida:** "❌ Não consegui..."
3. **Logs do servidor:** (cole aqui)
4. **Hora do erro:** 2025-11-21 14:30
5. **Usuário ID:** (se possível)

### Próximas Melhorias

- [ ] Cache de gráficos (evitar regenerar)
- [ ] Gráfico de gastos por conta
- [ ] Gráfico de tendências (ML)
- [ ] Exportar gráfico em PDF
- [ ] Gráficos comparativos (usuário vs média)

---

## 🎉 Conclusão

Parabéns! Você implementou com sucesso a funcionalidade de gráficos via WhatsApp.

**Recursos úteis:**
- [Matplotlib Documentation](https://matplotlib.org/stable/contents.html)
- [WhatsApp Business API](https://developers.facebook.com/docs/whatsapp)
- [PostgreSQL Date Functions](https://www.postgresql.org/docs/current/functions-datetime.html)

**Contato:**
- GitHub Issues: [Seu repositório]
- Email: [Seu email]
