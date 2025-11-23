# app/services/transaction_feedback_service.py
"""
Serviço de Feedback Financeiro em Tempo Real

Responsável por gerar mensagens enriquecidas após confirmação de transações,
incluindo:
- Status do pote relacionado à categoria (se existir)
- Valor da fatura atual (se crédito) OU saldo da conta (se débito/pix/dinheiro)
- Alertas visuais com semáforo (🟢🟡🔴)
"""

from sqlalchemy import text
from datetime import date

def calcular_status_pote(conn, usuario_id, subcategoria_id):
    """
    Busca o pote relacionado à subcategoria e calcula saldo restante.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        subcategoria_id: ID da subcategoria da transação

    Returns:
        Dict com informações do pote ou None se não houver pote configurado:
        {
            'nome_pote': 'Alimentação',
            'valor_limite': 500.00,
            'gasto_periodo': 380.00,
            'saldo_restante': 120.00,
            'percentual_usado': 76.0,
            'periodicidade': 'Semanal'
        }
    """
    # Query para buscar pote e calcular gastos
    # Suporta periodicidade SEMANAL e MENSAL
    sql = text("""
        SELECT
            p.nome_pote,
            p.valor_limite,
            p.periodicidade,
            COALESCE(SUM(ABS(t.valor)), 0) as gasto_periodo,
            (p.valor_limite - COALESCE(SUM(ABS(t.valor)), 0)) as saldo_restante,
            CASE
                WHEN p.valor_limite > 0 THEN
                    (COALESCE(SUM(ABS(t.valor)), 0) / p.valor_limite * 100)
                ELSE 0
            END as percentual_usado
        FROM PotesDeGastos p
        JOIN PoteSubCategorias psc ON p.id = psc.pote_id
        LEFT JOIN Transacoes t ON t.subcategoria_id = psc.subcategoria_id
            AND t.usuario_id = p.usuario_id
            AND t.tipo_transacao = 'Despesa'
            AND t.data_transacao >= CASE
                WHEN p.periodicidade = 'SEMANAL' THEN date_trunc('week', CURRENT_DATE)
                WHEN p.periodicidade = 'QUINZENAL' THEN date_trunc('week', CURRENT_DATE)
                ELSE date_trunc('month', CURRENT_DATE)
            END
        WHERE p.usuario_id = :uid
            AND psc.subcategoria_id = :subcategoria_id
            AND p.ativo = TRUE
        GROUP BY p.id, p.nome_pote, p.valor_limite, p.periodicidade
        LIMIT 1
    """)

    result = conn.execute(sql, {
        "uid": usuario_id,
        "subcategoria_id": subcategoria_id
    }).fetchone()

    if not result:
        return None

    return {
        'nome_pote': result[0],
        'valor_limite': float(result[1]),
        'gasto_periodo': float(result[2]),
        'saldo_restante': float(result[3]),
        'percentual_usado': float(result[4]),
        'periodicidade': result[5].capitalize()  # SEMANAL -> Semanal
    }


def verificar_tipo_conta(conn, conta_id):
    """
    Verifica se a conta é cartão de crédito ou conta corrente/dinheiro.

    Args:
        conn: Conexão com o banco
        conta_id: ID da conta

    Returns:
        'credito' se for cartão de crédito, 'corrente' caso contrário
    """
    sql = text("""
        SELECT tipo_conta FROM Contas WHERE id = :conta_id
    """)

    tipo_conta = conn.execute(sql, {"conta_id": conta_id}).scalar_one_or_none()

    if tipo_conta and 'Crédito' in tipo_conta:
        return 'credito'
    else:
        return 'corrente'


