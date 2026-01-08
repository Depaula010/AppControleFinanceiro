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

        # DEBUG: Log detalhado para investigar problema de matching
        self._log(f"DEBUG - Hora atual completa: {datetime.now()}")
        self._log(f"DEBUG - hora_atual após replace: {hora_atual}")
        self._log(f"DEBUG - Tipo: {type(hora_atual)}")
        self._log(f"DEBUG - Formato string: {hora_atual.strftime('%H:%M:%S')}")

        self._log(f"Buscando usuários para {hora_atual.strftime('%H:%M')}")

        # Buscar usuários com check-in ativo para esta hora
        usuarios = NotificationConfigService.get_users_with_checkin_noturno_active(hora_atual)

        if not usuarios:
            self._log(f"Nenhum usuário configurado para {hora_atual}")
            return

        self._log(f"{len(usuarios)} usuário(s) encontrado(s)")

        # Processar cada usuário com MENSAGEM CONSOLIDADA (UX melhorado)
        for usuario_id, numero_whatsapp in usuarios:
            try:
                self._log(f"Processando usuário {usuario_id}...")

                hoje = date.today()

                # Buscar TODAS as informações de uma vez
                with db_engine.connect() as conn:
                    # 1. Contas pendentes (últimos 7 dias)
                    pending_bills = NightlyCheckinService.get_pending_bills(
                        conn, usuario_id, hoje
                    )

                    # DEBUG (2026-01-08): Separar receitas e despesas para log
                    receitas = [b for b in pending_bills if b['nome_grupo'] == 'Renda']
                    despesas = [b for b in pending_bills if b['nome_grupo'] == 'Despesa']
                    self._log(f"DEBUG - Pending bills: {len(receitas)} receita(s), {len(despesas)} despesa(s)")

                    # 2. Contas atrasadas (>7 dias) - usar query específica para check-in (com COALESCE corrigido - 2026-01-07)
                    from app.services.queries import AgendamentosQueries
                    sql_overdue = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
                    params_overdue = AgendamentosQueries.get_parametros_padrao(usuario_id, hoje)

                    # DEBUG: Log dos parâmetros
                    self._log(f"DEBUG - Parâmetros overdue_bills: {params_overdue}")

                    result_overdue = conn.execute(sql_overdue, params_overdue).fetchall()
                    overdue_bills = [dict(row._mapping) for row in result_overdue]

                    # DEBUG: Log das contas atrasadas retornadas
                    if overdue_bills:
                        self._log(f"DEBUG - {len(overdue_bills)} contas atrasadas encontradas:")
                        for bill in overdue_bills[:3]:  # Primeiras 3 para não lotar o log
                            self._log(f"  - {bill['descricao']}: vencimento_real={bill.get('data_vencimento_real')}, tipo_conta={bill.get('tipo_conta')}")

                    # 3. Contas vencendo hoje (já incluídas em pending_bills, mas podemos passar vazio)
                    bills_due_today = []  # Já incluídas em pending_bills

                    # 4. Faturas vencidas
                    from app.services.queries import FaturasQueries
                    sql_invoices = FaturasQueries.get_faturas_vencidas()
                    params_invoices = FaturasQueries.get_parametros_padrao(usuario_id, hoje)
                    result_invoices = conn.execute(sql_invoices, params_invoices).fetchall()
                    overdue_invoices = [dict(row._mapping) for row in result_invoices]

                    # 5. Faturas que vencem HOJE (alerta preventivo)
                    sql_faturas_hoje = FaturasQueries.get_faturas_vencendo_hoje()
                    params_faturas_hoje = {"uid": usuario_id, "hoje": hoje}
                    result_faturas_hoje = conn.execute(sql_faturas_hoje, params_faturas_hoje).fetchall()
                    faturas_vencendo_hoje = [dict(row._mapping) for row in result_faturas_hoje]

                # Se não há nada para mostrar, pular este usuário
                if not pending_bills and not overdue_bills and not overdue_invoices and not faturas_vencendo_hoje:
                    self._log(f"Nenhuma pendência para usuário {usuario_id}")
                    continue

                # Criar sessão de check-in no Redis (apenas para despesas pendentes)
                checkin_id = NightlyCheckinService.create_checkin_session(
                    numero_whatsapp, pending_bills
                )

                if not checkin_id:
                    self._log(f"Erro ao criar sessão - usuário {usuario_id}", level="ERROR")
                    continue

                # Formatar mensagem CONSOLIDADA
                mensagem = NightlyCheckinService.format_consolidated_checkin_message(
                    pending_bills,
                    overdue_bills,
                    bills_due_today,
                    overdue_invoices,
                    faturas_vencendo_hoje,
                    checkin_id
                )

                if mensagem:
                    # Enviar ÚNICA mensagem via WhatsApp
                    enviar_notificacao_whatsapp(
                        numero_whatsapp,
                        mensagem,
                        BOT_WHATSAPP_URL,
                        API_SECRET_KEY
                    )
                    self._log(f"✅ Check-in consolidado enviado para usuário {usuario_id}")
                    self._log(f"   - {len(pending_bills)} conta(s) pendente(s)")
                    self._log(f"   - {len(overdue_bills)} conta(s) atrasada(s)")
                    self._log(f"   - {len(overdue_invoices)} fatura(s) vencida(s)")
                else:
                    self._log(f"Sem conteúdo para enviar - usuário {usuario_id}")

            except Exception as e_user:
                self._log(f"Erro ao processar usuário {usuario_id}: {e_user}", level="ERROR")
                import traceback
                traceback.print_exc()
                continue

        self._log("Processamento finalizado")


if __name__ == "__main__":
    job = NightlyCheckinJob()
    exit_code = job.run()
    sys.exit(exit_code)
