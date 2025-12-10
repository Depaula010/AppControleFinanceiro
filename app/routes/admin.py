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
    - IPs na blacklist permanente
    - IPs bloqueados temporariamente
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


@admin_bp.route('/security-blacklist-add', methods=['POST'])
def security_blacklist_add():
    """
    Adiciona um IP à blacklist permanente (bloqueado por 1 ano)

    Body JSON:
    {
        "ip": "192.168.1.100",
        "reason": "Motivo do bloqueio" (opcional)
    }

    Exemplo:
    POST https://seu-backend.onrender.com/admin/security-blacklist-add
    Header: x-api-key: sua_chave_secreta
    Body: {"ip": "172.19.0.6", "reason": "Tentativas repetidas de invasão"}
    """
    # Verificar autenticação
    api_key = request.headers.get('x-api-key')
    if api_key != API_SECRET_KEY:
        return jsonify({"erro": "Chave de API inválida"}), 401

    from app.middleware.security import blacklist_ip

    data = request.get_json()
    if not data or 'ip' not in data:
        return jsonify({
            "status": "erro",
            "mensagem": "Campo 'ip' é obrigatório"
        }), 400

    ip = data['ip']
    reason = data.get('reason', 'Manual block via API')

    success = blacklist_ip(ip, reason)

    if success:
        return jsonify({
            "status": "sucesso",
            "mensagem": f"IP {ip} adicionado à blacklist permanente",
            "ip": ip,
            "reason": reason
        }), 200
    else:
        return jsonify({
            "status": "erro",
            "mensagem": "Falha ao adicionar IP à blacklist (Redis indisponível)"
        }), 500


@admin_bp.route('/security-blacklist-remove', methods=['POST'])
def security_blacklist_remove():
    """
    Remove um IP da blacklist permanente

    Body JSON:
    {
        "ip": "192.168.1.100"
    }

    Exemplo:
    POST https://seu-backend.onrender.com/admin/security-blacklist-remove
    Header: x-api-key: sua_chave_secreta
    Body: {"ip": "172.19.0.6"}
    """
    # Verificar autenticação
    api_key = request.headers.get('x-api-key')
    if api_key != API_SECRET_KEY:
        return jsonify({"erro": "Chave de API inválida"}), 401

    from app.middleware.security import remove_from_blacklist

    data = request.get_json()
    if not data or 'ip' not in data:
        return jsonify({
            "status": "erro",
            "mensagem": "Campo 'ip' é obrigatório"
        }), 400

    ip = data['ip']
    success = remove_from_blacklist(ip)

    if success:
        return jsonify({
            "status": "sucesso",
            "mensagem": f"IP {ip} removido da blacklist",
            "ip": ip
        }), 200
    else:
        return jsonify({
            "status": "erro",
            "mensagem": "IP não encontrado na blacklist ou Redis indisponível"
        }), 404


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


