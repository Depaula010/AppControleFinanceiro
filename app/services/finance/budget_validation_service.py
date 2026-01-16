# app/services/finance/budget_validation_service.py
"""
Serviço de validação de limites de potes de gastos.

Este módulo contém funções para verificar se uma transação ultrapassa
os limites definidos nos potes de gastos do usuário.
"""

from dataclasses import dataclass
from decimal import Decimal
from typing import List, Tuple, Optional

from ._database import text, date, timedelta, monthrange, Connection


@dataclass
class BudgetValidationResult:
    """Resultado da validação de um pote específico."""
    pote_id: int
    nome_pote: str
    valor_limite: float
    valor_gasto_atual: float
    valor_apos_transacao: float
    percentual_usado: float
    ultrapassaria_limite: bool


@dataclass
class ValidateBudgetOutput:
    """Resultado completo da validação de budget."""
    pode_prosseguir: bool
    requer_confirmacao: bool
    validacoes: List[BudgetValidationResult]
    mensagem: str


def get_potes_for_subcategoria(
    conn: Connection,
    usuario_id: int,
    subcategoria_id: int
) -> List[dict]:
    """
    Retorna potes ativos que incluem a subcategoria especificada.

    Args:
        conn: Conexão do SQLAlchemy
        usuario_id: ID do usuário
        subcategoria_id: ID da subcategoria

    Returns:
        Lista de dicts com dados dos potes (id, nome_pote, valor_limite, periodicidade, data_inicio)
    """
    sql = text("""
        SELECT
            p.id,
            p.nome_pote,
            p.valor_limite,
            p.periodicidade,
            p.data_inicio
        FROM PotesDeGastos p
        INNER JOIN PoteSubCategorias psc ON p.id = psc.pote_id
        WHERE p.usuario_id = :uid
          AND psc.subcategoria_id = :scid
          AND p.ativo = TRUE
    """)

    result = conn.execute(sql, {"uid": usuario_id, "scid": subcategoria_id}).fetchall()

    return [
        {
            "id": row[0],
            "nome_pote": row[1],
            "valor_limite": float(row[2]),
            "periodicidade": row[3],
            "data_inicio": row[4]
        }
        for row in result
    ]


def calcular_periodo_pote(
    periodicidade: str,
    data_referencia: date
) -> Tuple[date, date]:
    """
    Calcula o período atual do pote baseado na periodicidade.

    Args:
        periodicidade: SEMANAL, QUINZENAL, MENSAL ou ANUAL
        data_referencia: Data de referência (geralmente hoje)

    Returns:
        Tupla (data_inicio, data_fim) do período
    """
    if periodicidade == "SEMANAL":
        # Segunda a Domingo
        dias_desde_segunda = data_referencia.weekday()
        inicio = data_referencia - timedelta(days=dias_desde_segunda)
        fim = inicio + timedelta(days=6)

    elif periodicidade == "QUINZENAL":
        # 1-15 ou 16-fim do mês
        if data_referencia.day <= 15:
            inicio = data_referencia.replace(day=1)
            fim = data_referencia.replace(day=15)
        else:
            inicio = data_referencia.replace(day=16)
            ultimo_dia = monthrange(data_referencia.year, data_referencia.month)[1]
            fim = data_referencia.replace(day=ultimo_dia)

    elif periodicidade == "MENSAL":
        # Dia 1 ao último dia do mês
        inicio = data_referencia.replace(day=1)
        ultimo_dia = monthrange(data_referencia.year, data_referencia.month)[1]
        fim = data_referencia.replace(day=ultimo_dia)

    elif periodicidade == "ANUAL":
        # Ano civil (1/Jan a 31/Dez)
        inicio = date(data_referencia.year, 1, 1)
        fim = date(data_referencia.year, 12, 31)

    else:
        # Default: período mensal
        inicio = data_referencia.replace(day=1)
        ultimo_dia = monthrange(data_referencia.year, data_referencia.month)[1]
        fim = data_referencia.replace(day=ultimo_dia)

    return inicio, fim


def get_gasto_periodo_pote(
    conn: Connection,
    pote_id: int,
    usuario_id: int,
    data_inicio: date,
    data_fim: date
) -> Decimal:
    """
    Calcula o gasto total de um pote em um período específico.

    Args:
        conn: Conexão do SQLAlchemy
        pote_id: ID do pote
        usuario_id: ID do usuário
        data_inicio: Data inicial do período
        data_fim: Data final do período

    Returns:
        Valor total gasto (positivo, já convertido de negativo)
    """
    sql = text("""
        SELECT COALESCE(SUM(t.valor), 0) AS valor_gasto
        FROM Transacoes t
        INNER JOIN PoteSubCategorias psc ON t.subcategoria_id = psc.subcategoria_id
        WHERE t.usuario_id = :uid
          AND psc.pote_id = :pid
          AND t.tipo_transacao = 'Despesa'
          AND t.data_transacao >= :data_inicio
          AND t.data_transacao <= :data_fim
    """)

    resultado = conn.execute(sql, {
        "uid": usuario_id,
        "pid": pote_id,
        "data_inicio": data_inicio,
        "data_fim": data_fim
    }).scalar()

    # Despesas são armazenadas como negativo, converter para positivo
    return abs(Decimal(resultado or 0))


