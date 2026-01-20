# app/routes/webhooks/handlers/reserve_handler.py
"""
ReserveHandler - Processa webhooks de reserva de emergencia.

Rotas:
- /api/agendamento/<id>/reserva: Toggle incluir na reserva
- /api/agendamentos/reserva: Listar agendamentos
"""

from typing import Tuple, Any
from flask import jsonify, request
from sqlalchemy import text

from app import db_engine
from app.services import finance_service


class ReserveHandler:
    """Handler para webhooks de reserva de emergencia."""

    def handle_toggle_reserva(self, agendamento_id: int) -> Tuple[Any, int]:
        """
        Altera o flag incluir_na_reserva de um agendamento específico.

        LÓGICA CORRETA:
        - Reserva de emergência = gastos essenciais MENSAIS × 6 meses
        - Este endpoint permite marcar quais contas fixas incluir (água, luz, aluguel, Netflix, etc.)

        Body JSON:
        {
            "incluir": true/false,
            "api_key": "user_api_key"
        }

        Exemplo:
        PATCH https://seu-backend.onrender.com/api/agendamento/123/reserva
        Body: {"incluir": true, "api_key": "abc123"}

        Resposta de sucesso:
        {
            "status": "sucesso",
            "mensagem": "Agendamento 'Netflix' incluído no cálculo de reserva",
            "agendamento": {
                "id": 123,
                "descricao": "Netflix",
                "valor_previsto": 49.90,
                "periodicidade": "MENSAL",
                "incluir_na_reserva": true
            },
            "impacto": {
                "gasto_mensal_anterior": 2500.00,
                "gasto_mensal_novo": 2549.90,
                "reserva_ideal_anterior": 15000.00,
                "reserva_ideal_nova": 15299.40
            }
        }
        """
        try:
            data = request.json
            incluir = data.get('incluir')
            user_api_key = data.get('api_key')

            # Validar campos obrigatórios
            if incluir is None or not user_api_key:
                return jsonify({
                    "status": "erro",
                    "mensagem": "Campos 'incluir' e 'api_key' são obrigatórios"
                }), 400

            # Autenticar usuário
            user_info = finance_service.get_user_by_api_key(user_api_key)
            if not user_info:
                return jsonify({
                    "status": "erro",
                    "mensagem": "API key inválida"
                }), 401

            usuario_id, _ = user_info

            with db_engine.connect() as conn:
                conn.begin()

                # Calcular reserva ANTES da mudança
                gasto_anterior, reserva_anterior, _ = finance_service.get_reserva_status(conn, usuario_id)

                # Verificar se agendamento pertence ao usuário
                sql_check = text("""
                    SELECT descricao, valor_previsto, periodicidade, tipo_agendamento
                    FROM Agendamentos
                    WHERE id = :aid AND usuario_id = :uid AND ativo = TRUE
                """)
                agend = conn.execute(sql_check, {
                    "aid": agendamento_id,
                    "uid": usuario_id
                }).fetchone()

                if not agend:
                    return jsonify({
                        "status": "erro",
                        "mensagem": "Agendamento não encontrado ou inativo"
                    }), 404

                descricao, valor_previsto, periodicidade, tipo_agend = agend

                # Atualizar flag
                sql_update = text("""
                    UPDATE Agendamentos
                    SET incluir_na_reserva = :incluir
                    WHERE id = :aid AND usuario_id = :uid
                """)
                conn.execute(sql_update, {
                    "incluir": incluir,
                    "aid": agendamento_id,
                    "uid": usuario_id
                })
                conn.commit()

                # Calcular reserva DEPOIS da mudança
                gasto_novo, reserva_nova, _ = finance_service.get_reserva_status(conn, usuario_id)

                status_text = "incluído" if incluir else "excluído"
                return jsonify({
                    "status": "sucesso",
                    "mensagem": f"Agendamento '{descricao}' {status_text} do cálculo de reserva",
                    "agendamento": {
                        "id": agendamento_id,
                        "descricao": descricao,
                        "valor_previsto": float(valor_previsto or 0),
                        "periodicidade": periodicidade,
                        "tipo_agendamento": tipo_agend,
                        "incluir_na_reserva": incluir
                    },
                    "impacto": {
                        "gasto_mensal_anterior": gasto_anterior,
                        "gasto_mensal_novo": gasto_novo,
                        "reserva_ideal_anterior": reserva_anterior,
                        "reserva_ideal_nova": reserva_nova
                    }
                }), 200

        except Exception as e:
            print(f"[RESERVA-TOGGLE] Erro: {e}")
            import traceback
            traceback.print_exc()
            return jsonify({
                "status": "erro",
                "mensagem": str(e)
            }), 500

    def handle_listar_reserva(self) -> Tuple[Any, int]:
        """
        Lista agendamentos do usuário com filtros para gerenciar a reserva de emergência.

        LÓGICA CORRETA:
        - Lista agendamentos fixos/recorrentes (contas mensais)
        - Mostra quais estão incluídos no cálculo da reserva
        - Exibe o impacto no cálculo da reserva ideal

        Query params:
            - api_key (obrigatório): API key do usuário
            - incluir_na_reserva (opcional): true/false - filtrar por flag
            - periodicidade (opcional): MENSAL, SEMANAL, QUINZENAL, ANUAL
            - categoria (opcional): nome da categoria para filtrar
            - limit (opcional): limite de resultados (padrão: 100)
            - offset (opcional): offset para paginação (padrão: 0)

        Exemplo 1 - Listar apenas agendamentos incluídos na reserva:
        GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&incluir_na_reserva=true

        Exemplo 2 - Listar apenas agendamentos mensais:
        GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&periodicidade=MENSAL

        Exemplo 3 - Listar agendamentos de uma categoria:
        GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&categoria=Internet

        Resposta de sucesso:
        {
            "status": "sucesso",
            "total": 15,
            "agendamentos": [
                {
                    "id": 123,
                    "descricao": "Aluguel",
                    "valor_previsto": 1500.00,
                    "periodicidade": "MENSAL",
                    "tipo_agendamento": "FIXO",
                    "dia_execucao": 5,
                    "categoria": "Moradia",
                    "incluir_na_reserva": true
                },
                ...
            ],
            "resumo_reserva": {
                "gasto_mensal_essencial": 2500.00,
                "reserva_ideal_6_meses": 15000.00
            }
        }
        """
        try:
            # Autenticar
            user_api_key = request.args.get('api_key')
            if not user_api_key:
                return jsonify({
                    "status": "erro",
                    "mensagem": "Parâmetro 'api_key' é obrigatório"
                }), 400

            user_info = finance_service.get_user_by_api_key(user_api_key)
            if not user_info:
                return jsonify({
                    "status": "erro",
                    "mensagem": "API key inválida"
                }), 401

            usuario_id, _ = user_info

            # Parâmetros de filtro
            incluir_na_reserva_param = request.args.get('incluir_na_reserva')
            periodicidade_param = request.args.get('periodicidade')
            categoria_param = request.args.get('categoria')
            limit = int(request.args.get('limit', 100))
            offset = int(request.args.get('offset', 0))

            # Construir query base
            sql_parts = []
            sql_parts.append("""
                SELECT
                    a.id,
                    a.descricao,
                    a.valor_previsto,
                    a.periodicidade,
                    a.tipo_agendamento,
                    a.dia_execucao,
                    a.incluir_na_reserva,
                    s.nome_sub as categoria,
                    m.nome_macro as macro_categoria,
                    g.nome_grupo as grupo
                FROM Agendamentos a
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
                WHERE a.usuario_id = :uid
                  AND a.ativo = TRUE
            """)

            params = {"uid": usuario_id}

            # Filtro por flag incluir_na_reserva
            if incluir_na_reserva_param is not None:
                incluir_bool = incluir_na_reserva_param.lower() == 'true'
                sql_parts.append("AND a.incluir_na_reserva = :incluir")
                params["incluir"] = incluir_bool

            # Filtro por periodicidade
            if periodicidade_param:
                periodicidade_upper = periodicidade_param.upper()
                if periodicidade_upper not in ['DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']:
                    return jsonify({
                        "status": "erro",
                        "mensagem": "Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL"
                    }), 400
                sql_parts.append("AND a.periodicidade = :periodicidade")
                params["periodicidade"] = periodicidade_upper

            # Filtro por categoria
            if categoria_param:
                sql_parts.append("AND s.nome_sub ILIKE :categoria")
                params["categoria"] = f"%{categoria_param}%"

            # Ordenação e paginação
            sql_parts.append("ORDER BY a.valor_previsto DESC, a.descricao ASC")
            sql_parts.append("LIMIT :limit OFFSET :offset")
            params["limit"] = limit
            params["offset"] = offset

            sql_query = text(" ".join(sql_parts))

            # Query para contar total
            sql_count_parts = [
                """
                SELECT COUNT(*) as total
                FROM Agendamentos a
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                WHERE a.usuario_id = :uid
                  AND a.ativo = TRUE
                """
            ]

            if incluir_na_reserva_param is not None:
                sql_count_parts.append("AND a.incluir_na_reserva = :incluir")

            if periodicidade_param:
                sql_count_parts.append("AND a.periodicidade = :periodicidade")

            if categoria_param:
                sql_count_parts.append("AND s.nome_sub ILIKE :categoria")

            sql_count = text(" ".join(sql_count_parts))

            with db_engine.connect() as conn:
                # Buscar agendamentos
                results = conn.execute(sql_query, params).fetchall()

                # Contar total
                total = conn.execute(sql_count, params).scalar()

                agendamentos = []
                for row in results:
                    agendamentos.append({
                        "id": row.id,
                        "descricao": row.descricao,
                        "valor_previsto": float(row.valor_previsto or 0),
                        "periodicidade": row.periodicidade,
                        "tipo_agendamento": row.tipo_agendamento,
                        "dia_execucao": row.dia_execucao,
                        "categoria": row.categoria,
                        "macro_categoria": row.macro_categoria,
                        "grupo": row.grupo,
                        "incluir_na_reserva": row.incluir_na_reserva
                    })

                # Calcular resumo da reserva
                gasto_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)

                return jsonify({
                    "status": "sucesso",
                    "total": total,
                    "limit": limit,
                    "offset": offset,
                    "agendamentos": agendamentos,
                    "resumo_reserva": {
                        "gasto_mensal_essencial": gasto_mensal,
                        "reserva_ideal": reserva_ideal,
                        "meses_configurados": meses
                    }
                }), 200

        except Exception as e:
            print(f"[RESERVA-LIST] Erro: {e}")
            import traceback
            traceback.print_exc()
            return jsonify({
                "status": "erro",
                "mensagem": str(e)
            }), 500


# Instancia singleton
_handler = ReserveHandler()


def toggle_incluir_reserva_agendamento(agendamento_id: int) -> Tuple[Any, int]:
    """Função de entrada para toggle de reserva."""
    return _handler.handle_toggle_reserva(agendamento_id)


def listar_agendamentos_reserva() -> Tuple[Any, int]:
    """Função de entrada para listar agendamentos de reserva."""
    return _handler.handle_listar_reserva()
