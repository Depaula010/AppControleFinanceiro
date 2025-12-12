# app/routes/api.py
"""
Blueprint de API REST para Dashboard Web
Endpoints para consumo do frontend Angular
"""
from functools import wraps
from flask import Blueprint, jsonify, request
from sqlalchemy import text
from datetime import date, datetime, timedelta

from app import db_engine
from app.routes.auth import verify_jwt_token
from app.services import finance_service, analytics_service
from app.services.period_query_service import PeriodQueryService
from app.utils import formatar_moeda

api_bp = Blueprint('api', __name__, url_prefix='/api')


# ============================================================
# DECORATOR DE AUTENTICAÇÃO
# ============================================================

def token_required(f):
    """
    Decorator para proteger rotas com autenticação JWT.

    Valida o token JWT do header Authorization: Bearer <token>
    Injeta user_id como primeiro argumento na função decorada.

    Uso:
        @api_bp.route('/protected', methods=['GET'])
        @token_required
        def protected_route(user_id):
            return jsonify({"message": f"Hello user {user_id}"})
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        # Buscar token do header Authorization
        auth_header = request.headers.get('Authorization', '')

        if not auth_header:
            return jsonify({
                "status": "error",
                "message": "Token de autenticação não fornecido"
            }), 401

        # Formato esperado: "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
        parts = auth_header.split()

        if len(parts) != 2 or parts[0].lower() != 'bearer':
            return jsonify({
                "status": "error",
                "message": "Formato de autenticação inválido. Use: Bearer <token>"
            }), 401

        token = parts[1]

        # Verificar token JWT
        payload = verify_jwt_token(token)

        if not payload:
            return jsonify({
                "status": "error",
                "message": "Token inválido ou expirado"
            }), 401

        # Extrair user_id do payload
        user_id = payload.get('user_id')

        if not user_id:
            return jsonify({
                "status": "error",
                "message": "Token não contém user_id"
            }), 401

        # Chamar a função original com user_id como primeiro argumento
        return f(user_id, *args, **kwargs)

    return decorated_function


# ============================================================
# ENDPOINTS DO DASHBOARD
# ============================================================

@api_bp.route('/dashboard/summary', methods=['GET'])
@token_required
def get_dashboard_summary(user_id):
    """
    GET /api/dashboard/summary

    Retorna resumo do dashboard: saldo total, receitas e despesas do mês.

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "data": {
                "saldo_total": 5430.50,
                "receitas_mes": 8000.00,
                "despesas_mes": 3245.30,
                "saldo_mes": 4754.70,
                "mes_referencia": "Dezembro/2025"
            }
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            # 1. Saldo total de todas as contas
            contas = finance_service.get_saldo_contas(conn, user_id)
            saldo_total = sum(conta['saldo'] for conta in contas)

            # 2. Receitas e Despesas do mês atual
            hoje = date.today()
            primeiro_dia_mes = hoje.replace(day=1)

            sql_mes_atual = text("""
                SELECT
                    tipo_transacao,
                    SUM(valor) as total
                FROM Transacoes
                WHERE usuario_id = :uid
                    AND data_transacao >= :data_inicio
                    AND data_transacao <= :data_fim
                    AND consolidada = true
                GROUP BY tipo_transacao
            """)

            result = conn.execute(sql_mes_atual, {
                "uid": user_id,
                "data_inicio": primeiro_dia_mes,
                "data_fim": hoje
            }).fetchall()

            receitas_mes = 0.0
            despesas_mes = 0.0

            for row in result:
                tipo = row.tipo_transacao
                total = abs(float(row.total or 0))

                if tipo == 'Receita':
                    receitas_mes = total
                elif tipo == 'Despesa':
                    despesas_mes = total

            saldo_mes = receitas_mes - despesas_mes

            # Formatar mês de referência
            meses_pt = {
                1: "Janeiro", 2: "Fevereiro", 3: "Março", 4: "Abril",
                5: "Maio", 6: "Junho", 7: "Julho", 8: "Agosto",
                9: "Setembro", 10: "Outubro", 11: "Novembro", 12: "Dezembro"
            }
            mes_referencia = f"{meses_pt[hoje.month]}/{hoje.year}"

            return jsonify({
                "status": "success",
                "data": {
                    "saldo_total": round(saldo_total, 2),
                    "receitas_mes": round(receitas_mes, 2),
                    "despesas_mes": round(despesas_mes, 2),
                    "saldo_mes": round(saldo_mes, 2),
                    "mes_referencia": mes_referencia
                }
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar resumo do dashboard: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar resumo do dashboard"
        }), 500


