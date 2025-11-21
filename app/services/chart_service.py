# app/services/chart_service.py
"""
Serviço para geração de gráficos financeiros usando Matplotlib.
Gera imagens PNG para envio via WhatsApp.
"""
import matplotlib
matplotlib.use('Agg')  # Backend sem interface gráfica (servidor)
import matplotlib.pyplot as plt
import io
import os
from datetime import datetime, timedelta, date
from calendar import monthrange
from sqlalchemy import text
from app import db_engine


def _format_currency(value):
    """Formata valor como moeda brasileira."""
    return f"R$ {value:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


def generate_pie_chart(usuario_id, period_days=30):
    """
    Gera gráfico de pizza com gastos por categoria.

    Args:
        usuario_id: ID do usuário
        period_days: Período em dias (padrão 30)

    Returns:
        bytes: Imagem PNG em bytes ou None se não houver dados
    """
    try:
        with db_engine.connect() as conn:
            # Data de início do período
            data_inicio = datetime.now().date() - timedelta(days=period_days)

            # Busca gastos por categoria
            sql = text("""
                SELECT
                    COALESCE(mc.nome, 'Outros') as categoria,
                    SUM(t.valor) as total
                FROM Transacoes t
                LEFT JOIN SubCategoria sc ON t.id_subcategoria = sc.id
                LEFT JOIN MacroCategoria mc ON sc.id_macrocategoria = mc.id
                WHERE t.usuario_id = :uid
                    AND t.tipo_fluxo = 'Despesa'
                    AND t.data_transacao >= :data_inicio
                GROUP BY mc.nome
                ORDER BY total DESC
            """)

            result = conn.execute(sql, {
                "uid": usuario_id,
                "data_inicio": data_inicio
            }).fetchall()

            if not result or len(result) == 0:
                return None

            # Preparar dados
            categorias = [row.categoria for row in result]
            valores = [float(row.total) for row in result]

            # Criar gráfico
            plt.figure(figsize=(10, 8))
            colors = plt.cm.Set3.colors

            # Gráfico de pizza
            wedges, texts, autotexts = plt.pie(
                valores,
                labels=categorias,
                autopct='%1.1f%%',
                colors=colors,
                startangle=90
            )

            # Melhorar visualização
            for autotext in autotexts:
                autotext.set_color('white')
                autotext.set_fontsize(10)
                autotext.set_weight('bold')

            for text in texts:
                text.set_fontsize(11)

            plt.title(f'Gastos por Categoria - Últimos {period_days} dias',
                     fontsize=14, weight='bold', pad=20)

            # Adicionar legenda com valores
            legend_labels = [f'{cat}: {_format_currency(val)}'
                           for cat, val in zip(categorias, valores)]
            plt.legend(legend_labels, loc='center left', bbox_to_anchor=(1, 0, 0.5, 1))

            plt.tight_layout()

            # Salvar em bytes
            buf = io.BytesIO()
            plt.savefig(buf, format='png', dpi=150, bbox_inches='tight')
            buf.seek(0)
            plt.close()

            return buf.getvalue()

    except Exception as e:
        print(f"[CHART] Erro ao gerar gráfico de pizza: {e}")
        return None


def generate_bar_chart(usuario_id, num_months=6):
    """
    Gera gráfico de barras com evolução mensal de gastos.

    Args:
        usuario_id: ID do usuário
        num_months: Número de meses para exibir (padrão 6)

    Returns:
        bytes: Imagem PNG em bytes ou None se não houver dados
    """
    try:
        with db_engine.connect() as conn:
            # Busca gastos por mês
            sql = text("""
                SELECT
                    DATE_TRUNC('month', data_transacao) as mes,
                    SUM(CASE WHEN tipo_fluxo = 'Despesa' THEN valor ELSE 0 END) as despesas,
                    SUM(CASE WHEN tipo_fluxo = 'Renda' THEN valor ELSE 0 END) as rendas
                FROM Transacoes
                WHERE usuario_id = :uid
                    AND data_transacao >= CURRENT_DATE - INTERVAL ':months months'
                GROUP BY mes
                ORDER BY mes
            """)

            result = conn.execute(sql, {
                "uid": usuario_id,
                "months": num_months
            }).fetchall()

            if not result or len(result) == 0:
                return None

            # Preparar dados
            meses = [row.mes.strftime('%b/%y') for row in result]
            despesas = [float(row.despesas) for row in result]
            rendas = [float(row.rendas) for row in result]

            # Criar gráfico
            fig, ax = plt.subplots(figsize=(12, 7))

            x = range(len(meses))
            width = 0.35

            # Barras
            bars1 = ax.bar([i - width/2 for i in x], despesas, width,
                          label='Despesas', color='#ff6b6b')
            bars2 = ax.bar([i + width/2 for i in x], rendas, width,
                          label='Rendas', color='#51cf66')

            # Adicionar valores nas barras
            for bars in [bars1, bars2]:
                for bar in bars:
                    height = bar.get_height()
                    ax.text(bar.get_x() + bar.get_width()/2., height,
                           _format_currency(height),
                           ha='center', va='bottom', fontsize=9)

            # Configurações
            ax.set_xlabel('Mês', fontsize=12, weight='bold')
            ax.set_ylabel('Valor (R$)', fontsize=12, weight='bold')
            ax.set_title('Evolução Mensal - Despesas vs Rendas',
                        fontsize=14, weight='bold', pad=20)
            ax.set_xticks(x)
            ax.set_xticklabels(meses, rotation=45, ha='right')
            ax.legend()
            ax.grid(axis='y', alpha=0.3)

            plt.tight_layout()

            # Salvar em bytes
            buf = io.BytesIO()
            plt.savefig(buf, format='png', dpi=150, bbox_inches='tight')
            buf.seek(0)
            plt.close()

            return buf.getvalue()

    except Exception as e:
        print(f"[CHART] Erro ao gerar gráfico de barras: {e}")
        return None


