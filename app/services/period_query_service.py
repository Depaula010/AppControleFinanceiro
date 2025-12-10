# app/services/period_query_service.py
"""
Serviço para consultas de gastos por período (ontem, hoje, final de semana, etc.)
"""
from sqlalchemy import text
from datetime import date, datetime, timedelta
from app.utils import formatar_moeda, formatar_mes_ano_pt

class PeriodQueryService:
    """Gerencia consultas de gastos por períodos específicos"""
    
    @staticmethod
    def get_period_dates(period_type):
        """
        Calcula as datas de início e fim baseado no tipo de período.
        
        Args:
            period_type: 'ontem', 'hoje', 'final_de_semana', 'semana_passada', 
                        'ultimos_7_dias', 'este_mes', 'mes_passado'
        
        Returns:
            (data_inicio, data_fim, descricao_periodo)
        """
        hoje = date.today()
        
        if period_type == 'ontem':
            ontem = hoje - timedelta(days=1)
            return ontem, ontem, f"ontem ({ontem.strftime('%d/%m/%Y')})"
        
        elif period_type == 'hoje':
            return hoje, hoje, f"hoje ({hoje.strftime('%d/%m/%Y')})"
        
        elif period_type == 'final_de_semana':
            # Calcular último final de semana (sábado e domingo)
            dias_desde_domingo = (hoje.weekday() + 1) % 7
            
            if dias_desde_domingo == 0:  # Hoje é domingo
                domingo = hoje
                sabado = hoje - timedelta(days=1)
            elif dias_desde_domingo == 1:  # Hoje é segunda
                domingo = hoje - timedelta(days=1)
                sabado = hoje - timedelta(days=2)
            else:
                # Último domingo
                domingo = hoje - timedelta(days=dias_desde_domingo)
                sabado = domingo - timedelta(days=1)
            
            return sabado, domingo, f"no final de semana ({sabado.strftime('%d/%m')} e {domingo.strftime('%d/%m')})"
        
        elif period_type == 'esta_semana':
            # Segunda a hoje
            dias_desde_segunda = hoje.weekday()  # 0 = segunda
            segunda = hoje - timedelta(days=dias_desde_segunda)
            return segunda, hoje, f"esta semana (desde {segunda.strftime('%d/%m')})"
        
        elif period_type == 'semana_passada':
            # Segunda a domingo da semana passada
            dias_desde_segunda = hoje.weekday()
            segunda_esta_semana = hoje - timedelta(days=dias_desde_segunda)
            domingo_semana_passada = segunda_esta_semana - timedelta(days=1)
            segunda_semana_passada = domingo_semana_passada - timedelta(days=6)
            return segunda_semana_passada, domingo_semana_passada, \
                   f"na semana passada ({segunda_semana_passada.strftime('%d/%m')} a {domingo_semana_passada.strftime('%d/%m')})"
        
        elif period_type == 'ultimos_7_dias':
            inicio = hoje - timedelta(days=6)
            return inicio, hoje, f"nos últimos 7 dias (desde {inicio.strftime('%d/%m')})"
        
        elif period_type == 'este_mes':
            primeiro_dia = hoje.replace(day=1)
            return primeiro_dia, hoje, f"este mês ({formatar_mes_ano_pt(hoje)})"
        
        elif period_type == 'mes_passado':
            primeiro_dia_este_mes = hoje.replace(day=1)
            ultimo_dia_mes_passado = primeiro_dia_este_mes - timedelta(days=1)
            primeiro_dia_mes_passado = ultimo_dia_mes_passado.replace(day=1)
            return primeiro_dia_mes_passado, ultimo_dia_mes_passado, \
                   f"no mês passado ({formatar_mes_ano_pt(ultimo_dia_mes_passado)})"
        
        else:
            # Padrão: hoje
            return hoje, hoje, "hoje"
    
    @staticmethod
    def query_expenses_by_period(conn, usuario_id, data_inicio, data_fim):
        """
        Busca todas as despesas de um período.
        
        Returns:
            (total_gasto, lista_transacoes)
            lista_transacoes: [(descricao, valor, categoria, data), ...]
        """
        sql = text("""
            SELECT 
                t.descricao,
                t.valor,
                s.nome_sub as categoria,
                t.data_transacao,
                m.nome_macro
            FROM Transacoes t
            JOIN SubCategoria s ON t.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            WHERE t.usuario_id = :uid
              AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= :data_inicio
              AND t.data_transacao <= :data_fim
            ORDER BY t.data_transacao DESC, t.created_at DESC
        """)
        
        result = conn.execute(sql, {
            "uid": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchall()
        
        total = sum(abs(float(row[1])) for row in result)
        
        return total, result
    
    @staticmethod
    def query_by_category_and_period(conn, usuario_id, categoria_nome, data_inicio, data_fim):
        """
        Busca gastos de uma categoria específica em um período.
        """
        sql = text("""
            SELECT 
                t.descricao,
                t.valor,
                t.data_transacao
            FROM Transacoes t
            JOIN SubCategoria s ON t.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            WHERE t.usuario_id = :uid
              AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= :data_inicio
              AND t.data_transacao <= :data_fim
              AND (s.nome_sub ILIKE :cat_nome OR m.nome_macro ILIKE :cat_nome)
            ORDER BY t.data_transacao DESC
        """)
        
        result = conn.execute(sql, {
            "uid": usuario_id,
            "cat_nome": f"%{categoria_nome}%",
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchall()
        
        total = sum(abs(float(row[1])) for row in result)
        
        return total, result
    
    @staticmethod
    def format_period_query_response(total_gasto, transacoes, descricao_periodo):
        """
        Formata a resposta para o usuário.
        """
        if total_gasto == 0 or not transacoes:
            return f"✅ Você não teve gastos {descricao_periodo}! 🎉"
        
        resposta = f"💸 *GASTOS {descricao_periodo.upper()}* 💸\n\n"
        resposta += f"💰 *Total: {formatar_moeda(total_gasto)}*\n\n"
        resposta += "━━━━━━━━━━━━━━━━━━━━\n"
        resposta += "📋 *Detalhamento:*\n\n"
        
        # Agrupar por categoria
        por_categoria = {}
        for trans in transacoes:
            desc, valor, cat, data, macro = trans
            valor_abs = abs(float(valor))
            cat_completa = f"{macro} → {cat}"
            
            if cat_completa not in por_categoria:
                por_categoria[cat_completa] = []
            
            por_categoria[cat_completa].append({
                'descricao': desc,
                'valor': valor_abs,
                'data': data
            })
        
        # Formatar por categoria
        for categoria, items in sorted(por_categoria.items(), 
                                       key=lambda x: sum(i['valor'] for i in x[1]), 
                                       reverse=True):
            subtotal = sum(i['valor'] for i in items)
            resposta += f"🏷️ *{categoria}*: {formatar_moeda(subtotal)}\n"
            
            for item in items[:5]:  # Limitar a 5 itens por categoria
                data_fmt = item['data'].strftime('%d/%m')
                resposta += f"   • {item['descricao']}: {formatar_moeda(item['valor'])} ({data_fmt})\n"
            
            if len(items) > 5:
                resposta += f"   ... e mais {len(items) - 5} transação(ões)\n"
            
            resposta += "\n"
        
        return resposta.strip()