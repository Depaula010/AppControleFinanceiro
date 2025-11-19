# app/routes/admin.py
from flask import Blueprint, jsonify, request
from sqlalchemy import text
# Importa o motor (ainda necessário para a rota do motor)
from motor_agendamentos import processar_agendamentos
# Importa nossos novos serviços
from app.services import finance_service
# Importa a config
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app import db_engine

from datetime import datetime, date, time, timedelta
from zoneinfo import ZoneInfo
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.calendar_query_service import CalendarQueryService
from app.services.notification_processor_service import NotificationProcessorService
from app.services.notification_service import enviar_notificacao_whatsapp
from app.utils import formatar_moeda

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

# Este é o equivalente ao [Route("admin")] do .NET
admin_bp = Blueprint('admin', __name__, url_prefix='/admin')


@admin_bp.route('/clear-bot-session', methods=['POST'])
def clear_bot_session():
    """
    ENDPOINT DE EMERGÊNCIA: Limpa a tabela 'baileys_auth'.
    Agora o controller só chama o serviço.
    """
    try:
        deleted_rows = finance_service.clear_bot_session()
        mensagem_sucesso = f"Sessão do bot ('baileys_auth') limpa com sucesso. {deleted_rows} linhas deletadas."
        print(f"[ADMIN-FIX] {mensagem_sucesso}")
        return jsonify({
            "status": "sucesso", 
            "mensagem": mensagem_sucesso
        }), 200

    except Exception as e:
        print(f"[ADMIN-FIX] Erro ao limpar a sessão do bot: {e}")
        return jsonify({"status": "erro", "mensagem": f"Erro ao limpar sessão: {str(e)}"}), 500


@admin_bp.route('/setup-database', methods=['GET'])
def setup_database():
    """ 
    Cria/Recria a ESTRUTURA final do banco (v12). 
    """
    try:
        finance_service.setup_database_schema()
        return "Estrutura final do banco (v12) criada/recriada com sucesso!", 200
    except Exception as e:
        print(f"Erro ao criar estrutura do banco: {e}")
        return f"Erro ao criar estrutura do banco: {str(e)}", 500


@admin_bp.route('/populate-global-categories', methods=['GET'])
def populate_global_categories():
    """ Insere os TEMPLATES GLOBAIS de categorias. """
    try:
        finance_service.populate_global_categories()
        return "Templates globais de categorias (v12) inseridos com sucesso!", 200
    except Exception as e:
        print(f"Erro ao inserir templates globais: {e}")
        return f"Erro ao inserir templates globais: {str(e)}", 500


@admin_bp.route('/setup-user-data', methods=['GET']) 
def setup_user_data():
    """ 
    Roda para inserir/atualizar o usuário e contas. 
    """
    try:
        # Dados que estavam "hardcoded" no app.py
        user_id, api_key = finance_service.setup_user_data(
            numero_whatsapp='553194001072',
            dia_venc_cartao=20,
            dia_fech_cartao=13
        )
        
        return jsonify({
            "status": "sucesso",
            "mensagem": f"Usuário e Contas inseridos/atualizados (Usuário ID: {user_id})!",
            "user_api_key_para_automate": api_key 
        }), 200

    except Exception as e:
        print(f"Erro ao inserir dados do usuário: {e}")
        return f"Erro ao inserir dados do usuário: {str(e)}", 500


