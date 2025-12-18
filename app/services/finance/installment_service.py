# app/services/finance/installment_service.py
"""
Serviço de gerenciamento de parcelamentos.

Este módulo contém funções para criar e gerenciar parcelamentos de compras.
"""

from datetime import date
from typing import Optional
from ._database import text, Connection


def create_parcelamento_agendamento(
    conn: Connection,
    usuario_id: int,
    conta_id: int,
    categoria_id: int,
    descricao: str,
    valor_parcela: float,
    num_parcelas: int,
    data_primeira_parcela: date
) -> int:
    """
    Cria agendamentos para as parcelas futuras de uma compra parcelada.

    A primeira parcela já foi registrada como transação. Este agendamento
    cria as parcelas restantes (2ª até a última).

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID da conta (cartão de crédito)
        categoria_id: ID da categoria
        descricao: Descrição da compra
        valor_parcela: Valor de cada parcela
        num_parcelas: Número total de parcelas
        data_primeira_parcela: Data da primeira parcela (geralmente hoje)

    Returns:
        ID do agendamento criado para as parcelas restantes
    """
    from dateutil.relativedelta import relativedelta

    # Calcular dia de execução (mesmo dia do mês da primeira parcela)
    dia_execucao = data_primeira_parcela.day

    # Data de início é o mês seguinte (segunda parcela)
    data_inicio = data_primeira_parcela + relativedelta(months=1)

    # Criar agendamento
    sql = text("""
        INSERT INTO Agendamentos (
            usuario_id, conta_id, subcategoria_id, descricao, valor_previsto,
            tipo_agendamento, periodicidade, data_inicio, dia_execucao,
            total_parcelas, parcelas_executadas, ativo
        ) VALUES (
            :uid, :cid, :scid, :desc, :val,
            'PARCELADO', 'MENSAL', :data_inicio, :dia_exec,
            :total, 1, TRUE
        ) RETURNING id
    """)

    resultado = conn.execute(sql, {
        "uid": usuario_id,
        "cid": conta_id,
        "scid": categoria_id,
        "desc": descricao,
        "val": valor_parcela,
        "data_inicio": data_inicio,
        "dia_exec": dia_execucao,
        "total": num_parcelas
    })

    agendamento_id = resultado.scalar_one()
    print(f"[PARCELAMENTO] Agendamento criado: ID {agendamento_id} para {num_parcelas-1} parcelas restantes")

    return agendamento_id


__all__ = ['create_parcelamento_agendamento']
