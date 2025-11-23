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
from app.services.monthly_report_config_service import criar_tabela_monthly_report_configs
from app.services.monthly_report_processor_service import processar_relatorios_mensais, enviar_relatorio_manual
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

@admin_bp.route('/oauth-config-check', methods=['GET'])
def oauth_config_check():
    """Endpoint para verificar configuração OAuth"""
    from app.config import GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, GOOGLE_REDIRECT_URI

    return jsonify({
        "client_id_configured": bool(GOOGLE_CLIENT_ID),
        "client_id_prefix": GOOGLE_CLIENT_ID[:20] + "..." if GOOGLE_CLIENT_ID else None,
        "client_secret_configured": bool(GOOGLE_CLIENT_SECRET),
        "redirect_uri": GOOGLE_REDIRECT_URI
    }), 200


@admin_bp.route('/security-stats', methods=['GET'])
def security_stats():
    """
    Endpoint para visualizar estatísticas de segurança

    Retorna:
    - IPs bloqueados atualmente
    - Atividade suspeita recente
    - Totais de bloqueios e tentativas

    Exemplo:
    GET https://seu-backend.onrender.com/admin/security-stats
    Header: x-api-key: sua_chave_secreta
    """
    # Verificar autenticação
    api_key = request.headers.get('x-api-key')
    if api_key != API_SECRET_KEY:
        return jsonify({"erro": "Chave de API inválida"}), 401

    from app.middleware.security import get_security_stats

    stats = get_security_stats()
    return jsonify(stats), 200


@admin_bp.route('/setup-monthly-reports-table', methods=['GET'])
def setup_monthly_reports_table():
    """
    Cria a tabela MonthlyReportConfigs para configuração de relatórios mensais.

    Exemplo:
    GET https://seu-backend.onrender.com/admin/setup-monthly-reports-table
    """
    try:
        criar_tabela_monthly_report_configs()
        return jsonify({
            "status": "sucesso",
            "mensagem": "✅ Tabela MonthlyReportConfigs criada com sucesso!"
        }), 200
    except Exception as e:
        print(f"[MONTHLY-REPORT] ❌ Erro ao criar tabela: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": f"Erro ao criar tabela: {str(e)}"
        }), 500


