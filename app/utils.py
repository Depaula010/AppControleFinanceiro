# app/utils.py
import locale

def formatar_moeda(valor):
    """ 
    Tenta formatar como R$ (BRL). Se falhar, usa um formato simples. 
    Esta função agora é centralizada aqui.
    """
    if valor is None:
        return "R$ 0,00"
    try:
        # A chamada correta é para a biblioteca 'locale'.
        return locale.currency(valor, grouping=True)
    except Exception:
        # Se o locale 'pt_BR' não estiver disponível no servidor, usa um fallback manual.
        return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")