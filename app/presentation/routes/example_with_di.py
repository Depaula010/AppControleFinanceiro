"""
Exemplo de rotas Flask usando Dependency Injection.

Este arquivo demonstra como usar DI em rotas Flask.
NÃO é para uso em produção - apenas exemplo educacional.
"""

from flask import Blueprint, jsonify, request
from app.core import inject_repositories, get_user_repository
from app.core.container import get_container

# Blueprint de exemplo
example_bp = Blueprint('example_di', __name__, url_prefix='/api/example')


# ============================================================================
# EXEMPLO 1: Usando decorator @inject_repositories
# ============================================================================

@example_bp.route('/users/<int:user_id>', methods=['GET'])
@inject_repositories('user', 'account')
def get_user_with_accounts(user_id, user_repository, account_repository):
    """
    Exemplo de rota com injeção automática de repositórios.

    Os repositórios são injetados automaticamente como kwargs.
    A sessão é gerenciada automaticamente (commit/rollback/close).

    GET /api/example/users/1
    """
    # Buscar usuário
    user = user_repository.get_by_id(user_id)
    if not user:
        return jsonify({"error": "Usuário não encontrado"}), 404

    # Buscar contas
    accounts = account_repository.get_by_user(user_id)

    return jsonify({
        "user": {
            "id": user.id,
            "nome": user.nome,
            "whatsapp": user.numero_whatsapp,
            "email": user.email,
        },
        "accounts": [
            {
                "id": acc.id,
                "nome": acc.nome_conta,
                "tipo": acc.tipo_conta,
            }
            for acc in accounts
        ]
    })


# ============================================================================
# EXEMPLO 2: Usando helpers get_*_repository()
# ============================================================================

@example_bp.route('/users/whatsapp/<whatsapp>', methods=['GET'])
def get_user_by_whatsapp(whatsapp):
    """
    Exemplo usando helper get_user_repository().

    GET /api/example/users/whatsapp/+5511999999999
    """
    user_repo = get_user_repository()

    user = user_repo.get_by_whatsapp(whatsapp)
    if not user:
        return jsonify({"error": "Usuário não encontrado"}), 404

    return jsonify({
        "id": user.id,
        "nome": user.nome,
        "whatsapp": user.numero_whatsapp,
        "email": user.email,
        "ativo": user.ativo,
    })


# ============================================================================
# EXEMPLO 3: Usando serviço (Service Layer)
# ============================================================================

@example_bp.route('/users/<int:user_id>/summary', methods=['GET'])
def get_user_summary(user_id):
    """
    Exemplo usando Service Layer com DI.

    GET /api/example/users/1/summary
    """
    # Obter container
    container = get_container()

    # Obter serviço (com repositórios injetados automaticamente)
    user_service = container.user_service()

    try:
        summary = user_service.get_user_summary(user_id)
        return jsonify(summary)
    except ValueError as e:
        return jsonify({"error": str(e)}), 404


# ============================================================================
# EXEMPLO 4: Criar usuário (POST)
# ============================================================================

@example_bp.route('/users', methods=['POST'])
def create_user():
    """
    Exemplo de criação usando Service Layer.

    POST /api/example/users
    Body: {
        "nome": "João Silva",
        "whatsapp": "+5511999999999",
        "email": "joao@example.com"
    }
    """
    data = request.get_json()

    # Validação básica
    if not data or 'nome' not in data or 'whatsapp' not in data:
        return jsonify({"error": "nome e whatsapp são obrigatórios"}), 400

    # Obter serviço
    container = get_container()
    user_service = container.user_service()

    try:
        # Criar usuário
        user = user_service.register_user(
            nome=data['nome'],
            numero_whatsapp=data['whatsapp'],
            email=data.get('email'),
            fuso_horario=data.get('fuso_horario', 'America/Sao_Paulo'),
        )

        return jsonify({
            "id": user.id,
            "nome": user.nome,
            "whatsapp": user.numero_whatsapp,
            "email": user.email,
        }), 201

    except ValueError as e:
        return jsonify({"error": str(e)}), 400


# ============================================================================
# EXEMPLO 5: Atualizar email
# ============================================================================

@example_bp.route('/users/<int:user_id>/email', methods=['PUT'])
def update_user_email(user_id):
    """
    Exemplo de atualização usando Service Layer.

    PUT /api/example/users/1/email
    Body: {"email": "novo@example.com"}
    """
    data = request.get_json()

    if not data or 'email' not in data:
        return jsonify({"error": "email é obrigatório"}), 400

    container = get_container()
    user_service = container.user_service()

    try:
        success = user_service.update_user_email(user_id, data['email'])

        if success:
            return jsonify({"message": "Email atualizado com sucesso"})
        else:
            return jsonify({"error": "Usuário não encontrado"}), 404

    except ValueError as e:
        return jsonify({"error": str(e)}), 400


# ============================================================================
# EXEMPLO 6: Listar transações com filtros
# ============================================================================

@example_bp.route('/users/<int:user_id>/transactions', methods=['GET'])
@inject_repositories('transaction')
def get_user_transactions(user_id, transaction_repository):
    """
    Exemplo com query parameters.

    GET /api/example/users/1/transactions?limit=10&skip=0
    """
    # Query parameters
    limit = request.args.get('limit', 100, type=int)
    skip = request.args.get('skip', 0, type=int)

    # Buscar transações
    transactions = transaction_repository.get_by_user(
        usuario_id=user_id,
        skip=skip,
        limit=limit
    )

    return jsonify({
        "total": len(transactions),
        "skip": skip,
        "limit": limit,
        "transactions": [
            {
                "id": t.id,
                "descricao": t.descricao,
                "valor": float(t.valor),
                "data": t.data_transacao.isoformat(),
                "tipo": t.tipo_transacao,
            }
            for t in transactions
        ]
    })


# ============================================================================
# EXEMPLO 7: Calcular resumo financeiro
# ============================================================================

@example_bp.route('/users/<int:user_id>/financial-summary', methods=['GET'])
@inject_repositories('transaction')
def get_financial_summary(user_id, transaction_repository):
    """
    Exemplo de cálculos agregados.

    GET /api/example/users/1/financial-summary?month=2025-12
    """
    from datetime import date

    # Parâmetros
    month_str = request.args.get('month')  # Formato: YYYY-MM

    if month_str:
        year, month = map(int, month_str.split('-'))
    else:
        hoje = date.today()
        year, month = hoje.year, hoje.month

    # Calcular início e fim do mês
    inicio_mes = date(year, month, 1)

    if month == 12:
        fim_mes = date(year + 1, 1, 1)
    else:
        fim_mes = date(year, month + 1, 1)

    from datetime import timedelta
    fim_mes = fim_mes - timedelta(days=1)

    # Calcular totais
    receitas = transaction_repository.calculate_total_income(
        usuario_id=user_id,
        data_inicio=inicio_mes,
        data_fim=fim_mes
    )

    despesas = transaction_repository.calculate_total_expenses(
        usuario_id=user_id,
        data_inicio=inicio_mes,
        data_fim=fim_mes
    )

    return jsonify({
        "periodo": {
            "inicio": inicio_mes.isoformat(),
            "fim": fim_mes.isoformat(),
        },
        "receitas": float(receitas),
        "despesas": float(despesas),
        "saldo": float(receitas - despesas),
    })


# ============================================================================
# Registrar blueprint (em app/__init__.py ou similar)
# ============================================================================
# from app.presentation.routes.example_with_di import example_bp
# app.register_blueprint(example_bp)
