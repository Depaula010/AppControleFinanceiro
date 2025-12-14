# app/shared/formatters/currency_formatter.py
"""
Formatação de moedas e valores monetários
"""
import locale


def formatar_moeda(valor):
    """
    Formata um valor como moeda brasileira (R$).
    Se o locale pt_BR não estiver disponível, usa um fallback manual.

    Args:
        valor: Valor numérico a ser formatado

    Returns:
        str: Valor formatado como moeda (ex: "R$ 1.234,56")

    Examples:
        >>> formatar_moeda(1234.56)
        'R$ 1.234,56'
        >>> formatar_moeda(None)
        'R$ 0,00'
    """
    if valor is None:
        return "R$ 0,00"
    try:
        # Tenta usar o locale configurado
        return locale.currency(valor, grouping=True)
    except Exception:
        # Fallback manual se locale pt_BR não estiver disponível
        return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")