def calcular_fatura_atual(conn, usuario_id, conta_id):
    """
    Calcula o valor total da fatura em aberto de um cartão de crédito.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID do cartão de crédito

    Returns:
        Dict com informações da fatura ou None:
        {
            'valor_total': 1250.00,
            'dia_fechamento': 15,
            'dias_ate_fechar': 5,
            'nome_conta': 'Nubank'
        }
    """
    sql = text("""
        SELECT
            c.nome_conta,
            c.dia_fechamento,
            COALESCE(SUM(ABS(t.valor)), 0) as valor_total,
            CASE
                WHEN EXTRACT(DAY FROM CURRENT_DATE) <= c.dia_fechamento
                THEN (c.dia_fechamento - EXTRACT(DAY FROM CURRENT_DATE))
                ELSE (
                    EXTRACT(DAY FROM (DATE_TRUNC('month', CURRENT_DATE) + INTERVAL '1 month' - INTERVAL '1 day'))
                    - EXTRACT(DAY FROM CURRENT_DATE)
                    + c.dia_fechamento
                )
            END as dias_ate_fechar
        FROM Contas c
        LEFT JOIN Faturas f ON f.conta_id = c.id
            AND f.data_vencimento > CURRENT_DATE
            AND f.status = 'Aberta'
        LEFT JOIN Transacoes t ON t.fatura_id = f.id
            AND t.tipo_transacao = 'Despesa'
        WHERE c.id = :conta_id
            AND c.usuario_id = :uid
            AND c.tipo_conta = 'Cartão de Crédito'
        GROUP BY c.id, c.nome_conta, c.dia_fechamento
    """)

    result = conn.execute(sql, {
        "uid": usuario_id,
        "conta_id": conta_id
    }).fetchone()

    if not result:
        return None

    return {
        'nome_conta': result[0],
        'dia_fechamento': result[1],
        'valor_total': float(result[2]),
        'dias_ate_fechar': int(result[3]) if result[3] else 0
    }


def calcular_saldo_conta(conn, usuario_id, conta_id):
    """
    Calcula o saldo disponível de uma conta corrente/poupança/dinheiro.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID da conta

    Returns:
        Dict com informações da conta:
        {
            'saldo_disponivel': 890.50,
            'nome_conta': 'Itaú'
        }
    """
    sql = text("""
        SELECT
            c.nome_conta,
            COALESCE(SUM(t.valor), 0) as saldo_disponivel
        FROM Contas c
        LEFT JOIN Transacoes t ON t.conta_id = c.id
            AND t.usuario_id = :uid
        WHERE c.id = :conta_id
            AND c.usuario_id = :uid
        GROUP BY c.id, c.nome_conta
    """)

    result = conn.execute(sql, {
        "uid": usuario_id,
        "conta_id": conta_id
    }).fetchone()

    if not result:
        return None

    return {
        'nome_conta': result[0],
        'saldo_disponivel': float(result[1])
    }


def deve_exibir_alerta(conn, usuario_id, percentual_usado):
    """
    Verifica se deve exibir alerta do pote baseado na configuração do usuário.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        percentual_usado: Percentual do pote já utilizado

    Returns:
        True se deve exibir, False caso contrário
    """
    sql = text("""
        SELECT alerta_potes_ativo, alerta_potes_threshold
        FROM NotificationConfigs
        WHERE usuario_id = :uid
    """)

    result = conn.execute(sql, {"uid": usuario_id}).fetchone()

    if not result:
        # Padrão: sempre mostrar se não tiver configuração
        return True

    ativo = result[0]
    threshold = result[1]

    # Se alertas estão desativados, não mostrar
    if not ativo:
        return False

    # Se threshold é 0, sempre mostrar
    if threshold == 0:
        return True

    # Caso contrário, só mostrar se percentual usado >= threshold
    return percentual_usado >= threshold


def get_emoji_status(percentual_usado):
    """
    Retorna emoji do semáforo baseado no percentual usado do pote.

    Args:
        percentual_usado: Percentual do limite já utilizado (0-100)

    Returns:
        Emoji: 🟢 (< 70%), 🟡 (70-90%), 🔴 (>= 90%)
    """
    if percentual_usado < 70:
        return '🟢'
    elif percentual_usado < 90:
        return '🟡'
    else:
        return '🔴'


