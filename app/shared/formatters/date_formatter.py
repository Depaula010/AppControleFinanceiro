# app/shared/formatters/date_formatter.py
"""
Formatação de datas em português brasileiro
"""

# Dicionários de tradução para português brasileiro
MESES_PT_BR = {
    'January': 'Janeiro',
    'February': 'Fevereiro',
    'March': 'Março',
    'April': 'Abril',
    'May': 'Maio',
    'June': 'Junho',
    'July': 'Julho',
    'August': 'Agosto',
    'September': 'Setembro',
    'October': 'Outubro',
    'November': 'Novembro',
    'December': 'Dezembro'
}

DIAS_SEMANA_PT_BR = {
    'Monday': 'Segunda-feira',
    'Tuesday': 'Terça-feira',
    'Wednesday': 'Quarta-feira',
    'Thursday': 'Quinta-feira',
    'Friday': 'Sexta-feira',
    'Saturday': 'Sábado',
    'Sunday': 'Domingo'
}


def formatar_mes_pt(data):
    """
    Formata o nome do mês em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Nome do mês em português (ex: "Janeiro", "Dezembro")

    Examples:
        >>> from datetime import date
        >>> formatar_mes_pt(date(2025, 1, 15))
        'Janeiro'
    """
    mes_en = data.strftime('%B')
    return MESES_PT_BR.get(mes_en, mes_en)


def formatar_mes_ano_pt(data):
    """
    Formata mês/ano em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Mês/Ano em português (ex: "Janeiro/2025", "Dezembro/2024")

    Examples:
        >>> from datetime import date
        >>> formatar_mes_ano_pt(date(2025, 1, 15))
        'Janeiro/2025'
    """
    mes_pt = formatar_mes_pt(data)
    ano = data.strftime('%Y')
    return f"{mes_pt}/{ano}"


def formatar_dia_semana_pt(data):
    """
    Formata o nome do dia da semana em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Nome do dia em português (ex: "Segunda-feira", "Sábado")

    Examples:
        >>> from datetime import date
        >>> formatar_dia_semana_pt(date(2025, 1, 15))  # Quarta-feira
        'Quarta-feira'
    """
    dia_en = data.strftime('%A')
    return DIAS_SEMANA_PT_BR.get(dia_en, dia_en)
