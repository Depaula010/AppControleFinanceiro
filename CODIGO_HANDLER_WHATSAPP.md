# 📱 Código para Adicionar ao Bot do WhatsApp

## Onde adicionar?

Arquivo: `app/routes/webhooks.py`

Adicione **ANTES** da linha `#==== INTENÇÃO: Análise Inteligente ====` (linha ~1323)

---

## Código 1: Handler "Configurar Localização"

```python
            #==== INTENÇÃO: Configurar Localização ====
            elif intent == 'Configurar Localização':
                print(f"[WHATSAPP] Intenção de Configurar Localização detectada")

                from app.services.gemini_service import extract_location_config
                from app.services.location_service import LocationService

                try:
                    # Extrair cidade e estado com Gemini
                    location_data = extract_location_config(texto_msg)
                    cidade = location_data.get('cidade')
                    estado = location_data.get('estado')

                    if not cidade:
                        resposta_para_usuario = ("❌ Não consegui identificar a cidade.\n\n"
                                                "Por favor, envie no formato:\n"
                                                '"Configurar localização: São Paulo, SP"')
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Atualizar no banco
                    sucesso, mensagem = LocationService.update_user_location(
                        usuario_id,
                        cidade,
                        estado
                    )

                    if sucesso:
                        resposta_para_usuario = f"✅ {mensagem}\n\n"
                        resposta_para_usuario += "Agora você receberá informações de clima nos resumos matinais!"
                    else:
                        resposta_para_usuario = f"❌ {mensagem}"

                except Exception as e:
                    print(f"[WHATSAPP] Erro ao configurar localização: {e}")
                    resposta_para_usuario = ("❌ Erro ao configurar localização.\n\n"
                                            "Tente: 'Configurar localização: São Paulo, SP'")

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
```

---

## Código 2: Atualizar Handler "Configurar Notificações"

**SUBSTITUA** o handler existente de "Configurar Notificações" (linhas ~1254-1321) por este código atualizado:

```python
            #==== INTENÇÃO: Configurar Notificações ====
            elif intent == 'Configurar Notificações':
                print(f"[WHATSAPP] Intenção de Configurar Notificações detectada")

                # Verificar se é sobre RESUMO MATINAL especificamente
                texto_lower = texto_msg.lower()
                is_resumo_matinal = any(kw in texto_lower for kw in ['resumo', 'matinal', 'briefing', 'preparação do dia'])

                if is_resumo_matinal:
                    # HANDLER ESPECÍFICO PARA RESUMO MATINAL
                    print(f"[WHATSAPP] Configuração de Resumo Matinal detectada")

                    # Detectar ação (ativar/desativar/configurar)
                    if any(kw in texto_lower for kw in ['ativar', 'ligar', 'ativa', 'ative']):
                        acao = 'ativar'
                    elif any(kw in texto_lower for kw in ['desativar', 'desligar', 'desative']):
                        acao = 'desativar'
                    else:
                        acao = 'configurar'

                    # Extrair horário (se houver)
                    import re
                    hora_match = re.search(r'(\d{1,2})[h:](\d{2})?', texto_msg)
                    hora = None

                    if hora_match:
                        hora_h = int(hora_match.group(1))
                        hora_m = int(hora_match.group(2)) if hora_match.group(2) else 0
                        hora = f"{hora_h:02d}:{hora_m:02d}"

                    # Executar ação
                    if acao == 'ativar':
                        sucesso, msg = NotificationConfigService.update_resumo_matinal_config(
                            usuario_id, ativo=True, hora=hora if hora else None
                        )
                    elif acao == 'desativar':
                        sucesso, msg = NotificationConfigService.update_resumo_matinal_config(
                            usuario_id, ativo=False
                        )
                    elif acao == 'configurar':
                        sucesso, msg = NotificationConfigService.update_resumo_matinal_config(
                            usuario_id, ativo=True, hora=hora
                        )
                    else:
                        sucesso = False
                        msg = "Ação não reconhecida"

                    if sucesso:
                        # Buscar config atual
                        config = NotificationConfigService.get_or_create_config(usuario_id)
                        resposta_para_usuario = f"✅ {msg}\n\n"
                        resposta_para_usuario += f"📱 *Resumo Matinal - Status atual:*\n"
                        resposta_para_usuario += f"• Ativo: {'Sim' if config['resumo_matinal_ativo'] else 'Não'}\n"
                        resposta_para_usuario += f"• Horário: {config['resumo_matinal_hora'].strftime('%H:%M')}\n\n"
                        resposta_para_usuario += "💡 Configure sua localização para receber informações de clima:\n"
                        resposta_para_usuario += '"Configurar localização: [Cidade], [Estado]"'
                    else:
                        resposta_para_usuario = f"❌ {msg}"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # Se NÃO for resumo matinal, processar outras notificações (CÓDIGO EXISTENTE)
                config_data = gemini_service.extract_notification_config(texto_msg)

                tipo = config_data.get('tipo')
                acao = config_data.get('acao')
                hora = config_data.get('hora')
                dias_antes = config_data.get('dias_antes')

                if tipo == 'agenda_diaria':
                    if acao == 'ativar':
                        sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                            usuario_id, ativa=True
                        )
                    elif acao == 'desativar':
                        sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                            usuario_id, ativa=False
                        )
                    elif acao == 'configurar':
                        sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                            usuario_id, ativa=True, hora=hora
                        )
                    else:
                        sucesso = False
                        msg = "Ação não reconhecida"

                    if sucesso:
                        # Buscar config atual
                        config = NotificationConfigService.get_or_create_config(usuario_id)
                        resposta_para_usuario = f"✅ {msg}\n\n"
                        resposta_para_usuario += f"📱 *Agenda Diária - Status atual:*\n"
                        resposta_para_usuario += f"• Ativa: {'Sim' if config['agenda_diaria_ativa'] else 'Não'}\n"
                        resposta_para_usuario += f"• Horário: {config['agenda_diaria_hora'].strftime('%H:%M')}\n"
                    else:
                        resposta_para_usuario = f"❌ {msg}"

                elif tipo == 'contas_vencer':
                    if acao == 'ativar':
                        sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                            usuario_id, ativa=True
                        )
                    elif acao == 'desativar':
                        sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                            usuario_id, ativa=False
                        )
                    elif acao == 'configurar':
                        sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                            usuario_id, ativa=True, dias_antes=dias_antes, hora=hora
                        )
                    else:
                        sucesso = False
                        msg = "Ação não reconhecida"

                    if sucesso:
                        config = NotificationConfigService.get_or_create_config(usuario_id)
                        resposta_para_usuario = f"✅ {msg}\n\n"
                        resposta_para_usuario += f"📱 *Contas a Vencer - Status atual:*\n"
                        resposta_para_usuario += f"• Ativa: {'Sim' if config['contas_vencer_ativa'] else 'Não'}\n"
                        resposta_para_usuario += f"• Dias antes: {config['contas_vencer_dias_antes']}\n"
                        resposta_para_usuario += f"• Horário: {config['contas_vencer_hora'].strftime('%H:%M')}\n"
                    else:
                        resposta_para_usuario = f"❌ {msg}"

                else:
                    resposta_para_usuario = "🤔 Não entendi qual tipo de notificação você quer configurar."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
```