@admin_bp.route('/trigger-monthly-reports-inicio', methods=['POST'])
def trigger_monthly_reports_inicio():
    """
    Endpoint para cron job disparar relatórios mensais no início do mês.
    Relatório refere-se ao MÊS ANTERIOR.

    Cron job deve chamar a cada hora no dia 1:
    POST https://seu-backend.onrender.com/admin/trigger-monthly-reports-inicio
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[MONTHLY-REPORT] ❌ Tentativa não autorizada (INICIO)")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        print("[MONTHLY-REPORT] 📊 Processando relatórios de INICIO DO MES...")
        resultado = processar_relatorios_mensais(
            momento_envio='INICIO_MES',
            janela_minutos=5
        )

        print(f"[MONTHLY-REPORT] ✅ Processamento concluído: {resultado['enviados_sucesso']} enviados")

        return jsonify({
            "status": "sucesso",
            **resultado
        }), 200

    except Exception as e:
        print(f"[MONTHLY-REPORT] ❌ Erro crítico: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/trigger-monthly-reports-fim', methods=['POST'])
def trigger_monthly_reports_fim():
    """
    Endpoint para cron job disparar relatórios mensais no fim do mês.
    Relatório refere-se ao MÊS ATUAL.

    Cron job deve chamar a cada hora no último dia do mês:
    POST https://seu-backend.onrender.com/admin/trigger-monthly-reports-fim
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[MONTHLY-REPORT] ❌ Tentativa não autorizada (FIM)")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        print("[MONTHLY-REPORT] 📊 Processando relatórios de FIM DO MES...")
        resultado = processar_relatorios_mensais(
            momento_envio='FIM_MES',
            janela_minutos=5
        )

        print(f"[MONTHLY-REPORT] ✅ Processamento concluído: {resultado['enviados_sucesso']} enviados")

        return jsonify({
            "status": "sucesso",
            **resultado
        }), 200

    except Exception as e:
        print(f"[MONTHLY-REPORT] ❌ Erro crítico: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/test-monthly-report/<int:usuario_id>', methods=['POST'])
def test_monthly_report(usuario_id):
    """
    Endpoint para testar envio manual de relatório mensal.

    Query params:
        - momento: 'INICIO_MES' (padrão) ou 'FIM_MES'

    Exemplo:
    POST https://seu-backend.onrender.com/admin/test-monthly-report/1?momento=INICIO_MES
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        momento_envio = request.args.get('momento', 'INICIO_MES')

        if momento_envio not in ['INICIO_MES', 'FIM_MES']:
            return jsonify({
                "status": "erro",
                "mensagem": "Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'"
            }), 400

        print(f"[MONTHLY-REPORT-TEST] 📊 Enviando relatório manual para usuário {usuario_id}...")
        resultado = enviar_relatorio_manual(usuario_id, momento_envio)

        if resultado['sucesso']:
            return jsonify({
                "status": "sucesso",
                **resultado
            }), 200
        else:
            return jsonify({
                "status": "erro",
                **resultado
            }), 400

    except Exception as e:
        print(f"[MONTHLY-REPORT-TEST] ❌ Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/setup-resumo-matinal', methods=['GET'])
def setup_resumo_matinal():
    """
    Cria as colunas necessárias para o Resumo Matinal.

    - Adiciona 'cidade' e 'estado' na tabela Usuarios
    - Adiciona 'resumo_matinal_ativo' e 'resumo_matinal_hora' na tabela NotificationConfigs

    Exemplo:
    GET http://212.47.65.37:8000/admin/setup-resumo-matinal
    """
    try:
        output = []
        output.append("="*60)
        output.append("SETUP: Resumo Matinal (Daily Briefing)")
        output.append("="*60)

        # Migration 0: Criar tabela NotificationConfigs se não existir
        output.append("\n[0/3] Verificando tabela NotificationConfigs...")

        sql_create_table = text("""
            CREATE TABLE IF NOT EXISTS NotificationConfigs (
                id SERIAL PRIMARY KEY,
                usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,

                -- Agenda Diária
                agenda_diaria_ativa BOOLEAN NOT NULL DEFAULT TRUE,
                agenda_diaria_hora TIME NOT NULL DEFAULT '08:00:00',

                -- Contas a Vencer
                contas_vencer_ativa BOOLEAN NOT NULL DEFAULT TRUE,
                contas_vencer_dias_antes INT NOT NULL DEFAULT 1,
                contas_vencer_hora TIME NOT NULL DEFAULT '09:00:00',

                -- Timestamps
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

                UNIQUE(usuario_id)
            );

            CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
            ON NotificationConfigs(usuario_id);
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_create_table)
            conn.commit()

        output.append("OK - Tabela NotificationConfigs criada/verificada!")

        # Migration 1: Campos de localização
        output.append("\n[1/3] Adicionando campos de localizacao na tabela Usuarios...")

        sql_location = text("""
            ALTER TABLE Usuarios
            ADD COLUMN IF NOT EXISTS cidade VARCHAR(100) DEFAULT 'Sao Paulo';

            ALTER TABLE Usuarios
            ADD COLUMN IF NOT EXISTS estado VARCHAR(2) DEFAULT 'SP';

            CREATE INDEX IF NOT EXISTS idx_usuarios_localizacao
            ON Usuarios(cidade, estado);
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_location)
            conn.commit()

        output.append("OK - Campos 'cidade' e 'estado' adicionados!")

        # Migration 2: Campos de resumo matinal
        output.append("\n[2/3] Adicionando campos de resumo matinal na tabela NotificationConfigs...")

        sql_briefing = text("""
            ALTER TABLE NotificationConfigs
            ADD COLUMN IF NOT EXISTS resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE;

            ALTER TABLE NotificationConfigs
            ADD COLUMN IF NOT EXISTS resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00';
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_briefing)
            conn.commit()

        output.append("OK - Campos 'resumo_matinal_ativo' e 'resumo_matinal_hora' adicionados!")

        output.append("\n" + "="*60)
        output.append("SUCESSO! Resumo Matinal configurado")
        output.append("="*60)
        output.append("\nProximos passos:")
        output.append("1. Configurar WEATHER_API_KEY no .env (opcional)")
        output.append("2. Testar via WhatsApp: 'Configurar localizacao: Sao Paulo, SP'")
        output.append("3. Testar via WhatsApp: 'Ativar resumo matinal'")
        output.append("4. Configurar cron job para /admin/trigger-daily-briefing")

        return "<pre>" + "\n".join(output) + "</pre>", 200

    except Exception as e:
        print(f"[RESUMO-MATINAL-SETUP] Erro: {e}")
        import traceback
        traceback.print_exc()
        return f"<pre>Erro ao configurar Resumo Matinal:\n\n{traceback.format_exc()}</pre>", 500


