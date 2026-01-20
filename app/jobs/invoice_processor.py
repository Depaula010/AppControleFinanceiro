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

import sys
import os
from datetime import date
from typing import List, Dict

# Adicionar diretório raiz ao path para encontrar o módulo 'app'
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

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
            get_overdue_invoices,
            ensure_current_invoice_exists
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

                    # Criar nova fatura para o próximo período
                    self._log(f"📝 Gerando próxima fatura para {invoice['nome_conta']}...")
                    try:
                        ensure_current_invoice_exists(conn, invoice['usuario_id'], invoice['conta_id'])
                        self._log(f"✅ Nova fatura criada para {invoice['nome_conta']}")
                    except Exception as e:
                        self._log(f"❌ Erro ao criar nova fatura para {invoice['nome_conta']}: {e}", level="ERROR")
            else:
                self._log("Nenhuma fatura para fechar hoje")

            # ============================================================
            # TAREFA 2: Popular faturas abertas com agendamentos
            # ============================================================
            self._log("Verificando faturas abertas sem agendamentos...")

            from app.services.finance.invoice_service import process_invoice_schedules
            from sqlalchemy import text

            sql_faturas_abertas = text("""
                SELECT f.id, f.conta_id, f.data_vencimento, c.nome_conta
                FROM Faturas f
                JOIN Contas c ON f.conta_id = c.id
                WHERE f.status = 'Aberta'
                ORDER BY f.data_vencimento
            """)

            faturas_abertas = conn.execute(sql_faturas_abertas).fetchall()

            if faturas_abertas:
                self._log(f"Encontradas {len(faturas_abertas)} fatura(s) aberta(s)")

                for fatura in faturas_abertas:
                    try:
                        stats = process_invoice_schedules(
                            conn,
                            fatura.id,
                            fatura.conta_id,
                            fatura.data_vencimento
                        )

                        if stats['total_processados'] > 0:
                            self._log(
                                f"✅ Fatura #{fatura.id} ({fatura.nome_conta}): "
                                f"{stats['total_processados']} agendamentos processados "
                                f"(Fixos: {stats['fixos']}, Parcelados: {stats['parcelados']}, Lembretes: {stats['lembretes']})"
                            )
                        else:
                            self._log(f"⏭️ Fatura #{fatura.id} ({fatura.nome_conta}): já processada")

                    except Exception as e:
                        self._log(f"❌ Erro ao processar fatura #{fatura.id}: {e}", level="ERROR")

                conn.commit()
            else:
                self._log("Nenhuma fatura aberta encontrada")

            # ============================================================
            # TAREFA 3: Alertar vencimentos próximos (3 dias antes)
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

            self._log("Processamento de faturas concluído!")


# Entry point para execução via Ofelia
if __name__ == "__main__":
    import sys
    job = InvoiceProcessorJob()
    exit_code = job.run()
    sys.exit(exit_code)
