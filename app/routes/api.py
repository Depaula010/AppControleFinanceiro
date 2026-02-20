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
from app.services.finance_service import get_or_create_fatura
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

def _get_dashboard_summary_impl(user_id):
    """Lógica interna para buscar resumo do dashboard (sem decorator)."""
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


@api_bp.route('/dashboard/summary', methods=['GET'])
@token_required
def get_dashboard_summary(user_id):
    """GET /api/dashboard/summary - Retorna resumo do dashboard."""
    return _get_dashboard_summary_impl(user_id)


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

        # Buscar dados do serviço de analytics (já retorna estruturado)
        dados = analytics_service.get_spending_analysis(user_id, meses_analise=meses_analise)

        # Formatar dados para o frontend (ajustar valores negativos para positivos)
        gastos_mensais = [
            {
                "mes": item["mes"],
                "total": abs(item["total"])
            }
            for item in dados.get('gastos_mensais', [])
        ]

        gastos_categoria = [
            {
                "macro_categoria": item["categoria"],
                "subcategoria": item["subcategoria"],
                "total": abs(item["total"]),
                "quantidade": item["quantidade"]
            }
            for item in dados.get('gastos_por_categoria', [])
        ]

        gastos_dia_semana = [
            {
                "dia": item["dia"],
                "total": abs(item["total"]),
                "quantidade": item["quantidade"]
            }
            for item in dados.get('gastos_por_dia_semana', [])
        ]

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

def _get_accounts_impl(user_id):
    """Lógica interna para buscar contas (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            # Query direta retornando id, nome e tipo para compatibilidade com o frontend Angular.
            # O modelo BankAccount espera: { id, nome, banco, tipo, saldo }
            # A coluna 'banco' não existe na tabela; usamos tipo_conta como banco.
            sql = text("""
                SELECT
                    c.id,
                    c.nome_conta,
                    c.tipo_conta,
                    c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS saldo
                FROM Contas c
                LEFT JOIN Transacoes t ON c.id = t.conta_id
                WHERE c.usuario_id = :uid
                    AND c.ativa = true
                GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
                ORDER BY c.tipo_conta, c.nome_conta
            """)
            rows = conn.execute(sql, {"uid": user_id}).fetchall()

            contas = [{
                "id": row.id,
                "nome": row.nome_conta,
                "banco": row.tipo_conta,   # Usamos tipo_conta como label do banco
                "tipo": row.tipo_conta,
                "saldo": float(row.saldo)
            } for row in rows]

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


@api_bp.route('/accounts', methods=['GET'])
@token_required
def get_accounts(user_id):
    """GET /api/accounts - Lista todas as contas do usuário."""
    return _get_accounts_impl(user_id)


# Tipos de conta válidos
TIPOS_CONTA_VALIDOS = [
    'Conta Corrente', 'Conta Poupança', 'Investimento',
    'Cartão de Crédito', 'Dinheiro', 'Outro'
]

# Tipos de agendamento e periodicidades válidos
TIPOS_AGENDAMENTO = ['FIXO', 'LEMBRETE_VARIAVEL']
PERIODICIDADES = ['DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']


def _create_account_impl(user_id):
    """Lógica interna para criar conta (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.get_json()

        if not data:
            return jsonify({"status": "error", "message": "Dados não fornecidos"}), 400

        # Validar campos obrigatórios
        nome_conta = data.get('nome_conta', '').strip()
        tipo_conta = data.get('tipo_conta', '').strip()

        if not nome_conta:
            return jsonify({"status": "error", "message": "Nome da conta é obrigatório"}), 400

        if not tipo_conta:
            return jsonify({"status": "error", "message": "Tipo da conta é obrigatório"}), 400

        if tipo_conta not in TIPOS_CONTA_VALIDOS:
            return jsonify({
                "status": "error",
                "message": f"Tipo de conta inválido. Valores permitidos: {', '.join(TIPOS_CONTA_VALIDOS)}"
            }), 400

        # Sanitizar nome (prevenir XSS)
        nome_conta = nome_conta[:100]  # Limitar tamanho

        # Campos opcionais
        saldo_inicial = data.get('saldo_inicial', 0.0)
        cor_hex = data.get('cor_hex', '#3B82F6')
        icone = data.get('icone', 'wallet')
        limite_credito = data.get('limite_credito')
        dia_fechamento = data.get('dia_fechamento')
        dia_vencimento = data.get('dia_vencimento')

        # Validar campos numéricos
        try:
            saldo_inicial = float(saldo_inicial) if saldo_inicial is not None else 0.0
        except (ValueError, TypeError):
            return jsonify({"status": "error", "message": "Saldo inicial deve ser um número válido"}), 400

        # Validar campos de cartão de crédito
        if tipo_conta == 'Cartão de Crédito':
            if limite_credito is not None:
                try:
                    limite_credito = float(limite_credito)
                except (ValueError, TypeError):
                    return jsonify({"status": "error", "message": "Limite de crédito deve ser um número válido"}), 400

            if dia_fechamento is not None:
                try:
                    dia_fechamento = int(dia_fechamento)
                    if not 1 <= dia_fechamento <= 31:
                        raise ValueError()
                except (ValueError, TypeError):
                    return jsonify({"status": "error", "message": "Dia de fechamento deve ser entre 1 e 31"}), 400

            if dia_vencimento is not None:
                try:
                    dia_vencimento = int(dia_vencimento)
                    if not 1 <= dia_vencimento <= 31:
                        raise ValueError()
                except (ValueError, TypeError):
                    return jsonify({"status": "error", "message": "Dia de vencimento deve ser entre 1 e 31"}), 400

        with db_engine.connect() as conn:
            # Verificar se já existe conta com mesmo nome
            check_sql = text("""
                SELECT id FROM Contas
                WHERE usuario_id = :uid AND nome_conta = :nome AND ativa = true
            """)
            existing = conn.execute(check_sql, {"uid": user_id, "nome": nome_conta}).fetchone()

            if existing:
                return jsonify({
                    "status": "error",
                    "message": "Já existe uma conta com este nome"
                }), 400

            # Inserir nova conta
            insert_sql = text("""
                INSERT INTO Contas (
                    usuario_id, nome_conta, tipo_conta, saldo_inicial,
                    cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento,
                    ativa, inclui_saldo_total, created_at
                ) VALUES (
                    :uid, :nome, :tipo, :saldo,
                    :cor, :icone, :limite, :fechamento, :vencimento,
                    true, true, CURRENT_TIMESTAMP
                )
            """)

            conn.execute(insert_sql, {
                "uid": user_id,
                "nome": nome_conta,
                "tipo": tipo_conta,
                "saldo": saldo_inicial,
                "cor": cor_hex,
                "icone": icone,
                "limite": limite_credito,
                "fechamento": dia_fechamento,
                "vencimento": dia_vencimento
            })
            conn.commit()

            # Buscar conta criada
            select_sql = text("""
                SELECT id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone,
                       limite_credito, dia_fechamento, dia_vencimento
                FROM Contas
                WHERE usuario_id = :uid AND nome_conta = :nome AND ativa = true
            """)
            conta = conn.execute(select_sql, {"uid": user_id, "nome": nome_conta}).fetchone()

            return jsonify({
                "status": "success",
                "message": "Conta criada com sucesso",
                "data": {
                    "id": conta.id,
                    "nome_conta": conta.nome_conta,
                    "tipo_conta": conta.tipo_conta,
                    "saldo_inicial": float(conta.saldo_inicial),
                    "cor_hex": conta.cor_hex,
                    "icone": conta.icone,
                    "limite_credito": float(conta.limite_credito) if conta.limite_credito else None,
                    "dia_fechamento": conta.dia_fechamento,
                    "dia_vencimento": conta.dia_vencimento
                }
            }), 201

    except Exception as e:
        print(f"[API] ❌ Erro ao criar conta: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao criar conta"
        }), 500


