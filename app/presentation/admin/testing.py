# app/presentation/admin/testing.py
"""
Módulo para rotas de teste e debug do sistema.

Este módulo contém endpoints administrativos para:
- Debug do Google Calendar
- Teste de notificações (agenda, contas, relatórios)
- Testes de resumo matinal
"""

from flask import Blueprint, request
from sqlalchemy import text
from datetime import datetime, date, timezone, time, timedelta

from app.shared.decorators import require_api_key, handle_errors
from app.shared.responses import ApiResponse
from app.utils import formatar_moeda
from ._common import (
    db_engine,
    API_SECRET_KEY,
    BOT_WHATSAPP_URL,
    get_current_datetime_brazil
)

testing_bp = Blueprint('admin_testing', __name__)


@testing_bp.route('/debug-calendar', methods=['GET'])
@handle_errors(tag="DEBUG-CALENDAR")
def debug_calendar():
    """
    Rota temporária para debug do Google Calendar.

    Executa 5 testes:
    1. Verificar conexão do usuário
    2. Obter credenciais OAuth
    3. Criar serviço Calendar
    4. Listar calendários
    5. Buscar eventos de hoje

    Exemplo:
    GET http://localhost:5000/admin/debug-calendar
    """
    from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService

    output = []
    output.append("=" * 60)
    output.append("🧪 TESTE DE DEBUG - GOOGLE CALENDAR")
    output.append("=" * 60)

    usuario_id = 1

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

    return "<pre>" + "\n".join(output) + "</pre>"


@testing_bp.route('/test-notification', methods=['POST'])
@require_api_key
@handle_errors(tag="TEST-NOTIFICATION")
def test_notification():
    """
    Endpoint para testar notificações manualmente.

    Body JSON:
    {
        "tipo": "agenda" ou "contas",
        "usuario_id": 1
    }

    Exemplo:
    POST http://localhost:5000/admin/test-notification
    Headers: x-api-key: sua_chave_secreta
    Body: {"tipo": "agenda", "usuario_id": 1}
    """
    from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
    from app.services.calendar_query_service import CalendarQueryService
    from app.services.notification_service import enviar_notificacao_whatsapp

    data = request.json
    tipo = data.get('tipo', 'agenda')
    usuario_id = data.get('usuario_id', 1)

    # Buscar dados do usuário
    with db_engine.connect() as conn:
        sql = text("SELECT nome, numero_whatsapp FROM Usuarios WHERE id = :uid")
        usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()

    if not usuario:
        return ApiResponse.error("Usuário não encontrado", status_code=404)

    nome, numero_whatsapp = usuario

    if tipo == 'agenda':
        # Testar agenda
        if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
            return ApiResponse.error(
                "Usuário não conectou Google Calendar",
                status_code=400
            )

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
        return ApiResponse.error("Tipo inválido", status_code=400)

    # Enviar
    sucesso = enviar_notificacao_whatsapp(
        numero_whatsapp,
        mensagem,
        BOT_WHATSAPP_URL,
        API_SECRET_KEY
    )

    if sucesso:
        return ApiResponse.success(
            f"Notificação de teste enviada para {nome}",
            tipo=tipo,
            usuario_id=usuario_id,
            numero_whatsapp=numero_whatsapp
        )
    else:
        return ApiResponse.error("Falha ao enviar notificação", status_code=500)


@testing_bp.route('/test-monthly-report/<int:usuario_id>', methods=['POST'])
@require_api_key
@handle_errors(tag="TEST-MONTHLY-REPORT")
def test_monthly_report(usuario_id):
    """
    Endpoint para testar envio manual de relatório mensal.

    Query params:
        - momento: 'INICIO_MES' (padrão) ou 'FIM_MES'

    Exemplo:
    POST https://seu-backend.onrender.com/admin/test-monthly-report/1?momento=INICIO_MES
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.monthly_report_processor_service import enviar_relatorio_manual

    momento_envio = request.args.get('momento', 'INICIO_MES')

    if momento_envio not in ['INICIO_MES', 'FIM_MES']:
        return ApiResponse.error(
            "Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'",
            status_code=400
        )

    print(f"[MONTHLY-REPORT-TEST] 📊 Enviando relatório manual para usuário {usuario_id}...")
    resultado = enviar_relatorio_manual(usuario_id, momento_envio)

    if resultado['sucesso']:
        return ApiResponse.success(
            "Relatório mensal enviado com sucesso",
            **resultado
        )
    else:
        return ApiResponse.error(
            resultado.get('mensagem', 'Erro ao enviar relatório'),
            status_code=400,
            **resultado
        )


@testing_bp.route('/test-daily-briefing', methods=['POST'])
@require_api_key
@handle_errors(tag="TEST-DAILY-BRIEFING")
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
    from app.services.daily_briefing_service import DailyBriefingService
    from app.services.gemini_service import generate_daily_briefing
    from app.services.notification_service import enviar_notificacao_whatsapp

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
        return ApiResponse.error(
            f"Usuário {usuario_id} não encontrado",
            status_code=404
        )

    numero_whatsapp = result.numero_whatsapp

    if not numero_whatsapp:
        return ApiResponse.error(
            f"Usuário {usuario_id} não tem número de WhatsApp cadastrado",
            status_code=400
        )

    # Inicializar serviço
    briefing_service = DailyBriefingService()

    # Preparar dados do resumo
    briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

    if not briefing_data:
        return ApiResponse.error(
            "Erro ao preparar dados do briefing",
            status_code=500
        )

    # Gerar mensagem
    mensagem = ""
    if briefing_data['total_eventos'] == 0:
        print(f"[TEST-BRIEFING] Sem eventos. Gerando mensagem básica.")
        mensagem = briefing_service.generate_briefing_message(usuario_id, date.today())
    else:
        print(f"[TEST-BRIEFING] Gerando resumo com IA ({briefing_data['total_eventos']} eventos)...")
        mensagem = generate_daily_briefing(briefing_data)

    if not mensagem:
        return ApiResponse.error("Falha ao gerar mensagem", status_code=500)

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
        return ApiResponse.success(
            "Resumo matinal enviado com sucesso",
            usuario_id=usuario_id,
            numero_whatsapp=numero_whatsapp,
            total_eventos=briefing_data['total_eventos'],
            preview_mensagem=mensagem[:200] + "..." if len(mensagem) > 200 else mensagem
        )
    else:
        return ApiResponse.error("Falha ao enviar WhatsApp", status_code=500)
