"""
Serviço de Check-in Noturno
Gerencia o fluxo de confirmação em lote de contas pendentes (Agendamentos)
"""

from app.services.redis_service import redis_service
from app.services import finance_service
from app.services.queries import AgendamentosQueries
from app.utils import formatar_moeda
from datetime import date, timedelta
import uuid
import re
import calendar


class NightlyCheckinService:
    """Gerencia o fluxo de check-in noturno para contas pendentes"""

    # Keywords que interrompem o check-in (Escape Hatch)
    ESCAPE_KEYWORDS = [
        # Transações
        'gastei', 'gasto', 'comprei', 'compra', 'paguei', 'pagamento',
        'recebi', 'recebimento', 'transferi', 'transferência', 'transferencia',
        # Consultas
        'saldo', 'extrato', 'consulta', 'quanto', 'valor',
        # Comandos
        'add', 'criar', 'registrar', 'lançar', 'lancar', 'agendar',
        # Calendário
        'evento', 'compromisso', 'agenda', 'horário', 'horario',
        # Configuração
        'config', 'configurar', 'mudar', 'alterar'
    ]

    @staticmethod
    def get_pending_bills(conn, usuario_id, target_date=None):
        """
        Busca contas pendentes (Agendamentos não pagos nos últimos 60 dias).
        Limita aos últimos 7 dias para evitar lixo antigo.

        Args:
            conn: Conexão do banco
            usuario_id: ID do usuário
            target_date: Data alvo (padrão: hoje)

        Returns:
            list: Lista de dicts com dados dos agendamentos pendentes
        """
        if target_date is None:
            target_date = date.today()

        # Usar query centralizada específica para check-in (com COALESCE corrigido - 2026-01-07)
        sql = AgendamentosQueries.get_contas_pendentes_checkin_noturno()

        # Obter parâmetros padrão e ajustar target_date
        params = AgendamentosQueries.get_parametros_padrao(usuario_id, target_date)
        params["target_date"] = target_date  # Garantir que usa a data passada

        result = conn.execute(sql, params).fetchall()

        return [dict(row._mapping) for row in result]

    @staticmethod
    def calculate_days_overdue(dia_execucao, hoje):
        """
        Calcula dias de atraso considerando virada de mês.

        Args:
            dia_execucao: Dia do mês do agendamento
            hoje: Data de hoje

        Returns:
            int: Número de dias atrasado
        """
        if dia_execucao <= hoje.day:
            # Mesmo mês
            return hoje.day - dia_execucao
        else:
            # Mês anterior (ex: venceu dia 28, hoje é dia 3)
            ultimo_dia_mes_anterior = calendar.monthrange(
                hoje.year if hoje.month > 1 else hoje.year - 1,
                hoje.month - 1 if hoje.month > 1 else 12
            )[1]

            return (ultimo_dia_mes_anterior - dia_execucao) + hoje.day

    @staticmethod
    def categorize_by_delay(bill, hoje):
        """
        Categoriza item por dias de atraso para formatação visual.

        Args:
            bill: Dict do agendamento
            hoje: Data de hoje

        Returns:
            tuple: (status_text, dias_atraso) ou (None, dias_atraso) se >3 dias
        """
        dias_atraso = NightlyCheckinService.calculate_days_overdue(
            bill['dia_execucao'], hoje
        )

        if dias_atraso == 0:
            status = "Vence hoje"
        elif dias_atraso == 1:
            status = "Venceu ontem"
        elif dias_atraso <= 3:
            status = f"Venceu dia {bill['dia_execucao']:02d}"
        else:
            # +3 dias: vai para alerta agrupado
            return None, dias_atraso

        return status, dias_atraso

    @staticmethod
    def create_checkin_session(numero_whatsapp, pending_bills):
        """
        Cria uma sessão de check-in no Redis.

        Args:
            numero_whatsapp: Número do usuário
            pending_bills: Lista de contas pendentes

        Returns:
            str: checkin_id (UUID)
        """
        checkin_id = str(uuid.uuid4())[:8]
        hoje = date.today()

        # Separar recentes (<=3 dias) de atrasados (+3 dias)
        itens_recentes = {}
        itens_atrasados = []

        idx = 1
        for bill in pending_bills:
            status, dias_atraso = NightlyCheckinService.categorize_by_delay(bill, hoje)

            if status is None:
                # Item atrasado (+3 dias)
                itens_atrasados.append({
                    'descricao': bill['descricao'],
                    'valor': bill['valor_previsto'],
                    'dias_atraso': dias_atraso
                })
            else:
                # Item recente
                bill['status_text'] = status
                bill['dias_atraso'] = dias_atraso
                itens_recentes[str(idx)] = bill
                idx += 1

        session_data = {
            'items': itens_recentes,
            'itens_atrasados': itens_atrasados,
            'created_at': str(hoje),
            'total_items': len(itens_recentes)
        }

        # Salvar sessão no Redis (1h TTL)
        session_key = f"nightly_checkin:{numero_whatsapp}:{checkin_id}"
        redis_service.set_with_ttl(session_key, session_data, ttl_seconds=3600)  # 1 hora

        # Setar flag ativa (1h TTL)
        active_key = f"nightly_checkin_active:{numero_whatsapp}"
        redis_service.set_with_ttl(active_key, checkin_id, ttl_seconds=3600)

        print(f"[CHECKIN-SESSION] Criada sessão {checkin_id} para {numero_whatsapp}")
        print(f"[CHECKIN-SESSION] {len(itens_recentes)} itens recentes, {len(itens_atrasados)} atrasados")

        return checkin_id

    @staticmethod
    def format_consolidated_checkin_message(pending_bills, overdue_bills, bills_due_today, overdue_invoices, checkin_id):
        """
        Formata mensagem consolidada de check-in com TODAS as informações em uma única mensagem.
        Melhora UX evitando múltiplas notificações fragmentadas.

        Args:
            pending_bills: Lista de contas pendentes (últimos 7 dias)
            overdue_bills: Lista de contas atrasadas (>7 dias)
            bills_due_today: Lista de contas que vencem hoje
            overdue_invoices: Lista de faturas vencidas
            checkin_id: ID da sessão

        Returns:
            str: Mensagem consolidada formatada ou None se vazio
        """
        hoje = date.today()

        # Separar contas pendentes por tipo
        despesas_pendentes = []
        receitas_pendentes = []

        # Separar contas atrasadas por tipo
        despesas_atrasadas = []
        receitas_atrasadas = []

        # NOVO (2026-01-07): Coletar lembretes de cartão (despesas fixas que vencem hoje)
        lembretes_cartao = []

        # Processar contas pendentes (últimos 7 dias) - com numeração para resposta interativa
        idx = 1
        for bill in pending_bills:
            # NOVO (2026-01-06): Usar data_vencimento_real retornada pela query
            # em vez de calcular por dia_execucao
            data_vencimento = bill.get('data_vencimento_real')

            if data_vencimento:
                # Calcular dias de atraso usando data real
                dias_atraso = (hoje - data_vencimento).days

                # Determinar status baseado em dias de atraso
                if dias_atraso == 0:
                    status = "Vence hoje"
                elif dias_atraso == 1:
                    status = "Venceu ontem"
                elif dias_atraso <= 3:
                    status = f"Venceu em {data_vencimento.strftime('%d/%m')}"
                else:
                    # Vai para seção de atrasados (>3 dias)
                    status = None
            else:
                # Fallback: se data_vencimento_real não vier na query, usar lógica antiga
                status, dias_atraso = NightlyCheckinService.categorize_by_delay(bill, hoje)

            # NOVO (2026-01-07): Filtrar despesas fixas de cartão
            # Só aparecem se vencem HOJE (informativo, não confirmável)
            if bill['tipo_conta'] == 'Cartão de Crédito' and bill['nome_grupo'] == 'Despesa':
                if dias_atraso == 0:
                    # Adicionar à seção de lembretes de cartão (não confirmável)
                    lembretes_cartao.append({
                        'descricao': bill['descricao'],
                        'valor': bill['valor_previsto'] or 0,
                        'conta': bill['nome_conta']
                    })
                continue  # Não adiciona às listas de confirmação

            item = {
                'numero': idx,
                'descricao': bill['descricao'],
                'valor': bill['valor_previsto'] or 0,
                'conta': bill['nome_conta'],
                'status': status or f"Atrasado {dias_atraso} dias",
                'dia_execucao': bill['dia_execucao'],
                'data_vencimento': data_vencimento
            }

            if bill['nome_grupo'] == 'Renda':
                receitas_pendentes.append(item)
            else:
                despesas_pendentes.append(item)

            idx += 1

        # Processar contas atrasadas (>7 dias) - sem numeração, apenas informativo
        for bill in overdue_bills:
            item = {
                'descricao': bill['descricao'],
                'valor': bill['valor_previsto'] or 0,
                'data_vencimento': bill.get('data_vencimento_real')
            }

            if bill['nome_grupo'] == 'Renda':
                receitas_atrasadas.append(item)
            else:
                despesas_atrasadas.append(item)

        # Se não há nada para mostrar, retornar None
        if not despesas_pendentes and not receitas_pendentes and not despesas_atrasadas and not receitas_atrasadas and not overdue_invoices and not lembretes_cartao:
            return None

        # Construir mensagem consolidada
        msg = "🌙 *CHECK-IN NOTURNO* 🌙\n\n"

        # 1. RECEITAS PENDENTES (informativo - não precisa confirmar)
        if receitas_pendentes or receitas_atrasadas:
            msg += "💵 *RECEITAS PENDENTES:*\n"
            msg += "_Valores previstos que ainda não foram recebidos_\n\n"

            total_receitas = 0

            # Receitas recentes (últimos 7 dias)
            for item in receitas_pendentes:
                valor_fmt = formatar_moeda(item['valor'])
                total_receitas += item['valor']
                msg += f"• {item['descricao']} - {valor_fmt}\n"
                msg += f"  Previsto em {item['dia_execucao']:02d}/{hoje.month:02d}\n"

            # Receitas atrasadas (>7 dias)
            for item in receitas_atrasadas:
                valor_fmt = formatar_moeda(item['valor'])
                total_receitas += item['valor']
                msg += f"• {item['descricao']} - {valor_fmt}\n"
                if item['data_vencimento']:
                    msg += f"  Previsto em {item['data_vencimento'].strftime('%d/%m/%Y')}\n"

            msg += f"\n💰 *Total:* {formatar_moeda(total_receitas)}\n\n"

        # NOVO (2026-01-07): LEMBRETES DE CARTÃO (informativo - não precisa confirmar)
        if lembretes_cartao:
            msg += "💳 *CARTÃO (na fatura):*\n"
            msg += "_Débitos recorrentes que serão cobrados na fatura do cartão_\n\n"

            for item in lembretes_cartao:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"ℹ️ {item['descricao']} - {valor_fmt} [{item['conta']}] [Debitado hoje]\n"

            msg += "\n"

        # 2. DESPESAS PENDENTES (interativo - precisa confirmar)
        if despesas_pendentes:
            msg += "💸 *DESPESAS PENDENTES:*\n"
            for item in despesas_pendentes:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"{item['numero']}. {item['descricao']} - {valor_fmt} ({item['conta']}) [{item['status']}]\n"
            msg += "\n"

        # 3. DESPESAS ATRASADAS (>7 dias) - apenas alerta
        if despesas_atrasadas:
            msg += "🔴 *DESPESAS ATRASADAS (há mais de 7 dias):*\n"
            total_atrasado = 0
            for item in despesas_atrasadas:
                valor_fmt = formatar_moeda(item['valor'])
                total_atrasado += item['valor']
                msg += f"• {item['descricao']} - {valor_fmt}\n"
                if item['data_vencimento']:
                    dias_atraso = (hoje - item['data_vencimento']).days
                    # Validação: só mostra se realmente está atrasado (dias > 0)
                    if dias_atraso > 0:
                        msg += f"  Venceu em {item['data_vencimento'].strftime('%d/%m/%Y')} ({dias_atraso} dias) ⚠️\n"
                    else:
                        # Sanity check: data futura não deveria estar aqui (bug na query)
                        msg += f"  Previsto para {item['data_vencimento'].strftime('%d/%m/%Y')}\n"

            msg += f"\n💸 *Total atrasado:* {formatar_moeda(total_atrasado)}\n"
            msg += "_Digite 'Pendencias' para ver todos os detalhes._\n\n"

        # 4. FATURAS VENCIDAS (se houver)
        if overdue_invoices:
            msg += "🔴 *FATURAS VENCIDAS:*\n"
            for fatura in overdue_invoices:
                valor_fmt = formatar_moeda(fatura.get('valor_total', 0))
                msg += f"• {fatura['nome_conta']} - {valor_fmt}\n"
                msg += f"  Venceu em {fatura['data_vencimento'].strftime('%d/%m/%Y')}\n"
            msg += "\n"

        # 5. INSTRUÇÕES (apenas se houver despesas pendentes para confirmar)
        if despesas_pendentes:
            msg += "━━━━━━━━━━━━━━━━━━━━\n"
            msg += "🔹 *COMO RESPONDER:*\n\n"
            msg += "✅ Tudo pago:\n"
            msg += "👍 ou \"Ok\"\n\n"
            msg += "📝 Pagamento parcial:\n"
            msg += "\"1\" / \"1 e 3\" / \"1, 2, 4\"\n\n"
            msg += "⏸️ Não paguei ainda:\n"
            msg += "\"Depois\" / \"Não\"\n\n"
            msg += f"_ID: {checkin_id} | Expira em 1h_"

        return msg

    @staticmethod
    def format_checkin_message(pending_bills, checkin_id):
        """
        DEPRECATED: Use format_consolidated_checkin_message() para melhor UX.

        Mantido para compatibilidade retroativa.
        """
        if not pending_bills:
            return None

        hoje = date.today()

        # Separar por tipo (receitas vs despesas vs cartão) e por delay
        despesas_recentes = []
        despesas_cartao_recentes = []
        receitas_recentes = []
        itens_atrasados = []

        idx = 1
        for bill in pending_bills:
            status, dias_atraso = NightlyCheckinService.categorize_by_delay(bill, hoje)

            if status is None:
                # Atrasado +3 dias
                itens_atrasados.append({
                    'descricao': bill['descricao'],
                    'valor': bill['valor_previsto'],
                    'dias_atraso': dias_atraso
                })
            else:
                # Recente
                # Gerar status neutro para despesas de cartão
                if dias_atraso == 0:
                    status_neutro = "Debitado hoje"
                elif dias_atraso < 0:
                    dias_futuro = abs(dias_atraso)
                    status_neutro = f"Será debitado em {dias_futuro} dias"
                else:
                    status_neutro = f"Debitado há {dias_atraso} dias"

                item = {
                    'numero': idx,
                    'descricao': bill['descricao'],
                    'valor': bill['valor_previsto'] or 0,
                    'conta': bill['nome_conta'],
                    'status': status,
                    'status_neutro': status_neutro
                }

                if bill['nome_grupo'] == 'Renda':
                    receitas_recentes.append(item)
                elif bill['tipo_conta'] == 'Cartão de Crédito':
                    despesas_cartao_recentes.append(item)
                else:
                    despesas_recentes.append(item)

                idx += 1

        # Se não há itens recentes nem atrasados, não enviar
        if not despesas_recentes and not despesas_cartao_recentes and not receitas_recentes and not itens_atrasados:
            return None

        # Construir mensagem
        msg = "🌙 *CHECK-IN NOTURNO* 🌙\n\n"
        msg += "📋 *Contas pendentes:*\n\n"

        # Despesas Normais
        if despesas_recentes:
            msg += "💸 *DESPESAS:*\n"
            for item in despesas_recentes:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"{item['numero']}. {item['descricao']} - {valor_fmt} ({item['conta']}) [{item['status']}]\n"
            msg += "\n"

        # Despesas do Cartão (Lembretes)
        if despesas_cartao_recentes:
            msg += "💳 *CARTÃO (na fatura):*\n"
            for item in despesas_cartao_recentes:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"ℹ️ {item['descricao']} - {valor_fmt} [{item['status_neutro']}]\n"
            msg += "\n"

        # Receitas
        if receitas_recentes:
            msg += "💰 *RECEITAS:*\n"
            for item in receitas_recentes:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"{item['numero']}. {item['descricao']} - {valor_fmt} ({item['conta']}) [{item['status']}]\n"
            msg += "\n"

        # Alerta de itens atrasados
        if itens_atrasados:
            msg += f"⚠️ *Você tem {len(itens_atrasados)} conta(s) atrasada(s) há mais de 3 dias.*\n"
            msg += "Digite 'Pendencias' para ver detalhes.\n\n"

        # Instruções
        if despesas_recentes or receitas_recentes:
            msg += "━━━━━━━━━━━━━━━━━━━━\n"
            msg += "🔹 *COMO RESPONDER:*\n\n"
            msg += "✅ Tudo pago:\n"
            msg += "👍 👌 ✅ ou \"Sim\" / \"Ok\" / \"Confirmar\"\n\n"
            msg += "📝 Pagamento parcial:\n"
            msg += "\"1\" / \"1 e 3\" / \"1, 2, 4\"\n\n"
            msg += "⏸️ Não paguei ainda:\n"
            msg += "\"Não\" / \"Depois\" / \"Ainda não\"\n\n"
            if despesas_cartao_recentes:
                msg += "_💳 Despesas do cartão são apenas lembretes (já na fatura)_\n"
            msg += f"_ID: {checkin_id} | Expira em 1h_"
        else:
            # Só tem itens atrasados ou cartão
            if despesas_cartao_recentes:
                msg += "_💳 Despesas do cartão são apenas lembretes informativos_\n\n"
            msg += "_Use 'Pendencias' para ver contas antigas_"

        return msg

    @staticmethod
    def parse_checkin_response(mensagem, total_itens):
        """
        Faz parsing da resposta do usuário.

        Args:
            mensagem: Texto da mensagem
            total_itens: Total de itens na lista

        Returns:
            tuple: (tipo, lista_indices ou mensagem_erro)
            tipo: 'full', 'partial', 'defer', 'error', 'invalid'
        """
        msg_lower = mensagem.strip().lower()

        # 1. Confirmação Total
        emojis_conf = ['👍', '👌', '✅', '✓', '☑']
        palavras_conf = ['sim', 'ok', 'confirmar', 'tudo pago', 'tudo certo', 'tudo ok']

        if any(e in mensagem for e in emojis_conf) or any(p in msg_lower for p in palavras_conf):
            return ('full', list(range(1, total_itens + 1)))

        # 2. Adiar
        palavras_defer = ['não', 'nao', 'depois', 'ainda não', 'ainda nao', 'amanhã', 'amanha']
        if any(p in msg_lower for p in palavras_defer):
            return ('defer', [])

        # 3. Parcial (números)
        numeros = re.findall(r'\d+', mensagem)
        if numeros:
            indices = [int(n) for n in numeros]

            # Validar range
            invalidos = [i for i in indices if i < 1 or i > total_itens]
            if invalidos:
                return ('error', f"Números inválidos: {invalidos}. Use 1 a {total_itens}")

            # Remover duplicatas
            indices = list(set(indices))
            return ('partial', indices)

        # 4. Não reconhecido
        return ('invalid', "❓ Não entendi. Responda com:\n\n"
                          "✅ Emoji ou \"Ok\" para tudo\n"
                          "📝 Números das contas pagas (ex: \"1, 3\")\n"
                          "⏸️ \"Não\" para adiar")

    @staticmethod
    def mark_bills_as_paid(conn, usuario_id, bills_to_confirm):
        """
        Cria transações no banco para marcar contas como pagas.

        Args:
            conn: Conexão do banco
            usuario_id: ID do usuário
            bills_to_confirm: Lista de dicts do Agendamentos

        Returns:
            list: Lista de descrições confirmadas
        """
        confirmadas = []
        hoje = date.today()

        for bill in bills_to_confirm:
            # Determinar data de pagamento
            # Se venceu hoje/ontem, usar dia de vencimento
            # Senão, usar hoje (pagamento atrasado)
            dia_venc = bill['dia_execucao']

            if hoje.day - dia_venc <= 1 and hoje.day - dia_venc >= 0:
                data_tx = hoje.replace(day=dia_venc)
            else:
                # Pagamento atrasado - usar hoje
                data_tx = hoje

            # Criar fatura se cartão de crédito
            fatura_id = None
            if bill['tipo_conta'] == 'Cartão de Crédito':
                fatura_id = finance_service.get_or_create_fatura(
                    conn, bill['conta_id'], data_tx, usuario_id
                )

            # Valor negativo para despesas (ou positivo para receitas)
            if bill['nome_grupo'] == 'Renda':
                valor_db = abs(bill['valor_previsto'] or 0)
                tipo_tx = 'Renda'
            else:
                valor_db = (bill['valor_previsto'] or 0) * -1
                tipo_tx = 'Despesa'

            # Criar transação
            finance_service.create_transaction(
                conn,
                usuario_id,
                bill['conta_id'],
                bill['subcategoria_id'],
                fatura_id,
                bill['descricao'],
                valor_db,
                tipo_tx,
                data_tx
            )

            confirmadas.append(bill['descricao'])
            print(f"[CHECKIN-MARK] Criada transação: {bill['descricao']} - {valor_db}")

        return confirmadas

    @staticmethod
    def process_response(numero_whatsapp, mensagem_usuario, checkin_id):
        """
        Processa resposta do usuário ao check-in.

        Args:
            numero_whatsapp: Número do usuário
            mensagem_usuario: Texto da mensagem
            checkin_id: ID da sessão

        Returns:
            tuple: (status, resposta_texto)
            status: 'completed', 'deferred', 'error'
        """
        # Buscar sessão
        session_key = f"nightly_checkin:{numero_whatsapp}:{checkin_id}"
        session_data = redis_service.get(session_key)

        if not session_data:
            # CORRIGIDO (2026-01-07): Remover flag ativa para evitar loop infinito
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")
            return ('error', "⏱️ Esta sessão expirou. Aguarde o próximo check-in ou registre manualmente.")

        items_map = session_data['items']
        total_items = session_data['total_items']

        if total_items == 0:
            # Só tinha itens atrasados - remover flag para evitar loop
            redis_service.delete(session_key)
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")
            return ('error', "Não há itens recentes para confirmar. Use 'Pendencias' para ver contas antigas.")

        # Fazer parsing da resposta
        tipo, resultado = NightlyCheckinService.parse_checkin_response(
            mensagem_usuario, total_items
        )

        # Caso 1: Adiar
        if tipo == 'defer':
            # Limpar sessão
            redis_service.delete(session_key)
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")

            return ('deferred', "⏸️ Ok, te lembro amanhã no próximo check-in!\n\n"
                               "Você pode registrar os pagamentos manualmente a qualquer momento.")

        # Caso 2: Erro ou inválido
        if tipo in ['error', 'invalid']:
            # Encerrar sessão em caso de resposta inválida (evitar loop)
            redis_service.delete(session_key)
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")
            return ('error', resultado)

        # Caso 3: Confirmação (full ou partial)
        indices_confirmar = resultado

        # Buscar bills correspondentes
        from app import db_engine

        bills_to_confirm = []
        for idx in indices_confirmar:
            bill = items_map.get(str(idx))
            if bill:
                bills_to_confirm.append(bill)

        if not bills_to_confirm:
            return ('error', "Nenhum item válido para confirmar.")

        # Criar transações
        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    usuario_id = bills_to_confirm[0]['usuario_id']
                    confirmadas = NightlyCheckinService.mark_bills_as_paid(
                        conn, usuario_id, bills_to_confirm
                    )
        except Exception as e:
            print(f"[CHECKIN-ERROR] Erro ao criar transações: {e}")
            import traceback
            traceback.print_exc()
            return ('error', f"❌ Erro ao salvar transações: {str(e)}")

        # Limpar sessão
        redis_service.delete(session_key)
        redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")

        # Montar resposta
        if len(confirmadas) == total_items:
            resposta = f"✅ *Todas as {total_items} contas confirmadas!*\n\n"
        else:
            resposta = f"✅ *{len(confirmadas)} conta(s) confirmada(s):*\n\n"

        for desc in confirmadas:
            resposta += f"• {desc}\n"

        resposta += "\n💚 Transações registradas com sucesso!"

        return ('completed', resposta)
