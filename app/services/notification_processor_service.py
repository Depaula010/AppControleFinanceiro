# app/services/notification_processor_service.py
"""
Processa notificações agendadas (agenda e contas)
"""

from datetime import date, datetime, timedelta, time
from zoneinfo import ZoneInfo
from sqlalchemy import text
from app import db_engine
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.calendar_query_service import CalendarQueryService
from app.services.notification_service import enviar_notificacao_whatsapp
from app.utils import formatar_moeda

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


class NotificationProcessorService:
    """Processa e envia notificações agendadas"""
    
    @staticmethod
    def processar_agenda_diaria(bot_url, api_key):
        """
        Processa notificações de agenda diária.
        
        Args:
            bot_url: URL do bot WhatsApp
            api_key: API key para autenticação
        
        Returns:
            dict: {usuarios_processados, enviadas, erros, hora}
        """
        print("[AGENDA-NOTIF] Iniciando processamento...")
        
        if not db_engine:
            raise Exception("Banco não configurado")
        
        # Hora atual em BRT
        agora_br = datetime.now(TIMEZONE_BR)
        hora_atual = agora_br.time().replace(second=0, microsecond=0)
        
        print(f"[AGENDA-NOTIF] Hora atual BRT: {hora_atual}")
        
        # Janela de 10 minutos (±5min)
        hora_min = (datetime.combine(date.today(), hora_atual) - timedelta(minutes=5)).time()
        hora_max = (datetime.combine(date.today(), hora_atual) + timedelta(minutes=5)).time()
        
        # Buscar usuários que devem receber notificação nesta janela
        sql = text("""
            SELECT u.id, u.numero_whatsapp, u.nome
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.agenda_diaria_ativa = TRUE
              AND nc.agenda_diaria_hora BETWEEN :hora_min AND :hora_max
        """)
        
        with db_engine.connect() as conn:
            usuarios = conn.execute(sql, {
                "hora_min": hora_min,
                "hora_max": hora_max
            }).fetchall()
        
        print(f"[AGENDA-NOTIF] {len(usuarios)} usuário(s) para processar")
        
        enviados = 0
        erros = 0
        
        for usuario in usuarios:
            usuario_id, numero_whatsapp, nome = usuario
            
            try:
                print(f"[AGENDA-NOTIF] Processando {nome} (ID: {usuario_id})...")
                
                # Verificar se usuário conectou Calendar
                if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
                    print(f"[AGENDA-NOTIF] ⚠️ {nome} não conectou Calendar")
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
                    
                    for idx, event in enumerate(events[:15], 1):
                        summary = event['summary']
                        
                        if event['all_day']:
                            mensagem += f"{idx}. 📆 *{summary}* (dia inteiro)\n"
                        else:
                            start_str = event['start']
                            try:
                                if 'T' in start_str:
                                    start_dt = datetime.fromisoformat(start_str)
                                    if start_dt.tzinfo is None:
                                        start_dt = start_dt.replace(tzinfo=TIMEZONE_BR)
                                    else:
                                        start_dt = start_dt.astimezone(TIMEZONE_BR)
                                    hora_fmt = start_dt.strftime('%H:%M')
                                    mensagem += f"{idx}. ⏰ *{hora_fmt}* - {summary}\n"
                                else:
                                    mensagem += f"{idx}. 📆 *{summary}*\n"
                            except:
                                mensagem += f"{idx}. 📆 *{summary}*\n"
                        
                        # Adicionar calendário se não for primary
                        cal_name = event.get('calendar_name', '')
                        if cal_name and 'primary' not in cal_name.lower():
                            mensagem += f"   📂 {cal_name}\n"
                    
                    if len(events) > 15:
                        mensagem += f"\n... e mais {len(events) - 15} compromisso(s)"
                    
                    mensagem += "\n\n_Tenha um ótimo dia! 😊_"
                
                # Enviar
                sucesso = enviar_notificacao_whatsapp(
                    numero_whatsapp, mensagem, bot_url, api_key
                )
                
                if sucesso:
                    enviados += 1
                else:
                    erros += 1
                
            except Exception as e:
                erros += 1
                print(f"[AGENDA-NOTIF] ❌ Erro ao processar {nome}: {e}")
                import traceback
                traceback.print_exc()
                continue
        
        print(f"[AGENDA-NOTIF] ✅ Concluído: {enviados} enviadas, {erros} erros")
        
        return {
            "usuarios_processados": len(usuarios),
            "notificacoes_enviadas": enviados,
            "erros": erros,
            "hora_processamento": agora_br.isoformat()
        }
    
    @staticmethod
    def processar_contas_vencer(bot_url, api_key):
        """
        Processa notificações de contas a vencer.
        
        Args:
            bot_url: URL do bot WhatsApp
            api_key: API key para autenticação
        
        Returns:
            dict: {usuarios_processados, enviadas, erros, hora}
        """
        print("[BILLS-NOTIF] Iniciando processamento...")
        
        if not db_engine:
            raise Exception("Banco não configurado")
        
        agora_br = datetime.now(TIMEZONE_BR)
        hora_atual = agora_br.time().replace(second=0, microsecond=0)
        
        print(f"[BILLS-NOTIF] Hora atual BRT: {hora_atual}")
        
        # Janela de 10 minutos
        hora_min = (datetime.combine(date.today(), hora_atual) - timedelta(minutes=5)).time()
        hora_max = (datetime.combine(date.today(), hora_atual) + timedelta(minutes=5)).time()
        
        # Buscar usuários
        sql = text("""
            SELECT u.id, u.numero_whatsapp, u.nome, nc.contas_vencer_dias_antes
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.contas_vencer_ativa = TRUE
              AND nc.contas_vencer_hora BETWEEN :hora_min AND :hora_max
        """)
        
        with db_engine.connect() as conn:
            usuarios = conn.execute(sql, {
                "hora_min": hora_min,
                "hora_max": hora_max
            }).fetchall()
        
        print(f"[BILLS-NOTIF] {len(usuarios)} usuário(s) para processar")
        
        enviados = 0
        erros = 0
        
        for usuario in usuarios:
            usuario_id, numero_whatsapp, nome, dias_antes = usuario
            
            try:
                print(f"[BILLS-NOTIF] Processando {nome} (ID: {usuario_id})...")
                
                # Calcular data alvo
                data_alvo = date.today() + timedelta(days=dias_antes)
                
                # Buscar contas
                with db_engine.connect() as conn:
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
                    elif dias_antes == 0:
                        mensagem += f"📅 Você tem *{len(contas)} conta(s)* que vencem *hoje* ({data_alvo.strftime('%d/%m')}):\n\n"
                    else:
                        mensagem += f"📅 Você tem *{len(contas)} conta(s)* que vencem em *{dias_antes} dias* ({data_alvo.strftime('%d/%m')}):\n\n"
                    
                    total = 0
                    for idx, conta in enumerate(contas, 1):
                        desc, valor, dia_venc, categoria = conta
                        valor_float = float(valor or 0)
                        total += valor_float
                        
                        mensagem += f"{idx}. *{desc}*\n"
                        mensagem += f"   💰 {formatar_moeda(valor_float)}\n"
                        mensagem += f"   📊 {categoria}\n\n"
                    
                    mensagem += "━━━━━━━━━━━━━━━━\n"
                    mensagem += f"💵 *Total: {formatar_moeda(total)}*\n\n"
                    mensagem += "_Não esqueça de pagar! 😊_"
                    
                    # Enviar
                    sucesso = enviar_notificacao_whatsapp(
                        numero_whatsapp, mensagem, bot_url, api_key
                    )
                    
                    if sucesso:
                        enviados += 1
                        print(f"[BILLS-NOTIF] ✅ Enviado para {nome} ({len(contas)} contas)")
                    else:
                        erros += 1
                else:
                    print(f"[BILLS-NOTIF] ℹ️ {nome} não tem contas para {data_alvo.strftime('%d/%m')}")
                
            except Exception as e:
                erros += 1
                print(f"[BILLS-NOTIF] ❌ Erro ao processar {nome}: {e}")
                import traceback
                traceback.print_exc()
                continue
        
        print(f"[BILLS-NOTIF] ✅ Concluído: {enviados} enviadas, {erros} erros")
        
        return {
            "usuarios_processados": len(usuarios),
            "notificacoes_enviadas": enviados,
            "erros": erros,
            "hora_processamento": agora_br.isoformat()
        }