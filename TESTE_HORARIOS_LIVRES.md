# 🧪 Guia de Teste - Horários Livres

## 📋 Pré-requisitos

Antes de testar, certifique-se de:

1. ✅ Google Calendar conectado ao seu usuário
2. ✅ Alguns eventos já criados na agenda (para testar gaps)
3. ✅ Servidor Flask rodando
4. ✅ WhatsApp bot conectado

## 🎯 Casos de Teste

### Teste 1: Consulta Simples - Hoje
**Comando WhatsApp:**
```
quando estou livre hoje?
```

**Resultado Esperado:**
```
🗓️ Horários Livres

📅 Quinta, 21/11
⭐ 14:00-17:00 Sem eventos adjacentes, período preferido
✓ 09:00-11:30 Dia menos ocupado

💡 Você costuma ter compromissos entre 9h e 18h...
```

**Validar:**
- [ ] Sistema detectou intenção "Horários Livres"
- [ ] Listou apenas horários de hoje
- [ ] Horários passados não aparecem
- [ ] Emojis de qualidade corretos (⭐, ✓, •)

---

### Teste 2: Consulta com Contexto - Dentista
**Comando WhatsApp:**
```
quando posso marcar dentista esta semana?
```

**Resultado Esperado:**
```
🗓️ Horários Livres para dentista

📅 Quarta, 21/11
⭐ 14:00-17:00 Sem eventos adjacentes, período preferido (tarde)

📅 Quinta, 22/11
✓ 10:00-12:00 Período preferido (manhã)

💡 Você costuma ter compromissos entre 9h e 18h...

🤖 Sugestão IA: Sugiro quarta às 14h porque dentista é melhor
após o almoço e você tem a tarde toda livre sem eventos adjacentes.
```

**Validar:**
- [ ] Título menciona "para dentista"
- [ ] Mostra múltiplos dias da semana
- [ ] Sugestão da IA apareceu (BONUS)
- [ ] Sugestão faz sentido para "dentista"

---

### Teste 3: Consulta Amanhã
**Comando WhatsApp:**
```
quando estou livre amanhã?
```

**Resultado Esperado:**
```
🗓️ Horários Livres

📅 Sexta, 22/11
⭐ 09:00-18:00 Dia livre, longo período disponível

💡 Você costuma ter compromissos entre 9h e 18h...
```

**Validar:**
- [ ] Mostra apenas o dia de amanhã
- [ ] Se não há eventos, mostra o dia todo como livre
- [ ] Qualidade é "ótimo" (⭐) para dia livre

---

### Teste 4: Próxima Semana
**Comando WhatsApp:**
```
horários livres na próxima semana
```

**Resultado Esperado:**
```
🗓️ Horários Livres

📅 Segunda, 25/11
⭐ 14:00-18:00 Sem eventos adjacentes

📅 Terça, 26/11
✓ 10:00-12:00 Período preferido

📅 Quarta, 27/11
• 15:00-17:00

...

💡 Você costuma ter compromissos entre 9h e 18h...
```

**Validar:**
- [ ] Mostra apenas dias da próxima semana (segunda a domingo)
- [ ] Não mostra dias da semana atual
- [ ] Múltiplos dias listados

---

### Teste 5: Com Duração Específica
**Comando WhatsApp:**
```
quando posso marcar reunião de 2 horas hoje?
```

**Resultado Esperado:**
```
🗓️ Horários Livres para reunião

📅 Quinta, 21/11
⭐ 14:00-17:00 Sem eventos adjacentes (180 min)

💡 Você costuma ter compromissos entre 9h e 18h...

🤖 Sugestão IA: Sugiro hoje às 14h porque é início da tarde,
período de maior produtividade e você tem 3h livres.
```

**Validar:**
- [ ] Sistema detectou duração de 120 minutos
- [ ] Não mostra slots menores que 2 horas
- [ ] Sugestão da IA considera duração longa

---

### Teste 6: Variações de Linguagem
Teste se Gemini reconhece diferentes formas:

**Comandos:**
```
melhor horário para treinar amanhã
estou livre quando esta semana?
que horas posso marcar médico?
quando tenho tempo livre hoje?
```

**Validar:**
- [ ] Todas as variações são reconhecidas como "Horários Livres"
- [ ] Sistema responde adequadamente

---

### Teste 7: Sem Histórico (Usuário Novo)
**Setup:** Usuário sem eventos criados nos últimos 90 dias

**Comando:**
```
quando estou livre amanhã?
```

**Resultado Esperado:**
```
🗓️ Horários Livres

📅 Sexta, 22/11
⭐ 09:00-18:00 Dia livre, longo período disponível

💡 Sem histórico suficiente para análise de padrões.
```

**Validar:**
- [ ] Sistema usa padrões default (9h-18h)
- [ ] Mensagem indica falta de histórico
- [ ] Funcionalidade ainda funciona

---

### Teste 8: Dia Completamente Ocupado
**Setup:** Criar eventos ocupando o dia todo (8h-20h sem gaps)

**Comando:**
```
quando estou livre hoje?
```

**Resultado Esperado:**
```
🗓️ Horários Livres

❌ Nenhum horário livre encontrado no período solicitado.

💡 Você costuma ter compromissos entre 9h e 18h...
```

**Validar:**
- [ ] Mensagem de "nenhum horário livre"
- [ ] Não quebra ou retorna erro
- [ ] Insights do usuário ainda aparecem

---

### Teste 9: Evento de Dia Inteiro
**Setup:** Criar evento "Feriado" (dia inteiro, sem horário)

**Comando:**
```
quando estou livre hoje?
```