@admin_bp.route('/run-motor-agendamentos', methods=['POST'])
def run_motor_agendamentos():
    """ Rota secreta que o Bot chama para rodar os agendamentos. """
    secret_key_recebida = request.headers.get('x-api-key')
    if secret_key_recebida != API_SECRET_KEY: 
        print(f"[MOTOR] Acesso negado à rota /run-motor-agendamentos. Chave errada.")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401
    
    try:
        print("[MOTOR] Rota /run-motor-agendamentos chamada com sucesso! Iniciando processamento...")
        processar_agendamentos() # Chama a função importada
        return jsonify({"status": "sucesso", "mensagem": "Agendamentos processados."}), 200
    except Exception as e:
        print(f"[MOTOR] ERRO CRÍTICO ao rodar /run-motor-agendamentos: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500
    
@admin_bp.route('/setup-calendar-table', methods=['GET'])
def setup_calendar_table():
    try:
        finance_service.add_google_calendar_tokens_table()
        return "✅ Tabela GoogleCalendarTokens criada!", 200
    except Exception as e:
        return f"❌ Erro: {str(e)}", 500
    
    
@admin_bp.route('/debug-calendar', methods=['GET'])
def debug_calendar():
    """Rota temporária para debug do Calendar"""
    from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
    from datetime import datetime, date, timezone, time
    
    output = []
    output.append("=" * 60)
    output.append("🧪 TESTE DE DEBUG - GOOGLE CALENDAR")
    output.append("=" * 60)
    
    usuario_id = 1
    
    try:
        # TESTE 1: Verificar conexão
        output.append("\n[TESTE 1] Verificando conexão...")
        is_connected = GoogleCalendarOAuthService.is_user_connected(usuario_id)
        output.append(f"✅ Usuário conectado? {is_connected}")
        
        if not is_connected:
            return "<br>".join(output) + "<br>❌ Usuário não conectado!"
        
        # TESTE 2: Obter credenciais
        output.append("\n[TESTE 2] Obtendo credenciais...")
        credentials = GoogleCalendarOAuthService.get_credentials(usuario_id)
        if credentials:
            output.append(f"✅ Credenciais obtidas")
            output.append(f"   - Token: {credentials.token[:20]}...")
            output.append(f"   - Expiry: {credentials.expiry}")
        
        # TESTE 3: Criar serviço
        output.append("\n[TESTE 3] Criando serviço Calendar...")
        service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
        output.append(f"✅ Serviço criado")
        
        # TESTE 4: Listar calendários
        output.append("\n[TESTE 4] Listando calendários...")
        calendars = service.calendarList().list().execute()
        output.append(f"✅ {len(calendars.get('items', []))} calendários encontrados")
        
        # TESTE 5: Buscar eventos de HOJE
        output.append("\n[TESTE 5] Buscando eventos de HOJE...")
        hoje = date.today()
        output.append(f"   Data: {hoje}")
        
        # CORREÇÃO: Usar string direta
        date_str = hoje.strftime('%Y-%m-%d')
        start_iso = f"{date_str}T00:00:00Z"
        end_iso = f"{date_str}T23:59:59Z"
        
        output.append(f"   Start ISO: {start_iso}")
        output.append(f"   End ISO: {end_iso}")
        
        # Chamar API
        output.append("\n   Chamando API...")
        events_result = service.events().list(
            calendarId='primary',
            timeMin=start_iso,
            timeMax=end_iso,
            singleEvents=True,
            orderBy='startTime'
        ).execute()
        
        events = events_result.get('items', [])
        output.append(f"✅ API retornou {len(events)} eventos")
        
        if events:
            output.append("\n   Eventos encontrados:")
            for idx, event in enumerate(events[:3], 1):
                output.append(f"   {idx}. {event.get('summary')}")
        
        output.append("\n✅ TODOS OS TESTES PASSARAM!")
        
    except Exception as e:
        output.append(f"\n❌ ERRO: {e}")
        import traceback
        output.append("\n" + traceback.format_exc().replace("\n", "<br>"))
    
    return "<pre>" + "\n".join(output) + "</pre>"

@admin_bp.route('/trigger-agenda-notifications', methods=['POST'])
def trigger_agenda_notifications():
    """
    Endpoint para UptimeRobot disparar notificações de agenda diária.
    
    UptimeRobot deve chamar a cada hora:
    POST https://seu-backend.onrender.com/admin/trigger-agenda-notifications
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[AGENDA-NOTIF] ❌ Tentativa não autorizada")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401
    
    try:
        resultado = NotificationProcessorService.processar_agenda_diaria(
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )
        
        return jsonify({
            "status": "sucesso",
            **resultado
        }), 200
        
    except Exception as e:
        print(f"[AGENDA-NOTIF] ❌ Erro crítico: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/trigger-bills-notifications', methods=['POST'])
def trigger_bills_notifications():
    """
    Endpoint para UptimeRobot disparar notificações de contas a vencer.
    
    UptimeRobot deve chamar a cada hora:
    POST https://seu-backend.onrender.com/admin/trigger-bills-notifications
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[BILLS-NOTIF] ❌ Tentativa não autorizada")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401
    
    try:
        resultado = NotificationProcessorService.processar_contas_vencer(
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )
        
        return jsonify({
            "status": "sucesso",
            **resultado
        }), 200
        
    except Exception as e:
        print(f"[BILLS-NOTIF] ❌ Erro crítico: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/test-notification', methods=['POST'])
def test_notification():
    """
    Endpoint para testar notificações manualmente.

    Body JSON:
    {
        "tipo": "agenda" ou "contas",
        "usuario_id": 1
    }
    """
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        data = request.json
        tipo = data.get('tipo', 'agenda')
        usuario_id = data.get('usuario_id', 1)

        # Buscar dados do usuário
        with db_engine.connect() as conn:
            sql = text("SELECT nome, numero_whatsapp FROM Usuarios WHERE id = :uid")
            usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()

        if not usuario:
            return jsonify({"status": "erro", "mensagem": "Usuário não encontrado"}), 404

        nome, numero_whatsapp = usuario

        if tipo == 'agenda':
            # Testar agenda
            if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
                return jsonify({
                    "status": "erro",
                    "mensagem": "Usuário não conectou Google Calendar"
                }), 400

            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
            hoje = date.today()
            events = CalendarQueryService._get_events_for_date(service, hoje)

            mensagem = f"🧪 *TESTE - Agenda de Hoje*\n\n"
            mensagem += f"📅 {len(events)} evento(s) encontrado(s)\n\n"

            for idx, event in enumerate(events[:5], 1):
                mensagem += f"{idx}. {event['summary']}\n"

            if len(events) > 5:
                mensagem += f"\n... e mais {len(events) - 5}"

        elif tipo == 'contas':
            # Testar contas
            amanha = date.today() + timedelta(days=1)

            with db_engine.connect() as conn:
                sql = text("""
                    SELECT descricao, valor_previsto
                    FROM Agendamentos
                    WHERE usuario_id = :uid
                      AND ativo = TRUE
                      AND dia_execucao = :dia
                    LIMIT 5
                """)
                contas = conn.execute(sql, {
                    "uid": usuario_id,
                    "dia": amanha.day
                }).fetchall()

            mensagem = f"🧪 *TESTE - Contas de Amanhã*\n\n"
            mensagem += f"💰 {len(contas)} conta(s) encontrada(s)\n\n"

            for idx, conta in enumerate(contas, 1):
                desc, valor = conta
                mensagem += f"{idx}. {desc}: {formatar_moeda(float(valor or 0))}\n"

        else:
            return jsonify({"status": "erro", "mensagem": "Tipo inválido"}), 400

        # Enviar
        sucesso = enviar_notificacao_whatsapp(
            numero_whatsapp,
            mensagem,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        if sucesso:
            return jsonify({
                "status": "sucesso",
                "mensagem": f"Notificação de teste enviada para {nome}"
            }), 200
        else:
            return jsonify({
                "status": "erro",
                "mensagem": "Falha ao enviar notificação"
            }), 500

    except Exception as e:
        print(f"[TEST-NOTIF] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500