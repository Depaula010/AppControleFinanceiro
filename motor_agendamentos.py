import os
import requests
import locale 
from sqlalchemy import create_engine, text, exc as sqlalchemy_exc
from datetime import date, timedelta
from calendar import monthrange

# Configura o locale para R$ (Padrão Brasileiro)
try:
    locale.setlocale(locale.LC_ALL, 'pt_BR.UTF-8')
except locale.Error:
    try:
        locale.setlocale(locale.LC_ALL, 'Portuguese_Brazil.1252') # Windows
    except Exception as e:
        print(f"[MOTOR AVISO] Locale 'pt_BR' não encontrado. Usando padrão. Erro: {e}")


def formatar_moeda(valor):
    """ Tenta formatar como R$ (BRL). Se falhar, usa um formato simples. """
    if valor is None:
        return "R$ 0,00"
    try:
        # Tenta usar a formatação de moeda do locale configurado (pt_BR)
        return formatar_moeda(valor, grouping=True)
    except Exception:
        # Se o locale 'pt_BR' não estiver disponível no servidor, usa um fallback manual.
        # Formata com 2 casas decimais, troca ',' por 'X', '.' por ',' e 'X' por '.'
        return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")
    
    

# (Função de fatura, oculta por brevidade)
def get_or_create_fatura(conn, conta_id, data_transacao, usuario_id):
    # ... (Lógica completa de cálculo de fatura) ...
    sql_get_card_info = text("SELECT dia_fechamento, dia_vencimento FROM Contas WHERE id = :conta_id AND usuario_id = :uid AND tipo_conta = 'Cartão de Crédito'"); card_info = conn.execute(sql_get_card_info, {"conta_id": conta_id, "uid": usuario_id}).fetchone()
    if not card_info or not card_info.dia_fechamento or not card_info.dia_vencimento: return None 
    dia_fechamento = card_info.dia_fechamento; dia_vencimento = card_info.dia_vencimento; dia_transacao = data_transacao.day; mes_transacao = data_transacao.month; ano_transacao = data_transacao.year; data_fatura_fechamento = None; data_fatura_vencimento = None
    try: data_fechamento_mes_atual = date(ano_transacao, mes_transacao, dia_fechamento)
    except ValueError: _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao); data_fechamento_mes_atual = date(ano_transacao, mes_transacao, ultimo_dia_mes)
    if data_transacao <= data_fechamento_mes_atual:
        try: data_fatura_vencimento = date(ano_transacao, mes_transacao, dia_vencimento)
        except ValueError: _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao); data_fatura_vencimento = date(ano_transacao, mes_transacao, ultimo_dia_mes)
        data_fatura_fechamento = data_fechamento_mes_atual
        if dia_vencimento < dia_fechamento: 
            ano_venc, mes_venc = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1)
            try: data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
            except ValueError: _, ultimo_dia_mes = monthrange(ano_venc, mes_venc); data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
    else:
        ano_fech, mes_fech = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1); ano_venc, mes_venc = (ano_fech, mes_fech + 1) if mes_fech < 12 else (ano_fech + 1, 1)
        try: data_fatura_fechamento = date(ano_fech, mes_fech, dia_fechamento)
        except ValueError: _, ultimo_dia_mes = monthrange(ano_fech, mes_fech); data_fatura_fechamento = date(ano_fech, mes_fech, ultimo_dia_mes)
        try: data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
        except ValueError: _, ultimo_dia_mes = monthrange(ano_venc, mes_venc); data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
    sql_find_fatura = text("SELECT id FROM Faturas WHERE conta_id = :cid AND data_vencimento = :dv"); result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento}); fatura_id = result.scalar_one_or_none()
    if fatura_id is None:
        sql_create_fatura = text("INSERT INTO Faturas (conta_id, data_vencimento, data_fechamento, status) VALUES (:cid, :dv, :df, 'Aberta') ON CONFLICT (conta_id, data_vencimento) DO NOTHING RETURNING id"); result = conn.execute(sql_create_fatura, {"cid": conta_id, "dv": data_fatura_vencimento, "df": data_fatura_fechamento}); fatura_id = result.scalar_one_or_none()
        if fatura_id is None: result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento}); fatura_id = result.scalar_one_or_none()
        print(f"[MOTOR] Fatura ID {fatura_id} (Venc: {data_fatura_vencimento}) sendo usada/criada para Cartão ID {conta_id}")
    return fatura_id