@admin_bp.route('/setup-checkin-noturno', methods=['GET'])
def setup_checkin_noturno():
    """
    Adiciona as colunas necessárias para o Check-in Noturno.

    - Adiciona 'checkin_noturno_ativo' e 'checkin_noturno_hora' na tabela NotificationConfigs
    - Cria constraint de validação de horário (18:00-23:00)

    Exemplo:
    GET http://localhost:5000/admin/setup-checkin-noturno
    """
    try:
        output = []
        output.append("="*60)
        output.append("SETUP: Check-in Noturno (Confirmação de Contas Pendentes)")
        output.append("="*60)

        # Usar a função de migração do finance_service
        from app.services.finance_service import add_nightly_checkin_config_columns

        output.append("\n[1/1] Adicionando campos de check-in noturno...")

        sucesso = add_nightly_checkin_config_columns()

        if sucesso:
            output.append("OK - Campos 'checkin_noturno_ativo' e 'checkin_noturno_hora' adicionados!")
            output.append("OK - Constraint de horário (18:00-23:00) criada!")
        else:
            output.append("ERRO - Falha ao adicionar campos (verifique logs)")

        output.append("\n" + "="*60)
        output.append("SUCESSO! Check-in Noturno configurado")
        output.append("="*60)
        output.append("\nPróximos passos:")
        output.append("1. Rebuild containers: docker-compose up -d --build")
        output.append("2. Testar via WhatsApp: 'Ativar check-in noturno às 20:00'")
        output.append("3. Testar via WhatsApp: 'Configurar check-in noturno'")
        output.append("4. Verificar logs Ofelia: docker logs meu-secretario-cron")
        output.append("5. Teste manual: docker exec meu-secretario-web python /app/processar_checkin_noturno.py")

        return "<pre>" + "\n".join(output) + "</pre>", 200

    except Exception as e:
        print(f"[CHECKIN-NOTURNO-SETUP] Erro: {e}")
        import traceback
        traceback.print_exc()
        return f"<pre>Erro ao configurar Check-in Noturno:\n\n{traceback.format_exc()}</pre>", 500


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

        # Buscar usuários que devem receber neste horário (resumo matinal OU alertas financeiros)
        usuarios = NotificationConfigService.get_users_with_notifications_active(hora_atual)

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


