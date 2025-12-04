# app/services/gerenciador_chaves_api.py
"""
Gerenciador centralizado de chaves de API (PT-BR).
Implementa escolha explícita do usuário (sem fallback automático).

O usuário deve escolher entre:
- usar_chave_propria = TRUE: Usa chave própria (SEM CUSTO)
- usar_chave_propria = FALSE: Usa chave do sistema (COM CUSTO)

Se não configurou preferência → erro amigável pedindo configuração.
"""
import os
from datetime import datetime
from sqlalchemy import text
from app import db_engine


class GerenciadorChavesApi:
    """
    Gerenciador de chaves de API com escolha explícita.
    Usuário decide: chave própria OU chave do sistema.
    """

    PROVEDORES_SUPORTADOS = ['gemini', 'weather', 'openroute']

    CHAVES_SISTEMA = {
        'gemini': lambda: os.getenv('GEMINI_API_KEY'),
        'weather': lambda: os.getenv('WEATHER_API_KEY'),
        'openroute': lambda: os.getenv('OPENROUTE_API_KEY')
    }

    @staticmethod
    def obter_chave_api(usuario_id: int, provedor: str) -> tuple:
        """
        Retorna (chave_api, tipo_chave).
        tipo_chave: 'propria' ou 'sistema'

        Raises:
            ValueError: Provedor inválido
            Exception: Preferência não configurada ou chave indisponível
        """
        if provedor not in GerenciadorChavesApi.PROVEDORES_SUPORTADOS:
            raise ValueError(f"Provedor inválido: {provedor}")

        # 1. Buscar preferência do usuário
        preferencia = GerenciadorChavesApi._buscar_preferencia(usuario_id, provedor)

        if not preferencia:
            raise Exception(
                f"⚙️ Você ainda não configurou suas preferências para {provedor}.\n"
                f"Acesse o dashboard e escolha:\n"
                f"• Usar sua própria chave (grátis)\n"
                f"• Usar chave do sistema (com custo adicional)"
            )

        # 2. Usuário escolheu chave própria
        if preferencia['usar_chave_propria']:
            return GerenciadorChavesApi._usar_chave_propria(usuario_id, provedor)

        # 3. Usuário escolheu chave do sistema
        else:
            return GerenciadorChavesApi._usar_chave_sistema(usuario_id, provedor)

    @staticmethod
    def _buscar_preferencia(usuario_id: int, provedor: str):
        """Busca preferência do usuário"""
        if not db_engine:
            return None

        sql = text("""
            SELECT usar_chave_propria
            FROM PreferenciasChaveApi
            WHERE usuario_id = :uid AND provedor = :prov
        """)

        try:
            with db_engine.connect() as conn:
                result = conn.execute(sql, {"uid": usuario_id, "prov": provedor}).fetchone()
                return {'usar_chave_propria': result[0]} if result else None
        except Exception as e:
            print(f"[GERENCIADOR-API] Erro ao buscar preferência: {e}")
            return None

    @staticmethod
    def _usar_chave_propria(usuario_id: int, provedor: str) -> tuple:
        """Usa chave própria do usuário"""
        from app.services.encryption_service import encryption_service

        sql = text("""
            SELECT chave_api_criptografada
            FROM ChavesApiUsuario
            WHERE usuario_id = :uid AND provedor = :prov AND ativo = TRUE
        """)

        try:
            with db_engine.connect() as conn:
                result = conn.execute(sql, {"uid": usuario_id, "prov": provedor}).fetchone()

                if not result:
                    raise Exception(
                        f"🔑 Você configurou para usar sua chave de {provedor}, "
                        f"mas ainda não cadastrou.\n"
                        f"Acesse o dashboard para cadastrar sua chave.\n"
                        f"Ou responda: *Cadastrar chave de {provedor}*"
                    )

                # Descriptografar
                chave = encryption_service.decrypt(result[0])

                # Atualizar último uso
                GerenciadorChavesApi._atualizar_ultimo_uso(usuario_id, provedor)

                # Registrar uso
                GerenciadorChavesApi._registrar_uso(usuario_id, provedor, 'propria')

                return (chave, 'propria')

        except Exception as e:
            print(f"[GERENCIADOR-API] Erro ao obter chave própria: {e}")
            raise

    @staticmethod
    def _usar_chave_sistema(usuario_id: int, provedor: str) -> tuple:
        """Usa chave do sistema (PAGA)"""
        chave_sistema = GerenciadorChavesApi.CHAVES_SISTEMA[provedor]()

        if not chave_sistema:
            raise Exception(
                f"❌ Chave do sistema para {provedor} indisponível no momento.\n"
                f"Entre em contato com o suporte ou configure sua própria chave."
            )

        # Verificar limite do plano
        GerenciadorChavesApi._verificar_limite_plano(usuario_id, provedor)

        # Registrar uso (PARA COBRANÇA)
        GerenciadorChavesApi._registrar_uso(usuario_id, provedor, 'sistema')

        return (chave_sistema, 'sistema')

    @staticmethod
    def _verificar_limite_plano(usuario_id: int, provedor: str):
        """
        Verifica se usuário atingiu limite do plano.
        Raises Exception se limite excedido.
        """
        if not db_engine:
            return

        # Buscar plano ativo do usuário
        sql_plano = text("""
            SELECT p.limite_gemini, p.limite_weather, p.limite_openroute, p.nome_plano
            FROM AssinaturasUsuario a
            JOIN Planos p ON a.plano_id = p.id
            WHERE a.usuario_id = :uid
              AND a.status = 'ativo'
              AND (a.data_fim IS NULL OR a.data_fim > CURRENT_TIMESTAMP)
            LIMIT 1
        """)

        # Buscar uso no mês atual
        mes_atual = datetime.now().strftime('%Y-%m')
        sql_uso = text("""
            SELECT COALESCE(SUM(quantidade_chamadas), 0) as total
            FROM RastreamentoUsoApi
            WHERE usuario_id = :uid
              AND provedor = :prov
              AND mes_ano = :mes
        """)

        try:
            with db_engine.connect() as conn:
                # Buscar plano
                plano_result = conn.execute(sql_plano, {"uid": usuario_id}).fetchone()

                if not plano_result:
                    # Sem plano ativo, não verifica limite
                    return

                # Buscar uso
                uso_result = conn.execute(sql_uso, {
                    "uid": usuario_id,
                    "prov": provedor,
                    "mes": mes_atual
                }).fetchone()

                total_usado = uso_result[0] if uso_result else 0

                # Verificar limite do provedor
                if provedor == 'gemini':
                    limite = plano_result.limite_gemini
                elif provedor == 'weather':
                    limite = plano_result.limite_weather
                elif provedor == 'openroute':
                    limite = plano_result.limite_openroute
                else:
                    limite = None

                # Se limite é None, é ilimitado
                if limite is not None and total_usado >= limite:
                    raise Exception(
                        f"🚫 Limite mensal atingido para {provedor}!\n"
                        f"Seu plano {plano_result.nome_plano} permite {limite} chamadas/mês.\n"
                        f"Você já usou {total_usado} chamadas.\n\n"
                        f"💡 Opções:\n"
                        f"• Faça upgrade do plano\n"
                        f"• Use sua própria chave (sem limites)"
                    )

        except Exception as e:
            if "🚫" in str(e):
                # Re-lançar exceção de limite atingido
                raise
            print(f"[GERENCIADOR-API] Erro ao verificar limite: {e}")
            # Não bloqueia se houver erro na verificação

    @staticmethod
    def _atualizar_ultimo_uso(usuario_id: int, provedor: str):
        """Atualiza timestamp de último uso"""
        sql = text("""
            UPDATE ChavesApiUsuario
            SET ultimo_uso_em = CURRENT_TIMESTAMP,
                atualizado_em = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid AND provedor = :prov
        """)

        try:
            with db_engine.connect() as conn:
                conn.begin()
                conn.execute(sql, {"uid": usuario_id, "prov": provedor})
                conn.commit()
        except Exception as e:
            print(f"[GERENCIADOR-API] Erro ao atualizar último uso: {e}")

    @staticmethod
    def _registrar_uso(usuario_id: int, provedor: str, tipo_chave: str):
        """Registra uso para billing"""
        mes_ano = datetime.now().strftime('%Y-%m')

        sql = text("""
            INSERT INTO RastreamentoUsoApi
                (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano, atualizado_em)
            VALUES (:uid, :prov, :tipo, 1, :mes, CURRENT_TIMESTAMP)
            ON CONFLICT (usuario_id, provedor, tipo_chave, mes_ano)
            DO UPDATE SET
                quantidade_chamadas = RastreamentoUsoApi.quantidade_chamadas + 1,
                atualizado_em = CURRENT_TIMESTAMP
        """)

        try:
            with db_engine.connect() as conn:
                conn.begin()
                conn.execute(sql, {
                    "uid": usuario_id,
                    "prov": provedor,
                    "tipo": tipo_chave,
                    "mes": mes_ano
                })
                conn.commit()
        except Exception as e:
            print(f"[GERENCIADOR-API] Erro ao registrar uso: {e}")

    @staticmethod
    def _registrar_log_acesso(usuario_id: int, provedor: str, tipo_chave: str,
                             operacao: str, sucesso: bool, mensagem_erro: str = None):
        """Registra log de acesso para auditoria"""
        sql = text("""
            INSERT INTO LogAcessoChaveApi
                (usuario_id, provedor, tipo_chave, operacao, sucesso, mensagem_erro)
            VALUES (:uid, :prov, :tipo, :op, :sucesso, :erro)
        """)

        try:
            with db_engine.connect() as conn:
                conn.begin()
                conn.execute(sql, {
                    "uid": usuario_id,
                    "prov": provedor,
                    "tipo": tipo_chave,
                    "op": operacao,
                    "sucesso": sucesso,
                    "erro": mensagem_erro
                })
                conn.commit()
        except Exception as e:
            print(f"[GERENCIADOR-API] Erro ao registrar log: {e}")
