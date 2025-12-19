"""
Módulo de setup inicial do banco de dados.

Rotas para criar estrutura do banco, tabelas e dados iniciais.
Geralmente executadas uma vez durante deploy/setup.
"""

from flask import Blueprint, request
from app.shared.decorators import require_api_key, handle_errors
from app.shared.responses import ApiResponse

# Blueprint para database setup
database_setup_bp = Blueprint('admin_database_setup', __name__)


@database_setup_bp.route('/clear-bot-session', methods=['POST'])
@handle_errors(tag="ADMIN-FIX")
def clear_bot_session():
    """
    ENDPOINT DE EMERGÊNCIA: Limpa a tabela 'baileys_auth'.

    Exemplo:
    POST /admin/clear-bot-session
    """
    from app.services import finance_service

    deleted_rows = finance_service.clear_bot_session()
    mensagem = f"Sessão do bot ('baileys_auth') limpa com sucesso. {deleted_rows} linhas deletadas."

    print(f"[ADMIN-FIX] {mensagem}")
    return ApiResponse.success(mensagem, linhas_deletadas=deleted_rows)


@database_setup_bp.route('/setup-database', methods=['GET'])
@handle_errors(tag="SETUP-DATABASE")
def setup_database():
    """
    Cria/Recria a ESTRUTURA final do banco (v12).

    Exemplo:
    GET /admin/setup-database
    """
    from app.services import finance_service

    finance_service.setup_database_schema()
    return "Estrutura final do banco (v12) criada/recriada com sucesso!", 200


@database_setup_bp.route('/populate-global-categories', methods=['GET'])
@handle_errors(tag="POPULATE-CATEGORIES")
def populate_global_categories():
    """
    Insere os TEMPLATES GLOBAIS de categorias.

    Exemplo:
    GET /admin/populate-global-categories
    """
    from app.services import finance_service

    finance_service.populate_global_categories()
    return "Templates globais de categorias (v12) inseridos com sucesso!", 200


@database_setup_bp.route('/setup-user-data', methods=['GET'])
@handle_errors(tag="SETUP-USER")
def setup_user_data():
    """
    Insere/atualiza o usuário e contas.

    Exemplo:
    GET /admin/setup-user-data
    """
    from app.services import finance_service

    # Dados que estavam "hardcoded" no app.py
    user_id, api_key = finance_service.setup_user_data(
        numero_whatsapp='553194001072',
        dia_venc_cartao=20,
        dia_fech_cartao=13
    )

    return ApiResponse.success(
        f"Usuário e Contas inseridos/atualizados (Usuário ID: {user_id})!",
        user_api_key_para_automate=api_key
    )


@database_setup_bp.route('/setup-calendar-table', methods=['GET'])
@handle_errors(tag="SETUP-CALENDAR")
def setup_calendar_table():
    """
    Cria a tabela GoogleCalendarTokens.

    Exemplo:
    GET /admin/setup-calendar-table
    """
    from app.services import finance_service

    finance_service.add_google_calendar_tokens_table()
    return "✅ Tabela GoogleCalendarTokens criada!", 200


@database_setup_bp.route('/setup-monthly-reports-table', methods=['GET'])
@handle_errors(tag="SETUP-MONTHLY-REPORTS")
def setup_monthly_reports_table():
    """
    Cria a tabela MonthlyReportConfigs para configuração de relatórios mensais.

    Exemplo:
    GET https://seu-backend.onrender.com/admin/setup-monthly-reports-table
    """
    from app.services.monthly_report_config_service import criar_tabela_monthly_report_configs

    criar_tabela_monthly_report_configs()
    return ApiResponse.success("✅ Tabela MonthlyReportConfigs criada com sucesso!")


@database_setup_bp.route('/setup-api-keys-tables', methods=['POST'])
@require_api_key
@handle_errors(tag="SETUP-API-KEYS")
def setup_api_keys_tables():
    """
    Endpoint administrativo para criar tabelas de API Keys (SaaS).

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
    from app.services.finance_service import criar_tabelas_chaves_api

    sucesso = criar_tabelas_chaves_api()

    if sucesso:
        return ApiResponse.success(
            "Tabelas de API Keys criadas com sucesso!",
            tabelas_criadas=[
                "ChavesApiUsuario",
                "PreferenciasChaveApi",
                "LogAcessoChaveApi",
                "RastreamentoUsoApi",
                "ConsentimentoUsuario",
                "Planos",
                "AssinaturasUsuario"
            ],
            planos_inseridos=["Bronze (gratuito)", "Prata (R$ 29,90)", "Ouro (R$ 79,90)"]
        )
    else:
        return ApiResponse.error("Erro ao criar tabelas. Veja logs do servidor.")
