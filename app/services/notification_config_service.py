# app/services/notification_config_service.py
"""
Serviço para gerenciar configurações de notificações do usuário
"""

from sqlalchemy import text
from datetime import time
from app import db_engine

class NotificationConfigService:
    """Gerencia configurações de notificações"""
    
    @staticmethod
    def create_notification_config_table():
        """Cria tabela de configurações de notificações"""
        if not db_engine:
            raise Exception("Banco não configurado")

        sql = text("""
            CREATE TABLE IF NOT EXISTS NotificationConfigs (
                id SERIAL PRIMARY KEY,
                usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,

                -- Resumo Matinal (Daily Briefing com agenda e clima)
                resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE,
                resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00',

                -- Alertas Financeiros (contas e faturas a vencer)
                alertas_financeiros_ativos BOOLEAN NOT NULL DEFAULT TRUE,

                -- Timestamps
                created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

                UNIQUE(usuario_id)
            );

            CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
            ON NotificationConfigs(usuario_id);

            -- Comentários para documentação
            COMMENT ON COLUMN NotificationConfigs.resumo_matinal_ativo IS
            'Se TRUE, envia resumo matinal com agenda e clima';

            COMMENT ON COLUMN NotificationConfigs.resumo_matinal_hora IS
            'Horário único para envio de TODAS as notificações matinais';

            COMMENT ON COLUMN NotificationConfigs.alertas_financeiros_ativos IS
            'Se TRUE, inclui alertas de contas/faturas a vencer (hoje e amanhã)';
        """)
        
        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    conn.execute(sql)
                print("[NOTIF-CONFIG] ✅ Tabela NotificationConfigs criada")
        except Exception as e:
            print(f"[NOTIF-CONFIG] Erro ao criar tabela: {e}")
            raise
    
    @staticmethod
    def get_or_create_config(usuario_id):
        """
        Obtém configuração do usuário ou cria uma padrão.

        Returns:
            dict com configurações
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        sql_get = text("""
            SELECT
                resumo_matinal_ativo,
                resumo_matinal_hora,
                alertas_financeiros_ativos
            FROM NotificationConfigs
            WHERE usuario_id = :uid
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql_get, {"uid": usuario_id}).fetchone()

            if result:
                return {
                    'resumo_matinal_ativo': result.resumo_matinal_ativo,
                    'resumo_matinal_hora': result.resumo_matinal_hora,
                    'alertas_financeiros_ativos': result.alertas_financeiros_ativos
                }

        # Se não existir, criar em uma nova conexão com transação
        sql_create = text("""
            INSERT INTO NotificationConfigs (usuario_id)
            VALUES (:uid)
            RETURNING
                resumo_matinal_ativo,
                resumo_matinal_hora,
                alertas_financeiros_ativos
        """)

        with db_engine.connect() as conn:
            with conn.begin():
                result = conn.execute(sql_create, {"uid": usuario_id}).fetchone()

        return {
            'resumo_matinal_ativo': result.resumo_matinal_ativo,
            'resumo_matinal_hora': result.resumo_matinal_hora,
            'alertas_financeiros_ativos': result.alertas_financeiros_ativos
        }
    
    @staticmethod
    def update_agenda_diaria_config(usuario_id, ativa=None, hora=None):
        """
        Atualiza configuração de agenda diária.
        
        Args:
            usuario_id: ID do usuário
            ativa: True/False ou None (manter atual)
            hora: time object, string 'HH:MM', ou None (manter atual)
        
        Returns:
            (sucesso: bool, mensagem: str)
        """
        if not db_engine:
            raise Exception("Banco não configurado")
        
        # Garantir que config existe
        NotificationConfigService.get_or_create_config(usuario_id)
        
        updates = []
        params = {"uid": usuario_id}
        
        if ativa is not None:
            updates.append("agenda_diaria_ativa = :ativa")
            params['ativa'] = ativa
        
        if hora is not None:
            if isinstance(hora, str):
                # Converter 'HH:MM' para time
                from datetime import datetime
                hora = datetime.strptime(hora, '%H:%M').time()
            
            updates.append("agenda_diaria_hora = :hora")
            params['hora'] = hora
        
        if not updates:
            return False, "Nenhuma alteração fornecida"
        
        sql = text(f"""
            UPDATE NotificationConfigs
            SET {', '.join(updates)}, updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
        """)
        
        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    conn.execute(sql, params)

            status = "ativa" if ativa else "desativada" if ativa is False else None
            hora_fmt = hora.strftime('%H:%M') if hora else None

            msg_parts = []
            if status:
                msg_parts.append(f"Notificação de agenda diária {status}")
            if hora_fmt:
                msg_parts.append(f"horário configurado para {hora_fmt}")

            mensagem = " e ".join(msg_parts) if msg_parts else "Configuração atualizada"

            print(f"[NOTIF-CONFIG] Agenda diária atualizada para usuário {usuario_id}")
            return True, mensagem

        except Exception as e:
            print(f"[NOTIF-CONFIG] Erro ao atualizar: {e}")
            return False, f"Erro ao atualizar configuração: {str(e)}"
    
    @staticmethod
    def update_contas_vencer_config(usuario_id, ativa=None, dias_antes=None, hora=None):
        """
        Atualiza configuração de contas a vencer.
        
        Args:
            usuario_id: ID do usuário
            ativa: True/False ou None
            dias_antes: int (1, 2, 3...) ou None
            hora: time object, string 'HH:MM', ou None
        
        Returns:
            (sucesso: bool, mensagem: str)
        """
        if not db_engine:
            raise Exception("Banco não configurado")
        
        NotificationConfigService.get_or_create_config(usuario_id)
        
        updates = []
        params = {"uid": usuario_id}
        
        if ativa is not None:
            updates.append("contas_vencer_ativa = :ativa")
            params['ativa'] = ativa
        
        if dias_antes is not None:
            updates.append("contas_vencer_dias_antes = :dias")
            params['dias'] = dias_antes
        
        if hora is not None:
            if isinstance(hora, str):
                from datetime import datetime
                hora = datetime.strptime(hora, '%H:%M').time()
            
            updates.append("contas_vencer_hora = :hora")
            params['hora'] = hora
        
        if not updates:
            return False, "Nenhuma alteração fornecida"
        
        sql = text(f"""
            UPDATE NotificationConfigs
            SET {', '.join(updates)}, updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
        """)
        
        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    conn.execute(sql, params)

            msg_parts = []
            if ativa is not None:
                status = "ativa" if ativa else "desativada"
                msg_parts.append(f"Notificação de contas {status}")
            if dias_antes:
                msg_parts.append(f"alerta configurado para {dias_antes} dia(s) antes")
            if hora:
                msg_parts.append(f"horário às {hora.strftime('%H:%M')}")

            mensagem = ", ".join(msg_parts) if msg_parts else "Configuração atualizada"

            print(f"[NOTIF-CONFIG] Contas a vencer atualizada para usuário {usuario_id}")
            return True, mensagem

        except Exception as e:
            print(f"[NOTIF-CONFIG] Erro ao atualizar: {e}")
            return False, f"Erro: {str(e)}"
    
    @staticmethod
    def get_users_with_agenda_diaria_active(target_hour):
        """
        Retorna usuários que devem receber notificação de agenda diária agora.
        
        Args:
            target_hour: time object da hora atual
        
        Returns:
            list: [(usuario_id, numero_whatsapp), ...]
        """
        if not db_engine:
            raise Exception("Banco não configurado")
        
        sql = text("""
            SELECT u.id, u.numero_whatsapp
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.agenda_diaria_ativa = TRUE
              AND nc.agenda_diaria_hora = :hora
        """)
        
        with db_engine.connect() as conn:
            return conn.execute(sql, {"hora": target_hour}).fetchall()
    
    @staticmethod
    def get_users_with_contas_vencer_active(target_hour):
        """
        Retorna usuários que devem receber notificação de contas a vencer agora.
        
        Args:
            target_hour: time object da hora atual
        
        Returns:
            list: [(usuario_id, numero_whatsapp, dias_antes), ...]
        """
        if not db_engine:
            raise Exception("Banco não configurado")
        
        sql = text("""
            SELECT u.id, u.numero_whatsapp, nc.contas_vencer_dias_antes
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.contas_vencer_ativa = TRUE
              AND nc.contas_vencer_hora = :hora
        """)

        with db_engine.connect() as conn:
            return conn.execute(sql, {"hora": target_hour}).fetchall()

    @staticmethod
    def update_resumo_matinal_config(usuario_id, ativo=None, hora=None):
        """
        Atualiza configuração de resumo matinal.

        Args:
            usuario_id: ID do usuário
            ativo: True/False ou None (manter atual)
            hora: time object, string 'HH:MM', ou None (manter atual)

        Returns:
            (sucesso: bool, mensagem: str, config: dict or None)
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        # Garantir que config existe
        NotificationConfigService.get_or_create_config(usuario_id)

        updates = []
        params = {"uid": usuario_id}

        if ativo is not None:
            updates.append("resumo_matinal_ativo = :ativo")
            params['ativo'] = ativo

        if hora is not None:
            if isinstance(hora, str):
                # Converter 'HH:MM' para time
                from datetime import datetime
                hora = datetime.strptime(hora, '%H:%M').time()

            updates.append("resumo_matinal_hora = :hora")
            params['hora'] = hora

        if not updates:
            return False, "Nenhuma alteração fornecida", None

        sql = text(f"""
            UPDATE NotificationConfigs
            SET {', '.join(updates)}, updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
            RETURNING resumo_matinal_ativo, resumo_matinal_hora
        """)

        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    result = conn.execute(sql, params).fetchone()

            status = "ativado" if ativo else "desativado" if ativo is False else None
            hora_fmt = hora.strftime('%H:%M') if hora else None

            msg_parts = []
            if status:
                msg_parts.append(f"Resumo matinal {status}")
            if hora_fmt:
                msg_parts.append(f"horário configurado para {hora_fmt}")

            mensagem = " e ".join(msg_parts) if msg_parts else "Configuração atualizada"

            config = {
                'resumo_matinal_ativo': result.resumo_matinal_ativo,
                'resumo_matinal_hora': result.resumo_matinal_hora
            }

            print(f"[NOTIF-CONFIG] Resumo matinal atualizado para usuário {usuario_id}")
            return True, mensagem, config

        except Exception as e:
            print(f"[NOTIF-CONFIG] Erro ao atualizar: {e}")
            return False, f"Erro ao atualizar configuração: {str(e)}", None

    @staticmethod
    def get_users_with_resumo_matinal_active(target_hour):
        """
        Retorna usuários que devem receber resumo matinal agora.

        Args:
            target_hour: time object da hora atual

        Returns:
            list: [(usuario_id, numero_whatsapp), ...]
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        sql = text("""
            SELECT u.id, u.numero_whatsapp
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.resumo_matinal_ativo = TRUE
              AND nc.resumo_matinal_hora = :hora
        """)

        with db_engine.connect() as conn:
            return conn.execute(sql, {"hora": target_hour}).fetchall()

    @staticmethod
    def get_users_with_notifications_active(target_hour):
        """
        Retorna usuários que devem receber QUALQUER tipo de notificação neste horário.
        Inclui tanto resumo matinal quanto alertas financeiros.

        Args:
            target_hour: time object da hora atual

        Returns:
            list: [(usuario_id, numero_whatsapp), ...]
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        sql = text("""
            SELECT DISTINCT u.id, u.numero_whatsapp
            FROM NotificationConfigs nc
            JOIN Usuarios u ON nc.usuario_id = u.id
            WHERE nc.resumo_matinal_hora = :hora
              AND (nc.resumo_matinal_ativo = TRUE OR nc.alertas_financeiros_ativos = TRUE)
        """)

        with db_engine.connect() as conn:
            return conn.execute(sql, {"hora": target_hour}).fetchall()

    @staticmethod
    def update_alertas_financeiros_config(usuario_id, ativo=None):
        """
        Atualiza configuração de alertas financeiros.

        Args:
            usuario_id: ID do usuário
            ativo: True/False ou None (manter atual)

        Returns:
            (sucesso: bool, mensagem: str, config: dict or None)
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        # Garantir que config existe
        NotificationConfigService.get_or_create_config(usuario_id)

        if ativo is None:
            return False, "Nenhuma alteração fornecida", None

        sql = text("""
            UPDATE NotificationConfigs
            SET alertas_financeiros_ativos = :ativo, updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
            RETURNING alertas_financeiros_ativos
        """)

        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    result = conn.execute(sql, {"uid": usuario_id, "ativo": ativo}).fetchone()

            status = "ativados" if ativo else "desativados"
            mensagem = f"Alertas financeiros {status} com sucesso"

            config = {
                'alertas_financeiros_ativos': result.alertas_financeiros_ativos
            }

            print(f"[NOTIF-CONFIG] Alertas financeiros atualizados para usuário {usuario_id}")
            return True, mensagem, config

        except Exception as e:
            print(f"[NOTIF-CONFIG] Erro ao atualizar: {e}")
            return False, f"Erro ao atualizar configuração: {str(e)}", None