@admin_bp.route('/test-daily-briefing', methods=['POST'])
def test_daily_briefing():
    """
    Endpoint de TESTE para enviar resumo matinal para o usuário padrão (ID 1).
    Ignora horário configurado - envia imediatamente.

    Uso:
    POST /admin/test-daily-briefing
    Header: x-api-key: SUA_SECRET_KEY

    Opcional - Body JSON:
    {
        "usuario_id": 1  // Se não informar, usa 1 como padrão
    }
    """
    # Autenticar
    secret_key = request.headers.get('x-api-key')
    if secret_key != API_SECRET_KEY:
        print("[TEST-BRIEFING] Tentativa não autorizada")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        from app.services.daily_briefing_service import DailyBriefingService
        from app.services.gemini_service import generate_daily_briefing

        # Pegar usuario_id do body ou usar 1 como padrão
        data = request.get_json(silent=True) or {}
        usuario_id = data.get('usuario_id', 1)

        print(f"[TEST-BRIEFING] Testando resumo matinal para usuário {usuario_id}...")

        # Buscar dados do usuário
        sql = text("""
            SELECT u.numero_whatsapp
            FROM Usuarios u
            WHERE u.id = :uid
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql, {"uid": usuario_id}).fetchone()

        if not result:
            return jsonify({
                "status": "erro",
                "mensagem": f"Usuário {usuario_id} não encontrado"
            }), 404

        numero_whatsapp = result.numero_whatsapp

        if not numero_whatsapp:
            return jsonify({
                "status": "erro",
                "mensagem": f"Usuário {usuario_id} não tem número de WhatsApp cadastrado"
            }), 400

        # Inicializar serviço
        briefing_service = DailyBriefingService()

        # Preparar dados do resumo
        briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

        if not briefing_data:
            return jsonify({
                "status": "erro",
                "mensagem": "Erro ao preparar dados do briefing"
            }), 500

        # Gerar mensagem
        mensagem = ""
        if briefing_data['total_eventos'] == 0:
            print(f"[TEST-BRIEFING] Sem eventos. Gerando mensagem básica.")
            mensagem = briefing_service.generate_briefing_message(usuario_id, date.today())
        else:
            print(f"[TEST-BRIEFING] Gerando resumo com IA ({briefing_data['total_eventos']} eventos)...")
            mensagem = generate_daily_briefing(briefing_data)

        if not mensagem:
            return jsonify({
                "status": "erro",
                "mensagem": "Falha ao gerar mensagem"
            }), 500

        # Enviar via WhatsApp
        print(f"[TEST-BRIEFING] Enviando para {numero_whatsapp}...")
        sucesso = enviar_notificacao_whatsapp(
            numero_whatsapp,
            mensagem,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        if sucesso:
            print(f"[TEST-BRIEFING] ✅ Resumo enviado com sucesso!")
            return jsonify({
                "status": "sucesso",
                "mensagem": "Resumo matinal enviado com sucesso",
                "usuario_id": usuario_id,
                "numero_whatsapp": numero_whatsapp,
                "total_eventos": briefing_data['total_eventos'],
                "preview_mensagem": mensagem[:200] + "..." if len(mensagem) > 200 else mensagem
            }), 200
        else:
            return jsonify({
                "status": "erro",
                "mensagem": "Falha ao enviar WhatsApp"
            }), 500

    except Exception as e:
        print(f"[TEST-BRIEFING] ❌ Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e),
            "traceback": traceback.format_exc()
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
            'resumo_matinal_ativo': config['resumo_matinal_ativo'],
            'resumo_matinal_hora': config['resumo_matinal_hora'].strftime('%H:%M'),
            'alertas_financeiros_ativos': config['alertas_financeiros_ativos']
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


@admin_bp.route('/setup-alertas-financeiros', methods=['GET'])
def setup_alertas_financeiros():
    """
    Adiciona o campo alertas_financeiros_ativos na tabela NotificationConfigs.
    Migra dados existentes de contas_vencer_ativa para o novo campo.

    Exemplo:
    GET http://212.47.65.37:8000/admin/setup-alertas-financeiros
    """
    try:
        from sqlalchemy import text

        output = []
        output.append("="*60)
        output.append("SETUP: Alertas Financeiros Unificados")
        output.append("="*60)
        output.append("\nNOTA: Este endpoint verifica se a estrutura está correta.")
        output.append("Se você já rodou o cleanup, a tabela já está limpa.\n")

        # Verificar se campo existe
        output.append("[1/2] Verificando campo alertas_financeiros_ativos...")

        sql_add_column = text("""
            ALTER TABLE NotificationConfigs
            ADD COLUMN IF NOT EXISTS alertas_financeiros_ativos BOOLEAN DEFAULT TRUE;
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_add_column)
            conn.commit()

        output.append("OK - Campo existe e está configurado!")

        # Adicionar comentário no banco
        output.append("\n[2/2] Documentando estrutura...")

        sql_comment = text("""
            COMMENT ON COLUMN NotificationConfigs.alertas_financeiros_ativos IS
            'Se TRUE, inclui alertas de contas e faturas a vencer no resumo matinal (ou envia separado se resumo desativado)';
        """)

        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_comment)
            conn.commit()

        output.append("OK - Comentário adicionado!")

        output.append("\n" + "="*60)
        output.append("SUCESSO! Sistema de Alertas Financeiros configurado")
        output.append("="*60)
        output.append("\nEstrutura atual (limpa):")
        output.append("- resumo_matinal_ativo (controla resumo com agenda/clima)")
        output.append("- resumo_matinal_hora (horário único de envio)")
        output.append("- alertas_financeiros_ativos (controla alertas de contas/faturas)")
        output.append("\nComportamento:")
        output.append("- Ambos ativos: 1 mensagem unificada (resumo + alertas)")
        output.append("- Só resumo ativo: Apenas agenda e clima")
        output.append("- Só alertas ativo: Apenas contas/faturas a vencer")
        output.append("- Ambos desativados: Nenhuma mensagem enviada")
        output.append("\nPróximos passos:")
        output.append("1. Testar com: GET /admin/get-notification-config/1")
        output.append("2. Configurar alertas: POST /admin/config-alertas-financeiros")
        output.append("3. Executar processador: GET /admin/trigger-daily-briefing")

        return "<pre>" + "\n".join(output) + "</pre>", 200

    except Exception as e:
        print(f"[ALERTAS-FIN-SETUP] Erro: {e}")
        import traceback
        traceback.print_exc()
        return f"<pre>Erro ao configurar Alertas Financeiros:\n\n{traceback.format_exc()}</pre>", 500