@api_bp.route('/dashboard/charts', methods=['GET'])
@token_required
def get_dashboard_charts(user_id):
    """
    GET /api/dashboard/charts

    Retorna dados para gráficos do dashboard.

    Headers:
        Authorization: Bearer <jwt_token>

    Query Parameters:
        meses: Quantidade de meses para análise (padrão: 3)

    Response:
        {
            "status": "success",
            "data": {
                "gastos_mensais": [
                    {"mes": "2025-10", "total": 3200.50},
                    {"mes": "2025-11", "total": 2890.30},
                    {"mes": "2025-12", "total": 3245.30}
                ],
                "gastos_categoria": [
                    {"categoria": "Alimentação", "total": 1200.00, "quantidade": 45},
                    {"categoria": "Transporte", "total": 800.00, "quantidade": 12}
                ],
                "gastos_dia_semana": [
                    {"dia": "Segunda", "total": 450.00, "quantidade": 12},
                    {"dia": "Terça", "total": 380.00, "quantidade": 10}
                ]
            }
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        # Parâmetro opcional: quantidade de meses
        meses_analise = request.args.get('meses', 3, type=int)

        if meses_analise < 1 or meses_analise > 12:
            return jsonify({
                "status": "error",
                "message": "Parâmetro 'meses' deve estar entre 1 e 12"
            }), 400

        # Buscar dados do serviço de analytics
        dados = analytics_service.get_spending_analysis(user_id, meses_analise=meses_analise)

        # Formatar dados para o frontend
        gastos_mensais = []
        for row in dados.get('gastos_mensais', []):
            gastos_mensais.append({
                "mes": row[0],  # YYYY-MM
                "total": abs(float(row[1] or 0))
            })

        gastos_categoria = []
        for row in dados.get('gastos_categoria', []):
            gastos_categoria.append({
                "macro_categoria": row[0],
                "subcategoria": row[1],
                "total": abs(float(row[2] or 0)),
                "quantidade": int(row[3])
            })

        gastos_dia_semana = []
        for row in dados.get('gastos_dia_semana', []):
            gastos_dia_semana.append({
                "dia": row[0].strip(),
                "total": abs(float(row[1] or 0)),
                "quantidade": int(row[2])
            })

        return jsonify({
            "status": "success",
            "data": {
                "gastos_mensais": gastos_mensais,
                "gastos_categoria": gastos_categoria,
                "gastos_dia_semana": gastos_dia_semana
            }
        }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar dados para gráficos: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar dados dos gráficos"
        }), 500


# ============================================================
# ENDPOINTS DE CONTAS
# ============================================================

@api_bp.route('/accounts', methods=['GET'])
@token_required
def get_accounts(user_id):
    """
    GET /api/accounts

    Lista todas as contas do usuário com saldos.

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "data": [
                {
                    "nome_conta": "Nubank",
                    "tipo_conta": "Conta Corrente",
                    "saldo": 2345.50
                },
                {
                    "nome_conta": "Cartão Inter",
                    "tipo_conta": "Cartão de Crédito",
                    "saldo": -1200.00
                }
            ]
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            contas = finance_service.get_saldo_contas(conn, user_id)

            return jsonify({
                "status": "success",
                "data": contas
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar contas: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar contas"
        }), 500


