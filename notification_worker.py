# notification_worker.py
"""
Worker para enviar notificações agendadas (agenda diária e contas a vencer).
Deve ser executado a cada hora via cron ou similar.
"""

import os
from datetime import date, datetime, time, timedelta
from zoneinfo import ZoneInfo
from sqlalchemy import create_engine, text

# Configurações
DATABASE_URL = os.environ['DATABASE_URL']
if DATABASE_URL.startswith("postgres://"):
    DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)

BOT_WHATSAPP_URL = os.environ.get('BOT_WHATSAPP_URL')
API_SECRET_KEY = os.environ.get('API_SECRET_KEY')

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

# Criar engine
engine = create_engine(DATABASE_URL)


def enviar_notificacao_whatsapp(numero, mensagem):
    """Envia notificação via bot do WhatsApp"""
    import requests
    
    try:
        headers = {'x-api-key': API_SECRET_KEY}
        payload = {'numero': numero, 'mensagem': mensagem}
        response = requests.post(
            f"{BOT_WHATSAPP_URL}/enviar-mensagem",
            json=payload,
            headers=headers,
            timeout=10
        )
        
        if response.status_code == 200:
            print(f"[NOTIF-WORKER] ✅ Notificação enviada para {numero}")
            return True
        else:
            print(f"[NOTIF-WORKER] ❌ Erro {response.status_code} para {numero}")
            return False
            
    except Exception as e:
        print(f"[NOTIF-WORKER] ❌ Erro ao enviar para {numero}: {e}")
        return False


def processar_agenda_diaria():
    """Envia notificação de agenda diária para usuários configurados"""
    print("[NOTIF-WORKER] Processando agenda diária...")
    
    # Hora atual em BRT
    agora_br = datetime.now(TIMEZONE_BR)
    hora_atual = agora_br.time().replace(second=0, microsecond=0)
    
    print(f"[NOTIF-WORKER] Hora atual: {hora_atual}")
    
    # Buscar usuários que devem receber notificação nesta hora
    sql = text("""
        SELECT u.id, u.numero_whatsapp, u.nome
        FROM NotificationConfigs nc
        JOIN Usuarios u ON nc.usuario_id = u.id
        WHERE nc.agenda_diaria_ativa = TRUE
          AND nc.agenda_diaria_hora BETWEEN :hora_min AND :hora_max
    """)
    
    # Janela de 5 minutos (para não perder caso o cron atrase)
    hora_min = (datetime.combine(date.today(), hora_atual) - timedelta(minutes=2)).time()
    hora_max = (datetime.combine(date.today(), hora_atual) + timedelta(minutes=3)).time()
    
    with engine.connect() as conn:
        usuarios = conn.execute(sql, {"hora_min": hora_min, "hora_max": hora_max}).fetchall()
        
        print(f"[NOTIF-WORKER] {len(usuarios)} usuários para processar")
        
        for usuario in usuarios:
            usuario_id, numero_whatsapp, nome = usuario
            
            try:
                # Buscar eventos do dia via Google Calendar
                from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
                from app.services.calendar_query_service import CalendarQueryService
                
                # Verificar se usuário conectou Calendar
                if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
                    print(f"[NOTIF-WORKER] Usuário {usuario_id} não conectou Calendar")
                    continue
                
                # Buscar eventos de hoje
                service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
                hoje = date.today()
                events = CalendarQueryService._get_events_for_date(service, hoje)
                
                # Montar mensagem
                if not events:
                    mensagem = f"🌅 *Bom dia, {nome}!*\n\n"
                    mensagem += f"📅 Você não tem compromissos agendados para hoje! 🎉"
                else:
                    mensagem = f"🌅 *Bom dia, {nome}!*\n\n"
                    mensagem += f"📅 *AGENDA DE HOJE ({hoje.strftime('%d/%m/%Y')})*\n\n"
                    mensagem += f"Você tem *{len(events)} compromisso(s)*:\n\n"
                    
                    for idx, event in enumerate(events[:10], 1):  # Limitar a 10
                        summary = event['summary']
                        
                        if event['all_day']:
                            mensagem += f"{idx}. 📆 *{summary}* (dia inteiro)\n"
                        else:
                            # Formatar horário
                            start_str = event['start']
                            try:
                                if 'T' in start_str:
                                    start_dt = datetime.fromisoformat(start_str)
                                    hora_fmt = start_dt.strftime('%H:%M')
                                    mensagem += f"{idx}. ⏰ *{hora_fmt}* - {summary}\n"
                                else:
                                    mensagem += f"{idx}. 📆 *{summary}*\n"
                            except:
                                mensagem += f"{idx}. 📆 *{summary}*\n"
                        
                        # Adicionar calendário se não for primary
                        cal_name = event.get('calendar_name', '')
                        if cal_name and cal_name != 'Rafael de Paula':
                            mensagem += f"   📂 {cal_name}\n"
                        
                        mensagem += "\n"
                    
                    if len(events) > 10:
                        mensagem += f"... e mais {len(events) - 10} compromisso(s)\n"
                    
                    mensagem += "\n_Tenha um ótimo dia! 😊_"
                
                # Enviar notificação
                enviar_notificacao_whatsapp(numero_whatsapp, mensagem)
                
            except Exception as e:
                print(f"[NOTIF-WORKER] Erro ao processar usuário {usuario_id}: {e}")
                import traceback
                traceback.print_exc()
                continue


