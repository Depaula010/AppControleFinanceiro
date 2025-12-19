# app/services/finance/user_service.py
"""
Serviço de gerenciamento de usuários.

Este módulo contém funções para buscar e gerenciar usuários do sistema.
"""

from typing import Optional, Tuple
from ._database import text, db_engine


def get_user_by_api_key(api_key: str) -> Optional[Tuple[int, str]]:
    """
    Encontra um usuário pela sua API key do Automate.

    NOTA: Busca todas as API keys e descriptografa para comparar.
    Em produção com muitos usuários, considere indexar hash da chave.

    Args:
        api_key: Chave da API do Automate

    Returns:
        Tupla (usuario_id, numero_whatsapp) ou None se não encontrado
    """
    if not db_engine:
        raise Exception("Banco não configurado")

    from app.services.encryption_service import encryption_service

    # Buscar todos os usuários com API key
    sql = text("SELECT id, numero_whatsapp, api_key_automate FROM Usuarios WHERE api_key_automate IS NOT NULL")

    with db_engine.connect() as conn:
        results = conn.execute(sql).fetchall()

        # Comparar descriptografando cada chave
        for row in results:
            stored_key = row.api_key_automate

            try:
                # Tentar descriptografar
                decrypted_key = encryption_service.decrypt(stored_key)

                if decrypted_key == api_key:
                    # Retornar no mesmo formato que antes
                    return (row.id, row.numero_whatsapp)
            except:
                # Chave pode estar em plain text (dados antigos)
                # Comparação direta como fallback
                if stored_key == api_key:
                    return (row.id, row.numero_whatsapp)

        return None  # Nenhuma chave correspondente encontrada


def get_user_by_whatsapp(numero_whatsapp: str) -> Optional[int]:
    """
    Encontra um usuário pelo seu número de WhatsApp.

    Args:
        numero_whatsapp: Número do WhatsApp do usuário

    Returns:
        ID do usuário ou None se não encontrado
    """
    if not db_engine:
        raise Exception("Banco não configurado")

    sql = text("SELECT id FROM Usuarios WHERE numero_whatsapp = :num")

    with db_engine.connect() as conn:
        return conn.execute(sql, {"num": numero_whatsapp}).scalar_one_or_none()


__all__ = [
    'get_user_by_api_key',
    'get_user_by_whatsapp',
]
