# app/services/location_service.py
"""
Serviço para gerenciar localização dos usuários
"""

from sqlalchemy import text
from app import db_engine


class LocationService:
    """Gerencia configuração de localização do usuário"""

    @staticmethod
    def update_user_location(usuario_id: int, cidade: str, estado: str = None) -> tuple:
        """
        Atualiza a localização do usuário.

        Args:
            usuario_id: ID do usuário
            cidade: Nome da cidade (ex: "São Paulo")
            estado: Sigla do estado (ex: "SP") - opcional

        Returns:
            tuple: (sucesso: bool, mensagem: str)
        """
        if not db_engine:
            return False, "Banco de dados não configurado"

        if not cidade or len(cidade.strip()) == 0:
            return False, "Cidade inválida"

        # Validar estado (se fornecido)
        if estado:
            estado = estado.upper().strip()
            if len(estado) != 2:
                return False, "Estado deve ter 2 letras (ex: SP, RJ, MG)"

        cidade = cidade.strip()

        try:
            sql = text("""
                UPDATE Usuarios
                SET cidade = :cidade, estado = :estado
                WHERE id = :uid
            """)

            with db_engine.connect() as conn:
                conn.begin()
                conn.execute(sql, {
                    "uid": usuario_id,
                    "cidade": cidade,
                    "estado": estado
                })
                conn.commit()

                estado_str = f", {estado}" if estado else ""
                mensagem = f"📍 Localização configurada: {cidade}{estado_str}"

                print(f"[LOCATION] Localização atualizada para usuário {usuario_id}: {cidade}, {estado}")
                return True, mensagem

        except Exception as e:
            print(f"[LOCATION] Erro ao atualizar localização: {e}")
            return False, f"Erro ao salvar localização: {str(e)}"

    @staticmethod
    def get_user_location(usuario_id: int) -> tuple:
        """
        Obtém a localização configurada do usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            tuple: (cidade, estado) ou (None, None)
        """
        if not db_engine:
            return None, None

        sql = text("""
            SELECT cidade, estado
            FROM Usuarios
            WHERE id = :uid
        """)

        try:
            with db_engine.connect() as conn:
                result = conn.execute(sql, {"uid": usuario_id}).fetchone()

                if result:
                    return result.cidade, result.estado

                return None, None

        except Exception as e:
            print(f"[LOCATION] Erro ao buscar localização: {e}")
            return None, None

    @staticmethod
    def format_location_info(usuario_id: int) -> str:
        """
        Formata informações de localização para exibição.

        Args:
            usuario_id: ID do usuário

        Returns:
            str: Mensagem formatada
        """
        cidade, estado = LocationService.get_user_location(usuario_id)

        if not cidade:
            return ("📍 *Localização não configurada*\n\n"
                   "Configure sua cidade para receber informações de clima "
                   "no resumo matinal.\n\n"
                   "Exemplo:\n"
                   '"Configurar localização: São Paulo, SP"')

        estado_str = f", {estado}" if estado else ""
        msg = f"📍 *Sua localização atual:*\n{cidade}{estado_str}\n\n"
        msg += "Para alterar, envie:\n"
        msg += '"Configurar localização: [Cidade], [Estado]"'

        return msg
