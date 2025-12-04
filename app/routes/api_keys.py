# app/routes/api_keys.py
"""
Rotas para gerenciamento de chaves de API por usuário (SaaS).
Permite CRUD completo de ChavesApiUsuario e PreferenciasChaveApi.
"""

from flask import Blueprint, jsonify, request
from sqlalchemy import text
from app import db_engine
from app.config import API_SECRET_KEY
from app.services.encryption_service import encryption_service
from datetime import datetime

# Blueprint para rotas de API Keys
api_keys_bp = Blueprint('api_keys', __name__, url_prefix='/api-keys')


def require_api_key(f):
    """Decorator para exigir autenticação via API key"""
    from functools import wraps

    @wraps(f)
    def decorated_function(*args, **kwargs):
        api_key = request.headers.get('X-API-KEY')
        if not api_key or api_key != API_SECRET_KEY:
            return jsonify({
                "erro": "Não autorizado",
                "mensagem": "API key inválida ou ausente"
            }), 401
        return f(*args, **kwargs)
    return decorated_function


# ============================================================================
# CRUD - ChavesApiUsuario
# ============================================================================

@api_keys_bp.route('/usuario/<int:usuario_id>', methods=['POST'])
@require_api_key
def cadastrar_chave_api(usuario_id):
    """
    Cadastra nova chave de API para um usuário.

    Body JSON:
    {
        "provedor": "gemini" | "weather" | "openroute",
        "chave_api": "sua-chave-aqui"
    }
    """
    try:
        data = request.get_json()
        provedor = data.get('provedor')
        chave_api = data.get('chave_api')

        # Validações
        if not provedor or not chave_api:
            return jsonify({
                "erro": "Dados incompletos",
                "mensagem": "Provedor e chave_api são obrigatórios"
            }), 400

        provedores_validos = ['gemini', 'weather', 'openroute']
        if provedor not in provedores_validos:
            return jsonify({
                "erro": "Provedor inválido",
                "mensagem": f"Provedor deve ser um de: {', '.join(provedores_validos)}"
            }), 400

        # Criptografar chave
        chave_criptografada = encryption_service.encrypt(chave_api)

        # Inserir no banco
        sql = text("""
            INSERT INTO ChavesApiUsuario
                (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
            VALUES
                (:usuario_id, :provedor, :chave_criptografada, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            RETURNING id
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql, {
                "usuario_id": usuario_id,
                "provedor": provedor,
                "chave_criptografada": chave_criptografada
            })
            chave_id = result.fetchone()[0]
            conn.commit()

        print(f"[API-KEYS] ✅ Chave {provedor} cadastrada para usuário {usuario_id} (ID: {chave_id})")

        return jsonify({
            "mensagem": "Chave cadastrada com sucesso",
            "id": chave_id,
            "provedor": provedor,
            "usuario_id": usuario_id
        }), 201

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao cadastrar chave: {e}")
        return jsonify({
            "erro": "Erro ao cadastrar chave",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/usuario/<int:usuario_id>', methods=['GET'])
@require_api_key
def listar_chaves_usuario(usuario_id):
    """
    Lista todas as chaves de API de um usuário.

    Retorna chaves SEM descriptografar (apenas metadata).
    """
    try:
        sql = text("""
            SELECT
                id,
                provedor,
                ativo,
                ultimo_uso_em,
                criado_em,
                atualizado_em
            FROM ChavesApiUsuario
            WHERE usuario_id = :usuario_id
            ORDER BY provedor, criado_em DESC
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql, {"usuario_id": usuario_id})
            chaves = []

            for row in result:
                chaves.append({
                    "id": row.id,
                    "provedor": row.provedor,
                    "ativo": row.ativo,
                    "ultimo_uso_em": row.ultimo_uso_em.isoformat() if row.ultimo_uso_em else None,
                    "criado_em": row.criado_em.isoformat() if row.criado_em else None,
                    "atualizado_em": row.atualizado_em.isoformat() if row.atualizado_em else None
                })

        return jsonify({
            "usuario_id": usuario_id,
            "total": len(chaves),
            "chaves": chaves
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao listar chaves: {e}")
        return jsonify({
            "erro": "Erro ao listar chaves",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/chave/<int:chave_id>', methods=['PUT'])
@require_api_key
def atualizar_chave_api(chave_id):
    """
    Atualiza uma chave de API existente.

    Body JSON:
    {
        "chave_api": "nova-chave-aqui",  # Opcional
        "ativo": true/false               # Opcional
    }
    """
    try:
        data = request.get_json()
        nova_chave = data.get('chave_api')
        ativo = data.get('ativo')

        if not nova_chave and ativo is None:
            return jsonify({
                "erro": "Dados incompletos",
                "mensagem": "Forneça pelo menos 'chave_api' ou 'ativo'"
            }), 400

        # Montar SQL dinâmico
        updates = []
        params = {"chave_id": chave_id}

        if nova_chave:
            chave_criptografada = encryption_service.encrypt(nova_chave)
            updates.append("chave_api_criptografada = :chave_criptografada")
            params["chave_criptografada"] = chave_criptografada

        if ativo is not None:
            updates.append("ativo = :ativo")
            params["ativo"] = ativo

        updates.append("atualizado_em = CURRENT_TIMESTAMP")

        sql = text(f"""
            UPDATE ChavesApiUsuario
            SET {', '.join(updates)}
            WHERE id = :chave_id
            RETURNING provedor, usuario_id
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql, params)
            row = result.fetchone()

            if not row:
                conn.rollback()
                return jsonify({
                    "erro": "Chave não encontrada",
                    "mensagem": f"Nenhuma chave com ID {chave_id}"
                }), 404

            conn.commit()

            provedor = row.provedor
            usuario_id = row.usuario_id

        print(f"[API-KEYS] ✅ Chave {chave_id} ({provedor}) atualizada para usuário {usuario_id}")

        return jsonify({
            "mensagem": "Chave atualizada com sucesso",
            "id": chave_id,
            "provedor": provedor,
            "usuario_id": usuario_id
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao atualizar chave: {e}")
        return jsonify({
            "erro": "Erro ao atualizar chave",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/chave/<int:chave_id>', methods=['DELETE'])
@require_api_key
def deletar_chave_api(chave_id):
    """
    Deleta (desativa) uma chave de API.

    Na verdade marca como ativo=FALSE (soft delete).
    """
    try:
        sql = text("""
            UPDATE ChavesApiUsuario
            SET ativo = FALSE, atualizado_em = CURRENT_TIMESTAMP
            WHERE id = :chave_id
            RETURNING provedor, usuario_id
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql, {"chave_id": chave_id})
            row = result.fetchone()

            if not row:
                conn.rollback()
                return jsonify({
                    "erro": "Chave não encontrada",
                    "mensagem": f"Nenhuma chave com ID {chave_id}"
                }), 404

            conn.commit()

            provedor = row.provedor
            usuario_id = row.usuario_id

        print(f"[API-KEYS] ✅ Chave {chave_id} ({provedor}) desativada para usuário {usuario_id}")

        return jsonify({
            "mensagem": "Chave desativada com sucesso",
            "id": chave_id,
            "provedor": provedor
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao deletar chave: {e}")
        return jsonify({
            "erro": "Erro ao deletar chave",
            "mensagem": str(e)
        }), 500


# ============================================================================
# CRUD - PreferenciasChaveApi
# ============================================================================

@api_keys_bp.route('/preferencias/<int:usuario_id>', methods=['POST'])
@require_api_key
def configurar_preferencia(usuario_id):
    """
    Configura preferência de chave de API para um provedor.

    Body JSON:
    {
        "provedor": "gemini" | "weather" | "openroute",
        "usar_chave_propria": true | false
    }

    IMPORTANTE:
    - true = Usa chave própria do usuário (SEM CUSTO)
    - false = Usa chave do sistema (COM CUSTO)
    """
    try:
        data = request.get_json()
        provedor = data.get('provedor')
        usar_chave_propria = data.get('usar_chave_propria')

        # Validações
        if not provedor or usar_chave_propria is None:
            return jsonify({
                "erro": "Dados incompletos",
                "mensagem": "Provedor e usar_chave_propria são obrigatórios"
            }), 400

        provedores_validos = ['gemini', 'weather', 'openroute']
        if provedor not in provedores_validos:
            return jsonify({
                "erro": "Provedor inválido",
                "mensagem": f"Provedor deve ser um de: {', '.join(provedores_validos)}"
            }), 400

        # Inserir ou atualizar preferência
        sql = text("""
            INSERT INTO PreferenciasChaveApi
                (usuario_id, provedor, usar_chave_propria, atualizado_em)
            VALUES
                (:usuario_id, :provedor, :usar_chave_propria, CURRENT_TIMESTAMP)
            ON CONFLICT (usuario_id, provedor)
            DO UPDATE SET
                usar_chave_propria = EXCLUDED.usar_chave_propria,
                atualizado_em = CURRENT_TIMESTAMP
            RETURNING id
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql, {
                "usuario_id": usuario_id,
                "provedor": provedor,
                "usar_chave_propria": usar_chave_propria
            })
            pref_id = result.fetchone()[0]
            conn.commit()

        tipo = "própria (grátis)" if usar_chave_propria else "sistema (cobrado)"
        print(f"[API-KEYS] ✅ Preferência de {provedor} configurada para usuário {usuario_id}: {tipo}")

        return jsonify({
            "mensagem": "Preferência configurada com sucesso",
            "id": pref_id,
            "usuario_id": usuario_id,
            "provedor": provedor,
            "usar_chave_propria": usar_chave_propria,
            "tipo": tipo
        }), 201

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao configurar preferência: {e}")
        return jsonify({
            "erro": "Erro ao configurar preferência",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/preferencias/<int:usuario_id>', methods=['GET'])
@require_api_key
def listar_preferencias(usuario_id):
    """
    Lista todas as preferências de chaves de API de um usuário.
    """
    try:
        sql = text("""
            SELECT
                id,
                provedor,
                usar_chave_propria,
                atualizado_em
            FROM PreferenciasChaveApi
            WHERE usuario_id = :usuario_id
            ORDER BY provedor
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql, {"usuario_id": usuario_id})
            preferencias = []

            for row in result:
                tipo = "própria (grátis)" if row.usar_chave_propria else "sistema (cobrado)"
                preferencias.append({
                    "id": row.id,
                    "provedor": row.provedor,
                    "usar_chave_propria": row.usar_chave_propria,
                    "tipo": tipo,
                    "atualizado_em": row.atualizado_em.isoformat() if row.atualizado_em else None
                })

        return jsonify({
            "usuario_id": usuario_id,
            "total": len(preferencias),
            "preferencias": preferencias
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao listar preferências: {e}")
        return jsonify({
            "erro": "Erro ao listar preferências",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/preferencias/<int:usuario_id>/<provedor>', methods=['DELETE'])
@require_api_key
def deletar_preferencia(usuario_id, provedor):
    """
    Remove preferência de um provedor específico.

    Útil se o usuário quiser resetar a configuração.
    """
    try:
        sql = text("""
            DELETE FROM PreferenciasChaveApi
            WHERE usuario_id = :usuario_id AND provedor = :provedor
            RETURNING id
        """)

        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql, {
                "usuario_id": usuario_id,
                "provedor": provedor
            })
            row = result.fetchone()

            if not row:
                conn.rollback()
                return jsonify({
                    "erro": "Preferência não encontrada",
                    "mensagem": f"Nenhuma preferência para {provedor}"
                }), 404

            conn.commit()

        print(f"[API-KEYS] ✅ Preferência de {provedor} removida para usuário {usuario_id}")

        return jsonify({
            "mensagem": "Preferência removida com sucesso",
            "usuario_id": usuario_id,
            "provedor": provedor
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao deletar preferência: {e}")
        return jsonify({
            "erro": "Erro ao deletar preferência",
            "mensagem": str(e)
        }), 500


# ============================================================================
# Consultas e Auditoria
# ============================================================================

@api_keys_bp.route('/uso/<int:usuario_id>', methods=['GET'])
@require_api_key
def consultar_uso_mensal(usuario_id):
    """
    Consulta uso mensal de APIs por usuário.

    Query params:
    - mes_ano: "YYYY-MM" (opcional, padrão: mês atual)
    - provedor: "gemini" | "weather" | "openroute" (opcional, padrão: todos)
    """
    try:
        # Parâmetros opcionais
        mes_ano = request.args.get('mes_ano')
        provedor = request.args.get('provedor')

        if not mes_ano:
            from datetime import datetime
            mes_ano = datetime.now().strftime('%Y-%m')

        # Montar SQL dinâmico
        where_clauses = ["usuario_id = :usuario_id", "mes_ano = :mes_ano"]
        params = {"usuario_id": usuario_id, "mes_ano": mes_ano}

        if provedor:
            where_clauses.append("provedor = :provedor")
            params["provedor"] = provedor

        sql = text(f"""
            SELECT
                provedor,
                tipo_chave,
                SUM(quantidade_chamadas) as total_chamadas,
                mes_ano,
                MAX(atualizado_em) as ultima_atualizacao
            FROM RastreamentoUsoApi
            WHERE {' AND '.join(where_clauses)}
            GROUP BY provedor, tipo_chave, mes_ano
            ORDER BY provedor, tipo_chave
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql, params)
            uso = []

            for row in result:
                uso.append({
                    "provedor": row.provedor,
                    "tipo_chave": row.tipo_chave,
                    "total_chamadas": row.total_chamadas,
                    "mes_ano": row.mes_ano,
                    "ultima_atualizacao": row.ultima_atualizacao.isoformat() if row.ultima_atualizacao else None
                })

        # Calcular totais
        total_proprio = sum(u['total_chamadas'] for u in uso if u['tipo_chave'] == 'propria')
        total_sistema = sum(u['total_chamadas'] for u in uso if u['tipo_chave'] == 'sistema')

        return jsonify({
            "usuario_id": usuario_id,
            "mes_ano": mes_ano,
            "resumo": {
                "total_chamadas": total_proprio + total_sistema,
                "chamadas_chave_propria": total_proprio,
                "chamadas_chave_sistema": total_sistema
            },
            "detalhes": uso
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao consultar uso: {e}")
        return jsonify({
            "erro": "Erro ao consultar uso",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/logs/<int:usuario_id>', methods=['GET'])
@require_api_key
def consultar_logs_acesso(usuario_id):
    """
    Consulta logs de acesso às chaves de API.

    Query params:
    - limit: número de registros (padrão: 50, max: 500)
    - provedor: filtrar por provedor (opcional)
    - sucesso: true/false - filtrar por sucesso/erro (opcional)
    """
    try:
        # Parâmetros opcionais
        limit = min(int(request.args.get('limit', 50)), 500)
        provedor = request.args.get('provedor')
        sucesso_param = request.args.get('sucesso')

        # Montar SQL dinâmico
        where_clauses = ["usuario_id = :usuario_id"]
        params = {"usuario_id": usuario_id, "limit": limit}

        if provedor:
            where_clauses.append("provedor = :provedor")
            params["provedor"] = provedor

        if sucesso_param is not None:
            sucesso = sucesso_param.lower() == 'true'
            where_clauses.append("sucesso = :sucesso")
            params["sucesso"] = sucesso

        sql = text(f"""
            SELECT
                id,
                provedor,
                tipo_chave,
                operacao,
                sucesso,
                mensagem_erro,
                criado_em
            FROM LogAcessoChaveApi
            WHERE {' AND '.join(where_clauses)}
            ORDER BY criado_em DESC
            LIMIT :limit
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql, params)
            logs = []

            for row in result:
                logs.append({
                    "id": row.id,
                    "provedor": row.provedor,
                    "tipo_chave": row.tipo_chave,
                    "operacao": row.operacao,
                    "sucesso": row.sucesso,
                    "mensagem_erro": row.mensagem_erro,
                    "criado_em": row.criado_em.isoformat() if row.criado_em else None
                })

        return jsonify({
            "usuario_id": usuario_id,
            "total": len(logs),
            "logs": logs
        }), 200

    except Exception as e:
        print(f"[API-KEYS] ❌ Erro ao consultar logs: {e}")
        return jsonify({
            "erro": "Erro ao consultar logs",
            "mensagem": str(e)
        }), 500


# ============================================================================
# LGPD - Compliance e Direitos do Usuário
# ============================================================================

@api_keys_bp.route('/lgpd/consentimento', methods=['POST'])
@require_api_key
def registrar_consentimento():
    """
    Registra ou atualiza consentimento do usuário.

    Body JSON:
    {
        "usuario_id": 123,
        "tipo_consentimento": "uso_dados_pessoais" | "armazenamento_chaves_api" | etc,
        "consentimento_dado": true | false,
        "versao_termos": "1.0"  # Opcional
    }
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        data = request.get_json()
        usuario_id = data.get('usuario_id')
        tipo_consentimento = data.get('tipo_consentimento')
        consentimento_dado = data.get('consentimento_dado')
        versao_termos = data.get('versao_termos')

        # Validações
        if not usuario_id or not tipo_consentimento or consentimento_dado is None:
            return jsonify({
                "erro": "Dados incompletos",
                "mensagem": "usuario_id, tipo_consentimento e consentimento_dado são obrigatórios"
            }), 400

        resultado = ServicoConsentimentoLGPD.registrar_consentimento(
            usuario_id=usuario_id,
            tipo_consentimento=tipo_consentimento,
            consentimento_dado=consentimento_dado,
            versao_termos=versao_termos
        )

        return jsonify(resultado), 201

    except ValueError as e:
        return jsonify({
            "erro": "Validação",
            "mensagem": str(e)
        }), 400
    except Exception as e:
        print(f"[LGPD] ❌ Erro ao registrar consentimento: {e}")
        return jsonify({
            "erro": "Erro ao registrar consentimento",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/lgpd/consentimentos/<int:usuario_id>', methods=['GET'])
@require_api_key
def listar_consentimentos(usuario_id):
    """
    Lista histórico de consentimentos do usuário.
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        consentimentos = ServicoConsentimentoLGPD.listar_consentimentos(usuario_id)

        return jsonify({
            "usuario_id": usuario_id,
            "total": len(consentimentos),
            "consentimentos": consentimentos
        }), 200

    except Exception as e:
        print(f"[LGPD] ❌ Erro ao listar consentimentos: {e}")
        return jsonify({
            "erro": "Erro ao listar consentimentos",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/lgpd/consentimentos-iniciais/<int:usuario_id>', methods=['GET'])
@require_api_key
def solicitar_consentimentos_iniciais(usuario_id):
    """
    Retorna lista de consentimentos necessários para novo usuário.

    Útil para mostrar na tela de onboarding.
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        resultado = ServicoConsentimentoLGPD.solicitar_consentimentos_iniciais(usuario_id)

        return jsonify(resultado), 200

    except Exception as e:
        print(f"[LGPD] ❌ Erro ao solicitar consentimentos: {e}")
        return jsonify({
            "erro": "Erro ao solicitar consentimentos",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/lgpd/exportar/<int:usuario_id>', methods=['GET'])
@require_api_key
def exportar_dados_usuario(usuario_id):
    """
    Exporta TODOS os dados do usuário (Direito de Portabilidade - LGPD Art. 18, V).

    ATENÇÃO: Retorna chaves de API DESCRIPTOGRAFADAS!
    Use apenas com autenticação forte.

    Response:
    {
        "usuario_id": 123,
        "data_exportacao": "2025-12-04T10:30:00",
        "dados": {
            "chaves_api": [...],  # DESCRIPTOGRAFADAS
            "preferencias": [...],
            "uso_mensal": [...],
            "logs_acesso": [...],
            "consentimentos": [...],
            "assinatura": {...}
        }
    }
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        dados = ServicoConsentimentoLGPD.exportar_dados_usuario(usuario_id)

        print(f"[LGPD] ✅ Dados exportados para usuário {usuario_id}")

        return jsonify(dados), 200

    except Exception as e:
        print(f"[LGPD] ❌ Erro ao exportar dados: {e}")
        return jsonify({
            "erro": "Erro ao exportar dados",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/lgpd/deletar-conta/<int:usuario_id>', methods=['DELETE'])
@require_api_key
def deletar_conta_usuario(usuario_id):
    """
    Deleta TODOS os dados do usuário (Direito ao Esquecimento - LGPD Art. 18, VI).

    ATENÇÃO: Esta operação é IRREVERSÍVEL!

    Body JSON:
    {
        "confirmacao": "CONFIRMO_DELECAO"
    }

    IMPORTANTE: A string de confirmação deve ser EXATAMENTE "CONFIRMO_DELECAO".
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        data = request.get_json()
        confirmacao = data.get('confirmacao', '')

        resultado = ServicoConsentimentoLGPD.deletar_conta_usuario(
            usuario_id=usuario_id,
            confirmacao=confirmacao
        )

        return jsonify(resultado), 200

    except ValueError as e:
        return jsonify({
            "erro": "Confirmação inválida",
            "mensagem": str(e)
        }), 400
    except Exception as e:
        print(f"[LGPD] ❌ Erro ao deletar conta: {e}")
        return jsonify({
            "erro": "Erro ao deletar conta",
            "mensagem": str(e)
        }), 500


@api_keys_bp.route('/lgpd/verificar-consentimento/<int:usuario_id>/<tipo_consentimento>', methods=['GET'])
@require_api_key
def verificar_consentimento(usuario_id, tipo_consentimento):
    """
    Verifica se usuário possui consentimento ativo para determinado tipo.

    Response:
    {
        "usuario_id": 123,
        "tipo_consentimento": "uso_dados_pessoais",
        "consentimento_ativo": true | false
    }
    """
    try:
        from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

        consentimento_ativo = ServicoConsentimentoLGPD.verificar_consentimento(
            usuario_id=usuario_id,
            tipo_consentimento=tipo_consentimento
        )

        return jsonify({
            "usuario_id": usuario_id,
            "tipo_consentimento": tipo_consentimento,
            "consentimento_ativo": consentimento_ativo
        }), 200

    except Exception as e:
        print(f"[LGPD] ❌ Erro ao verificar consentimento: {e}")
        return jsonify({
            "erro": "Erro ao verificar consentimento",
            "mensagem": str(e)
        }), 500


# ============================================================================
# Endpoint de Health Check
# ============================================================================

@api_keys_bp.route('/health', methods=['GET'])
def health_check():
    """Health check simples (não requer autenticação)"""
    return jsonify({
        "status": "ok",
        "servico": "API Keys Management",
        "versao": "1.0.0"
    }), 200
