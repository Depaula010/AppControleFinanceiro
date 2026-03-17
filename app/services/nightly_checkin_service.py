"""
NightlyCheckinService — Serviço de Check-in Noturno
====================================================

RESPONSABILIDADE
----------------
Centraliza toda a lógica de coleta de dados financeiros pendentes e gerencia
o fluxo de confirmação de pagamentos via WhatsApp.

É a FONTE DA VERDADE para dados financeiros do sistema: tanto o job automático
quanto as intenções manuais do chatbot consomem os mesmos métodos, garantindo
que o usuário veja exatamente os mesmos dados em qualquer canal.


FLUXO COMPLETO
--------------

1. COLETA (collect_financial_snapshot)
   ├── pending_bills  → Agendamentos vencidos há ≤ 7 dias e ainda não pagos
   │                    (query: get_contas_pendentes_checkin_noturno)
   ├── overdue_bills  → Agendamentos vencidos há > 7 dias e não pagos
   │                    (query: get_contas_atrasadas_checkin_noturno)
   │                    deduplicados contra pending_bills pelo agendamento.id
   ├── overdue_invoices    → Faturas de cartão com data_vencimento < hoje
   └── invoices_due_today  → Faturas de cartão que vencem exatamente hoje

2. SESSÃO (create_checkin_session)
   ├── Recebe: all_bills = pending_bills + overdue_bills
   ├── Filtra: exclui Cartão de Crédito + Despesa (não confirmáveis via agendamento)
   ├── Ordena: receitas primeiro (1..R), despesas depois (R+1..N)
   ├── Serializa: date → ISO string, Decimal → float (para salvar no Redis)
   └── Persiste no Redis com TTL de 1 hora:
       • nightly_checkin:{numero}:{checkin_id}  → {items: {idx: bill}, total_items: N}
       • nightly_checkin_active:{numero}         → checkin_id

3. MENSAGEM (format_consolidated_checkin_message)
   Modos:
   • checkin_id fornecido  → "🌙 CHECK-IN NOTURNO" (job automático, interativo)
   • checkin_id = None     → "📊 RESUMO FINANCEIRO" (intenção manual, read-only)

   Seções da mensagem (em ordem):
   ┌─ 💳 DÉBITO CARTÃO       → informativo, sem número (lembretes_cartao)
   ├─ 💵 RECEITAS PENDENTES  → numeradas 1..R (receitas_pendentes + receitas_atrasadas)
   ├─ 💸 DESPESAS PENDENTES  → numeradas R+1..P (despesas_pendentes, sem CC)
   ├─ 🔴 DESPESAS ATRASADAS  → numeradas P+1..N (despesas_atrasadas, > 7 dias)
   ├─ 🔴 FATURAS VENCIDAS    → bullet "•" informativo (NÃO numerado, NÃO confirmável)
   ├─ ⏰ FATURAS HOJE        → bullet informativo, alerta preventivo
   └─ 🔹 COMO RESPONDER      → exibido sempre que há item numerado (any_confirmable)

   INVARIANTE: número exibido na mensagem == índice na sessão Redis.

4. RESPOSTA (process_response)
   ├── Busca sessão no Redis
   ├── Chama parse_checkin_response():
   │   • "ok" / 👍 / ✅  → full  → [1..N]
   │   • "não" / depois  → defer → [] (encerra sessão)
   │   • "1" / "1,3"     → partial → [1, 3]
   │   • inválido        → error  → encerra sessão (evita loop)
   ├── Chama mark_bills_as_paid() dentro de conn.begin()
   └── Limpa sessão Redis (inclusive em caso de exceção — evita loop infinito)

5. PAGAMENTO (mark_bills_as_paid)
   ├── Determina data_tx via data_vencimento_real (ISO string → date)
   │   • venceu hoje ou ontem → data_tx = data_vencimento_real
   │   • vencido há mais      → data_tx = hoje (pagamento tardio)
   ├── Se CC: cria/busca fatura via finance_service.get_or_create_fatura
   ├── Cria transação via finance_service.create_transaction (com agendamento_id)
   └── Se PARCELADO: incrementa parcelas_executadas; desativa se concluído


REGRAS DE VERIFICAÇÃO DE PAGAMENTO (nas queries SQL)
-----------------------------------------------------
Ambas as queries de pending e overdue usam a mesma lógica de NOT EXISTS:
  Regra 1 → pagamento no mesmo mês/ano do vencimento  (caso normal)
  Regra 2 → pagamento em até 20 dias após o vencimento (pagamento tardio cross-month)
Pagamentos de meses ANTERIORES ao vencimento são ignorados (não "bloqueiam" o mês corrente).


REGRAS DE NEGÓCIO — O QUE NÃO APARECE
---------------------------------------
• Cartão de Crédito + Despesa Fixa em pending_bills → seção "DÉBITO CARTÃO" (informativo)
  Motivo: já é cobrado automaticamente na fatura; não precisa confirmação manual.
• overdue_bills (query) já exclui CC+Despesa com dia_vencimento configurado.
• Agendamentos anuais só aparecem no mês do mes_execucao.
• Deduplicação: agendamento que aparece em pending_bills é removido de overdue_bills.


CHAVES REDIS
------------
nightly_checkin:{numero_whatsapp}:{checkin_id}   TTL 1h — dados da sessão
nightly_checkin_active:{numero_whatsapp}          TTL 1h — checkin_id ativo


USOS
----
• app/jobs/nightly_checkin.py           → job automático (Ofelia cron)
• app/routes/webhooks/handlers/         → processa resposta WhatsApp
• app/routes/webhooks/intents/          → intenção "Contas atrasadas" (read-only)


HISTÓRICO DE CORREÇÕES
----------------------
2026-01-06  data_vencimento_real via CTE (corrige virada de mês)
2026-01-07  Filtrar CC despesas da numeração; lembretes_cartao informativos
2026-01-08  Receitas confirmáveis; serialização JSON; agendamento_id nas transações
2026-01-11  collect_financial_snapshot() como fonte única; modo read-only
2026-03-11  NOT EXISTS usa mês/ano + janela 20 dias (fix falso-positivo cross-month)
2026-03-17  [BUG FIX] create_checkin_session filtra CC despesas (índice = mensagem)
            [BUG FIX] overdue_invoices bullet "•" em vez de número (não estão na sessão)
            [BUG FIX] instruções exibidas para qualquer item numerado (not only despesas)
            [BUG FIX] sessão Redis limpa em exceção de mark_bills_as_paid (evita loop)
            [BUG FIX] mark_bills_as_paid usa data_vencimento_real para data da transação
"""