---

## 🎯 Resumo das Mudanças

### ✅ **Handler 1: Configurar Localização**
- Nova intent completamente
- Adicionar **ANTES** da linha "Análise Inteligente"
- Processa: "Configurar localização: São Paulo, SP"

### ✅ **Handler 2: Configurar Notificações (ATUALIZADO)**
- **Substitui** o handler existente
- Adiciona lógica para detectar se é "resumo matinal"
- Mantém funcionalidade existente de "agenda diária" e "contas a vencer"
- Processa: "Ativar resumo matinal", "Configurar resumo matinal às 7h"

---

## 📋 Checklist de Implementação

- [ ] Adicionar handler "Configurar Localização" (copiar código 1)
- [ ] Substituir handler "Configurar Notificações" (copiar código 2)
- [ ] Testar via WhatsApp: "Configurar localização: São Paulo, SP"
- [ ] Testar via WhatsApp: "Ativar resumo matinal"
- [ ] Testar via WhatsApp: "Configurar resumo matinal às 7h"
- [ ] Verificar logs para ver se intent é detectada corretamente

---

## 🧪 Testes Sugeridos

**Teste 1: Configurar Localização**
```
Você: Configurar localização: Rio de Janeiro, RJ
Bot: ✅ Localização configurada: Rio de Janeiro, RJ
     Agora você receberá informações de clima nos resumos matinais!
```

**Teste 2: Ativar Resumo Matinal**
```
Você: Ativar resumo matinal
Bot: ✅ Resumo matinal ativado

     📱 Resumo Matinal - Status atual:
     • Ativo: Sim
     • Horário: 07:00

     💡 Configure sua localização para receber informações de clima:
     "Configurar localização: [Cidade], [Estado]"
```

**Teste 3: Configurar Horário**
```
Você: Configurar resumo matinal às 8h
Bot: ✅ Resumo matinal ativado e horário configurado para 08:00

     📱 Resumo Matinal - Status atual:
     • Ativo: Sim
     • Horário: 08:00
     ...
```

---

## ⚠️ Importante

Após adicionar os handlers:

1. **Reinicie a aplicação Flask**
2. **Teste as novas intents** via WhatsApp
3. **Verifique os logs** para debug: `tail -f /var/log/seu_app.log`
4. **Execute as migrations** se ainda não executou:
   ```bash
   python add_location_fields.py
   python add_notification_config_fields.py
   ```

---

## 🔧 Localização no Arquivo

**Adicionar Handler "Configurar Localização":**
- Linha: ~1322 (logo antes de "Análise Inteligente")

**Substituir Handler "Configurar Notificações":**
- Linhas: ~1254 até ~1321

---

Pronto! Com esses 2 handlers, seu bot estará completo para a feature de Resumo Matinal! 🚀