# ============================================================
# ENDPOINTS DE TRANSAÇÕES
# ============================================================

@api_bp.route('/transactions', methods=['GET'])
@token_required
def get_transactions(user_id):
    """
    GET /api/transactions

    Lista transações do usuário com paginação e filtros.

    Headers:
        Authorization: Bearer <jwt_token>

    Query Parameters:
        limit: Limite de registros por página (padrão: 20, máx: 100)
        offset: Deslocamento para paginação (padrão: 0)
        tipo: Filtrar por tipo ('Receita' ou 'Despesa', opcional)
        data_inicio: Data de início no formato YYYY-MM-DD (opcional)
        data_fim: Data de fim no formato YYYY-MM-DD (opcional)

    Response:
        {
            "status": "success",
            "data": {
                "total": 245,
                "limit": 20,
                "offset": 0,
                "transactions": [
                    {
                        "id": 1234,
                        "descricao": "Supermercado",
                        "valor": -150.50,
                        "tipo": "Despesa",
                        "data_transacao": "2025-12-11",
                        "categoria": "Alimentação",
                        "subcategoria": "Mercado",
                        "conta": "Nubank",
                        "consolidada": true
                    }
                ]
            }
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        # Parâmetros de paginação
        limit = request.args.get('limit', 20, type=int)
        offset = request.args.get('offset', 0, type=int)

        # Validar limite
        if limit < 1 or limit > 100:
            return jsonify({
                "status": "error",
                "message": "Parâmetro 'limit' deve estar entre 1 e 100"
            }), 400

        if offset < 0:
            return jsonify({
                "status": "error",
                "message": "Parâmetro 'offset' deve ser maior ou igual a 0"
            }), 400

        # Filtros opcionais
        tipo_filtro = request.args.get('tipo')  # 'Receita' ou 'Despesa'
        data_inicio = request.args.get('data_inicio')  # YYYY-MM-DD
        data_fim = request.args.get('data_fim')  # YYYY-MM-DD

        # Validar tipo
        if tipo_filtro and tipo_filtro not in ['Receita', 'Despesa']:
            return jsonify({
                "status": "error",
                "message": "Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'"
            }), 400

        # Validar datas
        try:
            if data_inicio:
                datetime.strptime(data_inicio, '%Y-%m-%d')
            if data_fim:
                datetime.strptime(data_fim, '%Y-%m-%d')
        except ValueError:
            return jsonify({
                "status": "error",
                "message": "Datas devem estar no formato YYYY-MM-DD"
            }), 400

        with db_engine.connect() as conn:
            # Construir query dinâmica
            where_conditions = ["t.usuario_id = :uid"]
            params = {
                "uid": user_id,
                "limit": limit,
                "offset": offset
            }

            if tipo_filtro:
                where_conditions.append("t.tipo_transacao = :tipo")
                params["tipo"] = tipo_filtro

            if data_inicio:
                where_conditions.append("t.data_transacao >= :data_inicio")
                params["data_inicio"] = data_inicio

            if data_fim:
                where_conditions.append("t.data_transacao <= :data_fim")
                params["data_fim"] = data_fim

            where_clause = " AND ".join(where_conditions)

            # Query para contar total de registros
            sql_count = text(f"""
                SELECT COUNT(*) as total
                FROM Transacoes t
                WHERE {where_clause}
            """)

            total = conn.execute(sql_count, params).scalar()

            # Query para buscar transações
            sql_transactions = text(f"""
                SELECT
                    t.id,
                    t.descricao,
                    t.valor,
                    t.tipo_transacao,
                    t.data_transacao,
                    m.nome_macro as categoria,
                    s.nome_sub as subcategoria,
                    c.nome_conta,
                    t.consolidada
                FROM Transacoes t
                JOIN SubCategoria s ON t.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN Contas c ON t.conta_id = c.id
                WHERE {where_clause}
                ORDER BY t.data_transacao DESC, t.created_at DESC
                LIMIT :limit OFFSET :offset
            """)

            result = conn.execute(sql_transactions, params).fetchall()

            transactions = []
            for row in result:
                transactions.append({
                    "id": row.id,
                    "descricao": row.descricao,
                    "valor": float(row.valor),
                    "tipo": row.tipo_transacao,
                    "data_transacao": row.data_transacao.isoformat() if row.data_transacao else None,
                    "categoria": row.categoria,
                    "subcategoria": row.subcategoria,
                    "conta": row.nome_conta,
                    "consolidada": row.consolidada
                })

            return jsonify({
                "status": "success",
                "data": {
                    "total": total,
                    "limit": limit,
                    "offset": offset,
                    "transactions": transactions
                }
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar transações: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar transações"
        }), 500


# ============================================================
# ENDPOINT DE TESTE (Health Check)
# ============================================================

@api_bp.route('/health', methods=['GET'])
def health_check():
    """
    GET /api/health

    Verifica se a API está funcionando.
    Não requer autenticação.
    """
    return jsonify({
        "status": "success",
        "message": "API está funcionando",
        "version": "1.0.0"
    }), 200


# ============================================================
# ENDPOINTS EM PORTUGUÊS (Aliases para compatibilidade)
# ============================================================

@api_bp.route('/dashboard/resumo', methods=['GET'])
@token_required
def get_dashboard_resumo(user_id):
    """
    GET /api/dashboard/resumo

    Alias em português para /api/dashboard/summary.
    Retorna resumo do dashboard: saldo total, receitas e despesas do mês.

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "data": {
                "saldo_total": 5430.50,
                "receitas_mes": 8000.00,
                "despesas_mes": 3245.30,
                "saldo_mes": 4754.70,
                "mes_referencia": "Dezembro/2025"
            }
        }
    """
    # Reutilizar a lógica do endpoint principal
    return get_dashboard_summary(user_id)


@api_bp.route('/transacoes/recentes', methods=['GET'])
@token_required
def get_transacoes_recentes(user_id):
    """
    GET /api/transacoes/recentes

    Lista as últimas 10 transações do usuário (mais recentes primeiro).

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "data": [
                {
                    "id": 1234,
                    "descricao": "Supermercado",
                    "valor": -150.50,
                    "tipo": "Despesa",
                    "data": "2025-12-11",
                    "categoria": "Alimentação",
                    "conta": "Nubank"
                }
            ]
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            sql = text("""
                SELECT
                    t.id,
                    t.descricao,
                    t.valor,
                    t.tipo_transacao,
                    t.data_transacao,
                    m.nome_macro as categoria,
                    c.nome_conta
                FROM Transacoes t
                JOIN SubCategoria s ON t.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN Contas c ON t.conta_id = c.id
                WHERE t.usuario_id = :uid
                ORDER BY t.data_transacao DESC, t.created_at DESC
                LIMIT 10
            """)

            result = conn.execute(sql, {"uid": user_id}).fetchall()

            transacoes = []
            for row in result:
                transacoes.append({
                    "id": row.id,
                    "descricao": row.descricao,
                    "valor": float(row.valor),
                    "tipo": row.tipo_transacao,
                    "data": row.data_transacao.isoformat() if row.data_transacao else None,
                    "categoria": row.categoria,
                    "conta": row.nome_conta
                })

            return jsonify({
                "status": "success",
                "data": transacoes
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar transações recentes: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar transações recentes"
        }), 500


@api_bp.route('/contas', methods=['GET'])
@token_required
def get_contas(user_id):
    """
    GET /api/contas

    Alias em português para /api/accounts.
    Lista todas as contas do usuário com saldos.

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "data": [
                {
                    "nome_conta": "Nubank",
                    "tipo_conta": "Conta Corrente",
                    "saldo": 2345.50
                }
            ]
        }
    """
    # Reutilizar a lógica do endpoint principal
    return get_accounts(user_id)
