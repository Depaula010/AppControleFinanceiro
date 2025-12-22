#!/usr/bin/env python3
"""
Processador Automático de Faturas de Cartão de Crédito

Responsabilidades:
1. Fechar faturas cujo data_fechamento já passou
2. Enviar notificações de fechamento
3. Alertar sobre vencimentos próximos (3 dias antes)
4. Alertar sobre faturas vencidas

Executado diariamente às 02:00 AM via Ofelia
"""

from datetime import date
from typing import List, Dict
from app.jobs.base_job import BaseJob


class InvoiceProcessorJob(BaseJob):
    """Job para processar fechamento automático e notificações de faturas."""

    def get_job_name(self) -> str:
        return "INVOICE-PROCESSOR"

    def execute(self):
        """
        Lógica principal do job.
        Executado dentro do Flask app context.
        """
        from app import db_engine
        from app.services.finance.invoice_service import (
            close_expired_invoices,
            get_invoices_due_soon,
            get_overdue_invoices
        )
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.shared.formatters.invoice_notification_formatter import InvoiceNotificationFormatter
        from app.services.redis_service import redis_service
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        with db_engine.begin() as conn:
            # ============================================================
            # TAREFA 1: Fechar faturas vencidas
            # ============================================================
            self._log("Buscando faturas para fechar...")

            invoices_to_close = close_expired_invoices(conn, dry_run=False)

            if invoices_to_close:
                self._log(f"Encontradas {len(invoices_to_close)} fatura(s) para fechar")

                for invoice in invoices_to_close:
                    self._log(
                        f"Fechando fatura #{invoice['id']} - "
                        f"{invoice['nome_conta']} - "
                        f"Fechamento: {invoice['data_fechamento']} - "
                        f"Valor: R$ {invoice['valor_total']:.2f}"
                    )

                    # Enviar notificação de fechamento (se não enviada antes)
                    redis_key = f"invoice_closed:{invoice['id']}"

                    if not redis_service.exists(redis_key):
                        msg = InvoiceNotificationFormatter.format_invoice_closed(invoice)

                        success = enviar_notificacao_whatsapp(
                            invoice['numero_whatsapp'],
                            msg,
                            BOT_WHATSAPP_URL,
                            API_SECRET_KEY
                        )

                        if success:
                            # Marcar como enviado (TTL: 7 dias)
                            redis_service.set_with_ttl(redis_key, True, ttl_seconds=7*24*60*60)
                            self._log(f"✅ Notificação de fechamento enviada - Fatura #{invoice['id']}")
                        else:
                            self._log(f"❌ Falha ao enviar notificação - Fatura #{invoice['id']}", level="WARNING")
                    else:
                        self._log(f"⏭️ Notificação já enviada anteriormente - Fatura #{invoice['id']}")
            else:
                self._log("Nenhuma fatura para fechar hoje")

            # ============================================================
            # TAREFA 2: Alertar vencimentos próximos (3 dias antes)
            # ============================================================
            self._log("Buscando faturas com vencimento próximo (3 dias)...")

            invoices_due_soon = get_invoices_due_soon(conn, days_before=3)

            if invoices_due_soon:
                self._log(f"Encontradas {len(invoices_due_soon)} fatura(s) vencendo em 3 dias")

                for invoice in invoices_due_soon:
                    redis_key = f"invoice_due_warning:{invoice['id']}:3d"

                    if not redis_service.exists(redis_key):
                        msg = InvoiceNotificationFormatter.format_due_date_warning(invoice)

                        success = enviar_notificacao_whatsapp(
                            invoice['numero_whatsapp'],
                            msg,
                            BOT_WHATSAPP_URL,
                            API_SECRET_KEY
                        )

                        if success:
                            redis_service.set_with_ttl(redis_key, True, ttl_seconds=7*24*60*60)
                            self._log(f"✅ Alerta de vencimento enviado - Fatura #{invoice['id']}")
                        else:
                            self._log(f"❌ Falha ao enviar alerta - Fatura #{invoice['id']}", level="WARNING")
                    else:
                        self._log(f"⏭️ Alerta já enviado - Fatura #{invoice['id']}")
            else:
                self._log("Nenhuma fatura vence em 3 dias")

            # ============================================================
            # TAREFA 3: Alertar faturas vencidas
            # ============================================================
            self._log("Buscando faturas vencidas...")

            overdue_invoices = get_overdue_invoices(conn)

            if overdue_invoices:
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
                            self._log(f"✅ Alerta de atraso enviado - Fatura #{invoice['id']}")
                        else:
                            self._log(f"❌ Falha ao enviar alerta de atraso - Fatura #{invoice['id']}", level="WARNING")
                    else:
                        self._log(f"⏭️ Alerta de atraso já enviado hoje - Fatura #{invoice['id']}")
            else:
                self._log("Nenhuma fatura vencida")

            self._log("Processamento de faturas concluído!")


# Entry point para execução via Ofelia
if __name__ == "__main__":
    import sys
    job = InvoiceProcessorJob()
    exit_code = job.run()
    sys.exit(exit_code)
