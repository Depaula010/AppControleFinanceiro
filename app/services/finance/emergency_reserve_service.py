# app/services/finance/emergency_reserve_service.py
"""
Serviço de cálculo de reserva de emergência.

Este módulo calcula a reserva de emergência ideal baseada nos agendamentos
fixos do usuário, normalizando todas as periodicidades (mensal, anual, semanal, etc.).
"""

from typing import Tuple
from ._database import text, Connection


def get_reserva_status(conn: Connection, usuario_id: int) -> Tuple[float, float, int]:
    """
    Calcula a reserva de emergência com base em agendamentos de TODAS as periodicidades.

    Versão 4.0: Quantidade de meses CONFIGURÁVEL por usuário (coluna meses_reserva_emergencia).
    ANUAIS são incluídos com valor INTEGRAL (mais conservador).

    Lógica de normalização DINÂMICA:
    - MENSAL: valor × N meses
    - ANUAL: valor INTEGRAL (ex: IPTU R$ 1200/ano → R$ 1200 - mais conservador)
    - SEMANAL: valor × round(N meses × 4.33 semanas/mês) - arredondado para semanas inteiras
    - QUINZENAL: valor × (N meses × 2 quinzenas/mês)
    - DIARIA: valor × (N meses × 30 dias/mês)

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário

    Returns:
        Tupla com:
        - gasto_mensal_equivalente: Gasto mensal médio de despesas essenciais
        - reserva_ideal_N_meses: Valor total da reserva ideal (N meses)
        - quantidade_meses_configurada: Quantidade de meses configurada pelo usuário
    """
    # Buscar quantos meses o usuário configurou (padrão: 6 meses)
    sql_meses = text("""
        SELECT COALESCE(meses_reserva_emergencia, 6) as meses
        FROM Usuarios
        WHERE id = :uid
    """)
    meses_row = conn.execute(sql_meses, {"uid": usuario_id}).fetchone()
    meses = int(meses_row.meses) if meses_row else 6

    sql = text("""
        SELECT
            a.periodicidade,
            COALESCE(SUM(a.valor_previsto), 0) AS total_periodo
        FROM Agendamentos a
        WHERE a.usuario_id = :uid
          AND a.ativo = TRUE
          AND a.incluir_na_reserva = TRUE
          AND (a.tipo_agendamento = 'FIXO' OR a.tipo_agendamento = 'LEMBRETE_VARIAVEL')
        GROUP BY a.periodicidade
    """)

    results = conn.execute(sql, {"uid": usuario_id}).fetchall()

    # Normalizar cada periodicidade para N meses (configurado pelo usuário)
    reserva_total_n_meses = 0.0

    for row in results:
        periodicidade = row.periodicidade
        valor_periodo = float(row.total_periodo or 0)

        if periodicidade == 'MENSAL':
            # Valor mensal × N meses
            reserva_total_n_meses += valor_periodo * meses

        elif periodicidade == 'ANUAL':
            # Valor anual INTEGRAL (mais conservador - IPTU/IPVA podem ter parcelas nos N meses)
            reserva_total_n_meses += valor_periodo

        elif periodicidade == 'SEMANAL':
            # Valor semanal × (N meses × 4.33 semanas/mês) - arredondado para semanas inteiras
            semanas = round(meses * 4.33)
            reserva_total_n_meses += valor_periodo * semanas

        elif periodicidade == 'QUINZENAL':
            # Valor quinzenal × (N meses × 2 quinzenas/mês)
            quinzenas = meses * 2
            reserva_total_n_meses += valor_periodo * quinzenas

        elif periodicidade == 'DIARIA':
            # Valor diário × (N meses × 30 dias/mês)
            dias = meses * 30
            reserva_total_n_meses += valor_periodo * dias

    # Calcular equivalente mensal (reserva / N)
    gasto_mensal_equivalente = reserva_total_n_meses / meses if meses > 0 else 0

    return gasto_mensal_equivalente, reserva_total_n_meses, meses


__all__ = ['get_reserva_status']