def enviar_notificacao_whatsapp(numero, mensagem, bot_url, api_key):
    # ... (Lógica de enviar notificação, sem mudança) ...
    try:
        headers = {'x-api-key': api_key}; payload = {'numero': numero, 'mensagem': mensagem}
        response = requests.post(f"{bot_url}/enviar-mensagem", json=payload, headers=headers, timeout=10) # Adicionado timeout
        if response.status_code == 200: print(f"[MOTOR] Notificação enviada com sucesso para {numero}.")
        else: print(f"[MOTOR] ERRO: Bot respondeu com status {response.status_code}")
    except Exception as e:
        print(f"[MOTOR] ERRO: Falha ao chamar a API do Bot: {e}")


def processar_agendamentos():
    """ A função principal que o Cron Job vai rodar. """
    print("[MOTOR] Início do processamento de agendamentos...")
    
    # 1. Carregar Configurações
    try:
        DATABASE_URL = os.environ['DATABASE_URL']
        API_SECRET_KEY = os.environ['API_SECRET_KEY']
        BOT_WHATSAPP_URL = os.environ['BOT_WHATSAPP_URL']
    except KeyError as e:
        print(f"[MOTOR] ERRO CRÍTICO: Variável de ambiente faltando: {e}")
        return

    # 2. Conectar ao Banco
    if DATABASE_URL.startswith("postgres://"):
        DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)
    
    try:
        engine = create_engine(DATABASE_URL)
        with engine.connect() as conn:
            
            # 3. Pegar a data de hoje
            hoje = date.today()
            dia_hoje = hoje.day
            
            # 4. Buscar agendamentos e usuários
            sql_get_agendamentos = text("""
                SELECT 
                    a.id as agendamento_id, a.usuario_id, a.conta_id, a.subcategoria_id,
                    a.descricao, a.valor_previsto, a.tipo_agendamento, a.periodicidade,
                    a.dia_execucao, a.total_parcelas, a.parcelas_executadas, 
                    a.notificar_antes_dias,
                    u.numero_whatsapp,
                    c.tipo_conta
                FROM Agendamentos a
                JOIN Usuarios u ON a.usuario_id = u.id
                JOIN Contas c ON a.conta_id = c.id
                WHERE a.ativo = TRUE
            """)
            
            agendamentos = conn.execute(sql_get_agendamentos).fetchall()
            print(f"[MOTOR] {len(agendamentos)} agendamentos ativos encontrados.")

            # 5. Processar cada agendamento
            for ag in agendamentos:
                try:
                    conn.begin() 
                    
                    # --- Lógica de Lembrete ---
                    if ag.tipo_agendamento == 'LEMBRETE_VARIAVEL':
                        dia_notificacao = ag.dia_execucao - ag.notificar_antes_dias
                        if dia_hoje == dia_notificacao:
                            print(f"[MOTOR] Processando LEMBRETE para Agendamento ID {ag.agendamento_id}...")
                            
                            valor_formatado = "???"
                            if ag.valor_previsto:
                                valor_formatado = formatar_moeda(ag.valor_previsto, grouping=True)

                            mensagem = f"🔔 *LEMBRETE DE CONTA VARIÁVEL* 🔔\n\nSua conta '{ag.descricao}' vence em {ag.notificar_antes_dias} dias (no dia {ag.dia_execucao}).\n\nO valor previsto é: *{valor_formatado}*\n\nPor favor, me diga o valor exato deste mês para eu registrar (ex: 'gastei 150.50 na conta de luz')."
                            enviar_notificacao_whatsapp(ag.numero_whatsapp, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY)

                    # --- Lógica de Gasto Fixo ---
                    elif ag.tipo_agendamento == 'FIXO':
                        if dia_hoje == ag.dia_execucao:
                            print(f"[MOTOR] Processando GASTO FIXO para Agendamento ID {ag.agendamento_id}...")
                            fatura_id = None
                            if ag.tipo_conta == 'Cartão de Crédito':
                                fatura_id = get_or_create_fatura(conn, ag.conta_id, hoje, ag.usuario_id)
                            valor_para_db = (ag.valor_previsto or 0) * -1 
                            sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, :tipo, :data)")
                            conn.execute(sql_insert, {"uid": ag.usuario_id, "cid": ag.conta_id, "scid": ag.subcategoria_id, "fid": fatura_id, "desc": ag.descricao, "val": valor_para_db, "tipo": 'Despesa', "data": hoje})
                            print(f"[MOTOR] Transação 'FIXO' (ID {ag.agendamento_id}) inserida com sucesso.")

                    # --- Lógica de Gasto Parcelado ---
                    elif ag.tipo_agendamento == 'PARCELADO':
                        if dia_hoje == ag.dia_execucao and (ag.total_parcelas is None or ag.parcelas_executadas < ag.total_parcelas):
                            print(f"[MOTOR] Processando PARCELA {ag.parcelas_executadas + 1}/{ag.total_parcelas} para Agendamento ID {ag.agendamento_id}...")
                            fatura_id = None
                            if ag.tipo_conta == 'Cartão de Crédito':
                                fatura_id = get_or_create_fatura(conn, ag.conta_id, hoje, ag.usuario_id)
                            descricao_parcela = f"{ag.descricao} ({ag.parcelas_executadas + 1}/{ag.total_parcelas})"
                            valor_para_db = (ag.valor_previsto or 0) * -1
                            sql_insert_parc = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, :tipo, :data)")
                            conn.execute(sql_insert_parc, {"uid": ag.usuario_id, "cid": ag.conta_id, "scid": ag.subcategoria_id, "fid": fatura_id, "desc": descricao_parcela, "val": valor_para_db, "tipo": 'Despesa', "data": hoje})
                            nova_parcela_exec = ag.parcelas_executadas + 1
                            desativar_agendamento = (ag.total_parcelas is not None and nova_parcela_exec == ag.total_parcelas)
                            sql_update_ag = text("UPDATE Agendamentos SET parcelas_executadas = :nova_parcela, ativo = :novo_ativo WHERE id = :ag_id")
                            conn.execute(sql_update_ag, {"nova_parcela": nova_parcela_exec, "novo_ativo": not desativar_agendamento, "ag_id": ag.agendamento_id})
                            print(f"[MOTOR] Parcela (ID {ag.agendamento_id}) inserida e agendamento atualizado.")

                    conn.commit() 
                
                # --- CORREÇÃO DE SINTAXE ---
                except Exception as e_ag:
                    print(f"[MOTOR] ERRO ao processar Agendamento ID {ag.agendamento_id}: {e_ag}")
                    try:
                        conn.rollback()
                    except:
                        pass
                # --- FIM DA CORREÇÃO ---

    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"[MOTOR] ERRO CRÍTICO de Banco de Dados: {db_err}")
    except Exception as e_main:
        print(f"[MOTOR] ERRO CRÍTICO Geral: {e_main}")

    print("[MOTOR] Processamento de agendamentos finalizado.")

# Ponto de Entrada (só para testes locais)
if __name__ == "__main__":
    if 'DATABASE_URL' not in os.environ:
        print("AVISO: Rode este script definindo as variáveis de ambiente.")
    else:
        processar_agendamentos()