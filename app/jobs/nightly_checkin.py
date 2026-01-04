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

                # Enviar check-in apenas se houver contas pendentes
                if pending_bills:
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

                    if mensagem:
                        # Enviar via WhatsApp
                        enviar_notificacao_whatsapp(
                            numero_whatsapp,
                            mensagem,
                            BOT_WHATSAPP_URL,
                            API_SECRET_KEY
                        )
                        self._log(f"Check-in enviado para usuário {usuario_id}")
                    else:
                        self._log(f"Sem mensagem de check-in para enviar - usuário {usuario_id}")
                else:
                    self._log(f"Sem contas pendentes para check-in - usuário {usuario_id}")

            except Exception as e_user:
                self._log(f"Erro ao processar check-in do usuário {usuario_id}: {e_user}", level="ERROR")
                import traceback
                traceback.print_exc()
                continue

        # NOVOS ALERTAS: Processar alertas para usuários com check-in noturno ativo
        # Os alertas seguem o mesmo horário do check-in noturno (checkin_noturno_hora)
        self._log("Processando alertas de contas atrasadas e vencimentos...")

        # Buscar todos os usuários com check-in noturno ativo neste horário
        from app.services.notification_config_service import NotificationConfigService

        # Usar o mesmo horário do check-in noturno para os alertas
        usuarios_alertas = NotificationConfigService.get_users_with_checkin_noturno_active(hora_atual)

        if usuarios_alertas:
            self._log(f"{len(usuarios_alertas)} usuário(s) com alertas ativos")
            for usuario_id, numero_whatsapp in usuarios_alertas:
                try:
                    # Processar alertas de faturas vencidas
                    self._process_overdue_invoices_for_user(usuario_id, numero_whatsapp)

                    # Processar alertas de contas atrasadas
                    self._process_overdue_bills_for_user(usuario_id, numero_whatsapp)

                    # Processar alertas de contas que vencem hoje
                    self._process_bills_due_today_for_user(usuario_id, numero_whatsapp)

                except Exception as e:
                    self._log(f"Erro ao processar alertas para usuário {usuario_id}: {e}", level="ERROR")
                    import traceback
                    traceback.print_exc()
        else:
            self._log("Nenhum usuário com alertas ativos neste horário")

        self._log("Processamento finalizado")

    def _process_overdue_invoices_for_user(self, usuario_id, numero_whatsapp):
        """
        Alertar sobre faturas vencidas de um usuário específico.
        """
        from app import db_engine
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.shared.formatters.invoice_notification_formatter import InvoiceNotificationFormatter
        from app.services.redis_service import redis_service
        from app.services.queries import FaturasQueries
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        with db_engine.connect() as conn:
            # Buscar faturas vencidas do usuário usando query centralizada
            hoje = date.today()

            sql = FaturasQueries.get_faturas_vencidas()
            params = FaturasQueries.get_parametros_padrao(usuario_id, hoje)

            result = conn.execute(sql, params).fetchall()
            overdue_invoices = [dict(row._mapping) for row in result]

        if not overdue_invoices:
            return

        today_str = date.today().strftime('%Y%m%d')

        for invoice in overdue_invoices:
            # Enviar 1 alerta por dia (não spammar)
            redis_key = f"invoice_overdue:{invoice['id']}:{today_str}"

            if not redis_service.exists(redis_key):
                msg = InvoiceNotificationFormatter.format_overdue_alert(invoice)

                success = enviar_notificacao_whatsapp(
                    numero_whatsapp,
                    msg,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                if success:
                    redis_service.set_with_ttl(redis_key, True, ttl_seconds=30*24*60*60)
                    self._log(f"Alerta de fatura vencida enviado - Usuário {usuario_id}, Fatura #{invoice['id']}")
                else:
                    self._log(f"Falha ao enviar alerta de fatura - Usuário {usuario_id}, Fatura #{invoice['id']}", level="WARNING")
            else:
                self._log(f"Alerta de fatura já enviado hoje - Usuário {usuario_id}, Fatura #{invoice['id']}")

    def _process_overdue_bills_for_user(self, usuario_id, numero_whatsapp):
        """
        Alertar sobre contas atrasadas (agendamentos não pagos) de um usuário específico.
        Considera apenas contas atrasadas há mais de 7 dias (as recentes vão no check-in).
        """
        from app import db_engine
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.services.redis_service import redis_service
        from app.services.queries import AgendamentosQueries
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY
        from app.utils import formatar_moeda

        hoje = date.today()

        with db_engine.connect() as conn:
            # Usar query centralizada para buscar contas atrasadas
            sql = AgendamentosQueries.get_contas_atrasadas_com_data_real()
            params = AgendamentosQueries.get_parametros_padrao(usuario_id, hoje)

            result = conn.execute(sql, params).fetchall()
            contas_atrasadas = [dict(row._mapping) for row in result]

        if not contas_atrasadas:
            return

        # Enviar 1 alerta por dia
        today_str = date.today().strftime('%Y%m%d')
        redis_key = f"bills_overdue:{usuario_id}:{today_str}"

        if redis_service.exists(redis_key):
            self._log(f"Alerta de contas atrasadas já enviado hoje - Usuário {usuario_id}")
            return

        # Formatar mensagem
        despesas = [c for c in contas_atrasadas if c['nome_grupo'] == 'Despesa']
        receitas = [c for c in contas_atrasadas if c['nome_grupo'] == 'Renda']

        if not despesas and not receitas:
            return

        msg = "🔴 *ALERTA DE CONTAS ATRASADAS*\n\n"

        if despesas:
            msg += "💸 *DESPESAS VENCIDAS (há mais de 7 dias):*\n\n"
            total = 0
            for conta in despesas:
                valor = conta['valor_previsto'] or 0
                total += valor
                dias_atraso = (hoje - conta['data_vencimento_real']).days
                msg += f"• {conta['descricao']} - {formatar_moeda(valor)}\n"
                msg += f"  Venceu em {conta['data_vencimento_real'].strftime('%d/%m')} ({dias_atraso} dias) ⚠️\n"

            msg += f"\n💸 *Total:* {formatar_moeda(total)}\n"
            msg += f"⚠️ *{len(despesas)} conta{'s' if len(despesas) != 1 else ''} atrasada{'s' if len(despesas) != 1 else ''}*\n\n"

        if receitas:
            msg += "💵 *RECEITAS PENDENTES:*\n"
            msg += "_Valores previstos que ainda não foram recebidos_\n\n"
            total_receitas = 0
            for conta in receitas:
                valor = conta['valor_previsto'] or 0
                total_receitas += valor
                msg += f"• {conta['descricao']} - {formatar_moeda(valor)}\n"
                msg += f"  Previsto em {conta['data_vencimento_real'].strftime('%d/%m')}\n"

            msg += f"\n💰 *Total:* {formatar_moeda(total_receitas)}\n"

        msg += "\n_Digite 'Pendencias' para ver todos os detalhes._"

        success = enviar_notificacao_whatsapp(
            numero_whatsapp,
            msg,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        if success:
            redis_service.set_with_ttl(redis_key, True, ttl_seconds=24*60*60)
            self._log(f"Alerta de contas atrasadas enviado - Usuário {usuario_id} ({len(contas_atrasadas)} contas)")
        else:
            self._log(f"Falha ao enviar alerta de contas atrasadas - Usuário {usuario_id}", level="WARNING")

    def _process_bills_due_today_for_user(self, usuario_id, numero_whatsapp):
        """
        Alertar sobre contas que vencem hoje.
        """
        from app import db_engine
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.services.redis_service import redis_service
        from app.services.queries import AgendamentosQueries
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY
        from app.utils import formatar_moeda

        hoje = date.today()

        with db_engine.connect() as conn:
            # Usar query centralizada para buscar contas que vencem hoje
            sql = AgendamentosQueries.get_contas_vencendo_hoje()
            params = AgendamentosQueries.get_parametros_padrao(usuario_id, hoje)

            result = conn.execute(sql, params).fetchall()
            contas_hoje = [dict(row._mapping) for row in result]

        if not contas_hoje:
            return

        # Enviar 1 alerta por dia
        today_str = date.today().strftime('%Y%m%d')
        redis_key = f"bills_due_today:{usuario_id}:{today_str}"

        if redis_service.exists(redis_key):
            self._log(f"Alerta de vencimentos de hoje já enviado - Usuário {usuario_id}")
            return

        # Formatar mensagem
        despesas = [c for c in contas_hoje if c['nome_grupo'] == 'Despesa']
        receitas = [c for c in contas_hoje if c['nome_grupo'] == 'Renda']

        if not despesas and not receitas:
            return

        msg = "📅 *VENCIMENTOS DE HOJE*\n\n"

        if despesas:
            msg += "💸 *CONTAS A PAGAR:*\n"
            total = 0
            for conta in despesas:
                valor = conta['valor_previsto'] or 0
                total += valor
                msg += f"• {conta['descricao']} - {formatar_moeda(valor)}\n"
                msg += f"  {conta['nome_conta']}\n"

            msg += f"\n💸 *Total:* {formatar_moeda(total)}\n\n"

        if receitas:
            msg += "💰 *RECEITAS PREVISTAS:*\n"
            total_receitas = 0
            for conta in receitas:
                valor = conta['valor_previsto'] or 0
                total_receitas += valor
                msg += f"• {conta['descricao']} - {formatar_moeda(valor)}\n"
                msg += f"  {conta['nome_conta']}\n"

            msg += f"\n💰 *Total:* {formatar_moeda(total_receitas)}\n\n"

        msg += "_Não esqueça de registrar os pagamentos/recebimentos!_"

        success = enviar_notificacao_whatsapp(
            numero_whatsapp,
            msg,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        if success:
            redis_service.set_with_ttl(redis_key, True, ttl_seconds=24*60*60)
            self._log(f"Alerta de vencimentos de hoje enviado - Usuário {usuario_id} ({len(contas_hoje)} contas)")
        else:
            self._log(f"Falha ao enviar alerta de vencimentos - Usuário {usuario_id}", level="WARNING")


if __name__ == "__main__":
    job = NightlyCheckinJob()
    exit_code = job.run()
    sys.exit(exit_code)
