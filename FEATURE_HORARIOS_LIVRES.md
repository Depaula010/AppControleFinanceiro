# 🗓️ Feature: Sugestões Inteligentes de Horários Livres

## 📋 Visão Geral

Esta funcionalidade permite que o usuário consulte seus horários livres através do WhatsApp. O sistema analisa a agenda do Google Calendar, identifica gaps entre eventos e sugere os melhores horários baseado em padrões de comportamento do usuário.

## 🎯 Exemplos de Uso

### Consultas Suportadas

```
Usuário: "quando estou livre amanhã?"
Usuário: "quando posso marcar dentista esta semana?"
Usuário: "melhor horário para reunião de 2 horas hoje"
Usuário: "horários livres na próxima semana"
```

### Resposta do Sistema

```
🗓️ Horários Livres para dentista

📅 Quarta, 21/11
⭐ 14:00-17:00 Sem eventos adjacentes, período preferido (tarde)
✓ 09:00-11:30 Dia menos ocupado

📅 Quinta, 22/11
✓ 10:00-12:00 Período preferido (manhã)
• 15:00-18:00

💡 Você costuma ter compromissos entre 9h e 18h, com preferência por manhãs e tardes. Seus dias mais ocupados: segunda e quarta.

🤖 Sugestão IA: Sugiro quarta às 14h porque dentista é melhor após o almoço e você tem a tarde toda livre sem eventos adjacentes.
```

## 🏗️ Arquitetura

### Componentes Criados

#### 1. **gemini_service.py** (Atualizado)
- Nova intenção: `"Horários Livres"`
- Nova função: `extract_free_time_query(texto_msg)`
  - Extrai: período, duração desejada, contexto (ex: "dentista")

#### 2. **user_schedule_pattern_service.py** (Novo)
Analisa padrões de comportamento do usuário baseado em histórico de eventos:

**Funcionalidades:**
- `analyze_user_patterns(db_engine, usuario_id, lookback_days=90)`
  - Analisa últimos 90 dias de eventos
  - Identifica:
    - Horário mais cedo/tarde que costuma ter compromissos
    - Dias da semana mais ocupados
    - Períodos preferidos (manhã/tarde/noite)
    - Duração média dos eventos

**Retorno:**
```python
{
    "horario_mais_cedo": "09:00",
    "horario_mais_tarde": "18:00",
    "dias_mais_ocupados": ["monday", "wednesday"],
    "periodos_preferidos": ["manha", "tarde"],
    "duracao_media_eventos": 60,
    "total_eventos_analisados": 45
}
```

#### 3. **free_time_finder_service.py** (Novo)
Service principal que detecta horários livres e analisa qualidade dos slots.

**Principais métodos:**

##### `find_free_slots(db_engine, usuario_id, period_type, duracao_minutos=60)`
- Calcula range de datas baseado no período solicitado
- Busca eventos existentes no Google Calendar
- Gera slots livres com análise de qualidade
- Ordena por qualidade (ótimo → bom → regular)

##### `_analyze_slot_quality(slot_inicio, slot_fim, evento_anterior, evento_posterior, patterns, current_date)`
Analisa qualidade de um slot baseado em:

**Fatores considerados (sistema de pontos):**
1. **Eventos adjacentes** (+3 pontos se dia livre, +2 sem adjacentes, +1 com adjacentes)
2. **Período do dia** (+2 pontos se for período preferido do usuário)
3. **Dia da semana** (+1 ponto se não for dia ocupado)
4. **Duração do slot** (+2 pontos se ≥3h, +1 se ≥2h)

**Classificação:**
- ≥6 pontos: "ótimo" ⭐
- ≥4 pontos: "bom" ✓
- <4 pontos: "regular" •

##### `format_free_slots_message(result, contexto=None)`
Formata mensagem humanizada para WhatsApp:
- Agrupa slots por data
- Mostra até 3 melhores slots por dia
- Adiciona emojis de qualidade
- Inclui insights do usuário

##### `suggest_best_slot_with_ai(result, contexto, usuario_preferences)`
**BONUS**: Usa Gemini para sugerir o melhor horário considerando:
- Tipo de compromisso (ex: dentista → melhor após almoço)
- Padrões do usuário
- Boas práticas gerais

**Exemplo de prompt para Gemini:**
```
O usuário quer marcar: "dentista"

Horários disponíveis:
[{"data": "2025-11-21", "horario": "14:00-17:00", "qualidade": "otimo"}]

Padrões do usuário:
Você costuma ter compromissos entre 9h e 18h...

Sugira o MELHOR horário considerando:
1. O tipo de compromisso
2. Os padrões do usuário
3. Boas práticas
```

