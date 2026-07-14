# app/services/fixed_bills_service.py
"""
Serviço para gerenciar contas fixas/agendamentos e quitação antecipada
"""
from sqlalchemy import text
from datetime import date
from calendar import monthrange
from app.utils import formatar_moeda, formatar_mes_ano_pt
from rapidfuzz import fuzz, process

class FixedBillsService:
    """Gerencia contas fixas e suas quitações"""
    
    @staticmethod
    def get_pending_bills_for_month(conn, usuario_id, mes=None, ano=None):
        """
        Busca contas fixas que ainda não foram executadas no mês.
        
        Args:
            conn: Conexão do banco
            usuario_id: ID do usuário
            mes: Mês (1-12), padrão: mês atual
            ano: Ano (YYYY), padrão: ano atual
        
        Returns:
            Lista de agendamentos pendentes
        """
        hoje = date.today()
        mes = mes or hoje.month
        ano = ano or hoje.year
        
        # Primeiro e último dia do mês
        primeiro_dia = date(ano, mes, 1)
        ultimo_dia = date(ano, mes, monthrange(ano, mes)[1])
        
        sql = text("""
            SELECT
                a.id,
                a.descricao,
                a.valor_previsto,
                a.dia_execucao,
                a.tipo_agendamento,
                s.nome_sub as categoria,
                c.nome_conta,
                c.tipo_conta,
                g.nome_grupo,
                -- Verificar se já foi executado este mês
                (SELECT COUNT(*) FROM Transacoes t
                 WHERE t.descricao = a.descricao
                   AND t.usuario_id = a.usuario_id
                   AND t.data_transacao >= :primeiro_dia
                   AND t.data_transacao <= :ultimo_dia) as ja_executado
            FROM Agendamentos a
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            JOIN Contas c ON a.conta_id = c.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              -- CRÍTICO: Filtrar agendamentos anuais pelo mês correto
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes)
              )
            ORDER BY a.dia_execucao ASC
        """)
        
        result = conn.execute(sql, {
            "uid": usuario_id,
            "primeiro_dia": primeiro_dia,
            "ultimo_dia": ultimo_dia,
            "mes": mes
        }).fetchall()
        
        # Filtrar apenas os não executados
        pendentes = [row for row in result if row.ja_executado == 0]
        
        return pendentes
    
    @staticmethod
    def find_matching_bill(conn, usuario_id, descricao_pagamento, threshold=65):
        """
        Busca uma conta fixa usando fuzzy matching.

        Args:
            descricao_pagamento: Texto do pagamento (ex: "água", "net", "baba")
            threshold: Score mínimo de similaridade (0-100, padrão 65)

        Returns:
            (agendamento_id, descricao_original, valor_previsto, dia_execucao,
             tipo_agendamento, categoria, conta_id) ou None
        """
        # Buscar todas as contas fixas ativas do usuário
        sql = text("""
            SELECT
                a.id,
                a.descricao,
                a.valor_previsto,
                a.dia_execucao,
                a.tipo_agendamento,
                a.conta_id,
                s.nome_sub as categoria
            FROM Agendamentos a
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
        """)

        bills = conn.execute(sql, {"uid": usuario_id}).fetchall()

        if not bills:
            return None

        # Limpar descrição de pagamento (remover artigos)
        desc_limpa = descricao_pagamento.lower().strip()
        artigos = ['a ', 'o ', 'da ', 'do ', 'de ', 'conta de ', 'conta ']
        for artigo in artigos:
            if desc_limpa.startswith(artigo):
                desc_limpa = desc_limpa[len(artigo):].strip()

        # Criar dicionário de descrições para busca
        descricoes = {bill.descricao: bill for bill in bills}

        # Fuzzy matching com WRatio (melhor para strings curtas)
        result = process.extractOne(
            desc_limpa,
            descricoes.keys(),
            scorer=fuzz.WRatio,  # Melhor que token_sort_ratio para casos como "net"
            score_cutoff=threshold
        )

        if result:
            melhor_match, score, _ = result
            bill = descricoes[melhor_match]
            print(f"[FUZZY-MATCH] '{descricao_pagamento}' → '{melhor_match}' (score: {score})")

            return (
                bill.id,               # agendamento_id
                bill.descricao,        # descricao_original
                bill.valor_previsto,   # valor_previsto
                bill.dia_execucao,     # dia_execucao
                bill.tipo_agendamento, # tipo_agendamento (FIXO/LEMBRETE_VARIAVEL)
                bill.categoria,        # categoria
                bill.conta_id          # **IMPORTANTE**: conta_id para debitar
            )

        return None
    
    @staticmethod
    def settle_fixed_bill(conn, usuario_id, agendamento_id, valor_pago, data_pagamento, 
                         conta_pagamento_id=None, observacao=None):
        """
        Quita uma conta fixa antecipadamente (antes da data de vencimento).
        
        Args:
            agendamento_id: ID do agendamento
            valor_pago: Valor efetivamente pago
            data_pagamento: Data em que foi pago
            conta_pagamento_id: Conta usada para pagar (opcional)
            observacao: Observação adicional (opcional)
        
        Returns:
            transaction_id: ID da transação criada
        """
        # Buscar dados do agendamento
        sql_get_agendamento = text("""
            SELECT 
                a.descricao,
                a.conta_id,
                a.subcategoria_id,
                a.valor_previsto,
                c.tipo_conta
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            WHERE a.id = :aid AND a.usuario_id = :uid
        """)
        
        agendamento = conn.execute(sql_get_agendamento, {
            "aid": agendamento_id,
            "uid": usuario_id
        }).fetchone()
        
        if not agendamento:
            raise Exception(f"Agendamento {agendamento_id} não encontrado")
        
        descricao_original = agendamento.descricao
        conta_id = conta_pagamento_id or agendamento.conta_id
        subcategoria_id = agendamento.subcategoria_id
        tipo_conta = agendamento.tipo_conta
        
        # Preparar descrição com observação
        descricao_final = descricao_original
        if observacao:
            descricao_final = f"{descricao_original} ({observacao})"
        
        # Criar transação
        fatura_id = None
        if tipo_conta == 'Cartão de Crédito':
            from app.services.finance_service import get_or_create_fatura
            fatura_id = get_or_create_fatura(conn, conta_id, data_pagamento, usuario_id)
        
        valor_para_db = float(valor_pago) * -1  # Negativo = despesa
        
        sql_insert = text("""
            INSERT INTO Transacoes 
            (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao)
            VALUES (:uid, :cid, :scid, :fid, :desc, :val, 'Despesa', :data)
            RETURNING id
        """)
        
        result = conn.execute(sql_insert, {
            "uid": usuario_id,
            "cid": conta_id,
            "scid": subcategoria_id,
            "fid": fatura_id,
            "desc": descricao_final,
            "val": valor_para_db,
            "data": data_pagamento
        })
        
        transaction_id = result.scalar_one()
        
        print(f"[FIXED-BILLS] Conta fixa '{descricao_original}' quitada antecipadamente. Transaction ID: {transaction_id}")
        
        return transaction_id
    
    @staticmethod
    def list_pending_bills_formatted(conn, usuario_id):
        """
        Retorna uma lista formatada de contas fixas pendentes para WhatsApp.
        Separa despesas de receitas e identifica despesas do cartão de crédito.
        """
        pendentes = FixedBillsService.get_pending_bills_for_month(conn, usuario_id)

        if not pendentes:
            return "✅ Você não tem contas fixas pendentes este mês! 🎉"

        hoje = date.today()

        # Separar despesas normais, despesas de cartão e receitas
        despesas_normais = []
        despesas_cartao = []
        receitas = []

        for conta in pendentes:
            agendamento_id, descricao, valor_previsto, dia_execucao, tipo, categoria, conta_nome, tipo_conta, nome_grupo, _ = conta

            valor_float = float(valor_previsto or 0)

            # Calcular status
            try:
                data_vencimento = date(hoje.year, hoje.month, dia_execucao)
                dias_restantes = (data_vencimento - hoje).days

                if dias_restantes < 0:
                    status = "🔴 ATRASADO"
                    status_neutro = f"Debitado dia {dia_execucao}"
                elif dias_restantes == 0:
                    status = "⚠️ VENCE HOJE"
                    status_neutro = "Debitado hoje"
                elif dias_restantes <= 3:
                    status = f"🟡 Vence em {dias_restantes} dias"
                    status_neutro = f"Será debitado em {dias_restantes} dias"
                else:
                    status = f"🟢 Vence dia {dia_execucao}"
                    status_neutro = f"Será debitado dia {dia_execucao}"
            except ValueError:
                status = f"Vence dia {dia_execucao}"
                status_neutro = f"Debitado dia {dia_execucao}"

            item = {
                'descricao': descricao,
                'valor': valor_float,
                'categoria': categoria,
                'status': status,
                'status_neutro': status_neutro
            }

            if nome_grupo == 'Renda':
                receitas.append(item)
            elif tipo_conta == 'Cartão de Crédito':
                despesas_cartao.append(item)
            else:
                despesas_normais.append(item)

        # Montar resposta
        resposta = f"📋 *CONTAS FIXAS PENDENTES - {formatar_mes_ano_pt(hoje).upper()}* 📋\n\n"

        total_despesas_normais = 0
        total_despesas_cartao = 0
        total_receitas = 0

        # Despesas Normais
        if despesas_normais:
            resposta += "💸 *DESPESAS:*\n\n"
            for idx, item in enumerate(despesas_normais, 1):
                total_despesas_normais += item['valor']
                resposta += f"{idx}. {item['descricao']}\n"
                resposta += f"   💰 {formatar_moeda(item['valor'])}\n"
                resposta += f"   📊 {item['categoria']}\n"
                resposta += f"   {item['status']}\n\n"

        # Despesas do Cartão (Lembretes Informativos)
        if despesas_cartao:
            resposta += "💳 *CARTÃO DE CRÉDITO (na fatura):*\n\n"
            for idx, item in enumerate(despesas_cartao, 1):
                total_despesas_cartao += item['valor']
                resposta += f"{idx}. {item['descricao']}\n"
                resposta += f"   💰 {formatar_moeda(item['valor'])}\n"
                resposta += f"   📊 {item['categoria']}\n"
                resposta += f"   ℹ️ {item['status_neutro']}\n\n"

        # Receitas
        if receitas:
            resposta += "💰 *RECEITAS:*\n\n"
            for idx, item in enumerate(receitas, 1):
                total_receitas += item['valor']
                resposta += f"{idx}. {item['descricao']}\n"
                resposta += f"   💵 {formatar_moeda(item['valor'])}\n"
                resposta += f"   📊 {item['categoria']}\n"
                resposta += f"   {item['status']}\n\n"

        # Totalizadores
        resposta += "━━━━━━━━━━━━━━━━━━━━\n"
        if despesas_normais:
            resposta += f"💸 Total Despesas: {formatar_moeda(total_despesas_normais)}\n"
        if despesas_cartao:
            resposta += f"💳 Total Cartão: {formatar_moeda(total_despesas_cartao)}\n"
        if receitas:
            resposta += f"💰 Total Receitas: {formatar_moeda(total_receitas)}\n"
        if despesas_normais or despesas_cartao or receitas:
            total_geral_despesas = total_despesas_normais + total_despesas_cartao
            saldo = total_receitas - total_geral_despesas
            resposta += f"📊 Saldo Líquido: {formatar_moeda(saldo)}"

        return resposta
    
    @staticmethod
    def get_all_scheduled_bills(conn, usuario_id):
        """
        Busca TODOS os agendamentos ativos do usuário, sem filtro de mês.
        Retorna o catálogo completo de contas recorrentes (mensal, semanal,
        quinzenal, anual, etc.).
        """
        sql = text("""
            SELECT
                a.id,
                a.descricao,
                a.valor_previsto,
                a.dia_execucao,
                a.mes_execucao,
                a.periodicidade,
                a.tipo_agendamento,
                s.nome_sub as categoria,
                c.nome_conta,
                c.tipo_conta,
                g.nome_grupo
            FROM Agendamentos a
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            JOIN Contas c ON a.conta_id = c.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL', 'PARCELADO')
            ORDER BY
                CASE a.periodicidade
                    WHEN 'SEMANAL'    THEN 1
                    WHEN 'QUINZENAL'  THEN 2
                    WHEN 'MENSAL'     THEN 3
                    WHEN 'ANUAL'      THEN 4
                    ELSE 5
                END,
                a.dia_execucao ASC
        """)

        return conn.execute(sql, {"uid": usuario_id}).fetchall()

    @staticmethod
    def list_all_bills_formatted(conn, usuario_id):
        """
        Catálogo completo de todas as contas recorrentes ativas do usuário,
        agrupadas por periodicidade. Independe do mês corrente ou status de pagamento.
        """
        MESES_PT = {
            1: 'Janeiro', 2: 'Fevereiro', 3: 'Março', 4: 'Abril',
            5: 'Maio', 6: 'Junho', 7: 'Julho', 8: 'Agosto',
            9: 'Setembro', 10: 'Outubro', 11: 'Novembro', 12: 'Dezembro'
        }
        LABEL_PERIODICIDADE = {
            'SEMANAL':   '🔄 SEMANAL',
            'QUINZENAL': '🔄 QUINZENAL',
            'MENSAL':    '📅 MENSAL',
            'ANUAL':     '📆 ANUAL',
        }

        todas = FixedBillsService.get_all_scheduled_bills(conn, usuario_id)

        if not todas:
            return "📋 Você não tem contas recorrentes cadastradas."

        # Agrupar por periodicidade → tipo (despesa/cartão/receita)
        grupos = {}  # periodicidade → {'despesas': [], 'cartao': [], 'receitas': []}

        for row in todas:
            (agend_id, descricao, valor_previsto, dia_execucao, mes_execucao,
             periodicidade, tipo_agend, categoria, conta_nome, tipo_conta, nome_grupo) = row

            valor_float = float(valor_previsto or 0)
            period = periodicidade or 'MENSAL'

            # Montar descrição do vencimento
            if period == 'SEMANAL':
                vencimento = f"toda semana (dia {dia_execucao})"
            elif period == 'QUINZENAL':
                vencimento = f"quinzenal (dia {dia_execucao})"
            elif period == 'ANUAL':
                mes_nome = MESES_PT.get(mes_execucao, str(mes_execucao)) if mes_execucao else '?'
                vencimento = f"{mes_nome}, dia {dia_execucao}"
            else:
                vencimento = f"dia {dia_execucao}"

            item = {
                'descricao': descricao,
                'valor': valor_float,
                'categoria': categoria,
                'vencimento': vencimento,
                'tipo_conta': tipo_conta,
                'nome_grupo': nome_grupo,
            }

            if period not in grupos:
                grupos[period] = {'despesas': [], 'cartao': [], 'receitas': []}

            if nome_grupo == 'Renda':
                grupos[period]['receitas'].append(item)
            elif tipo_conta == 'Cartão de Crédito':
                grupos[period]['cartao'].append(item)
            else:
                grupos[period]['despesas'].append(item)

        resposta = "📋 *TODAS AS SUAS CONTAS RECORRENTES* 📋\n\n"

        # Mesmo fator de normalização usado em /api/bills/summary (dashboard),
        # para que o total aqui bata com o card "Gastos Mensais" do site.
        # ANUAL fica de fora (contabilizada separadamente, não é gasto mensal).
        FATOR_MENSAL = {
            'MENSAL': 1.0,
            'SEMANAL': 4.33,
            'QUINZENAL': 2.16,
            'DIARIA': 30.0,
        }

        total_mensal_estimado = 0.0
        ORDER = ['SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']

        for period in ORDER:
            if period not in grupos:
                continue

            label = LABEL_PERIODICIDADE.get(period, period)
            g = grupos[period]
            todas_itens = g['despesas'] + g['cartao'] + g['receitas']
            if not todas_itens:
                continue

            fator = FATOR_MENSAL.get(period)

            resposta += f"*{label}*\n"
            resposta += "─────────────────────\n"

            if g['despesas']:
                resposta += "💸 *Despesas:*\n"
                for item in g['despesas']:
                    resposta += f"  • {item['descricao']}\n"
                    resposta += f"    {formatar_moeda(item['valor'])} · {item['vencimento']}\n"
                    if fator is not None:
                        total_mensal_estimado += item['valor'] * fator
                resposta += "\n"

            if g['cartao']:
                resposta += "💳 *Cartão de Crédito:*\n"
                for item in g['cartao']:
                    resposta += f"  • {item['descricao']}\n"
                    resposta += f"    {formatar_moeda(item['valor'])} · {item['vencimento']}\n"
                    if fator is not None:
                        total_mensal_estimado += item['valor'] * fator
                resposta += "\n"

            if g['receitas']:
                resposta += "💰 *Receitas:*\n"
                for item in g['receitas']:
                    resposta += f"  • {item['descricao']}\n"
                    resposta += f"    {formatar_moeda(item['valor'])} · {item['vencimento']}\n"
                resposta += "\n"

        if total_mensal_estimado:
            resposta += "━━━━━━━━━━━━━━━━━━━━\n"
            resposta += f"💸 Total despesas mensais: {formatar_moeda(total_mensal_estimado)}\n"
            resposta += "_(semanais e quinzenais convertidas para média mensal; anuais ficam de fora)_"

        return resposta

    @staticmethod
    def mark_bill_as_paid_response(descricao, valor_pago, categoria):
        """Formata mensagem de confirmação de pagamento"""
        return (
            f"✅ *CONTA QUITADA!* ✅\n\n"
            f"📝 {descricao}\n"
            f"💰 {formatar_moeda(valor_pago)}\n"
            f"📊 {categoria}\n\n"
            f"_Esta conta não será mais cobrada automaticamente este mês._"
        )