def buscar_dados_transacao(conn, transacao_id):
    """
    Busca os dados completos de uma transação recém-criada.

    Args:
        conn: Conexão com o banco
        transacao_id: ID da transação

    Returns:
        Dict com dados da transação
    """
    sql = text("""
        SELECT
            t.id,
            t.usuario_id,
            t.conta_id,
            t.subcategoria_id,
            t.descricao,
            t.valor,
            c.nome_conta,
            c.tipo_conta,
            s.nome_sub as categoria_nome
        FROM Transacoes t
        JOIN Contas c ON t.conta_id = c.id
        JOIN SubCategoria s ON t.subcategoria_id = s.id
        WHERE t.id = :transacao_id
    """)

    result = conn.execute(sql, {"transacao_id": transacao_id}).fetchone()

    if not result:
        raise Exception(f"Transação {transacao_id} não encontrada")

    return {
        'id': result[0],
        'usuario_id': result[1],
        'conta_id': result[2],
        'subcategoria_id': result[3],
        'descricao': result[4],
        'valor': float(result[5]),
        'nome_conta': result[6],
        'tipo_conta': result[7],
        'categoria_nome': result[8]
    }


def formatar_mensagem_feedback(transacao, status_pote, info_rodape, tipo_conta, deve_mostrar_pote):
    """
    Formata a mensagem final de feedback para o usuário.

    Args:
        transacao: Dict com dados da transação
        status_pote: Dict com status do pote (ou None)
        info_rodape: Dict com dados da fatura ou saldo (ou None)
        tipo_conta: 'credito' ou 'corrente'
        deve_mostrar_pote: Boolean indicando se deve exibir bloco do pote

    Returns:
        String com mensagem formatada para WhatsApp
    """
    # Cabeçalho
    msg = "✅ Transação Salva!\n"
    msg += f"📝 {transacao['descricao']}\n"
    msg += f"💵 R$ {abs(transacao['valor']):.2f} ({transacao['nome_conta']})\n"

    # Bloco do Pote (condicional)
    if status_pote and deve_mostrar_pote:
        emoji = get_emoji_status(status_pote['percentual_usado'])
        msg += f"\n🎯 Pote {status_pote['nome_pote']} ({status_pote['periodicidade']}):\n"
        msg += f"Restam: R$ {status_pote['saldo_restante']:.2f} {emoji}\n"

    # Rodapé (condicional por tipo de conta)
    if info_rodape:
        if tipo_conta == 'credito':
            msg += f"\n💳 Fatura {info_rodape['nome_conta']}:\n"
            msg += f"R$ {info_rodape['valor_total']:.2f} (Fecha dia {info_rodape['dia_fechamento']})\n"
        else:
            msg += f"\n🏦 Saldo {info_rodape['nome_conta']}:\n"
            msg += f"R$ {info_rodape['saldo_disponivel']:.2f} (Disponível)\n"

    return msg


def gerar_feedback_transacao(conn, transacao_id):
    """
    Função orquestradora principal que gera o feedback completo.

    Esta é a função que deve ser chamada após salvar uma transação.

    Args:
        conn: Conexão com o banco
        transacao_id: ID da transação recém-criada

    Returns:
        String com mensagem de feedback formatada
    """
    try:
        # 1. Buscar dados da transação
        transacao = buscar_dados_transacao(conn, transacao_id)

        # 2. Verificar tipo da conta (crédito vs corrente)
        tipo_conta = verificar_tipo_conta(conn, transacao['conta_id'])

        # 3. Calcular status do pote
        status_pote = calcular_status_pote(
            conn,
            transacao['usuario_id'],
            transacao['subcategoria_id']
        )

        # 4. Verificar se deve mostrar alerta do pote
        deve_mostrar = False
        if status_pote:
            deve_mostrar = deve_exibir_alerta(
                conn,
                transacao['usuario_id'],
                status_pote['percentual_usado']
            )

        # 5. Calcular rodapé (fatura ou saldo)
        info_rodape = None
        if tipo_conta == 'credito':
            info_rodape = calcular_fatura_atual(
                conn,
                transacao['usuario_id'],
                transacao['conta_id']
            )
        else:
            info_rodape = calcular_saldo_conta(
                conn,
                transacao['usuario_id'],
                transacao['conta_id']
            )

        # 6. Formatar mensagem final
        mensagem = formatar_mensagem_feedback(
            transacao,
            status_pote,
            info_rodape,
            tipo_conta,
            deve_mostrar
        )

        print(f"[FEEDBACK] Mensagem gerada para transação {transacao_id}")
        return mensagem

    except Exception as e:
        print(f"[FEEDBACK] ERRO ao gerar feedback: {e}")
        # Fallback: retornar mensagem simples em caso de erro
        return "✅ Transação Salva com Sucesso!"