#### 4. **webhooks.py** (Atualizado)
Nova rota para intenção "Horários Livres":

```python
elif intent == 'Horários Livres':
    # Extrair período e contexto
    free_time_data = gemini_service.extract_free_time_query(texto_msg)

    # Buscar horários livres
    result = FreeTimeFinderService.find_free_slots(
        db_engine, usuario_id, period_type, duracao_minutos
    )

    # Formatar mensagem
    resposta = FreeTimeFinderService.format_free_slots_message(result, contexto)

    # BONUS: Sugestão da IA
    if contexto:
        sugestao_ai = FreeTimeFinderService.suggest_best_slot_with_ai(...)
        resposta += sugestao_ai
```

## 🔄 Fluxo Completo

```
1. Usuário envia: "quando posso marcar dentista esta semana?"
   ↓
2. Gemini classifica intenção: "Horários Livres"
   ↓
3. Gemini extrai dados:
   - period_type: "esta_semana"
   - duracao_minutos: 60
   - contexto: "dentista"
   ↓
4. UserSchedulePatternService analisa padrões do usuário
   - Busca eventos dos últimos 90 dias
   - Identifica: horários habituais, dias ocupados, preferências
   ↓
5. FreeTimeFinderService busca horários livres:
   a) Calcula range de datas (hoje → domingo)
   b) Busca eventos existentes no Google Calendar
   c) Para cada dia:
      - Filtra eventos do dia
      - Identifica gaps entre eventos
      - Gera slots livres (≥ duracao_minutos)
      - Analisa qualidade de cada slot (pontuação)
   d) Ordena por qualidade
   ↓
6. Formata mensagem humanizada
   - Agrupa por data
   - Mostra top 3 slots/dia com emojis
   - Adiciona insights do usuário
   ↓
7. BONUS: Gemini sugere melhor horário
   - Considera contexto ("dentista")
   - Usa padrões do usuário
   - Aplica boas práticas
   ↓
8. Envia resposta completa via WhatsApp
```

## 🧠 Inteligência da Análise

### Períodos Suportados
- **hoje**: Horários disponíveis ainda hoje
- **amanhã**: Próximo dia
- **esta_semana**: Hoje até domingo
- **proxima_semana**: Próxima segunda até próximo domingo

### Horário de Trabalho Inteligente
O sistema define automaticamente o "horário de trabalho" baseado nos padrões:

**Padrão Default:**
- 8h às 20h (se usuário não tem histórico)

**Com Histórico:**
- Usa `horario_mais_cedo` - 1h
- Usa `horario_mais_tarde` + 1h
- Exemplo: Se usuário sempre marca 9h-18h → busca slots 8h-19h

### Ajuste para "Hoje"
Se período é "hoje", o sistema:
- Ignora horários passados
- Começa do próximo slot de 30 minutos
- Exemplo: São 14:37 → próximo slot: 15:00

### Eventos de Dia Inteiro
Eventos marcados como "dia inteiro" (sem horário específico):
- São ignorados na análise de slots
- Não bloqueiam horários específicos

## 📊 Exemplos de Cenários

### Cenário 1: Dia Livre
```
Entrada: "quando estou livre amanhã?"
Eventos: [] (nenhum evento)
Saída:
  • 09:00-18:00 (540 min) - Qualidade: ÓTIMO
    Motivos: Dia livre, período preferido, longo período disponível
```

### Cenário 2: Dia com 2 Eventos
```
Entrada: "horários livres hoje"
Eventos: [09:00-11:00 "Reunião", 15:00-16:00 "Médico"]
Saída:
  • 11:00-15:00 (240 min) - Qualidade: BOM
    Motivos: Período preferido (tarde), longo período disponível
  • 16:00-18:00 (120 min) - Qualidade: REGULAR
    Motivos: Após último evento
```

### Cenário 3: Semana Completa
```
Entrada: "quando posso marcar dentista esta semana?"
Eventos: [Vários eventos segunda e quarta]
Saída:
  📅 Terça, 22/11
  ⭐ 14:00-18:00 - Sem eventos adjacentes, período preferido

  📅 Quinta, 23/11
  ✓ 10:00-12:00 - Período preferido (manhã)

  🤖 Sugestão IA: Sugiro terça às 14h porque dentista é melhor após
  o almoço e você tem a tarde inteira livre.
```