def validate_budget(
    conn: Connection,
    usuario_id: int,
    subcategoria_id: int,
    valor_transacao: float,
    data_transacao: date
) -> ValidateBudgetOutput:
    """
    Valida se uma transação pode ser criada considerando os limites dos potes.

    Verifica todos os potes que contêm a subcategoria e retorna se algum
    seria ultrapassado.

    Args:
        conn: Conexão do SQLAlchemy
        usuario_id: ID do usuário
        subcategoria_id: ID da subcategoria da transação
        valor_transacao: Valor da transação (positivo)
        data_transacao: Data da transação

    Returns:
        ValidateBudgetOutput com resultado da validação
    """
    # Buscar potes que contêm esta subcategoria
    potes = get_potes_for_subcategoria(conn, usuario_id, subcategoria_id)

    if not potes:
        # Nenhum pote associado - pode prosseguir
        return ValidateBudgetOutput(
            pode_prosseguir=True,
            requer_confirmacao=False,
            validacoes=[],
            mensagem=""
        )

    validacoes: List[BudgetValidationResult] = []
    potes_ultrapassados: List[BudgetValidationResult] = []

    valor_abs = abs(float(valor_transacao))

    for pote in potes:
        # Calcular período do pote
        data_inicio, data_fim = calcular_periodo_pote(
            pote["periodicidade"],
            data_transacao
        )

        # Buscar gasto atual no período
        gasto_atual = float(get_gasto_periodo_pote(
            conn,
            pote["id"],
            usuario_id,
            data_inicio,
            data_fim
        ))

        # Calcular valores após transação
        gasto_apos = gasto_atual + valor_abs
        limite = pote["valor_limite"]
        percentual = (gasto_apos / limite * 100) if limite > 0 else 0
        ultrapassaria = gasto_apos > limite

        resultado = BudgetValidationResult(
            pote_id=pote["id"],
            nome_pote=pote["nome_pote"],
            valor_limite=limite,
            valor_gasto_atual=gasto_atual,
            valor_apos_transacao=gasto_apos,
            percentual_usado=round(percentual, 1),
            ultrapassaria_limite=ultrapassaria
        )

        validacoes.append(resultado)

        if ultrapassaria:
            potes_ultrapassados.append(resultado)

    # Se nenhum pote seria ultrapassado, pode prosseguir
    if not potes_ultrapassados:
        return ValidateBudgetOutput(
            pode_prosseguir=True,
            requer_confirmacao=False,
            validacoes=validacoes,
            mensagem=""
        )

    # Montar mensagem de aviso
    mensagem = _formatar_mensagem_aviso(potes_ultrapassados)

    return ValidateBudgetOutput(
        pode_prosseguir=False,
        requer_confirmacao=True,
        validacoes=validacoes,
        mensagem=mensagem
    )


def _formatar_mensagem_aviso(potes_ultrapassados: List[BudgetValidationResult]) -> str:
    """
    Formata mensagem de aviso para potes que seriam ultrapassados.

    Args:
        potes_ultrapassados: Lista de resultados de potes que excedem limite

    Returns:
        Mensagem formatada para exibição
    """
    if len(potes_ultrapassados) == 1:
        pote = potes_ultrapassados[0]
        excesso = pote.valor_apos_transacao - pote.valor_limite
        return (
            f"Atenção: Esta despesa ultrapassará seu limite!\n\n"
            f"Pote: {pote.nome_pote}\n"
            f"Limite: R$ {pote.valor_limite:,.2f}\n"
            f"Gasto atual: R$ {pote.valor_gasto_atual:,.2f}\n"
            f"Após transação: R$ {pote.valor_apos_transacao:,.2f} ({pote.percentual_usado}% do limite)\n"
            f"Excesso: R$ {excesso:,.2f}\n\n"
            f"Deseja prosseguir mesmo assim?"
        )
    else:
        # Múltiplos potes
        linhas = ["Atenção: Esta despesa ultrapassará limites em múltiplos potes!\n"]
        for pote in potes_ultrapassados:
            excesso = pote.valor_apos_transacao - pote.valor_limite
            linhas.append(
                f"\n• {pote.nome_pote}: R$ {pote.valor_apos_transacao:,.2f} / "
                f"R$ {pote.valor_limite:,.2f} ({pote.percentual_usado}%) - "
                f"Excesso: R$ {excesso:,.2f}"
            )
        linhas.append("\n\nDeseja prosseguir mesmo assim?")
        return "".join(linhas)


__all__ = [
    'BudgetValidationResult',
    'ValidateBudgetOutput',
    'get_potes_for_subcategoria',
    'calcular_periodo_pote',
    'get_gasto_periodo_pote',
    'validate_budget',
]
