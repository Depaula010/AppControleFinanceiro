#!/usr/bin/env python3
"""
Processador de Check-in Noturno + Alertas de Fatura Vencida
Executado via cron job (Ofelia) para enviar confirmações de contas pendentes
e alertar sobre faturas vencidas.
"""

import os
import sys
from datetime import datetime, time, date

# Adicionar diretório raiz ao path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from app.jobs.base_job import BaseJob


class NightlyCheckinJob(BaseJob):
    """Job para processar check-in noturno e alertas de fatura vencida."""

    def get_job_name(self) -> str:
        return "CHECKIN-NOTURNO"

    def execute(self):
        """
        Envia check-in noturno e alertas de fatura vencida para usuários configurados.
        Executado dentro do Flask app context.
        """
        # NOVO: Validar horário de execução (detectar misconfigurações)
        current_hour = datetime.now().hour
        if current_hour < 18 or current_hour > 23:
            self._log(
                f"⚠️ Job executado fora da janela permitida (18h-23h). Hora atual: {current_hour}h",
                level="WARNING"
            )
            self._log("Isso indica problema na configuração do cron do Ofelia", level="WARNING")
            self._log("Job continuará, mas alertas podem ser enviados em horário errado", level="WARNING")

        from app.services.notification_config_service import NotificationConfigService
        from app.services.nightly_checkin_service import NightlyCheckinService
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.services.redis_service import redis_service
        from app import db_engine
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        # Verificar se Redis está disponível
        if not redis_service.is_connected():
            self._log("Redis indisponível - abortando", level="ERROR")
            self._log("Check-in requer Redis para gerenciar sessões", level="ERROR")
            raise Exception("Redis indisponível")

        # Obter hora atual (zerando segundos)
        hora_atual = datetime.now().time().replace(second=0, microsecond=0)

        self._log(f"Buscando usuários para {hora_atual.strftime('%H:%M')}")

        # Buscar usuários com check-in ativo para esta hora
        usuarios = NotificationConfigService.get_users_with_checkin_noturno_active(hora_atual)

        if not usuarios:
            self._log(f"Nenhum usuário configurado para {hora_atual}")
            # Mesmo sem check-in, processar alertas de fatura
            self._process_overdue_invoices()
            return

        self._log(f"{len(usuarios)} usuário(s) encontrado(s)")

        # Processar cada usuário
        for usuario_id, numero_whatsapp in usuarios:
            try:
                self._log(f"Processando usuário {usuario_id}...")

                # Buscar contas pendentes
                with db_engine.connect() as conn:
                    pending_bills = NightlyCheckinService.get_pending_bills(
                        conn, usuario_id, date.today()
                    )

                if not pending_bills:
                    self._log(f"Sem contas pendentes - usuário {usuario_id}")
                    continue

                self._log(f"{len(pending_bills)} conta(s) pendente(s)")

                # Criar sessão de check-in no Redis
                checkin_id = NightlyCheckinService.create_checkin_session(
                    numero_whatsapp, pending_bills
                )

                if not checkin_id:
                    self._log(f"Erro ao criar sessão - usuário {usuario_id}", level="ERROR")
                    continue

                # Formatar mensagem
                mensagem = NightlyCheckinService.format_checkin_message(
                    pending_bills, checkin_id
                )

                if not mensagem:
                    self._log(f"Sem mensagem para enviar - usuário {usuario_id}")
                    continue

                # Enviar via WhatsApp
                enviar_notificacao_whatsapp(
                    numero_whatsapp,
                    mensagem,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                self._log(f"Mensagem enviada para usuário {usuario_id}")

            except Exception as e_user:
                self._log(f"Erro ao processar usuário {usuario_id}: {e_user}", level="ERROR")
                import traceback
                traceback.print_exc()
                continue

        # Processar alertas de fatura vencida (todos os usuários)
        self._process_overdue_invoices()

        self._log("Processamento finalizado")

    def _process_overdue_invoices(self):
        """
        NOVA FUNCIONALIDADE: Alertar sobre faturas vencidas.
        Movido de invoice_processor.py (linhas 126-160).
        """
        from app import db_engine
        from app.services.finance.invoice_service import get_overdue_invoices
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.shared.formatters.invoice_notification_formatter import InvoiceNotificationFormatter
        from app.services.redis_service import redis_service
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        self._log("Buscando faturas vencidas...")

        with db_engine.begin() as conn:
            overdue_invoices = get_overdue_invoices(conn)

        if not overdue_invoices:
            self._log("Nenhuma fatura vencida")
            return

        self._log(f"Encontradas {len(overdue_invoices)} fatura(s) vencida(s)")

        today_str = date.today().strftime('%Y%m%d')

        for invoice in overdue_invoices:
            # Enviar 1 alerta por dia (não spammar)
            redis_key = f"invoice_overdue:{invoice['id']}:{today_str}"

            if not redis_service.exists(redis_key):
                msg = InvoiceNotificationFormatter.format_overdue_alert(invoice)

                success = enviar_notificacao_whatsapp(
                    invoice['numero_whatsapp'],
                    msg,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                if success:
                    redis_service.set_with_ttl(redis_key, True, ttl_seconds=30*24*60*60)
                    self._log(f"Alerta de atraso enviado - Fatura #{invoice['id']}")
                else:
                    self._log(f"Falha ao enviar alerta de atraso - Fatura #{invoice['id']}", level="WARNING")
            else:
                self._log(f"Alerta de atraso já enviado hoje - Fatura #{invoice['id']}")


if __name__ == "__main__":
    job = NightlyCheckinJob()
    exit_code = job.run()
    sys.exit(exit_code)