@admin_bp.route('/setup-api-keys-tables', methods=['POST'])
def setup_api_keys_tables():
    """
    Endpoint administrativo para criar tabelas de API Keys (SaaS).
    Protegido por API_SECRET_KEY.

    Cria 7 tabelas:
    - ChavesApiUsuario: Chaves do usuário (criptografadas)
    - PreferenciasChaveApi: Escolha explícita (chave própria ou sistema)
    - LogAcessoChaveApi: Auditoria de segurança
    - RastreamentoUsoApi: Tracking para billing
    - ConsentimentoUsuario: LGPD compliance
    - Planos: Sistema de planos (Bronze, Prata, Ouro)
    - AssinaturasUsuario: Assinaturas dos usuários

    Exemplo:
    POST http://localhost:8000/admin/setup-api-keys-tables
    Headers: X-API-KEY: {API_SECRET_KEY}
    """
    # Verificar autenticação
    secret_key_recebida = request.headers.get('x-api-key')
    if secret_key_recebida != API_SECRET_KEY:
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        # Importar e executar função
        from app.services.finance_service import criar_tabelas_chaves_api

        sucesso = criar_tabelas_chaves_api()

        if sucesso:
            return jsonify({
                "status": "sucesso",
                "mensagem": "Tabelas de API Keys criadas com sucesso!",
                "tabelas_criadas": [
                    "ChavesApiUsuario",
                    "PreferenciasChaveApi",
                    "LogAcessoChaveApi",
                    "RastreamentoUsoApi",
                    "ConsentimentoUsuario",
                    "Planos",
                    "AssinaturasUsuario"
                ],
                "planos_inseridos": ["Bronze (gratuito)", "Prata (R$ 29,90)", "Ouro (R$ 79,90)"]
            }), 200
        else:
            return jsonify({
                "status": "erro",
                "mensagem": "Erro ao criar tabelas. Veja logs do servidor."
            }), 500

    except Exception as e:
        print(f"[SETUP-API-KEYS] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": f"Erro ao criar tabelas: {str(e)}"
        }), 500


@admin_bp.route('/cleanup-deprecated-notification-fields', methods=['GET'])
def cleanup_deprecated_notification_fields():
    """
    Remove campos DEPRECATED da tabela NotificationConfigs.

    ATENÇÃO: Esta operação é IRREVERSÍVEL!

    Remove os seguintes campos:
    - agenda_diaria_ativa (substituído por resumo_matinal_ativo)
    - agenda_diaria_hora (substituído por resumo_matinal_hora)
    - contas_vencer_ativa (substituído por alertas_financeiros_ativos)
    - contas_vencer_dias_antes (não mais usado - alertas sempre para hoje e amanhã)
    - contas_vencer_hora (substituído por resumo_matinal_hora)

    Exemplo:
    GET http://212.47.65.37:8000/admin/cleanup-deprecated-notification-fields
    """
    try:
        from sqlalchemy import text

        output = []
        output.append("="*60)
        output.append("CLEANUP: Removendo campos DEPRECATED")
        output.append("="*60)
        output.append("\n⚠️  ATENÇÃO: Esta operação é IRREVERSÍVEL!")

        # Lista de colunas a remover
        deprecated_columns = [
            'agenda_diaria_ativa',
            'agenda_diaria_hora',
            'contas_vencer_ativa',
            'contas_vencer_dias_antes',
            'contas_vencer_hora'
        ]

        output.append(f"\n[INFO] Removendo {len(deprecated_columns)} colunas deprecadas...")

        with db_engine.connect() as conn:
            conn.begin()

            for idx, column_name in enumerate(deprecated_columns, 1):
                output.append(f"\n[{idx}/{len(deprecated_columns)}] Removendo coluna '{column_name}'...")

                sql_drop = text(f"""
                    ALTER TABLE NotificationConfigs
                    DROP COLUMN IF EXISTS {column_name};
                """)

                try:
                    conn.execute(sql_drop)
                    output.append(f"    OK - '{column_name}' removida!")
                except Exception as e:
                    output.append(f"    AVISO - Erro ao remover '{column_name}': {e}")

            conn.commit()

        output.append("\n" + "="*60)
        output.append("SUCESSO! Campos deprecados removidos")
        output.append("="*60)
        output.append("\nCampos MANTIDOS (estrutura limpa):")
        output.append("- resumo_matinal_ativo (controla resumo com agenda/clima)")
        output.append("- resumo_matinal_hora (horário único de envio)")
        output.append("- alertas_financeiros_ativos (controla alertas de contas/faturas)")
        output.append("\nComportamento:")
        output.append("- Um único horário controla todas as notificações")
        output.append("- Usuário escolhe quais componentes quer receber")
        output.append("- Mensagens são unificadas quando ambos estão ativos")

        return "<pre>" + "\n".join(output) + "</pre>", 200

    except Exception as e:
        print(f"[CLEANUP] Erro: {e}")
        import traceback
        traceback.print_exc()
        return f"<pre>Erro ao limpar campos deprecados:\n\n{traceback.format_exc()}</pre>", 500