from app.services.redis_service import redis_service
from app.services import finance_service
from app.services.queries import AgendamentosQueries, FaturasQueries
from app.utils import formatar_moeda
from datetime import date, timedelta
from sqlalchemy import text
import uuid
import re
import calendar


class NightlyCheckinService:
    """
    Gerencia o fluxo de check-in noturno para contas pendentes.

    FONTE DA VERDADE para dados financeiros do sistema.

    Métodos principais:
    - collect_financial_snapshot(): Coleta snapshot completo (Job + Intenções)
    - format_consolidated_checkin_message(): Formata mensagem (com/sem interatividade)
    - process_response(): Processa confirmações do usuário
    - mark_bills_as_paid(): Cria transações para contas confirmadas
    """

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
    def collect_financial_snapshot(conn, usuario_id, target_date=None):
        """
        Coleta snapshot completo das finanças do usuário.
        FONTE DA VERDADE para Job Noturno e Intenções WhatsApp.

        Este método centraliza TODA a lógica de busca de dados financeiros,
        garantindo que Job e Intenções retornem exatamente os mesmos dados.

        Args:
            conn: Conexão do banco
            usuario_id: ID do usuário
            target_date: Data alvo (padrão: hoje)

        Returns:
            dict: {
                'pending_bills': list,      # Contas pendentes (últimos 7 dias)
                'overdue_bills': list,      # Contas atrasadas (>7 dias)
                'overdue_invoices': list,   # Faturas vencidas
                'invoices_due_today': list  # Faturas vencendo hoje (alerta)
            }
        """
        if target_date is None:
            target_date = date.today()

        hoje = target_date

        # 1. Receitas/despesas pendentes (últimos 7 dias)
        pending_bills = NightlyCheckinService.get_pending_bills(conn, usuario_id, hoje)

        # 2. Contas atrasadas (>7 dias) - USA A MESMA QUERY DO JOB
        sql_overdue = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
        params_overdue = AgendamentosQueries.get_parametros_padrao(usuario_id, hoje)
        params_overdue["hoje"] = hoje
        params_overdue["data_minima"] = hoje - timedelta(days=30)

        result_overdue = conn.execute(sql_overdue, params_overdue).fetchall()
        overdue_bills = [dict(row._mapping) for row in result_overdue]

        # Deduplicação: remove de overdue qualquer agendamento que já aparece em pending.
        # Isso evita que o mesmo agendamento apareça nas duas seções quando o mês anterior
        # está inadimplente E o mês atual vence hoje/esta semana (ex: dia_execucao=15, hoje=15/03).
        pending_ids = {b['id'] for b in pending_bills}
        overdue_bills = [b for b in overdue_bills if b['id'] not in pending_ids]

        # 3. Faturas vencidas
        sql_invoices = FaturasQueries.get_faturas_vencidas()
        params_invoices = FaturasQueries.get_parametros_padrao(usuario_id, hoje)

        result_invoices = conn.execute(sql_invoices, params_invoices).fetchall()
        overdue_invoices = [dict(row._mapping) for row in result_invoices]

        # 4. Faturas vencendo hoje (alerta preventivo)
        sql_faturas_hoje = FaturasQueries.get_faturas_vencendo_hoje()
        params_faturas_hoje = FaturasQueries.get_parametros_padrao(usuario_id, hoje)

        result_faturas_hoje = conn.execute(sql_faturas_hoje, params_faturas_hoje).fetchall()
        invoices_due_today = [dict(row._mapping) for row in result_faturas_hoje]

        return {
            'pending_bills': pending_bills,
            'overdue_bills': overdue_bills,
            'overdue_invoices': overdue_invoices,
            'invoices_due_today': invoices_due_today
        }

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

        CORRIGIDO (2026-01-08): Todas as contas (receitas + despesas) são confirmáveis,
        independente de quantos dias de atraso. Antes, apenas contas com <=3 dias eram salvas.

        Args:
            numero_whatsapp: Número do usuário
            pending_bills: Lista de contas pendentes (receitas + despesas)

        Returns:
            str: checkin_id (UUID)
        """
        checkin_id = str(uuid.uuid4())[:8]
        hoje = date.today()

        # CORRIGIDO (2026-01-08): Separar receitas e despesas para salvar NA MESMA ORDEM da mensagem
        # (mensagem mostra: receitas primeiro, depois despesas)
        # CORRIGIDO (2026-03-17): Filtrar CC despesas igual à mensagem (evita índice diferente)
        receitas = [b for b in pending_bills if b.get('nome_grupo') == 'Renda']
        despesas = [b for b in pending_bills
                    if b.get('nome_grupo') != 'Renda'
                    and not (b.get('tipo_conta') == 'Cartão de Crédito'
                             and b.get('nome_grupo') == 'Despesa')]

        # IMPORTANTE: Concatenar na ordem correta (receitas → despesas)
        bills_ordenadas = receitas + despesas

        itens_recentes = {}
        idx = 1
        for bill in bills_ordenadas:
            status, dias_atraso = NightlyCheckinService.categorize_by_delay(bill, hoje)

            # IMPORTANTE: Criar cópia para não modificar o dicionário original
            # (o original ainda será usado em format_consolidated_checkin_message)
            bill_copy = bill.copy()

            # Salvar status para formatação visual
            bill_copy['status_text'] = status if status else f"Atrasado {dias_atraso} dias"
            bill_copy['dias_atraso'] = dias_atraso

            # CORRIGIDO (2026-01-08): Converter tipos não-serializáveis para JSON
            # - date -> string (ISO format)
            # - Decimal -> float
            if 'data_vencimento_real' in bill_copy and bill_copy['data_vencimento_real']:
                from datetime import date as date_type
                if isinstance(bill_copy['data_vencimento_real'], date_type):
                    bill_copy['data_vencimento_real'] = bill_copy['data_vencimento_real'].isoformat()

            # Converter Decimal para float
            from decimal import Decimal
            for key, value in bill_copy.items():
                if isinstance(value, Decimal):
                    bill_copy[key] = float(value)

            # TODAS as contas são salvas na sessão (receitas + despesas)
            itens_recentes[str(idx)] = bill_copy
            idx += 1

        session_data = {
            'items': itens_recentes,
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
        print(f"[CHECKIN-SESSION] {len(receitas)} receita(s) + {len(despesas)} despesa(s) = {len(itens_recentes)} total")
        print(f"[CHECKIN-SESSION] Ordem: receitas (1-{len(receitas)}), despesas ({len(receitas)+1}-{len(itens_recentes)})")

        return checkin_id

    @staticmethod
    def format_consolidated_checkin_message(pending_bills, overdue_bills, bills_due_today, overdue_invoices, faturas_vencendo_hoje, checkin_id):
        """
        Formata mensagem consolidada de check-in com TODAS as informações em uma única mensagem.
        Melhora UX evitando múltiplas notificações fragmentadas.

        CORRIGIDO (2026-01-08): Receitas agora são confirmáveis via check-in (antes eram apenas informativas).
        CORRIGIDO (2026-01-11): Suporta modo read-only quando checkin_id=None (usado por intenções WhatsApp).
        CORRIGIDO (2026-01-11): Título contextual - "CHECK-IN NOTURNO" (job) vs "RESUMO FINANCEIRO" (intenção).

        Args:
            pending_bills: Lista de contas pendentes (últimos 7 dias) - receitas + despesas
            overdue_bills: Lista de contas atrasadas (>7 dias)
            bills_due_today: Lista de contas que vencem hoje
            overdue_invoices: Lista de faturas vencidas (passado)
            faturas_vencendo_hoje: Lista de faturas que vencem HOJE
            checkin_id: ID da sessão Redis (se None, modo read-only com título "RESUMO FINANCEIRO")

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

            # NOVO (2026-01-07): Mostrar número da parcela para agendamentos parcelados
            descricao = bill['descricao']
            if bill.get('tipo_agendamento') == 'PARCELADO' and bill.get('total_parcelas'):
                parcela_atual = bill.get('parcelas_executadas', 0) + 1
                descricao = f"{descricao} ({parcela_atual}/{bill['total_parcelas']})"

            item = {
                'numero': idx,
                'descricao': descricao,
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

        # Processar contas atrasadas (>7 dias)
        # CORRIGIDO (2026-03-17): Filtrar CC despesas igual ao loop de pending
        # (sessão também as filtra — manter consistência)
        for bill in overdue_bills:
            if bill.get('tipo_conta') == 'Cartão de Crédito' and bill.get('nome_grupo') == 'Despesa':
                continue
            # NOVO (2026-01-07): Mostrar número da parcela para agendamentos parcelados
            descricao = bill['descricao']
            if bill.get('tipo_agendamento') == 'PARCELADO' and bill.get('total_parcelas'):
                parcela_atual = bill.get('parcelas_executadas', 0) + 1
                descricao = f"{descricao} ({parcela_atual}/{bill['total_parcelas']})"

            item = {
                'descricao': descricao,
                'valor': bill['valor_previsto'] or 0,
                'data_vencimento': bill.get('data_vencimento_real')
            }

            if bill['nome_grupo'] == 'Renda':
                receitas_atrasadas.append(item)
            else:
                despesas_atrasadas.append(item)

        # Se não há nada para mostrar, retornar None
        if not despesas_pendentes and not receitas_pendentes and not despesas_atrasadas and not receitas_atrasadas and not overdue_invoices and not lembretes_cartao and not faturas_vencendo_hoje:
            return None

        # Construir mensagem consolidada
        # CORRIGIDO (2026-01-11): Título contextual baseado no modo
        if checkin_id:
            msg = "🌙 *CHECK-IN NOTURNO* 🌙\n\n"
        else:
            msg = "📊 *RESUMO FINANCEIRO* 📊\n\n"

        # 0. DÉBITO CARTÃO DE CRÉDITO (informativo - não numerado)
        if lembretes_cartao:
            msg += "💳 *DÉBITO CARTÃO DE CRÉDITO*\n"
            for item in lembretes_cartao:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f" * {item['descricao']} - {valor_fmt} - Será debitado hoje\n"
            msg += "\n"

        # Numeração contínua para todas as seções (RECEITAS E DESPESAS são confirmáveis)
        numero_global = 1

        # 1. RECEITAS PENDENTES (CONFIRMÁVEL - numerado)
        # CORRIGIDO (2026-01-08): Receitas agora são confirmáveis, não apenas informativas
        if receitas_pendentes or receitas_atrasadas:
            msg += "💵 *RECEITAS PENDENTES:*\n"
            msg += "_Valores previstos que ainda não foram recebidos - confirmáveis via check-in_\n\n"

            total_receitas = 0

            # Receitas recentes (últimos 7 dias)
            for item in receitas_pendentes:
                valor_fmt = formatar_moeda(item['valor'])
                total_receitas += item['valor']
                msg += f"{numero_global}. {item['descricao']} - {valor_fmt} - Previsto em {item['dia_execucao']:02d}/{hoje.month:02d}\n"
                numero_global += 1

            # Receitas atrasadas (>7 dias)
            for item in receitas_atrasadas:
                valor_fmt = formatar_moeda(item['valor'])
                total_receitas += item['valor']
                if item['data_vencimento']:
                    msg += f"{numero_global}. {item['descricao']} - {valor_fmt} - Previsto em {item['data_vencimento'].strftime('%d/%m/%Y')}\n"
                else:
                    msg += f"{numero_global}. {item['descricao']} - {valor_fmt}\n"
                numero_global += 1

            msg += f"💰 *Total:* {formatar_moeda(total_receitas)}\n\n"

        # 2. DESPESAS PENDENTES (interativo - continua numeração)
        if despesas_pendentes:
            msg += "💸 *DESPESAS PENDENTES:*\n"
            for item in despesas_pendentes:
                valor_fmt = formatar_moeda(item['valor'])
                msg += f"{numero_global}. {item['descricao']} - {valor_fmt} ({item['conta']}) - {item['status']}\n"
                numero_global += 1
            msg += "\n"

        # 3. DESPESAS ATRASADAS (>7 dias) - continua numeração
        if despesas_atrasadas:
            msg += "🔴 *DESPESAS ATRASADAS (há mais de 7 dias):*\n"
            total_atrasado = 0
            for item in despesas_atrasadas:
                valor_fmt = formatar_moeda(item['valor'])
                total_atrasado += item['valor']
                if item['data_vencimento']:
                    dias_atraso = (hoje - item['data_vencimento']).days
                    # Validação: só mostra se realmente está atrasado (dias > 0)
                    if dias_atraso > 0:
                        msg += f"{numero_global}. {item['descricao']} - {valor_fmt} - Venceu em {item['data_vencimento'].strftime('%d/%m/%Y')} ({dias_atraso} dias) ⚠️\n"
                    else:
                        # Sanity check: data futura não deveria estar aqui (bug na query)
                        msg += f"{numero_global}. {item['descricao']} - {valor_fmt} - Previsto para {item['data_vencimento'].strftime('%d/%m/%Y')}\n"
                else:
                    msg += f"{numero_global}. {item['descricao']} - {valor_fmt}\n"
                numero_global += 1

            msg += f"\n💸 *Total atrasado:* {formatar_moeda(total_atrasado)}\n"

        # 4. FATURAS VENCIDAS (informativo - não confirmável via check-in)
        if overdue_invoices:
            msg += "🔴 *FATURAS VENCIDAS:*\n"
            for fatura in overdue_invoices:
                valor_fmt = formatar_moeda(fatura.get('valor_total', 0))
                msg += f"• {fatura['nome_conta']} - {valor_fmt} - Venceu em {fatura['data_vencimento'].strftime('%d/%m/%Y')}\n"
            msg += "\n"

        # 4.5. FATURAS QUE VENCEM HOJE (alerta preventivo - não numerado)
        if faturas_vencendo_hoje:
            msg += "⏰ *FATURAS QUE VENCEM HOJE:*\n"
            msg += "_Atenção! Estas faturas devem ser pagas hoje para evitar juros._\n\n"
            for fatura in faturas_vencendo_hoje:
                valor_fmt = formatar_moeda(fatura.get('valor_total', 0))
                nome_cartao = fatura['nome_conta']
                msg += f"💳 {nome_cartao} - {valor_fmt} - Vence HOJE\n"
            msg += "\n"

        # 5. INSTRUÇÕES (para qualquer item confirmável numerado)
        # CORRIGIDO (2026-01-11): Modo condicional baseado em checkin_id
        # CORRIGIDO (2026-03-17): Mostrar instruções sempre que houver item numerado
        any_confirmable = bool(despesas_pendentes or receitas_pendentes or
                               despesas_atrasadas or receitas_atrasadas)
        if any_confirmable:
            if checkin_id:
                # Modo interativo (job noturno): instruções de confirmação
                msg += "━━━━━━━━━━━━━━━━━━━━\n"
                msg += "🔹 *COMO RESPONDER:*\n\n"
                msg += "✅ Tudo pago:\n"
                msg += "👍 ou \"Ok\"\n\n"
                msg += "📝 Pagamento parcial:\n"
                msg += "\"1\" / \"1 e 3\" / \"1, 2, 4\"\n\n"
                msg += "⏸️ Não paguei ainda:\n"
                msg += "\"Depois\" / \"Não\"\n\n"
                msg += f"_ID: {checkin_id} | Expira em 1h_"
            else:
                # Modo read-only (intenção manual): sem interatividade
                msg += "\n━━━━━━━━━━━━━━━━━━━━\n"
                msg += "_💡 Para confirmar pagamentos, aguarde o check-in noturno automático._"

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
            # CORRIGIDO (2026-03-17): Usar data_vencimento_real quando disponível (mais preciso)
            # Na sessão Redis, data_vencimento_real foi convertida para string ISO (ex: "2026-03-15")
            data_venc_real = bill.get('data_vencimento_real')
            if data_venc_real:
                if isinstance(data_venc_real, str):
                    from datetime import date as date_type
                    data_venc_real = date_type.fromisoformat(data_venc_real)
                dias_atraso_real = (hoje - data_venc_real).days
                data_tx = data_venc_real if dias_atraso_real <= 1 else hoje
            else:
                # Fallback: calcular por dia_execucao (pode falhar em cross-month)
                dia_venc = bill['dia_execucao']
                if 0 <= hoje.day - dia_venc <= 1:
                    data_tx = hoje.replace(day=dia_venc)
                else:
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

            # Criar transação vinculada ao agendamento
            # CORRIGIDO (2026-01-08): Passar agendamento_id para vincular transação ao agendamento original
            finance_service.create_transaction(
                conn,
                usuario_id,
                bill['conta_id'],
                bill['subcategoria_id'],
                fatura_id,
                bill['descricao'],
                valor_db,
                tipo_tx,
                data_tx,
                agendamento_id=bill.get('id')  # Vincular ao agendamento
            )

            confirmadas.append(bill['descricao'])
            print(f"[CHECKIN-MARK] Criada transação: {bill['descricao']} - {valor_db} (agendamento_id={bill.get('id')})")

            # NOVO (2026-01-08): Atualizar parcelas para agendamentos PARCELADO
            if bill.get('tipo_agendamento') == 'PARCELADO':
                nova_parcela = bill.get('parcelas_executadas', 0) + 1
                total = bill.get('total_parcelas')

                # Desativar se completou todas as parcelas
                desativar = (total is not None and nova_parcela >= total)

                sql_update_ag = text("""
                    UPDATE Agendamentos
                    SET parcelas_executadas = :nova_parcela,
                        ativo = :novo_ativo,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = :ag_id
                """)
                conn.execute(sql_update_ag, {
                    "nova_parcela": nova_parcela,
                    "novo_ativo": not desativar,
                    "ag_id": bill['id']
                })

                print(f"[CHECKIN-PARCELADO] Parcela {nova_parcela}/{total} confirmada. Desativado: {desativar}")

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

        # DEBUG (2026-01-08): Adicionar log detalhado
        print(f"[CHECKIN-RESPONSE] Buscando sessão: {session_key}")
        session_data = redis_service.get(session_key)
        print(f"[CHECKIN-RESPONSE] Sessão encontrada: {session_data is not None}")

        if session_data:
            print(f"[CHECKIN-RESPONSE] Total items na sessão: {session_data.get('total_items', 0)}")
            print(f"[CHECKIN-RESPONSE] Items keys: {list(session_data.get('items', {}).keys())}")

        if not session_data:
            # CORRIGIDO (2026-01-07): Remover flag ativa para evitar loop infinito
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")
            print(f"[CHECKIN-RESPONSE] ERRO: Sessão não encontrada no Redis")
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

        # DEBUG (2026-01-08): Log detalhado dos índices a confirmar
        print(f"[CHECKIN-RESPONSE] Índices a confirmar: {indices_confirmar}")
        print(f"[CHECKIN-RESPONSE] Items disponíveis na sessão: {list(items_map.keys())}")

        # Buscar bills correspondentes
        from app import db_engine

        bills_to_confirm = []
        for idx in indices_confirmar:
            bill = items_map.get(str(idx))
            if bill:
                print(f"[CHECKIN-RESPONSE] Índice {idx} -> {bill.get('descricao')} ({bill.get('nome_grupo')})")
                bills_to_confirm.append(bill)
            else:
                print(f"[CHECKIN-RESPONSE] AVISO: Índice {idx} não encontrado na sessão!")

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
            # CORRIGIDO (2026-03-17): Limpar sessão em caso de erro para evitar loop infinito
            redis_service.delete(session_key)
            redis_service.delete(f"nightly_checkin_active:{numero_whatsapp}")
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