def generate_line_chart(usuario_id, num_months=6):
    """
    Gera gráfico de linha com evolução do saldo ao longo do tempo.

    Args:
        usuario_id: ID do usuário
        num_months: Número de meses para exibir (padrão 6)

    Returns:
        bytes: Imagem PNG em bytes ou None se não houver dados
    """
    try:
        with db_engine.connect() as conn:
            # Busca saldo por dia
            data_inicio = datetime.now().date() - timedelta(days=num_months * 30)

            sql = text("""
                SELECT
                    data_transacao,
                    SUM(CASE WHEN tipo_fluxo = 'Renda' THEN valor ELSE -valor END)
                        OVER (ORDER BY data_transacao) as saldo_acumulado
                FROM Transacoes
                WHERE usuario_id = :uid
                    AND data_transacao >= :data_inicio
                ORDER BY data_transacao
            """)

            result = conn.execute(sql, {
                "uid": usuario_id,
                "data_inicio": data_inicio
            }).fetchall()

            if not result or len(result) == 0:
                return None

            # Preparar dados
            datas = [row.data_transacao for row in result]
            saldos = [float(row.saldo_acumulado) for row in result]

            # Criar gráfico
            fig, ax = plt.subplots(figsize=(12, 7))

            # Linha do saldo
            ax.plot(datas, saldos, linewidth=2.5, color='#339af0', marker='o',
                   markersize=4, markerfacecolor='white', markeredgewidth=2)

            # Preencher área
            ax.fill_between(datas, saldos, alpha=0.3, color='#339af0')

            # Linha zero
            ax.axhline(y=0, color='red', linestyle='--', linewidth=1, alpha=0.5)

            # Configurações
            ax.set_xlabel('Data', fontsize=12, weight='bold')
            ax.set_ylabel('Saldo (R$)', fontsize=12, weight='bold')
            ax.set_title('Evolução do Saldo ao Longo do Tempo',
                        fontsize=14, weight='bold', pad=20)
            ax.grid(True, alpha=0.3)

            # Formatar eixo Y
            ax.yaxis.set_major_formatter(plt.FuncFormatter(
                lambda x, p: _format_currency(x)
            ))

            # Rotacionar labels do eixo X
            plt.xticks(rotation=45, ha='right')

            plt.tight_layout()

            # Salvar em bytes
            buf = io.BytesIO()
            plt.savefig(buf, format='png', dpi=150, bbox_inches='tight')
            buf.seek(0)
            plt.close()

            return buf.getvalue()

    except Exception as e:
        print(f"[CHART] Erro ao gerar gráfico de linha: {e}")
        return None


def save_chart_temp(chart_bytes, chart_type='chart'):
    """
    Salva gráfico em arquivo temporário.

    Args:
        chart_bytes: Bytes da imagem PNG
        chart_type: Tipo do gráfico (para nome do arquivo)

    Returns:
        str: Caminho do arquivo temporário
    """
    try:
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f"chart_{chart_type}_{timestamp}.png"
        temp_path = os.path.join('/tmp', filename)

        with open(temp_path, 'wb') as f:
            f.write(chart_bytes)

        print(f"[CHART] Gráfico salvo: {temp_path}")
        return temp_path

    except Exception as e:
        print(f"[CHART] Erro ao salvar gráfico temporário: {e}")
        return None