**Resultado Esperado:**
```
🗓️ Horários Livres

📅 Quinta, 21/11
⭐ 09:00-18:00 Dia livre, longo período disponível

💡 Você costuma ter compromissos entre 9h e 18h...
```

**Validar:**
- [ ] Evento de dia inteiro NÃO bloqueia horários
- [ ] Slots são mostrados normalmente

---

### Teste 10: Múltiplos Calendários
**Setup:** Usuário com calendário pessoal + trabalho selecionados

**Comando:**
```
quando estou livre amanhã?
```

**Validar:**
- [ ] Sistema busca eventos de TODOS os calendários selecionados
- [ ] Considera bloqueios de ambos os calendários
- [ ] Não duplica eventos

---

## 🔍 Verificação de Logs

Para cada teste, verificar nos logs do servidor:

```
[GEMINI-INTENT] Intenção: Horários Livres
[GEMINI-FREE-TIME] Query extraída: {"period_type": "hoje", ...}
[WHATSAPP] Intenção de Horários Livres detectada
[WHATSAPP] Buscando horários livres: hoje, duração: 60min
[PATTERN] Usuário X tem 45 eventos analisados
[FREE-TIME] 3 slots encontrados para 21/11
```

**Verificar:**
- [ ] Nenhum erro de exceção
- [ ] Intenção detectada corretamente
- [ ] Período extraído corretamente
- [ ] Número de eventos analisados correto

---

## 🐛 Possíveis Problemas e Soluções

### Problema 1: "Módulo não encontrado"
**Erro:**
```
ModuleNotFoundError: No module named 'app.services.free_time_finder_service'
```

**Solução:**
- Reiniciar servidor Flask
- Verificar se arquivo foi salvo corretamente

---

### Problema 2: "Gemini não reconhece intenção"
**Sintoma:** Sistema responde como se fosse consulta normal de agenda

**Solução:**
- Verificar se gemini_service.py foi atualizado
- Testar frases mais diretas: "horários livres hoje"

---

### Problema 3: "Nenhum horário livre" (mas tem gaps)
**Sintoma:** Sistema diz que não há horários, mas claramente há gaps

**Debug:**
```python
# Adicionar print em free_time_finder_service.py
print(f"[DEBUG] Eventos do dia: {day_events}")
print(f"[DEBUG] Trabalho inicio: {trabalho_inicio}, fim: {trabalho_fim}")
print(f"[DEBUG] Slots gerados: {day_slots}")
```

**Possíveis causas:**
- Horário de trabalho muito restrito
- Duração mínima muito grande
- Eventos se sobrepondo

---

### Problema 4: "Sugestão da IA não aparece"
**Sintoma:** Lista de horários funciona, mas não há sugestão da IA

**Causa:** Contexto não foi extraído ou Gemini falhou

**Validar:**
```
[GEMINI-FREE-TIME] Query extraída: {"contexto": null}  ← Problema aqui
```

**Solução:**
- Usar frases mais explícitas: "marcar DENTISTA"
- Gemini pode estar offline (sugestão é BONUS, não crítica)

---

### Problema 5: "Timezone errado"
**Sintoma:** Horários aparecem com 3h de diferença

**Solução:**
- Verificar se TIMEZONE_BR está definido em todos os services
- Verificar logs: eventos devem ter "-03:00" no timestamp

---

## ✅ Checklist Final

Após todos os testes, verificar:

- [ ] Funcionalidade básica funciona (listar horários livres)
- [ ] Análise de padrões funciona (insights personalizados)
- [ ] Qualidade dos slots faz sentido (⭐ > ✓ > •)
- [ ] Sugestão da IA é relevante (quando tem contexto)
- [ ] Sem erros de exceção nos logs
- [ ] Performance aceitável (<5 segundos para responder)
- [ ] Múltiplos períodos funcionam (hoje, amanhã, semana)
- [ ] Tratamento de erros funciona (sem agenda, sem slots)

---

## 📊 Exemplo de Teste Completo

### Setup
1. Criar eventos de teste:
   - Hoje 10h-11h: "Reunião com equipe"
   - Hoje 15h-16h: "Médico"
   - Amanhã: SEM EVENTOS

2. Enviar comando:
   ```
   quando posso marcar dentista esta semana?
   ```

3. **Resultado esperado completo:**
   ```
   🗓️ Horários Livres para dentista

   📅 Quinta, 21/11
   ⭐ 11:00-15:00 Período preferido (tarde), longo período disponível
   ✓ 16:00-18:00

   📅 Sexta, 22/11
   ⭐ 09:00-18:00 Dia livre, longo período disponível

   💡 Você costuma ter compromissos entre 9h e 18h, com preferência
   por manhãs e tardes. Seus dias mais ocupados: segunda e quarta.

   🤖 Sugestão IA: Sugiro sexta às 14h porque dentista é melhor
   após o almoço e você tem o dia todo livre, garantindo flexibilidade.
   ```

4. **Validar todos os pontos:**
   - [x] Título menciona "dentista"
   - [x] Hoje mostra 2 gaps (11-15h e 16-18h)
   - [x] Amanhã mostra dia livre
   - [x] Gap maior tem qualidade "ótimo" (⭐)
   - [x] Insights personalizados corretos
   - [x] Sugestão da IA faz sentido

---

## 🎉 Teste Concluído!

Se todos os testes passaram, a funcionalidade está pronta para produção! 🚀

**Próximos passos sugeridos:**
1. Testar com usuários reais
2. Coletar feedback sobre sugestões da IA
3. Ajustar pontuação de qualidade se necessário
4. Considerar melhorias futuras do FEATURE_HORARIOS_LIVRES.md