@admin_bp.route('/trigger-daily-briefing', methods=['POST'])
def trigger_daily_briefing():
    """
    Endpoint para cron job disparar resumos matinais.

    Deve ser chamado a cada hora pelo UptimeRobot/cron:
    POST https://seu-backend.onrender.com/admin/trigger-daily-briefing
    Header: x-api-key: SUA_SECRET_KEY
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[DAILY-BRIEFING] Tentativa nao autorizada")
        return jsonify({"status": "erro", "mensagem": "Nao autorizado"}), 401

    try:
        from app.services.notification_config_service import NotificationConfigService
        from app.services.daily_briefing_service import DailyBriefingService
        from app.services.gemini_service import generate_daily_briefing
        from datetime import datetime

        hora_atual = datetime.now().time().replace(second=0, microsecond=0)

        print(f"[DAILY-BRIEFING] Processando resumos matinais para horario {hora_atual.strftime('%H:%M')}...")

        # Buscar usuários que devem receber neste horário
        usuarios = NotificationConfigService.get_users_with_resumo_matinal_active(hora_atual)

        if not usuarios:
            return jsonify({
                "status": "sucesso",
                "mensagem": f"Nenhum usuario configurado para {hora_atual.strftime('%H:%M')}",
                "usuarios_processados": 0,
                "enviados_sucesso": 0
            }), 200

        briefing_service = DailyBriefingService()
        enviados = 0
        erros = 0

        for usuario_id, numero_whatsapp in usuarios:
            try:
                # Preparar dados
                briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

                if not briefing_data:
                    print(f"[DAILY-BRIEFING] Erro ao preparar dados para usuario {usuario_id}")
                    erros += 1
                    continue

                # Gerar mensagem
                if briefing_data['total_eventos'] == 0:
                    mensagem = briefing_service.generate_briefing_message(usuario_id, date.today())
                else:
                    mensagem = generate_daily_briefing(briefing_data)

                if not mensagem:
                    print(f"[DAILY-BRIEFING] Falha ao gerar mensagem para usuario {usuario_id}")
                    erros += 1
                    continue

                # Enviar
                sucesso = enviar_notificacao_whatsapp(
                    numero_whatsapp,
                    mensagem,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                if sucesso:
                    enviados += 1
                    print(f"[DAILY-BRIEFING] Resumo enviado para usuario {usuario_id}")
                else:
                    erros += 1
                    print(f"[DAILY-BRIEFING] Falha ao enviar para usuario {usuario_id}")

            except Exception as e:
                erros += 1
                print(f"[DAILY-BRIEFING] Erro ao processar usuario {usuario_id}: {e}")

        return jsonify({
            "status": "sucesso",
            "usuarios_processados": len(usuarios),
            "enviados_sucesso": enviados,
            "erros": erros,
            "horario": hora_atual.strftime('%H:%M')
        }), 200

    except Exception as e:
        print(f"[DAILY-BRIEFING] Erro critico: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/setup-potes-alerts', methods=['GET'])
def setup_potes_alerts():
    """
    Adiciona colunas para alertas de potes na tabela NotificationConfigs.

    - Adiciona 'alerta_potes_ativo' e 'alerta_potes_threshold'
    - Garante que 'periodicidade' existe em PotesDeGastos
    - Insere configurações padrão para usuários existentes

    Exemplo:
    GET http://seu-backend.com/admin/setup-potes-alerts
    """
    try:
        output = []
        output.append("="*60)
        output.append("SETUP: Alertas de Potes (Feedback Financeiro)")
        output.append("="*60)

        # Migration 1: Criar tabela NotificationConfigs se não existir
        output.append("\n[1/4] Verificando tabela NotificationConfigs...")

        sql_create_table = text("""
            CREATE TABLE IF NOT EXISTS NotificationConfigs (
                id SERIAL PRIMARY KEY,
                usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,

                -- Agenda Diária
                agenda_diaria_ativa BOOLEAN NOT NULL DEFAULT TRUE,
                agenda_diaria_hora TIME NOT NULL DEFAULT '08:00:00',

                -- Resumo Matinal
                resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE,
                resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00',

                -- Contas a Vencer
                contas_vencer_ativa BOOLEAN NOT NULL DEFAULT TRUE,
                contas_vencer_dias_antes INT NOT NULL DEFAULT 1,
                contas_vencer_hora TIME NOT NULL DEFAULT '09:00:00',

                -- Timestamps
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

                UNIQUE(usuario_id)
            );

            CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
            ON NotificationConfigs(usuario_id);
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_create_table)
            conn.commit()

        output.append("OK - Tabela NotificationConfigs criada/verificada!")

        # Migration 2: Adicionar colunas de alertas de potes
        output.append("\n[2/4] Adicionando campos de alertas de potes...")

        sql_potes_alerts = text("""
            ALTER TABLE NotificationConfigs
            ADD COLUMN IF NOT EXISTS alerta_potes_ativo BOOLEAN NOT NULL DEFAULT TRUE;

            ALTER TABLE NotificationConfigs
            ADD COLUMN IF NOT EXISTS alerta_potes_threshold INT NOT NULL DEFAULT 0;
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_potes_alerts)
            conn.commit()

        output.append("OK - Campos 'alerta_potes_ativo' e 'alerta_potes_threshold' adicionados!")
        output.append("    - alerta_potes_ativo: TRUE (padrao)")
        output.append("    - alerta_potes_threshold: 0 (sempre mostrar)")

        # Migration 3: Garantir que periodicidade existe em PotesDeGastos
        output.append("\n[3/4] Verificando campo 'periodicidade' em PotesDeGastos...")

        sql_periodicidade = text("""
            ALTER TABLE PotesDeGastos
            ADD COLUMN IF NOT EXISTS periodicidade VARCHAR(20) NOT NULL DEFAULT 'MENSAL'
                CHECK (periodicidade IN ('SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL'));
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_periodicidade)
            conn.commit()

        output.append("OK - Campo 'periodicidade' verificado em PotesDeGastos!")

        # Migration 4: Inserir configurações padrão para usuários existentes
        output.append("\n[4/4] Inserindo configuracoes padrao para usuarios existentes...")

        sql_insert_defaults = text("""
            INSERT INTO NotificationConfigs (usuario_id, alerta_potes_ativo, alerta_potes_threshold)
            SELECT id, TRUE, 0
            FROM Usuarios
            WHERE id NOT IN (SELECT usuario_id FROM NotificationConfigs)
            ON CONFLICT (usuario_id) DO NOTHING;
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql_insert_defaults)
            conn.commit()
            rows_inserted = result.rowcount

        output.append(f"OK - {rows_inserted} configuracao(es) padrao inserida(s)!")

        # Comentários para documentação
        sql_comments = text("""
            COMMENT ON COLUMN NotificationConfigs.alerta_potes_ativo IS 'Se TRUE, mostra status do pote apos cada transacao';
            COMMENT ON COLUMN NotificationConfigs.alerta_potes_threshold IS 'Threshold de % usado para mostrar alerta: 0=sempre, 50/70/90=apenas se ultrapassar';
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_comments)
            conn.commit()

        output.append("\n" + "="*60)
        output.append("SUCESSO! Feature de Alertas de Potes configurada")
        output.append("="*60)
        output.append("\nO que foi feito:")
        output.append("1. Tabela NotificationConfigs criada/verificada")
        output.append("2. Colunas de alertas de potes adicionadas")
        output.append("3. Campo periodicidade em PotesDeGastos verificado")
        output.append("4. Configuracoes padrao inseridas para usuarios existentes")
        output.append("\nProximos passos:")
        output.append("1. Criar potes de gastos (via WhatsApp ou SQL)")
        output.append("2. Testar registrando uma despesa")
        output.append("3. Verificar mensagem de feedback enriquecida")
        output.append("4. (Futuro) Configurar threshold via WhatsApp")

        return "<pre>" + "\n".join(output) + "</pre>", 200

    except Exception as e:
        print(f"[POTES-ALERTS-SETUP] Erro: {e}")
        import traceback
        traceback.print_exc()
        return f"<pre>Erro ao configurar Alertas de Potes:\n\n{traceback.format_exc()}</pre>", 500