@api_bp.route('/accounts', methods=['POST'])
@token_required
def create_account(user_id):
    """POST /api/accounts - Cria nova conta bancária."""
    return _create_account_impl(user_id)


def _update_account_impl(user_id, account_id):
    """Lógica interna para atualizar conta (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.get_json()

        if not data:
            return jsonify({"status": "error", "message": "Dados não fornecidos"}), 400

        with db_engine.connect() as conn:
            # Verificar se conta existe e pertence ao usuário
            check_sql = text("""
                SELECT id, nome_conta FROM Contas
                WHERE id = :id AND usuario_id = :uid AND ativa = true
            """)
            conta = conn.execute(check_sql, {"id": account_id, "uid": user_id}).fetchone()

            if not conta:
                return jsonify({
                    "status": "error",
                    "message": "Conta não encontrada"
                }), 404

            # Preparar campos para atualização
            updates = []
            params = {"id": account_id, "uid": user_id}

            if 'nome_conta' in data:
                nome = data['nome_conta'].strip()[:100]
                if not nome:
                    return jsonify({"status": "error", "message": "Nome da conta não pode ser vazio"}), 400

                # Verificar duplicidade
                dup_sql = text("""
                    SELECT id FROM Contas
                    WHERE usuario_id = :uid AND nome_conta = :nome AND id != :id AND ativa = true
                """)
                dup = conn.execute(dup_sql, {"uid": user_id, "nome": nome, "id": account_id}).fetchone()
                if dup:
                    return jsonify({"status": "error", "message": "Já existe outra conta com este nome"}), 400

                updates.append("nome_conta = :nome")
                params["nome"] = nome

            if 'tipo_conta' in data:
                tipo = data['tipo_conta'].strip()
                if tipo not in TIPOS_CONTA_VALIDOS:
                    return jsonify({
                        "status": "error",
                        "message": f"Tipo de conta inválido. Valores permitidos: {', '.join(TIPOS_CONTA_VALIDOS)}"
                    }), 400
                updates.append("tipo_conta = :tipo")
                params["tipo"] = tipo

            if 'saldo_inicial' in data:
                try:
                    saldo = float(data['saldo_inicial'])
                    updates.append("saldo_inicial = :saldo")
                    params["saldo"] = saldo
                except (ValueError, TypeError):
                    return jsonify({"status": "error", "message": "Saldo inicial deve ser um número válido"}), 400

            if 'cor_hex' in data:
                updates.append("cor_hex = :cor")
                params["cor"] = data['cor_hex']

            if 'icone' in data:
                updates.append("icone = :icone")
                params["icone"] = data['icone']

            if 'limite_credito' in data:
                limite = data['limite_credito']
                if limite is not None:
                    try:
                        limite = float(limite)
                    except (ValueError, TypeError):
                        return jsonify({"status": "error", "message": "Limite de crédito deve ser um número válido"}), 400
                updates.append("limite_credito = :limite")
                params["limite"] = limite

            if 'dia_fechamento' in data:
                dia = data['dia_fechamento']
                if dia is not None:
                    try:
                        dia = int(dia)
                        if not 1 <= dia <= 31:
                            raise ValueError()
                    except (ValueError, TypeError):
                        return jsonify({"status": "error", "message": "Dia de fechamento deve ser entre 1 e 31"}), 400
                updates.append("dia_fechamento = :fechamento")
                params["fechamento"] = dia

            if 'dia_vencimento' in data:
                dia = data['dia_vencimento']
                if dia is not None:
                    try:
                        dia = int(dia)
                        if not 1 <= dia <= 31:
                            raise ValueError()
                    except (ValueError, TypeError):
                        return jsonify({"status": "error", "message": "Dia de vencimento deve ser entre 1 e 31"}), 400
                updates.append("dia_vencimento = :vencimento")
                params["vencimento"] = dia

            if not updates:
                return jsonify({"status": "error", "message": "Nenhum campo para atualizar"}), 400

            # Executar atualização
            update_sql = text(f"""
                UPDATE Contas SET {', '.join(updates)}
                WHERE id = :id AND usuario_id = :uid
            """)
            conn.execute(update_sql, params)
            conn.commit()

            # Buscar conta atualizada
            select_sql = text("""
                SELECT id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone,
                       limite_credito, dia_fechamento, dia_vencimento
                FROM Contas WHERE id = :id
            """)
            conta_atualizada = conn.execute(select_sql, {"id": account_id}).fetchone()

            return jsonify({
                "status": "success",
                "message": "Conta atualizada com sucesso",
                "data": {
                    "id": conta_atualizada.id,
                    "nome_conta": conta_atualizada.nome_conta,
                    "tipo_conta": conta_atualizada.tipo_conta,
                    "saldo_inicial": float(conta_atualizada.saldo_inicial),
                    "cor_hex": conta_atualizada.cor_hex,
                    "icone": conta_atualizada.icone,
                    "limite_credito": float(conta_atualizada.limite_credito) if conta_atualizada.limite_credito else None,
                    "dia_fechamento": conta_atualizada.dia_fechamento,
                    "dia_vencimento": conta_atualizada.dia_vencimento
                }
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao atualizar conta: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao atualizar conta"
        }), 500


@api_bp.route('/accounts/<int:account_id>', methods=['PUT'])
@token_required
def update_account(user_id, account_id):
    """PUT /api/accounts/<id> - Atualiza conta bancária."""
    return _update_account_impl(user_id, account_id)


def _delete_account_impl(user_id, account_id):
    """Lógica interna para remover conta (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            # Verificar se conta existe e pertence ao usuário
            check_sql = text("""
                SELECT id, nome_conta FROM Contas
                WHERE id = :id AND usuario_id = :uid AND ativa = true
            """)
            conta = conn.execute(check_sql, {"id": account_id, "uid": user_id}).fetchone()

            if not conta:
                return jsonify({
                    "status": "error",
                    "message": "Conta não encontrada"
                }), 404

            # Verificar se há transações vinculadas
            trans_sql = text("""
                SELECT COUNT(*) as total FROM Transacoes
                WHERE conta_id = :id
            """)
            result = conn.execute(trans_sql, {"id": account_id}).fetchone()

            if result.total > 0:
                return jsonify({
                    "status": "error",
                    "message": f"Não é possível excluir esta conta. Existem {result.total} transações vinculadas."
                }), 400

            # Soft delete: marcar como inativa
            delete_sql = text("""
                UPDATE Contas SET ativa = false
                WHERE id = :id AND usuario_id = :uid
            """)
            conn.execute(delete_sql, {"id": account_id, "uid": user_id})
            conn.commit()

            return jsonify({
                "status": "success",
                "message": "Conta removida com sucesso"
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao remover conta: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao remover conta"
        }), 500


@api_bp.route('/accounts/<int:account_id>', methods=['DELETE'])
@token_required
def delete_account(user_id, account_id):
    """DELETE /api/accounts/<id> - Remove conta bancária (soft delete)."""
    return _delete_account_impl(user_id, account_id)


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
    return _get_dashboard_summary_impl(user_id)


def _get_transacoes_recentes_impl(user_id):
    """Lógica interna para buscar transações recentes (sem decorator)."""
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


@api_bp.route('/transacoes/recentes', methods=['GET'])
@token_required
def get_transacoes_recentes(user_id):
    """GET /api/transacoes/recentes - Lista últimas 10 transações."""
    return _get_transacoes_recentes_impl(user_id)


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
    return _get_accounts_impl(user_id)


@api_bp.route('/contas', methods=['POST'])
@token_required
def create_conta(user_id):
    """POST /api/contas - Alias em português para criar conta."""
    return _create_account_impl(user_id)


@api_bp.route('/contas/<int:account_id>', methods=['PUT'])
@token_required
def update_conta(user_id, account_id):
    """PUT /api/contas/<id> - Alias em português para atualizar conta."""
    return _update_account_impl(user_id, account_id)


@api_bp.route('/contas/<int:account_id>', methods=['DELETE'])
@token_required
def delete_conta(user_id, account_id):
    """DELETE /api/contas/<id> - Alias em português para remover conta."""
    return _delete_account_impl(user_id, account_id)


# ============================================================
# ENDPOINTS EM INGLÊS (Aliases para compatibilidade com Frontend Angular)
# ============================================================

@api_bp.route('/dashboard/stats', methods=['GET'])
@token_required
def get_dashboard_stats(user_id):
    """
    GET /api/dashboard/stats

    Alias em inglês para /api/dashboard/summary.
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
    return _get_dashboard_summary_impl(user_id)


@api_bp.route('/transactions/recent', methods=['GET'])
@token_required
def get_transactions_recent(user_id):
    """
    GET /api/transactions/recent

    Alias em inglês para /api/transacoes/recentes.
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
    # Reutilizar a lógica do endpoint em português
    return _get_transacoes_recentes_impl(user_id)


@api_bp.route('/dashboard/recent', methods=['GET'])
@token_required
def get_dashboard_recent(user_id):
    """
    GET /api/dashboard/recent

    Alias alternativo para /api/transactions/recent.
    Lista as últimas 10 transações para o dashboard.

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
    # Reutilizar a lógica do endpoint de transações recentes
    return _get_transacoes_recentes_impl(user_id)


# ============================================================
# CRUD DE TRANSAÇÕES (POST, PUT, DELETE)
# ============================================================

@api_bp.route('/transactions', methods=['POST'])
@token_required
def create_transaction(user_id):
    """
    POST /api/transactions

    Cria uma nova transação.

    Headers:
        Authorization: Bearer <jwt_token>

    Body (JSON):
        {
            "descricao": "Supermercado",
            "valor": 150.50,
            "tipo": "Despesa",
            "data": "2025-12-11",
            "subcategoria_id": 15,
            "conta_id": 1,
            "observacoes": "Compras da semana" (opcional),
            "consolidada": true (opcional, padrão: true)
        }

    Response:
        {
            "status": "success",
            "message": "Transação criada com sucesso",
            "data": {
                "id": 1234,
                "descricao": "Supermercado",
                "valor": -150.50,
                "tipo": "Despesa",
                "data": "2025-12-11"
            }
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.json

        # Validar campos obrigatórios
        descricao = data.get('descricao', '').strip()
        valor = data.get('valor')
        tipo = data.get('tipo')
        data_transacao = data.get('data')
        subcategoria_id = data.get('subcategoria_id')
        conta_id = data.get('conta_id')

        # Campos opcionais
        observacoes = data.get('observacoes', '').strip() or None
        consolidada = data.get('consolidada', True)

        # Validações
        if not descricao:
            return jsonify({"status": "error", "message": "Campo 'descricao' é obrigatório"}), 400

        if valor is None:
            return jsonify({"status": "error", "message": "Campo 'valor' é obrigatório"}), 400

        try:
            valor = float(valor)
            if valor < 0:
                return jsonify({"status": "error", "message": "Valor deve ser positivo"}), 400
        except (ValueError, TypeError):
            return jsonify({"status": "error", "message": "Valor inválido"}), 400

        if tipo not in ['Receita', 'Despesa']:
            return jsonify({"status": "error", "message": "Tipo deve ser 'Receita' ou 'Despesa'"}), 400

        if not data_transacao:
            return jsonify({"status": "error", "message": "Campo 'data' é obrigatório"}), 400

        try:
            data_parsed = datetime.strptime(data_transacao, '%Y-%m-%d').date()
        except ValueError:
            return jsonify({"status": "error", "message": "Data deve estar no formato YYYY-MM-DD"}), 400

        if not subcategoria_id:
            return jsonify({"status": "error", "message": "Campo 'subcategoria_id' é obrigatório"}), 400

        if not conta_id:
            return jsonify({"status": "error", "message": "Campo 'conta_id' é obrigatório"}), 400

        with db_engine.connect() as conn:
            # Verificar se a conta pertence ao usuário
            sql_check_conta = text("SELECT id, tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid")
            conta = conn.execute(sql_check_conta, {"cid": conta_id, "uid": user_id}).fetchone()

            if not conta:
                return jsonify({"status": "error", "message": "Conta não encontrada ou não pertence ao usuário"}), 404

            # Verificar se a subcategoria existe
            sql_check_sub = text("""
                SELECT id FROM SubCategoria
                WHERE id = :scid AND (usuario_id IS NULL OR usuario_id = :uid)
            """)
            subcategoria = conn.execute(sql_check_sub, {"scid": subcategoria_id, "uid": user_id}).fetchone()

            if not subcategoria:
                return jsonify({"status": "error", "message": "Subcategoria não encontrada"}), 404

            # Determinar fatura_id se for cartão de crédito
            fatura_id = None
            if conta.tipo_conta == 'Cartão de Crédito' and tipo == 'Despesa':
                fatura_id = get_or_create_fatura(conn, conta_id, data_parsed, user_id)

            # Ajustar valor (despesa = negativo)
            valor_db = valor if tipo == 'Receita' else -valor

            # Inserir transação
            with conn.begin():
                sql_insert = text("""
                    INSERT INTO Transacoes
                    (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor,
                     tipo_transacao, data_transacao, observacoes, consolidada, origem)
                    VALUES (:uid, :cid, :scid, :fid, :desc, :val, :tipo, :data, :obs, :cons, 'api')
                    RETURNING id
                """)

                result = conn.execute(sql_insert, {
                    "uid": user_id,
                    "cid": conta_id,
                    "scid": subcategoria_id,
                    "fid": fatura_id,
                    "desc": descricao,
                    "val": valor_db,
                    "tipo": tipo,
                    "data": data_parsed,
                    "obs": observacoes,
                    "cons": consolidada
                })

                transaction_id = result.scalar_one()

            print(f"[API] ✅ Transação criada: ID {transaction_id} | {tipo} | R$ {valor} | {descricao}")

            return jsonify({
                "status": "success",
                "message": "Transação criada com sucesso",
                "data": {
                    "id": transaction_id,
                    "descricao": descricao,
                    "valor": valor_db,
                    "tipo": tipo,
                    "data": data_transacao
                }
            }), 201

    except Exception as e:
        print(f"[API] ❌ Erro ao criar transação: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao criar transação"
        }), 500


@api_bp.route('/transactions/<int:transaction_id>', methods=['PUT'])
@token_required
def update_transaction(user_id, transaction_id):
    """
    PUT /api/transactions/:id

    Atualiza uma transação existente.

    Headers:
        Authorization: Bearer <jwt_token>

    Body (JSON - todos os campos são opcionais):
        {
            "descricao": "Supermercado Extra",
            "valor": 180.00,
            "tipo": "Despesa",
            "data": "2025-12-12",
            "subcategoria_id": 16,
            "conta_id": 2,
            "observacoes": "Compras atualizadas",
            "consolidada": true
        }

    Response:
        {
            "status": "success",
            "message": "Transação atualizada com sucesso",
            "data": {
                "id": 1234,
                "descricao": "Supermercado Extra",
                "valor": -180.00,
                "tipo": "Despesa",
                "data": "2025-12-12"
            }
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.json

        if not data:
            return jsonify({"status": "error", "message": "Nenhum dado fornecido para atualização"}), 400

        with db_engine.connect() as conn:
            # Verificar se a transação existe e pertence ao usuário
            sql_check = text("""
                SELECT id, tipo_transacao, valor, conta_id
                FROM Transacoes
                WHERE id = :tid AND usuario_id = :uid
            """)
            transacao_atual = conn.execute(sql_check, {"tid": transaction_id, "uid": user_id}).fetchone()

            if not transacao_atual:
                return jsonify({"status": "error", "message": "Transação não encontrada"}), 404

            # Construir query de atualização dinâmica
            updates = []
            params = {"tid": transaction_id, "uid": user_id}

            if 'descricao' in data and data['descricao']:
                updates.append("descricao = :descricao")
                params["descricao"] = data['descricao'].strip()

            if 'valor' in data:
                try:
                    valor = float(data['valor'])
                    if valor < 0:
                        return jsonify({"status": "error", "message": "Valor deve ser positivo"}), 400

                    # Determinar se é receita ou despesa
                    tipo_atual = data.get('tipo', transacao_atual.tipo_transacao)
                    valor_db = valor if tipo_atual == 'Receita' else -valor

                    updates.append("valor = :valor")
                    params["valor"] = valor_db
                except (ValueError, TypeError):
                    return jsonify({"status": "error", "message": "Valor inválido"}), 400

            if 'tipo' in data:
                if data['tipo'] not in ['Receita', 'Despesa']:
                    return jsonify({"status": "error", "message": "Tipo deve ser 'Receita' ou 'Despesa'"}), 400
                updates.append("tipo_transacao = :tipo")
                params["tipo"] = data['tipo']

                # Se mudou o tipo, ajustar o sinal do valor
                if 'valor' not in data:
                    valor_atual = abs(float(transacao_atual.valor))
                    valor_db = valor_atual if data['tipo'] == 'Receita' else -valor_atual
                    updates.append("valor = :valor")
                    params["valor"] = valor_db

            if 'data' in data:
                try:
                    data_parsed = datetime.strptime(data['data'], '%Y-%m-%d').date()
                    updates.append("data_transacao = :data")
                    params["data"] = data_parsed
                except ValueError:
                    return jsonify({"status": "error", "message": "Data deve estar no formato YYYY-MM-DD"}), 400

            if 'subcategoria_id' in data:
                # Verificar se a subcategoria existe
                sql_check_sub = text("""
                    SELECT id FROM SubCategoria
                    WHERE id = :scid AND (usuario_id IS NULL OR usuario_id = :uid)
                """)
                subcategoria = conn.execute(sql_check_sub, {"scid": data['subcategoria_id'], "uid": user_id}).fetchone()

                if not subcategoria:
                    return jsonify({"status": "error", "message": "Subcategoria não encontrada"}), 404

                updates.append("subcategoria_id = :subcategoria_id")
                params["subcategoria_id"] = data['subcategoria_id']

            if 'conta_id' in data:
                # Verificar se a conta pertence ao usuário
                sql_check_conta = text("SELECT id FROM Contas WHERE id = :cid AND usuario_id = :uid")
                conta = conn.execute(sql_check_conta, {"cid": data['conta_id'], "uid": user_id}).fetchone()

                if not conta:
                    return jsonify({"status": "error", "message": "Conta não encontrada ou não pertence ao usuário"}), 404

                updates.append("conta_id = :conta_id")
                params["conta_id"] = data['conta_id']

            if 'observacoes' in data:
                updates.append("observacoes = :observacoes")
                params["observacoes"] = data['observacoes'].strip() if data['observacoes'] else None

            if 'consolidada' in data:
                updates.append("consolidada = :consolidada")
                params["consolidada"] = bool(data['consolidada'])

            if not updates:
                return jsonify({"status": "error", "message": "Nenhum campo válido para atualização"}), 400

            # Adicionar updated_at
            updates.append("updated_at = CURRENT_TIMESTAMP")

            # Executar atualização
            with conn.begin():
                sql_update = text(f"""
                    UPDATE Transacoes
                    SET {', '.join(updates)}
                    WHERE id = :tid AND usuario_id = :uid
                    RETURNING id, descricao, valor, tipo_transacao, data_transacao
                """)

                result = conn.execute(sql_update, params).fetchone()

            print(f"[API] ✅ Transação atualizada: ID {transaction_id}")

            return jsonify({
                "status": "success",
                "message": "Transação atualizada com sucesso",
                "data": {
                    "id": result.id,
                    "descricao": result.descricao,
                    "valor": float(result.valor),
                    "tipo": result.tipo_transacao,
                    "data": result.data_transacao.isoformat() if result.data_transacao else None
                }
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao atualizar transação: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao atualizar transação"
        }), 500


@api_bp.route('/transactions/<int:transaction_id>', methods=['DELETE'])
@token_required
def delete_transaction(user_id, transaction_id):
    """
    DELETE /api/transactions/:id

    Deleta uma transação.

    Headers:
        Authorization: Bearer <jwt_token>

    Response:
        {
            "status": "success",
            "message": "Transação deletada com sucesso"
        }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            # Verificar se a transação existe e pertence ao usuário
            sql_check = text("""
                SELECT id, descricao, tipo_transacao, transferencia_par_id
                FROM Transacoes
                WHERE id = :tid AND usuario_id = :uid
            """)
            transacao = conn.execute(sql_check, {"tid": transaction_id, "uid": user_id}).fetchone()

            if not transacao:
                return jsonify({"status": "error", "message": "Transação não encontrada"}), 404

            # Se for uma transferência, deletar o par também
            with conn.begin():
                if transacao.transferencia_par_id:
                    sql_delete_par = text("DELETE FROM Transacoes WHERE id = :par_id AND usuario_id = :uid")
                    conn.execute(sql_delete_par, {"par_id": transacao.transferencia_par_id, "uid": user_id})

                sql_delete = text("DELETE FROM Transacoes WHERE id = :tid AND usuario_id = :uid")
                conn.execute(sql_delete, {"tid": transaction_id, "uid": user_id})

            print(f"[API] ✅ Transação deletada: ID {transaction_id} | {transacao.tipo_transacao} | {transacao.descricao}")

            return jsonify({
                "status": "success",
                "message": "Transação deletada com sucesso"
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao deletar transação: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao deletar transação"
        }), 500


# ============================================================
# ENDPOINT DE CATEGORIAS
# ============================================================

def _get_categories_impl(user_id):
    """Lógica interna para buscar categorias (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        tipo_filtro = request.args.get('tipo')  # 'Receita' ou 'Despesa'

        # Construir filtro de grupo
        grupo_filter = ""
        if tipo_filtro == 'Receita':
            grupo_filter = "AND g.nome_grupo = 'Renda'"
        elif tipo_filtro == 'Despesa':
            grupo_filter = "AND g.nome_grupo != 'Renda'"

        with db_engine.connect() as conn:
            sql = text(f"""
                SELECT
                    g.nome_grupo as grupo,
                    m.id as macro_id,
                    m.nome_macro as macro_categoria,
                    s.id as subcategoria_id,
                    s.nome_sub as subcategoria_nome
                FROM SubCategoria s
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
                WHERE (s.usuario_id IS NULL OR s.usuario_id = :uid)
                    AND (m.usuario_id IS NULL OR m.usuario_id = :uid)
                    {grupo_filter}
                ORDER BY g.nome_grupo, m.ordem_macro, m.nome_macro, s.nome_sub
            """)

            result = conn.execute(sql, {"uid": user_id}).fetchall()

            # Agrupar por macro categoria
            categorias_dict = {}
            for row in result:
                key = (row.grupo, row.macro_id, row.macro_categoria)

                if key not in categorias_dict:
                    categorias_dict[key] = {
                        "grupo": row.grupo,
                        "macro_id": row.macro_id,
                        "macro_categoria": row.macro_categoria,
                        "subcategorias": []
                    }

                categorias_dict[key]["subcategorias"].append({
                    "id": row.subcategoria_id,
                    "nome": row.subcategoria_nome
                })

            categorias = list(categorias_dict.values())

            return jsonify({
                "status": "success",
                "data": categorias
            }), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar categorias: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao carregar categorias"
        }), 500


@api_bp.route('/categories', methods=['GET'])
@token_required
def get_categories(user_id):
    """GET /api/categories - Lista categorias disponíveis."""
    return _get_categories_impl(user_id)


# Alias em português
@api_bp.route('/categorias', methods=['GET'])
@token_required
def get_categorias(user_id):
    """
    GET /api/categorias

    Alias em português para /api/categories.
    """
    return _get_categories_impl(user_id)


# ============================================================
# CONTAS MENSAIS (AGENDAMENTOS)
# ============================================================

def _get_bills_impl(user_id):
    """Lógica interna para buscar contas mensais/agendamentos (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            sql = text("""
                SELECT
                    a.id,
                    a.descricao,
                    a.valor_previsto,
                    a.tipo_agendamento,
                    a.periodicidade,
                    a.dia_execucao,
                    a.mes_execucao,
                    a.notificar_antes_dias,
                    a.subcategoria_id,
                    s.nome_sub AS subcategoria_nome,
                    a.conta_id,
                    c.nome_conta AS conta_nome,
                    a.data_inicio
                FROM Agendamentos a
                LEFT JOIN SubCategoria s ON a.subcategoria_id = s.id
                LEFT JOIN Contas c ON a.conta_id = c.id
                WHERE a.usuario_id = :uid AND a.ativo = true
                ORDER BY a.periodicidade, a.dia_execucao, a.descricao
            """)
            rows = conn.execute(sql, {"uid": user_id}).fetchall()
            contas = [{
                "id": r.id,
                "descricao": r.descricao,
                "valor_previsto": float(r.valor_previsto) if r.valor_previsto is not None else None,
                "tipo_agendamento": r.tipo_agendamento,
                "periodicidade": r.periodicidade,
                "dia_execucao": r.dia_execucao,
                "mes_execucao": r.mes_execucao,
                "notificar_antes_dias": r.notificar_antes_dias,
                "subcategoria_id": r.subcategoria_id,
                "subcategoria_nome": r.subcategoria_nome,
                "conta_id": r.conta_id,
                "conta_nome": r.conta_nome,
                "data_inicio": r.data_inicio.isoformat() if r.data_inicio else None,
            } for r in rows]
            return jsonify({"status": "success", "data": contas}), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao buscar contas mensais (user_id={user_id}): {e}")
        import traceback
        traceback.print_exc()
        return jsonify({"status": "error", "message": f"Erro ao carregar contas mensais: {str(e)}"}), 500


def _create_bill_impl(user_id):
    """Lógica interna para criar conta mensal/agendamento (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.get_json()
        if not data:
            return jsonify({"status": "error", "message": "Dados não fornecidos"}), 400

        # Validar campos obrigatórios
        descricao = data.get('descricao', '').strip()
        if not descricao:
            return jsonify({"status": "error", "message": "Descrição é obrigatória"}), 400
        descricao = descricao[:255]  # Proteção XSS/tamanho

        tipo_agendamento = data.get('tipo_agendamento', '').strip()
        if not tipo_agendamento or tipo_agendamento not in TIPOS_AGENDAMENTO:
            return jsonify({
                "status": "error",
                "message": f"Tipo de agendamento inválido. Valores permitidos: {', '.join(TIPOS_AGENDAMENTO)}"
            }), 400

        periodicidade = data.get('periodicidade', '').strip()
        if not periodicidade or periodicidade not in PERIODICIDADES:
            return jsonify({
                "status": "error",
                "message": f"Periodicidade inválida. Valores permitidos: {', '.join(PERIODICIDADES)}"
            }), 400

        subcategoria_id = data.get('subcategoria_id')
        if not subcategoria_id:
            return jsonify({"status": "error", "message": "Subcategoria é obrigatória"}), 400

        conta_id = data.get('conta_id')
        if not conta_id:
            return jsonify({"status": "error", "message": "Conta bancária é obrigatória"}), 400

        # Validar dia de execução
        dia_execucao = data.get('dia_execucao')
        try:
            dia_execucao = int(dia_execucao)
            if not (1 <= dia_execucao <= 31):
                raise ValueError()
        except (TypeError, ValueError):
            return jsonify({"status": "error", "message": "Dia de execução deve ser entre 1 e 31"}), 400

        # valor_previsto obrigatório para FIXO
        valor_previsto = data.get('valor_previsto')
        if tipo_agendamento == 'FIXO':
            if valor_previsto is None:
                return jsonify({"status": "error", "message": "Valor previsto é obrigatório para contas fixas"}), 400
            try:
                valor_previsto = float(valor_previsto)
                if valor_previsto <= 0:
                    raise ValueError()
            except (TypeError, ValueError):
                return jsonify({"status": "error", "message": "Valor previsto deve ser um número positivo"}), 400
        elif valor_previsto is not None:
            try:
                valor_previsto = float(valor_previsto)
            except (TypeError, ValueError):
                valor_previsto = None

        # mes_execucao só válido para ANUAL
        mes_execucao = data.get('mes_execucao')
        if periodicidade == 'ANUAL' and mes_execucao is not None:
            try:
                mes_execucao = int(mes_execucao)
                if not (1 <= mes_execucao <= 12):
                    raise ValueError()
            except (TypeError, ValueError):
                return jsonify({"status": "error", "message": "Mês de execução deve ser entre 1 e 12"}), 400
        else:
            mes_execucao = None

        notificar_antes_dias = data.get('notificar_antes_dias', 3)
        try:
            notificar_antes_dias = int(notificar_antes_dias)
        except (TypeError, ValueError):
            notificar_antes_dias = 3

        data_inicio = data.get('data_inicio')
        if not data_inicio:
            data_inicio = date.today().isoformat()

        with db_engine.connect() as conn:
            # Verificar que conta_id pertence ao usuário
            check_sql = text("SELECT id FROM Contas WHERE id = :cid AND usuario_id = :uid AND ativa = true")
            conta_row = conn.execute(check_sql, {"cid": conta_id, "uid": user_id}).fetchone()
            if not conta_row:
                return jsonify({"status": "error", "message": "Conta bancária não encontrada"}), 404

            insert_sql = text("""
                INSERT INTO Agendamentos (
                    usuario_id, conta_id, subcategoria_id, descricao,
                    valor_previsto, tipo_agendamento, periodicidade,
                    data_inicio, dia_execucao, mes_execucao,
                    notificar_antes_dias, ativo, parcelas_executadas
                ) VALUES (
                    :uid, :conta_id, :sub_id, :descricao,
                    :valor_previsto, :tipo, :periodicidade,
                    :data_inicio, :dia_execucao, :mes_execucao,
                    :notificar_antes_dias, true, 0
                ) RETURNING id
            """)
            result = conn.execute(insert_sql, {
                "uid": user_id,
                "conta_id": conta_id,
                "sub_id": subcategoria_id,
                "descricao": descricao,
                "valor_previsto": valor_previsto,
                "tipo": tipo_agendamento,
                "periodicidade": periodicidade,
                "data_inicio": data_inicio,
                "dia_execucao": dia_execucao,
                "mes_execucao": mes_execucao,
                "notificar_antes_dias": notificar_antes_dias,
            })
            new_id = result.fetchone()[0]
            conn.commit()

            # Buscar registro criado com JOINs
            fetch_sql = text("""
                SELECT a.id, a.descricao, a.valor_previsto, a.tipo_agendamento,
                       a.periodicidade, a.dia_execucao, a.mes_execucao,
                       a.notificar_antes_dias, a.subcategoria_id, s.nome_sub AS subcategoria_nome,
                       a.conta_id, c.nome_conta AS conta_nome, a.data_inicio
                FROM Agendamentos a
                LEFT JOIN SubCategoria s ON a.subcategoria_id = s.id
                LEFT JOIN Contas c ON a.conta_id = c.id
                WHERE a.id = :id
            """)
            row = conn.execute(fetch_sql, {"id": new_id}).fetchone()
            bill = {
                "id": row.id,
                "descricao": row.descricao,
                "valor_previsto": float(row.valor_previsto) if row.valor_previsto is not None else None,
                "tipo_agendamento": row.tipo_agendamento,
                "periodicidade": row.periodicidade,
                "dia_execucao": row.dia_execucao,
                "mes_execucao": row.mes_execucao,
                "notificar_antes_dias": row.notificar_antes_dias,
                "subcategoria_id": row.subcategoria_id,
                "subcategoria_nome": row.subcategoria_nome,
                "conta_id": row.conta_id,
                "conta_nome": row.conta_nome,
                "data_inicio": row.data_inicio.isoformat() if row.data_inicio else None,
            }
            return jsonify({"status": "success", "data": bill}), 201

    except Exception as e:
        print(f"[API] ❌ Erro ao criar conta mensal: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({"status": "error", "message": "Erro ao criar conta mensal"}), 500


def _update_bill_impl(user_id, bill_id):
    """Lógica interna para atualizar conta mensal/agendamento (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.get_json()
        if not data:
            return jsonify({"status": "error", "message": "Dados não fornecidos"}), 400

        with db_engine.connect() as conn:
            # Verificar que o agendamento pertence ao usuário
            check_sql = text("SELECT id FROM Agendamentos WHERE id = :id AND usuario_id = :uid AND ativo = true")
            if not conn.execute(check_sql, {"id": bill_id, "uid": user_id}).fetchone():
                return jsonify({"status": "error", "message": "Conta mensal não encontrada"}), 404

            updates = []
            params = {"id": bill_id, "uid": user_id}

            if 'descricao' in data:
                desc = str(data['descricao']).strip()[:255]
                if not desc:
                    return jsonify({"status": "error", "message": "Descrição não pode ser vazia"}), 400
                updates.append("descricao = :descricao")
                params['descricao'] = desc

            if 'tipo_agendamento' in data:
                tipo = data['tipo_agendamento']
                if tipo not in TIPOS_AGENDAMENTO:
                    return jsonify({"status": "error", "message": f"Tipo inválido. Valores: {', '.join(TIPOS_AGENDAMENTO)}"}), 400
                updates.append("tipo_agendamento = :tipo_agendamento")
                params['tipo_agendamento'] = tipo

            if 'periodicidade' in data:
                per = data['periodicidade']
                if per not in PERIODICIDADES:
                    return jsonify({"status": "error", "message": f"Periodicidade inválida. Valores: {', '.join(PERIODICIDADES)}"}), 400
                updates.append("periodicidade = :periodicidade")
                params['periodicidade'] = per

            if 'dia_execucao' in data:
                try:
                    dia = int(data['dia_execucao'])
                    if not (1 <= dia <= 31):
                        raise ValueError()
                    updates.append("dia_execucao = :dia_execucao")
                    params['dia_execucao'] = dia
                except (TypeError, ValueError):
                    return jsonify({"status": "error", "message": "Dia de execução deve ser entre 1 e 31"}), 400

            if 'mes_execucao' in data:
                if data['mes_execucao'] is None:
                    updates.append("mes_execucao = :mes_execucao")
                    params['mes_execucao'] = None
                else:
                    try:
                        mes = int(data['mes_execucao'])
                        if not (1 <= mes <= 12):
                            raise ValueError()
                        updates.append("mes_execucao = :mes_execucao")
                        params['mes_execucao'] = mes
                    except (TypeError, ValueError):
                        return jsonify({"status": "error", "message": "Mês deve ser entre 1 e 12"}), 400

            if 'valor_previsto' in data:
                if data['valor_previsto'] is None:
                    updates.append("valor_previsto = :valor_previsto")
                    params['valor_previsto'] = None
                else:
                    try:
                        val = float(data['valor_previsto'])
                        updates.append("valor_previsto = :valor_previsto")
                        params['valor_previsto'] = val
                    except (TypeError, ValueError):
                        return jsonify({"status": "error", "message": "Valor previsto inválido"}), 400

            if 'subcategoria_id' in data:
                updates.append("subcategoria_id = :subcategoria_id")
                params['subcategoria_id'] = data['subcategoria_id']

            if 'conta_id' in data:
                chk = text("SELECT id FROM Contas WHERE id = :cid AND usuario_id = :uid AND ativa = true")
                if not conn.execute(chk, {"cid": data['conta_id'], "uid": user_id}).fetchone():
                    return jsonify({"status": "error", "message": "Conta bancária não encontrada"}), 404
                updates.append("conta_id = :conta_id")
                params['conta_id'] = data['conta_id']

            if 'notificar_antes_dias' in data:
                try:
                    updates.append("notificar_antes_dias = :notificar_antes_dias")
                    params['notificar_antes_dias'] = int(data['notificar_antes_dias'])
                except (TypeError, ValueError):
                    pass

            if 'data_inicio' in data:
                updates.append("data_inicio = :data_inicio")
                params['data_inicio'] = data['data_inicio']

            if not updates:
                return jsonify({"status": "error", "message": "Nenhum campo para atualizar"}), 400

            update_sql = text(f"UPDATE Agendamentos SET {', '.join(updates)} WHERE id = :id AND usuario_id = :uid")
            conn.execute(update_sql, params)
            conn.commit()

            # Buscar registro atualizado
            fetch_sql = text("""
                SELECT a.id, a.descricao, a.valor_previsto, a.tipo_agendamento,
                       a.periodicidade, a.dia_execucao, a.mes_execucao,
                       a.notificar_antes_dias, a.subcategoria_id, s.nome_sub AS subcategoria_nome,
                       a.conta_id, c.nome_conta AS conta_nome, a.data_inicio
                FROM Agendamentos a
                LEFT JOIN SubCategoria s ON a.subcategoria_id = s.id
                LEFT JOIN Contas c ON a.conta_id = c.id
                WHERE a.id = :id
            """)
            row = conn.execute(fetch_sql, {"id": bill_id}).fetchone()
            bill = {
                "id": row.id,
                "descricao": row.descricao,
                "valor_previsto": float(row.valor_previsto) if row.valor_previsto is not None else None,
                "tipo_agendamento": row.tipo_agendamento,
                "periodicidade": row.periodicidade,
                "dia_execucao": row.dia_execucao,
                "mes_execucao": row.mes_execucao,
                "notificar_antes_dias": row.notificar_antes_dias,
                "subcategoria_id": row.subcategoria_id,
                "subcategoria_nome": row.subcategoria_nome,
                "conta_id": row.conta_id,
                "conta_nome": row.conta_nome,
                "data_inicio": row.data_inicio.isoformat() if row.data_inicio else None,
            }
            return jsonify({"status": "success", "data": bill}), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao atualizar conta mensal: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({"status": "error", "message": "Erro ao atualizar conta mensal"}), 500


def _delete_bill_impl(user_id, bill_id):
    """Lógica interna para deletar (soft) conta mensal/agendamento (sem decorator)."""
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        with db_engine.connect() as conn:
            sql = text("""
                UPDATE Agendamentos
                SET ativo = false
                WHERE id = :id AND usuario_id = :uid AND ativo = true
            """)
            result = conn.execute(sql, {"id": bill_id, "uid": user_id})
            if result.rowcount == 0:
                return jsonify({"status": "error", "message": "Conta mensal não encontrada"}), 404
            conn.commit()
            return jsonify({"status": "success", "message": "Conta mensal removida com sucesso"}), 200

    except Exception as e:
        print(f"[API] ❌ Erro ao deletar conta mensal: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({"status": "error", "message": "Erro ao remover conta mensal"}), 500


# Rotas REST — contas mensais (bills)
@api_bp.route('/bills', methods=['GET'])
@token_required
def get_bills(user_id):
    """GET /api/bills - Lista contas mensais/agendamentos do usuário."""
    return _get_bills_impl(user_id)


@api_bp.route('/bills', methods=['POST'])
@token_required
def create_bill(user_id):
    """POST /api/bills - Cria nova conta mensal/agendamento."""
    return _create_bill_impl(user_id)


@api_bp.route('/bills/<int:bill_id>', methods=['PUT'])
@token_required
def update_bill(user_id, bill_id):
    """PUT /api/bills/<id> - Atualiza conta mensal/agendamento."""
    return _update_bill_impl(user_id, bill_id)


@api_bp.route('/bills/<int:bill_id>', methods=['DELETE'])
@token_required
def delete_bill(user_id, bill_id):
    """DELETE /api/bills/<id> - Remove (soft delete) conta mensal/agendamento."""
    return _delete_bill_impl(user_id, bill_id)


# Aliases em português
@api_bp.route('/contas-mensais', methods=['GET'])
@token_required
def get_contas_mensais(user_id):
    """GET /api/contas-mensais - Alias em português para /api/bills."""
    return _get_bills_impl(user_id)


@api_bp.route('/contas-mensais', methods=['POST'])
@token_required
def create_conta_mensal(user_id):
    """POST /api/contas-mensais - Alias em português para /api/bills."""
    return _create_bill_impl(user_id)


@api_bp.route('/contas-mensais/<int:bill_id>', methods=['PUT'])
@token_required
def update_conta_mensal(user_id, bill_id):
    """PUT /api/contas-mensais/<id> - Alias em português para /api/bills/<id>."""
    return _update_bill_impl(user_id, bill_id)


@api_bp.route('/contas-mensais/<int:bill_id>', methods=['DELETE'])
@token_required
def delete_conta_mensal(user_id, bill_id):
    """DELETE /api/contas-mensais/<id> - Alias em português para /api/bills/<id>."""
    return _delete_bill_impl(user_id, bill_id)
