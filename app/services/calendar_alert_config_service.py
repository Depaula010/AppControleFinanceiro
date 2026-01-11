# app/services/calendar_alert_config_service.py
"""
Serviço para gerenciar configurações de alertas de tarefas do Google Calendar
"""

from sqlalchemy import text
from app import db_engine

class CalendarAlertConfigService:
    """Gerencia configurações de alertas de tarefas do Google Calendar"""


    @staticmethod
    def update_alertas_tarefas_config(usuario_id, ativo=None, minutos_antes=None):
        """
        Atualiza configuração de alertas de tarefas.

        Args:
            usuario_id: ID do usuário
            ativo: True/False ou None (manter atual)
            minutos_antes: int (1-60) ou None (manter atual)

        Returns:
            (sucesso: bool, mensagem: str, config: dict or None)
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        # Garantir que config existe
        CalendarAlertConfigService.get_or_create_config(usuario_id)

        # Validar minutos_antes se fornecido
        if minutos_antes is not None:
            if not isinstance(minutos_antes, int) or not (1 <= minutos_antes <= 60):
                return False, "minutos_antes deve ser um número entre 1 e 60", None

        updates = []
        params = {"uid": usuario_id}

        if ativo is not None:
            updates.append("alertas_tarefas_ativo = :ativo")
            params['ativo'] = ativo

        if minutos_antes is not None:
            updates.append("minutos_antes = :minutos")
            params['minutos'] = minutos_antes

        if not updates:
            return False, "Nenhuma alteração fornecida", None

        sql = text(f"""
            UPDATE CalendarAlertConfigs
            SET {', '.join(updates)}, updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
            RETURNING alertas_tarefas_ativo, minutos_antes
        """)

        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    result = conn.execute(sql, params).fetchone()

            status = "ativados" if ativo else "desativados" if ativo is False else None

            msg_parts = []
            if status:
                msg_parts.append(f"Alertas de tarefas {status}")
            if minutos_antes is not None:
                msg_parts.append(f"alerta configurado para {minutos_antes} minuto(s) antes")

            mensagem = " e ".join(msg_parts) if msg_parts else "Configuração atualizada"

            config = {
                'alertas_tarefas_ativo': result.alertas_tarefas_ativo,
                'minutos_antes': result.minutos_antes
            }

            print(f"[CALENDAR-ALERT-CONFIG] Alertas de tarefas atualizados para usuário {usuario_id}")
            return True, mensagem, config

        except Exception as e:
            print(f"[CALENDAR-ALERT-CONFIG] Erro ao atualizar: {e}")
            return False, f"Erro ao atualizar configuração: {str(e)}", None

    @staticmethod
    def get_users_with_alerts_active():
        """
        Retorna todos os usuários que têm alertas de tarefas ativos
        e possuem integração com Google Calendar.

        Returns:
            list: [(usuario_id, numero_whatsapp, minutos_antes), ...]
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        sql = text("""
            SELECT u.id, u.numero_whatsapp, cac.minutos_antes
            FROM CalendarAlertConfigs cac
            JOIN Usuarios u ON cac.usuario_id = u.id
            JOIN GoogleCalendarTokens gct ON u.id = gct.usuario_id
            WHERE cac.alertas_tarefas_ativo = TRUE
              AND (gct.needs_reconnect = FALSE OR gct.needs_reconnect IS NULL)
        """)

        with db_engine.connect() as conn:
            result = conn.execute(sql).fetchall()

            # Logging: contar usuários excluídos por needs_reconnect
            sql_excluded = text("""
                SELECT COUNT(*) as total
                FROM CalendarAlertConfigs cac
                JOIN Usuarios u ON cac.usuario_id = u.id
                JOIN GoogleCalendarTokens gct ON u.id = gct.usuario_id
                WHERE cac.alertas_tarefas_ativo = TRUE
                  AND gct.needs_reconnect = TRUE
            """)
            excluded = conn.execute(sql_excluded).fetchone()

            if excluded and excluded.total > 0:
                print(f"[CALENDAR-ALERT-CONFIG] ℹ️ {excluded.total} usuário(s) excluído(s) - necessitam reconexão")

            return result