def processar_contas_vencer():
    """Envia notificação de contas a vencer"""
    print("[NOTIF-WORKER] Processando contas a vencer...")
    
    agora_br = datetime.now(TIMEZONE_BR)
    hora_atual = agora_br.time().replace(second=0, microsecond=0)
    
    # Buscar usuários
    sql = text("""
        SELECT u.id, u.numero_whatsapp, u.nome, nc.contas_vencer_dias_antes
        FROM NotificationConfigs nc
        JOIN Usuarios u ON nc.usuario_id = u.id
        WHERE nc.contas_vencer_ativa = TRUE
          AND nc.contas_vencer_hora BETWEEN :hora_min AND :hora_max
    """)
    
    hora_min = (datetime.combine(date.today(), hora_atual) - timedelta(minutes=2)).time()
    hora_max = (datetime.combine(date.today(), hora_atual) + timedelta(minutes=3)).time()
    
    with engine.connect() as conn:
        usuarios = conn.execute(sql, {"hora_min": hora_min, "hora_max": hora_max}).fetchall()
        
        print(f"[NOTIF-WORKER] {len(usuarios)} usuários para processar contas")
        
        for usuario in usuarios:
            usuario_id, numero_whatsapp, nome, dias_antes = usuario
            
            try:
                # Calcular data alvo
                data_alvo = date.today() + timedelta(days=dias_antes)
                
                # Buscar contas que vencem nessa data
                sql_contas = text("""
                    SELECT 
                        a.descricao,
                        a.valor_previsto,
                        a.dia_execucao,
                        s.nome_sub as categoria
                    FROM Agendamentos a
                    JOIN SubCategoria s ON a.subcategoria_id = s.id
                    WHERE a.usuario_id = :uid
                      AND a.ativo = TRUE
                      AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
                      AND a.dia_execucao = :dia_venc
                      -- Verificar se ainda não foi pago este mês
                      AND NOT EXISTS (
                          SELECT 1 FROM Transacoes t
                          WHERE t.descricao = a.descricao
                            AND t.usuario_id = a.usuario_id
                            AND t.data_transacao >= date_trunc('month', CURRENT_DATE)
                            AND t.data_transacao < date_trunc('month', CURRENT_DATE) + interval '1 month'
                      )
                """)
                
                contas = conn.execute(sql_contas, {
                    "uid": usuario_id,
                    "dia_venc": data_alvo.day
                }).fetchall()
                
                if contas:
                    # Montar mensagem
                    mensagem = f"🔔 *Lembrete de Contas, {nome}!*\n\n"
                    
                    if dias_antes == 1:
                        mensagem += f"📅 Você tem *{len(contas)} conta(s)* que vencem *amanhã* ({data_alvo.strftime('%d/%m')}):\n\n"
                    else:
                        mensagem += f"📅 Você tem *{len(contas)} conta(s)* que vencem em *{dias_antes} dias* ({data_alvo.strftime('%d/%m')}):\n\n"
                    
                    total = 0
                    for idx, conta in enumerate(contas, 1):
                        desc, valor, dia_venc, categoria = conta
                        valor_float = float(valor or 0)
                        total += valor_float
                        
                        from app.utils import formatar_moeda
                        mensagem += f"{idx}. *{desc}*\n"
                        mensagem += f"   💰 {formatar_moeda(valor_float)}\n"
                        mensagem += f"   📊 {categoria}\n\n"
                    
                    mensagem += "━━━━━━━━━━━━━━━━\n"
                    mensagem += f"💵 *Total: {formatar_moeda(total)}*\n\n"
                    mensagem += "_Não esqueça de pagar! 😊_"
                    
                    # Enviar
                    enviar_notificacao_whatsapp(numero_whatsapp, mensagem)
                
            except Exception as e:
                print(f"[NOTIF-WORKER] Erro ao processar contas do usuário {usuario_id}: {e}")
                import traceback
                traceback.print_exc()
                continue


def main():
    """Função principal do worker"""
    print("=" * 60)
    print("🔔 NOTIFICATION WORKER")
    print(f"Horário: {datetime.now(TIMEZONE_BR).strftime('%Y-%m-%d %H:%M:%S %Z')}")
    print("=" * 60)
    
    try:
        # Processar agenda diária
        processar_agenda_diaria()
        
        # Processar contas a vencer
        processar_contas_vencer()
        
        print("[NOTIF-WORKER] ✅ Processamento concluído")
        
    except Exception as e:
        print(f"[NOTIF-WORKER] ❌ Erro crítico: {e}")
        import traceback
        traceback.print_exc()
    
    print("=" * 60)


if __name__ == "__main__":
    main()