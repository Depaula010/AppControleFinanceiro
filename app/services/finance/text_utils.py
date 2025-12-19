# app/services/finance/text_utils.py
"""
Utilitários de processamento de texto para serviços financeiros.

Este módulo contém funções para extrair e processar informações
de mensagens de texto do usuário.
"""

from typing import Optional, Tuple
from ._database import text, Connection


def extract_mentioned_account(
    conn: Connection,
    usuario_id: int,
    texto_msg: str
) -> Optional[Tuple[int, str, str]]:
    """
    Detecta se o usuário mencionou uma conta na mensagem usando fuzzy matching.

    Procura por padrões como:
    - "com o X", "com a X"
    - "usando X"
    - "pelo X", "pela X"
    - "no X", "na X"
    - "paguei com X", "gastei com X"
    - "recebi com X", "entrou no X", "caiu no X"

    Usa fuzzy matching (RapidFuzz) para encontrar a conta mais similar.

    Args:
        conn: Conexão do banco de dados
        usuario_id: ID do usuário
        texto_msg: Texto da mensagem do WhatsApp

    Returns:
        Tupla com (conta_id, nome_conta, tipo_conta) ou None se não encontrar
    """
    from rapidfuzz import fuzz, process

    # Palavras-chave que indicam menção de conta
    palavras_chave = [
        'com o ', 'com a ', 'usando o ', 'usando a ', 'usando ',
        'pelo ', 'pela ', 'no ', 'na ', 'do ', 'da ',
        'paguei com ', 'gastei com ', 'recebi com ', 'entrou no ', 'caiu no '
    ]

    texto_lower = texto_msg.lower()

    # Tentar extrair nome da conta após palavra-chave
    conta_mencionada = None
    for palavra in palavras_chave:
        if palavra in texto_lower:
            # Pegar texto após a palavra-chave
            idx = texto_lower.find(palavra)
            resto = texto_lower[idx + len(palavra):].strip()

            # Pegar primeiras palavras (até 4) como possível nome da conta
            palavras = resto.split()[:4]
            conta_mencionada = ' '.join(palavras)
            break

    if not conta_mencionada:
        return None

    # Buscar contas do usuário
    sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid")
    contas = conn.execute(sql, {"uid": usuario_id}).fetchall()

    if not contas:
        return None

    # Fuzzy matching com nomes das contas
    nomes_contas = {c.nome_conta: c for c in contas}

    result = process.extractOne(
        conta_mencionada,
        nomes_contas.keys(),
        scorer=fuzz.WRatio,
        score_cutoff=60  # Threshold um pouco mais baixo para nomes de conta
    )

    if result:
        melhor_match, score, _ = result
        conta = nomes_contas[melhor_match]
        print(f"[CONTA-MENCIONADA] '{conta_mencionada}' → '{melhor_match}' (score: {score})")
        return (conta.id, conta.nome_conta, conta.tipo_conta)

    return None


__all__ = ['extract_mentioned_account']