## 🔧 Configurações e Personalizações

### Lookback Period (Análise de Padrões)
- Default: 90 dias
- Ajustável em `UserSchedulePatternService.analyze_user_patterns(lookback_days=X)`

### Duração Mínima dos Slots
- Default: 60 minutos
- Pode ser especificada pelo usuário: "reunião de 2 horas"
- Gemini extrai automaticamente

### Número de Slots Mostrados
- Default: Top 3 slots por dia
- Modificável em `format_free_slots_message()` linha:
  ```python
  for slot in day_slots[:3]:  # Mudar para [:5] para mostrar 5
  ```

### Horário de Trabalho
Pode ser hardcoded se preferir:
```python
trabalho_inicio = time(hour=8, minute=0)
trabalho_fim = time(hour=20, minute=0)
```

## 🐛 Tratamento de Erros

### Sem Google Calendar Conectado
```python
if not token_result:
    return _get_default_patterns()
```
Retorna padrões default (9h-18h) sem análise personalizada.

### Falha na API do Google
```python
except Exception as e:
    print(f"[FREE-TIME] Erro ao buscar eventos: {e}")
    return []
```
Retorna lista vazia → mensagem "Nenhum horário livre encontrado"

### Gemini Indisponível (Sugestão AI)
```python
except Exception as e:
    print(f"[FREE-TIME-AI] Erro ao gerar sugestão: {e}")
    return None
```
Simplesmente não mostra a sugestão da IA, mas lista de horários funciona normalmente.

## 🚀 Melhorias Futuras

### 1. Cache de Padrões
Atualmente analisa 90 dias toda vez. Melhorar:
```python
# Cachear padrões do usuário por 24h
redis_service.set_with_ttl(f"patterns:{usuario_id}", patterns, 86400)
```

### 2. Preferências Personalizadas
Permitir usuário configurar:
- "Não quero eventos antes das 10h"
- "Prefiro terças e quintas"
- Salvar em tabela `UsuarioPreferencias`

### 3. Integração com Criação de Evento
```
Bot: "⭐ 14:00-17:00 Sem eventos adjacentes"
Usuário: "marcar dentista nesse horário"
Bot: [Cria evento automaticamente às 14h]
```

### 4. Sugestões Proativas
```python
# Todo domingo, sugerir melhores horários da semana
if dia_semana == 'domingo':
    slots = find_free_slots('esta_semana')
    msg = "💡 Planeje sua semana!\n" + format_message(slots)
```

### 5. Considerar Deslocamento
Integrar com Google Maps API:
```python
# Se evento anterior tem local diferente
if evento_anterior.local != evento_atual.local:
    tempo_deslocamento = maps_api.get_duration(...)
    slot_inicio += tempo_deslocamento
```

## 📝 Testes Recomendados

### Teste 1: Sem histórico
- Usuário novo sem eventos
- Deve usar padrões default

### Teste 2: Dia lotado
- Criar 5+ eventos em um dia
- Verificar se detecta gaps pequenos

### Teste 3: Período inválido
- "quando estou livre ano que vem"
- Deve retornar erro de período inválido

### Teste 4: Evento de dia inteiro
- Criar evento "Feriado" (dia inteiro)
- Verificar se não bloqueia horários

### Teste 5: Múltiplos calendários
- Usuário com calendário pessoal + trabalho
- Verificar se busca em ambos

## 📚 Dependências

### Bibliotecas Python
- `datetime`, `timedelta`, `time` (padrão)
- `zoneinfo` (para timezone Brasil)
- `collections.defaultdict` (para análise de padrões)
- `google-api-python-client` (já instalado)
- `google.generativeai` (Gemini, já instalado)

### Services do Projeto
- `GoogleCalendarOAuthService` (autenticação)
- `gemini_service` (classificação e extração)
- `redis_service` (opcional, para cache futuro)

### Database
- Nenhuma tabela nova necessária
- Usa tabela existente: `GoogleCalendarTokens`

## 🎉 Conclusão

A funcionalidade "Horários Livres" transforma o assistente em um verdadeiro secretário pessoal que:

✅ Analisa padrões de comportamento do usuário
✅ Identifica automaticamente melhores horários
✅ Considera contexto do compromisso (dentista, reunião, etc)
✅ Usa IA para sugestões personalizadas
✅ Funciona 100% via WhatsApp

**Impacto:** Reduz drasticamente o tempo gasto para agendar compromissos e melhora a qualidade da organização pessoal do usuário.
