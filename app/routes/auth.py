# app/routes/auth.py
"""
Blueprint de Autenticação para Login Web
Endpoints: /auth/register, /auth/login
"""
import secrets
from datetime import datetime, timedelta
from flask import Blueprint, jsonify, request
from sqlalchemy import text
from werkzeug.security import generate_password_hash, check_password_hash
import jwt

from app import db_engine
from app.config import API_SECRET_KEY
from app.services.encryption_service import encryption_service

auth_bp = Blueprint('auth', __name__, url_prefix='/auth')


def generate_jwt_token(user_id, expires_in_hours=24):
    """
    Gera um token JWT com validade de 24 horas.

    Args:
        user_id: ID do usuário
        expires_in_hours: Tempo de expiração em horas

    Returns:
        Token JWT (string)
    """
    payload = {
        'user_id': user_id,
        'exp': datetime.utcnow() + timedelta(hours=expires_in_hours),
        'iat': datetime.utcnow()
    }

    # Usar API_SECRET_KEY como chave de assinatura
    token = jwt.encode(payload, API_SECRET_KEY, algorithm='HS256')
    return token


def verify_jwt_token(token):
    """
    Verifica e decodifica um token JWT.

    Args:
        token: Token JWT

    Returns:
        dict com payload ou None se inválido
    """
    try:
        payload = jwt.decode(token, API_SECRET_KEY, algorithms=['HS256'])
        return payload
    except jwt.ExpiredSignatureError:
        return None
    except jwt.InvalidTokenError:
        return None