@admin_bp.route('/config-alertas-financeiros', methods=['POST'])
def config_alertas_financeiros():
    """
    Configura alertas financeiros para um usuário.

    Body JSON:
    {
        "usuario_id": 1,
        "ativo": true/false
    }

    Exemplo:
    POST http://212.47.65.37:8000/admin/config-alertas-financeiros
    Body: {"usuario_id": 1, "ativo": true}
    """
    try:
        from app.services.notification_config_service import NotificationConfigService

        data = request.get_json()

        if not data or 'usuario_id' not in data:
            return jsonify({
                "status": "erro",
                "mensagem": "Campo 'usuario_id' é obrigatório"
            }), 400

        usuario_id = data['usuario_id']
        ativo = data.get('ativo')

        if ativo is None:
            return jsonify({
                "status": "erro",
                "mensagem": "Campo 'ativo' é obrigatório (true ou false)"
            }), 400

        # Atualizar configuração
        sucesso, mensagem, config = NotificationConfigService.update_alertas_financeiros_config(
            usuario_id, ativo
        )

        if sucesso:
            return jsonify({
                "status": "sucesso",
                "mensagem": mensagem,
                "configuracao": config
            }), 200
        else:
            return jsonify({
                "status": "erro",
                "mensagem": mensagem
            }), 500

    except Exception as e:
        print(f"[CONFIG-ALERTAS-FIN] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@admin_bp.route('/gemini-cache-stats', methods=['GET'])
def gemini_cache_stats():
    """
    Retorna estatísticas do cache do Gemini AI

    Mostra:
    - Total de requisições (hits + misses)
    - Cache hits e misses
    - Hit rate geral
    - Breakdown por tipo de operação (intent, category, extract_trans, etc)
    - Economia estimada de quota

    Exemplo:
    GET https://seu-backend.onrender.com/admin/gemini-cache-stats
    Header: x-api-key: sua_chave_secreta

    Resposta:
    {
        "status": "sucesso",
        "total_requests": 1000,
        "cache_hits": 650,
        "cache_misses": 350,
        "cache_saves": 650,
        "cache_errors": 0,
        "hit_rate": "65.0%",
        "breakdown_by_type": {
            "intent": {
                "hits": 400,
                "misses": 50,
                "total": 450,
                "hit_rate": "88.9%"
            },
            "category": {
                "hits": 200,
                "misses": 100,
                "total": 300,
                "hit_rate": "66.7%"
            },
            ...
        },
        "estimated_savings": {
            "calls_saved": 650,
            "quota_saved_pct": "65.0%"
        }
    }
    """
    # Verificar autenticação
    api_key = request.headers.get('x-api-key')
    if api_key != API_SECRET_KEY:
        return jsonify({"erro": "Chave de API inválida"}), 401

    try:
        from app.services.gemini_cache_service import gemini_cache_service

        # Obter métricas do serviço de cache
        metrics = gemini_cache_service.get_metrics()

        return jsonify({
            "status": "sucesso",
            **metrics
        }), 200

    except Exception as e:
        print(f"[GEMINI-CACHE-STATS] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500