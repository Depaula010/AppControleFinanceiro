# motor_agendamentos.py
import os
import locale # Removido, pois o app/__init__ já configura
from sqlalchemy import create_engine, text, exc as sqlalchemy_exc
from datetime import date
from calendar import monthrange # Removido, pois agora está no finance_service

# --- NOVAS IMPORTAÇÕES ---
# Importa os serviços que criamos
from app.utils import formatar_moeda
from app.services.finance_service import get_or_create_fatura
from app.services.notification_service import enviar_notificacao_whatsapp
# --- FIM DAS NOVAS IMPORTAÇÕES ---


# A função 'formatar_moeda' foi REMOVIDA daqui (agora importada)

# A função 'get_or_create_fatura' foi REMOVIDA daqui (agora importada)

# A função 'enviar_notificacao_whatsapp' foi REMOVIDA daqui (agora importada)


def processar_agendamentos():
    """ A função principal que o Cron Job vai rodar. """
    print("[MOTOR] Início do processamento de agendamentos...")
    
    # 1. Carregar Configurações
    try:
        # Note: Se este script rodar *totalmente* fora do contexto do Flask
        # (ex: python motor_agendamentos.py), ele não lerá do app/config.py.
        # Mas como ele é chamado pela rota /run-motor-agendamentos,
        # as variáveis de ambiente já estarão carregadas.
        DATABASE_URL_ENV = os.environ['DATABASE_URL']
        API_SECRET_KEY = os.environ['API_SECRET_KEY']
        BOT_WHATSAPP_URL = os.environ['BOT_WHATSAPP_URL']
    except KeyError as e:
        print(f"[MOTOR] ERRO CRÍTICO: Variável de ambiente faltando: {e}")
        return

    # 2. Conectar ao Banco
    DATABASE_URL = DATABASE_URL_ENV
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
                    c.tipo_conta,
                    g.nome_grupo
                FROM Agendamentos a
                JOIN Usuarios u ON a.usuario_id = u.id
                JOIN Contas c ON a.conta_id = c.id
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
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
                                # Usa a função importada!
                                valor_formatado = formatar_moeda(ag.valor_previsto)

                            mensagem = f"🔔 *LEMBRETE DE CONTA VARIÁVEL* 🔔\n\nSua conta '{ag.descricao}' vence em {ag.notificar_antes_dias} dias (no dia {ag.dia_execucao}).\n\nO valor previsto é: *{valor_formatado}*\n\nPor favor, me diga o valor exato deste mês para eu registrar (ex: 'gastei 150.50 na conta de luz')."
                            
                            # Usa a função importada!
                            enviar_notificacao_whatsapp(ag.numero_whatsapp, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY)

                    # --- Lógica de Gasto/Receita Fixa ---
                    elif ag.tipo_agendamento == 'FIXO':
                        if dia_hoje == ag.dia_execucao:
                            # Determinar tipo baseado no grupo da categoria
                            tipo_transacao = 'Renda' if ag.nome_grupo == 'Renda' else 'Despesa'
                            print(f"[MOTOR] Processando {tipo_transacao.upper()} FIXA para Agendamento ID {ag.agendamento_id}...")

                            fatura_id = None
                            if ag.tipo_conta == 'Cartão de Crédito':
                                # Usa a função importada!
                                fatura_id = get_or_create_fatura(conn, ag.conta_id, hoje, ag.usuario_id)

                            # Receitas: valor positivo, Despesas: valor negativo
                            valor_para_db = (ag.valor_previsto or 0) if tipo_transacao == 'Renda' else (ag.valor_previsto or 0) * -1
                            sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, :tipo, :data)")
                            conn.execute(sql_insert, {"uid": ag.usuario_id, "cid": ag.conta_id, "scid": ag.subcategoria_id, "fid": fatura_id, "desc": ag.descricao, "val": valor_para_db, "tipo": tipo_transacao, "data": hoje})
                            print(f"[MOTOR] Transação '{tipo_transacao}' FIXA (ID {ag.agendamento_id}) inserida com sucesso.")

                    # --- Lógica de Gasto/Receita Parcelada ---
                    elif ag.tipo_agendamento == 'PARCELADO':
                        if dia_hoje == ag.dia_execucao and (ag.total_parcelas is None or ag.parcelas_executadas < ag.total_parcelas):
                            # Determinar tipo baseado no grupo da categoria
                            tipo_transacao = 'Renda' if ag.nome_grupo == 'Renda' else 'Despesa'
                            print(f"[MOTOR] Processando {tipo_transacao.upper()} PARCELADA {ag.parcelas_executadas + 1}/{ag.total_parcelas} para Agendamento ID {ag.agendamento_id}...")

                            fatura_id = None
                            if ag.tipo_conta == 'Cartão de Crédito':
                                # Usa a função importada!
                                fatura_id = get_or_create_fatura(conn, ag.conta_id, hoje, ag.usuario_id)

                            descricao_parcela = f"{ag.descricao} ({ag.parcelas_executadas + 1}/{ag.total_parcelas})"
                            # Receitas: valor positivo, Despesas: valor negativo
                            valor_para_db = (ag.valor_previsto or 0) if tipo_transacao == 'Renda' else (ag.valor_previsto or 0) * -1
                            sql_insert_parc = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, :tipo, :data)")
                            conn.execute(sql_insert_parc, {"uid": ag.usuario_id, "cid": ag.conta_id, "scid": ag.subcategoria_id, "fid": fatura_id, "desc": descricao_parcela, "val": valor_para_db, "tipo": tipo_transacao, "data": hoje})

                            nova_parcela_exec = ag.parcelas_executadas + 1
                            desativar_agendamento = (ag.total_parcelas is not None and nova_parcela_exec == ag.total_parcelas)
                            sql_update_ag = text("UPDATE Agendamentos SET parcelas_executadas = :nova_parcela, ativo = :novo_ativo WHERE id = :ag_id")
                            conn.execute(sql_update_ag, {"nova_parcela": nova_parcela_exec, "novo_ativo": not desativar_agendamento, "ag_id": ag.agendamento_id})
                            print(f"[MOTOR] Parcela de {tipo_transacao} (ID {ag.agendamento_id}) inserida e agendamento atualizado.")

                    conn.commit() 
                
                except Exception as e_ag:
                    print(f"[MOTOR] ERRO ao processar Agendamento ID {ag.agendamento_id}: {e_ag}")
                    try:
                        conn.rollback()
                    except:
                        pass

    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"[MOTOR] ERRO CRÍTICO de Banco de Dados: {db_err}")
    except Exception as e_main:
        print(f"[MOTOR] ERRO CRÍTICO Geral: {e_main}")

    print("[MOTOR] Processamento de agendamentos finalizado.")

# Ponto de Entrada (só para testes locais)
if __name__ == "__main__":
    # Para rodar isso localmente (python motor_agendamentos.py), 
    # você precisará de um .env ou similar, pois o app/__init__.py não foi executado.
    # Mas para o servidor, está correto.
    if 'DATABASE_URL' not in os.environ:
        print("AVISO: Rode este script definindo as variáveis de ambiente.")
    else:
        processar_agendamentos()