@auth_bp.route('/register', methods=['POST'])
def register():
    """
    POST /auth/register

    Payload esperado:
    {
        "nome": "João Silva",
        "whatsapp": "5511999999999",
        "password": "SenhaSegura123",
        "dia_vencimento": 10,
        "dia_fechamento": 5
    }

    Retorno de sucesso:
    {
        "status": "success",
        "message": "Usuário cadastrado com sucesso",
        "user_id": 123
    }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.json

        # Validar campos obrigatórios
        nome = data.get('nome', '').strip()
        whatsapp = data.get('whatsapp', '').strip()
        password = data.get('password', '').strip()
        dia_vencimento = data.get('dia_vencimento')
        dia_fechamento = data.get('dia_fechamento')

        if not nome or not whatsapp or not password:
            return jsonify({
                "status": "error",
                "message": "Campos obrigatórios: nome, whatsapp, password"
            }), 400

        # Validar senha (mínimo 6 caracteres)
        if len(password) < 6:
            return jsonify({
                "status": "error",
                "message": "Senha deve ter no mínimo 6 caracteres"
            }), 400

        # Validar dias de vencimento/fechamento
        if dia_vencimento is None or dia_fechamento is None:
            return jsonify({
                "status": "error",
                "message": "Campos obrigatórios: dia_vencimento, dia_fechamento"
            }), 400

        try:
            dia_vencimento = int(dia_vencimento)
            dia_fechamento = int(dia_fechamento)

            if dia_vencimento < 1 or dia_vencimento > 31:
                raise ValueError("dia_vencimento deve estar entre 1 e 31")
            if dia_fechamento < 1 or dia_fechamento > 31:
                raise ValueError("dia_fechamento deve estar entre 1 e 31")
        except (ValueError, TypeError) as e:
            return jsonify({
                "status": "error",
                "message": f"Dias inválidos: {str(e)}"
            }), 400

        # Limpar número do WhatsApp (remover formatação)
        whatsapp_limpo = ''.join(filter(str.isdigit, whatsapp))

        with db_engine.connect() as conn:
            # Verificar se WhatsApp já existe
            sql_check = text("SELECT id FROM Usuarios WHERE numero_whatsapp = :whatsapp")
            existing_user = conn.execute(sql_check, {"whatsapp": whatsapp_limpo}).fetchone()

            if existing_user:
                return jsonify({
                    "status": "error",
                    "message": "WhatsApp já cadastrado"
                }), 409  # Conflict

            # Gerar hash da senha
            senha_hash = generate_password_hash(password, method='pbkdf2:sha256')

            # Gerar API key criptografada (para uso no bot)
            nova_api_key = secrets.token_urlsafe(32)
            try:
                api_key_encrypted = encryption_service.encrypt(nova_api_key)
            except Exception as e:
                print(f"[AUTH] ⚠️  Erro ao criptografar API key: {e}")
                api_key_encrypted = nova_api_key  # Fallback

            # Iniciar transação
            with conn.begin():
                # 1. Inserir usuário
                sql_user = text("""
                    INSERT INTO Usuarios (nome, numero_whatsapp, senha_hash, api_key_automate)
                    VALUES (:nome, :whatsapp, :senha_hash, :api_key)
                    RETURNING id;
                """)

                result = conn.execute(sql_user, {
                    "nome": nome,
                    "whatsapp": whatsapp_limpo,
                    "senha_hash": senha_hash,
                    "api_key": api_key_encrypted
                })

                user_id = result.scalar_one()

                # 2. Criar contas padrão (igual ao setup_user_data do finance_service)
                sql_contas = text("""
                    INSERT INTO Contas (usuario_id, nome_conta, tipo_conta, dia_vencimento, dia_fechamento)
                    VALUES
                        (:uid, 'Carteira', 'Dinheiro', NULL, NULL),
                        (:uid, 'Conta Corrente', 'Conta Corrente', NULL, NULL),
                        (:uid, 'Cartão de Crédito', 'Cartão de Crédito', :dia_venc, :dia_fech);
                """)

                conn.execute(sql_contas, {
                    "uid": user_id,
                    "dia_venc": dia_vencimento,
                    "dia_fech": dia_fechamento
                })

                print(f"[AUTH] ✅ Novo usuário registrado: {nome} (ID: {user_id}, WhatsApp: {whatsapp_limpo})")

        return jsonify({
            "status": "success",
            "message": "Usuário cadastrado com sucesso",
            "user_id": user_id
        }), 201

    except Exception as e:
        print(f"[AUTH] ❌ Erro ao registrar usuário: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao cadastrar usuário. Tente novamente."
        }), 500


@auth_bp.route('/login', methods=['POST'])
def login():
    """
    POST /auth/login

    Payload esperado:
    {
        "whatsapp": "5511999999999",
        "password": "SenhaSegura123"
    }

    Retorno de sucesso:
    {
        "status": "success",
        "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
        "user": {
            "id": 123,
            "nome": "João Silva",
            "whatsapp": "5511999999999"
        }
    }
    """
    if not db_engine:
        return jsonify({"status": "error", "message": "Banco de dados não configurado"}), 503

    try:
        data = request.json

        # Validar campos obrigatórios
        whatsapp = data.get('whatsapp', '').strip()
        password = data.get('password', '').strip()

        if not whatsapp or not password:
            return jsonify({
                "status": "error",
                "message": "Campos obrigatórios: whatsapp, password"
            }), 400

        # Limpar número do WhatsApp
        whatsapp_limpo = ''.join(filter(str.isdigit, whatsapp))

        with db_engine.connect() as conn:
            # Buscar usuário pelo WhatsApp
            sql_user = text("""
                SELECT id, nome, numero_whatsapp, senha_hash
                FROM Usuarios
                WHERE numero_whatsapp = :whatsapp
            """)

            user = conn.execute(sql_user, {"whatsapp": whatsapp_limpo}).fetchone()

            if not user:
                return jsonify({
                    "status": "error",
                    "message": "WhatsApp não cadastrado"
                }), 404

            # Verificar se usuário tem senha cadastrada
            if not user.senha_hash:
                return jsonify({
                    "status": "error",
                    "message": "Usuário sem senha cadastrada. Cadastre-se pelo site."
                }), 401

            # Verificar senha
            if not check_password_hash(user.senha_hash, password):
                return jsonify({
                    "status": "error",
                    "message": "Senha incorreta"
                }), 401

            # Gerar token JWT
            token = generate_jwt_token(user.id, expires_in_hours=24)

            print(f"[AUTH] ✅ Login bem-sucedido: {user.nome} (ID: {user.id})")

            return jsonify({
                "status": "success",
                "token": token,
                "user": {
                    "id": user.id,
                    "nome": user.nome,
                    "whatsapp": user.numero_whatsapp
                }
            }), 200

    except Exception as e:
        print(f"[AUTH] ❌ Erro ao fazer login: {e}")
        import traceback
        traceback.print_exc()

        return jsonify({
            "status": "error",
            "message": "Erro ao fazer login. Tente novamente."
        }), 500


@auth_bp.route('/verify', methods=['POST'])
def verify_token():
    """
    POST /auth/verify

    Verifica se um token JWT é válido.

    Payload esperado:
    {
        "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    }

    Retorno de sucesso:
    {
        "status": "success",
        "valid": true,
        "user_id": 123
    }
    """
    try:
        data = request.json
        token = data.get('token', '').strip()

        if not token:
            return jsonify({
                "status": "error",
                "message": "Token não fornecido"
            }), 400

        payload = verify_jwt_token(token)

        if not payload:
            return jsonify({
                "status": "error",
                "message": "Token inválido ou expirado"
            }), 401

        return jsonify({
            "status": "success",
            "valid": True,
            "user_id": payload['user_id']
        }), 200

    except Exception as e:
        print(f"[AUTH] ❌ Erro ao verificar token: {e}")

        return jsonify({
            "status": "error",
            "message": "Erro ao verificar token"
        }), 500
