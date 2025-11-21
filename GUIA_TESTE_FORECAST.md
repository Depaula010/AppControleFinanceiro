# 🧪 Guia de Testes - Previsão de Gastos Futuros

## 📋 Índice

1. [Preparação do Ambiente](#preparação-do-ambiente)
2. [Testes Manuais via WhatsApp](#testes-manuais-via-whatsapp)
3. [Testes com Python (Shell Interativo)](#testes-com-python-shell-interativo)
4. [Testes com Postman/cURL](#testes-com-postmancurl)
5. [Cenários de Teste](#cenários-de-teste)
6. [Validação de Resultados](#validação-de-resultados)
7. [Troubleshooting](#troubleshooting)

---

## 🔧 Preparação do Ambiente

### 1. Verificar Instalação

```bash
# Navegar até o diretório do projeto
cd e:\Projetos\Projetos\AppControleFinanceiro

# Verificar se o serviço existe
ls app/services/forecast_service.py

# Verificar variáveis de ambiente
cat .env | grep GEMINI_API_KEY
cat .env | grep DATABASE_URL
```

### 2. Iniciar Servidor Flask

```bash
# Ativar ambiente virtual (se aplicável)
source venv/bin/activate  # Linux/Mac
# ou
.\venv\Scripts\activate   # Windows

# Iniciar servidor
python run.py

# Você deve ver:
# * Running on http://127.0.0.1:5000
# [INIT] Gemini Flash configurado com sucesso
```

### 3. Verificar Banco de Dados

```bash
# Conectar ao PostgreSQL
psql -U seu_usuario -d seu_banco

# Verificar se há transações
SELECT COUNT(*) FROM "Transacoes" WHERE tipo_transacao = 'Despesa' AND consolidada = true;

# Verificar agendamentos
SELECT COUNT(*) FROM "Agendamentos" WHERE ativo = true;

# Desconectar
\q
```

---

## 📱 Testes Manuais via WhatsApp

### Teste 1: Ativar Feature via WhatsApp

**Pré-requisito:** Usuário cadastrado com `api_key_automate` e número de WhatsApp

**Passos:**

1. Enviar mensagem para o bot:
   ```
   Usuário: "quanto vou gastar este mês"
   ```

2. Aguardar resposta (3-5 segundos)

3. **Resultado Esperado:**
   ```
   Bot: 📈 *Previsão de Gastos*

   📈 Projeção [Mês]
   • Gastos até agora: R$ XXX (dia YY)
   • Projeção final: ~R$ ZZZZ
   • Faltam contas: [lista de contas]
   • Baseado em: média últimos 6 meses

   🔍 Análise de Tendências
   [insights da IA]

   ⚠️ Alertas
   [alertas se houver]

   💡 Recomendações
   [sugestões personalizadas]

   💬 _Baseado em: histórico de 6 meses + contas fixas_
   ```

### Teste 2: Variações de Frases

Teste diferentes formas de pedir previsão:

```
✅ "quanto vou gastar este mês"
✅ "qual a projeção de gastos"
✅ "estimativa de gastos próximo mês"
✅ "previsão financeira"
✅ "orçamento futuro"
✅ "projeção novembro"
```

**Validação:**
- Todas devem retornar a mesma resposta de previsão
- Intent classificado como "Previsão de Gastos"

---

## 🐍 Testes com Python (Shell Interativo)

### Teste 3: Coletar Dados de Previsão

```bash
# Iniciar shell Python com contexto Flask
python
```

```python
from app import create_app
from app.services.forecast_service import get_forecast_data, calculate_simple_forecast

# Criar contexto da aplicação
app = create_app()
with app.app_context():
    # Coletar dados (substitua 1 pelo ID do seu usuário)
    dados = get_forecast_data(usuario_id=1)

    # Imprimir estrutura
    print("=== DADOS COLETADOS ===")
    print(f"Data atual: {dados['data_atual']}")
    print(f"Dia atual: {dados['dia_atual']} de {dados['dias_no_mes']}")
    print(f"Mês atual: {dados['mes_atual']}")
    print(f"\nGastos mensais (histórico):")
    for g in dados['gastos_mensais']:
        print(f"  - {g['mes']}: R$ {g['total']:,.2f}")

    print(f"\nGasto até hoje: R$ {dados['gasto_ate_hoje']['total']:,.2f}")
    print(f"Quantidade de transações: {dados['gasto_ate_hoje']['quantidade']}")

    print(f"\nContas pendentes:")
    for c in dados['contas_pendentes']:
        print(f"  - {c['descricao']}: R$ {c['valor']:,.2f} (dia {c['dia']})")

    print(f"\nContas fixas cadastradas:")
    for c in dados['contas_fixas'][:5]:
        print(f"  - {c['descricao']}: R$ {c['valor']:,.2f} ({c['periodicidade']})")
```

**Resultado Esperado:**

```
=== DADOS COLETADOS ===
Data atual: 2024-11-20
Dia atual: 20 de 30
Mês atual: 2024-11

Gastos mensais (histórico):
  - 2024-11: R$ 1.300,00
  - 2024-10: R$ 2.100,00
  - 2024-09: R$ 1.950,00
  - 2024-08: R$ 2.050,00
  - 2024-07: R$ 1.900,00
  - 2024-06: R$ 2.000,00

Gasto até hoje: R$ 1.300,00
Quantidade de transações: 24

Contas pendentes:
  - Luz: R$ 150,00 (dia 25)
  - Internet: R$ 100,00 (dia 28)

Contas fixas cadastradas:
  - Aluguel: R$ 1.200,00 (MENSAL)
  - Luz: R$ 150,00 (MENSAL)
  - Internet: R$ 100,00 (MENSAL)
```

### Teste 4: Calcular Projeção Simples

```python
# Continuando no shell Python
from app.services.forecast_service import calculate_simple_forecast

with app.app_context():
    # Usar os dados coletados anteriormente
    projecao = calculate_simple_forecast(dados)

    print("\n=== PROJEÇÃO CALCULADA ===")
    print(f"Projeção mês atual: R$ {projecao['projecao_mes_atual']:,.2f}")
    print(f"Média histórica: R$ {projecao['media_historica']:,.2f}")
    print(f"Contas pendentes total: R$ {projecao['contas_pendentes_total']:,.2f}")
    print(f"Gasto até hoje: R$ {projecao['gasto_ate_hoje']:,.2f}")
    print(f"Taxa diária atual: R$ {projecao['taxa_diaria_atual']:,.2f}")

    # Calcular variação
    variacao = ((projecao['projecao_mes_atual'] - projecao['media_historica'])
                / projecao['media_historica'] * 100)
    print(f"\nVariação vs média: {variacao:+.1f}%")

    # Projetar gastos restantes
    dias_restantes = dados['dias_no_mes'] - dados['dia_atual']
    gasto_restante = projecao['projecao_mes_atual'] - projecao['gasto_ate_hoje']
    print(f"\nDias restantes: {dias_restantes}")
    print(f"Gastos estimados até fim do mês: R$ {gasto_restante:,.2f}")
    print(f"Meta diária: R$ {gasto_restante / dias_restantes:,.2f}/dia")
```

**Resultado Esperado:**

```
=== PROJEÇÃO CALCULADA ===
Projeção mês atual: R$ 1.950,00
Média histórica: R$ 1.983,33
Contas pendentes total: R$ 250,00
Gasto até hoje: R$ 1.300,00
Taxa diária atual: R$ 65,00

Variação vs média: -1,7%

Dias restantes: 10
Gastos estimados até fim do mês: R$ 650,00
Meta diária: R$ 65,00/dia
```

### Teste 5: Gerar Insights com IA (Gemini)

```python
from app.services.forecast_service import generate_forecast_insights

with app.app_context():
    # Gerar insights completos (chama Gemini)
    try:
        insights = generate_forecast_insights(usuario_id=1)
        print("\n=== INSIGHTS GERADOS PELA IA ===")
        print(insights)
    except Exception as e:
        print(f"❌ Erro: {e}")
```

**Resultado Esperado:**

```
=== INSIGHTS GERADOS PELA IA ===
📈 Projeção Novembro
• Gastos até agora: R$ 1.300 (dia 20)
• Projeção final: ~R$ 1.950
• Faltam contas: Luz (R$ 150), Internet (R$ 100)
• Baseado em: média últimos 6 meses + ritmo atual

🔍 Análise de Tendências
• Você está gastando dentro da média histórica (R$ 1.983)
• Taxa diária atual: R$ 65/dia está controlada
• Tendência de redução de 1,7% vs média

💡 Recomendações
• Continue no ritmo atual para fechar abaixo da média
• Atenção às contas pendentes de R$ 250
• Meta: gastar no máximo R$ 650 nos próximos 10 dias

💬 _Baseado em: histórico de 6 meses + contas fixas_
```

### Teste 6: Fallback (Sem IA)

```python
from app.services.forecast_service import generate_simple_forecast_text

with app.app_context():
    # Gerar previsão simples (sem IA)
    texto = generate_simple_forecast_text(usuario_id=1)
    print("\n=== PREVISÃO SIMPLES (SEM IA) ===")
    print(texto)
```

**Resultado Esperado:**

```
=== PREVISÃO SIMPLES (SEM IA) ===
📈 **Projeção Novembro**

💰 Gastos até agora: R$ 1.300,00 (dia 20)
📊 Projeção final: ~R$ 1.950,00

📋 **Faltam contas:**
• Luz: R$ 150,00
• Internet: R$ 100,00

📈 Baseado em: média últimos 6 meses (R$ 1.983,33)
```

---

## 🔌 Testes com Postman/cURL

### Teste 7: Endpoint via cURL

**Requisição:**

```bash
curl -X POST http://127.0.0.1:5000/webhook-whatsapp \
  -H "Content-Type: application/json" \
  -d '{
    "message": "quanto vou gastar este mês",
    "user_api_key": "sua_chave_aqui",
    "phone_number": "+5511999999999"
  }'
```

**Resposta Esperada:**

```json
{
  "status": "sucesso",
  "resposta": "📈 *Previsão de Gastos*\n\n📈 Projeção Novembro\n• Gastos até agora: R$ 1.300 (dia 20)\n• Projeção final: ~R$ 1.950\n..."
}
```

### Teste 8: Validar Intent no Gemini

```bash
curl -X POST http://127.0.0.1:5000/webhook-whatsapp \
  -H "Content-Type: application/json" \
  -d '{
    "message": "qual a projeção de gastos",
    "user_api_key": "sua_chave_aqui",
    "phone_number": "+5511999999999"
  }'
```

**Validação:**
- Intent deve ser classificado como "Previsão de Gastos"
- Resposta deve conter "📈 *Previsão de Gastos*"

---

## 🎯 Cenários de Teste

### Cenário 1: Usuário com Gastos Normais

**Setup:**
- Histórico: 6 meses de gastos (~R$ 2.000/mês)
- Mês atual: R$ 1.300 até dia 20
- Contas pendentes: R$ 250

**Teste:**
```python
dados = get_forecast_data(usuario_id=1)
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ `projecao_mes_atual` entre R$ 1.900 e R$ 2.100
- ✅ Variação < 10%
- ✅ Insights mencionam "dentro da média"

---

### Cenário 2: Usuário Gastando Muito

**Setup:**
- Histórico: 6 meses (~R$ 2.000/mês)
- Mês atual: R$ 2.500 até dia 20
- Contas pendentes: R$ 300

**Teste:**
```python
# Simular usuário com gastos altos
# (inserir transações no banco de teste)
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ `projecao_mes_atual` > `media_historica` em 20%+
- ✅ Insights mencionam "acima da média"
- ✅ Alertas presentes
- ✅ Recomendações de controle

---

### Cenário 3: Usuário Novo (Pouco Histórico)

**Setup:**
- Histórico: apenas 1 mês
- Mês atual: R$ 800 até dia 15

**Teste:**
```python
dados = get_forecast_data(usuario_id=novo_usuario_id, meses_historico=1)
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ Projeção funciona mesmo com pouco histórico
- ✅ Média calculada com dados disponíveis
- ✅ Insights mencionam "histórico limitado"

---

### Cenário 4: Início do Mês (Dia 1-5)

**Setup:**
- Data atual: dia 3 do mês
- Gasto até agora: R$ 150

**Teste:**
```python
# Taxa diária será alta (150 / 3 = 50)
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ Projeção não deve ser extrapolada demais
- ✅ Peso maior na média histórica
- ✅ Insights mencionam "início do mês"

---

### Cenário 5: Final do Mês (Dia 25-30)

**Setup:**
- Data atual: dia 28 do mês
- Gasto até agora: R$ 1.950
- Contas pendentes: R$ 50

**Teste:**
```python
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ Projeção próxima do gasto atual + pendentes
- ✅ Taxa diária bem estabelecida
- ✅ Insights mencionam "final do mês"

---

### Cenário 6: Sem Contas Pendentes

**Setup:**
- Todas as contas fixas já foram pagas
- `contas_pendentes = []`

**Teste:**
```python
projecao = calculate_simple_forecast(dados)
```

**Validação:**
- ✅ `contas_pendentes_total = 0`
- ✅ Projeção baseada em taxa diária
- ✅ Insights não mencionam contas pendentes

---

## ✅ Validação de Resultados

### Checklist de Validação

Após cada teste, validar:

#### 1. Dados Coletados
- [ ] `gastos_mensais` contém histórico correto
- [ ] `gasto_ate_hoje` está preciso
- [ ] `contas_pendentes` lista apenas não executadas
- [ ] `contas_fixas` lista apenas ativas
- [ ] `dia_atual` e `dias_no_mes` corretos

#### 2. Cálculos
- [ ] `media_historica` = soma / quantidade de meses
- [ ] `taxa_diaria` = gasto_ate_hoje / dia_atual
- [ ] `projecao_linear` = taxa_diaria * dias_no_mes
- [ ] `projecao_final` >= 85% da média histórica
- [ ] `variacao` calculada corretamente

#### 3. Insights (IA)
- [ ] Texto formatado com emojis
- [ ] Seções presentes: Projeção, Tendências, Alertas, Recomendações
- [ ] Valores reais dos dados (não genéricos)
- [ ] Insights acionáveis
- [ ] Rodapé com base de cálculo

#### 4. Integração WhatsApp
- [ ] Intent classificado corretamente
- [ ] Resposta formatada com markdown
- [ ] Tempo de resposta < 10 segundos
- [ ] Erros tratados com mensagens amigáveis

---

## 🐛 Troubleshooting

### Erro 1: "Modelo Gemini não configurado"

**Causa:** `GEMINI_API_KEY` não está no `.env`

**Solução:**
```bash
# Adicionar no .env
echo "GEMINI_API_KEY=sua_chave_aqui" >> .env

# Reiniciar servidor
python run.py
```

---

### Erro 2: "Não encontrei histórico"

**Causa:** Usuário sem transações consolidadas

**Solução:**
```sql
-- Verificar transações
SELECT COUNT(*) FROM "Transacoes"
WHERE usuario_id = 1 AND consolidada = true;

-- Inserir transações de teste (se necessário)
INSERT INTO "Transacoes" (usuario_id, conta_id, subcategoria_id, valor, tipo_transacao, consolidada, data_transacao, descricao)
VALUES
  (1, 1, 1, 100, 'Despesa', true, '2024-11-01', 'Teste 1'),
  (1, 1, 1, 200, 'Despesa', true, '2024-11-05', 'Teste 2');
```

---

### Erro 3: "Projeção muito alta/baixa"

**Causa:** Poucos dados ou transações atípicas

**Solução:**
```python
# Verificar dados coletados
dados = get_forecast_data(usuario_id=1)
print(f"Quantidade de meses: {len(dados['gastos_mensais'])}")
print(f"Gasto mês atual: {dados['gasto_ate_hoje']['total']}")

# Ajustar parâmetros
dados = get_forecast_data(usuario_id=1, meses_historico=3)
```

---

### Erro 4: Intent Errado

**Causa:** Gemini classificou errado

**Solução:**
```python
# Testar intent manualmente
from app.services.gemini_service import get_message_intent

intent = get_message_intent("quanto vou gastar este mês")
print(f"Intent detectado: {intent}")

# Se errado, revisar prompt em gemini_service.py (linha 84-122)
```

---

### Erro 5: Timeout do Gemini

**Causa:** API lenta ou indisponível

**Solução:**
```python
# Usar fallback (sem IA)
from app.services.forecast_service import generate_simple_forecast_text

texto = generate_simple_forecast_text(usuario_id=1)
print(texto)
```

---

## 📊 Logs e Debugging

### Habilitar Logs Detalhados

```python
# No início do forecast_service.py
import logging
logging.basicConfig(level=logging.DEBUG)
```

### Verificar Logs

```bash
# Durante execução do servidor
tail -f logs/app.log  # se configurado

# Ou verificar console
# Procurar por:
# [FORECAST] ...
# [FORECAST-ERRO] ...
```

### Exemplo de Log Esperado

```
[WHATSAPP] Intenção de Previsão de Gastos detectada
[FORECAST] Coletando dados para usuario_id=1
[FORECAST] Histórico: 6 meses, 2024-05 a 2024-11
[FORECAST] Gasto até hoje: R$ 1.300
[FORECAST] Contas pendentes: 2 (R$ 250)
[FORECAST] Projeção calculada: R$ 1.950
[FORECAST] Chamando Gemini para insights...
[FORECAST] Insights gerados com sucesso (1234 caracteres)
```

---

## 🧪 Testes Automatizados (Futuro)

### Criar `tests/test_forecast_service.py`

```python
import pytest
from app.services.forecast_service import calculate_simple_forecast

def test_projecao_simples():
    dados_mock = {
        "dia_atual": 20,
        "dias_no_mes": 30,
        "gasto_ate_hoje": {"total": 1300},
        "gastos_mensais": [
            {"total": 2000},
            {"total": 1900},
            {"total": 2100}
        ],
        "contas_pendentes": [
            {"valor": 150},
            {"valor": 100}
        ]
    }

    projecao = calculate_simple_forecast(dados_mock)

    assert projecao["gasto_ate_hoje"] == 1300
    assert projecao["contas_pendentes_total"] == 250
    assert projecao["media_historica"] == 2000
    assert 1800 <= projecao["projecao_mes_atual"] <= 2200

# Rodar testes
pytest tests/test_forecast_service.py -v
```

---

## 📝 Checklist Final

Antes de considerar os testes completos:

### Setup
- [ ] Servidor Flask rodando
- [ ] Gemini API configurada
- [ ] Banco de dados acessível
- [ ] Usuário de teste cadastrado

### Testes Funcionais
- [ ] Teste 1-8 executados com sucesso
- [ ] Cenários 1-6 validados
- [ ] Integração WhatsApp funcionando
- [ ] Fallback (sem IA) testado

### Validação de Qualidade
- [ ] Projeções dentro da margem esperada (±15%)
- [ ] Insights da IA relevantes e acionáveis
- [ ] Tempo de resposta < 10 segundos
- [ ] Erros tratados gracefully

### Documentação
- [ ] DOCS_FORECAST.md revisado
- [ ] GUIA_TESTE_FORECAST.md completo
- [ ] Comentários no código atualizados

---

## ✅ Conclusão

Após concluir todos os testes, a feature de **Previsão de Gastos Futuros** estará pronta para:

1. ✅ Uso em produção
2. ✅ Testes com usuários reais
3. ✅ Monitoramento de performance
4. ✅ Iteração baseada em feedback

**Próximos Passos:**
- Deploy em produção
- Monitorar logs e erros
- Coletar feedback dos usuários
- Implementar melhorias sugeridas

**Suporte:**
- Consultar [DOCS_FORECAST.md](./DOCS_FORECAST.md) para detalhes técnicos
- Ver código em `app/services/forecast_service.py`

---

**Data:** 2024-11-21
**Versão:** 1.0
**Feature:** Previsão de Gastos Futuros