@admin_bp.route('/get-notification-config/<int:usuario_id>', methods=['GET'])
def get_notification_config(usuario_id):
    """
    Endpoint para visualizar configurações de notificação de um usuário.

    Exemplo:
    GET http://212.47.65.37:8000/admin/get-notification-config/1
    """
    try:
        from app.services.notification_config_service import NotificationConfigService

        config = NotificationConfigService.get_or_create_config(usuario_id)

        # Converter objetos time para string para JSON
        config_json = {
            'agenda_diaria_ativa': config['agenda_diaria_ativa'],
            'agenda_diaria_hora': config['agenda_diaria_hora'].strftime('%H:%M'),
            'resumo_matinal_ativo': config['resumo_matinal_ativo'],
            'resumo_matinal_hora': config['resumo_matinal_hora'].strftime('%H:%M'),
            'contas_vencer_ativa': config['contas_vencer_ativa'],
            'contas_vencer_dias_antes': config['contas_vencer_dias_antes'],
            'contas_vencer_hora': config['contas_vencer_hora'].strftime('%H:%M'),
            'alerta_potes_ativo': config.get('alerta_potes_ativo', True),
            'alerta_potes_threshold': config.get('alerta_potes_threshold', 0)
        }

        return jsonify({
            "status": "sucesso",
            "usuario_id": usuario_id,
            "configuracoes": config_json
        }), 200

    except Exception as e:
        print(f"[GET-CONFIG] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500