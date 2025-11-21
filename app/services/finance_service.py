# app/services/finance_service.py
import secrets
from sqlalchemy import text
from datetime import date
from calendar import monthrange

# Importa o "Singleton" do motor de banco de dados
from app import db_engine 

# --- Lógica de Fatura (Movida do app.py e motor_agendamentos.py) ---

def get_or_create_fatura(conn, conta_id, data_transacao, usuario_id):
    """
    Serviço centralizado para calcular e obter/criar faturas.
    Nota: Esta função recebe a 'conn' (conexão) como parâmetro
    pois ela é feita para rodar DENTRO de uma transação existente.
    """
    # (Lógica completa de cálculo de fatura, como antes)
    sql_get_card_info = text("SELECT dia_fechamento, dia_vencimento FROM Contas WHERE id = :conta_id AND usuario_id = :uid AND tipo_conta = 'Cartão de Crédito'")
    card_info = conn.execute(sql_get_card_info, {"conta_id": conta_id, "uid": usuario_id}).fetchone()
    
    if not card_info or not card_info.dia_fechamento or not card_info.dia_vencimento: 
        return None 
        
    dia_fechamento = card_info.dia_fechamento; dia_vencimento = card_info.dia_vencimento
    dia_transacao = data_transacao.day; mes_transacao = data_transacao.month; ano_transacao = data_transacao.year
    data_fatura_fechamento = None; data_fatura_vencimento = None
    
    try: 
        data_fechamento_mes_atual = date(ano_transacao, mes_transacao, dia_fechamento)
    except ValueError: 
        _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao)
        data_fechamento_mes_atual = date(ano_transacao, mes_transacao, ultimo_dia_mes)
    
    if data_transacao <= data_fechamento_mes_atual:
        try: 
            data_fatura_vencimento = date(ano_transacao, mes_transacao, dia_vencimento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao)
            data_fatura_vencimento = date(ano_transacao, mes_transacao, ultimo_dia_mes)
        data_fatura_fechamento = data_fechamento_mes_atual
        if dia_vencimento < dia_fechamento: 
            ano_venc, mes_venc = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1)
            try: 
                data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
            except ValueError: 
                _, ultimo_dia_mes = monthrange(ano_venc, mes_venc)
                data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
    else:
        ano_fech, mes_fech = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1)
        ano_venc, mes_venc = (ano_fech, mes_fech + 1) if mes_fech < 12 else (ano_fech + 1, 1)
        try: 
            data_fatura_fechamento = date(ano_fech, mes_fech, dia_fechamento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_fech, mes_fech)
            data_fatura_fechamento = date(ano_fech, mes_fech, ultimo_dia_mes)
        try: 
            data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_venc, mes_venc)
            data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
            
    sql_find_fatura = text("SELECT id FROM Faturas WHERE conta_id = :cid AND data_vencimento = :dv")
    result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento})
    fatura_id = result.scalar_one_or_none()
    
    if fatura_id is None:
        sql_create_fatura = text("INSERT INTO Faturas (conta_id, data_vencimento, data_fechamento, status) VALUES (:cid, :dv, :df, 'Aberta') ON CONFLICT (conta_id, data_vencimento) DO NOTHING RETURNING id")
        result = conn.execute(sql_create_fatura, {"cid": conta_id, "dv": data_fatura_vencimento, "df": data_fatura_fechamento})
        fatura_id = result.scalar_one_or_none()
        if fatura_id is None: 
            result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento})
            fatura_id = result.scalar_one_or_none()
        print(f"[SERVICE-FIN] Fatura ID {fatura_id} (Venc: {data_fatura_vencimento}) sendo usada/criada para Cartão ID {conta_id}")
        
    return fatura_id

# --- Lógica de Admin (Movida das rotas do app.py) ---

def clear_bot_session():
    """ Limpa a sessão do bot no banco de dados. """
    if not db_engine: 
        raise Exception("Banco de dados não configurado")
        
    sql_clear = text("DELETE FROM baileys_auth WHERE session_id = 'baileys_session';")
    try:
        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql_clear)
            conn.commit()
            return result.rowcount
    except Exception as e:
        print(f"[SERVICE-FIN] Erro ao limpar sessão: {e}")
        # Tenta rollback
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        raise e # Re-levanta a exceção para o controller tratar

def setup_database_schema():
    """ Executa o DDL (script de criação de tabelas). """
    if not db_engine: 
        raise Exception("Banco de dados não configurado")
        
    # (DDL v12 completo, movido do app.py)
    sql_script_ddl = text("""
    DROP TABLE IF EXISTS PoteSubCategorias CASCADE; DROP TABLE IF EXISTS baileys_auth CASCADE; DROP TABLE IF EXISTS PotesDeGastos CASCADE; DROP TABLE IF EXISTS Agendamentos CASCADE; DROP TABLE IF EXISTS Transacoes CASCADE; DROP TABLE IF EXISTS Faturas CASCADE; DROP TABLE IF EXISTS Contas CASCADE; DROP TABLE IF EXISTS SubCategoria CASCADE; DROP TABLE IF EXISTS MacroCategoria CASCADE; DROP TABLE IF EXISTS GrupoCategoria CASCADE; DROP TABLE IF EXISTS Usuarios CASCADE; DROP TABLE IF EXISTS wwebjs_auth_sessions;
    CREATE TABLE Usuarios ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, nome VARCHAR(255) NOT NULL, numero_whatsapp VARCHAR(50) NOT NULL UNIQUE, api_key_automate VARCHAR(100) NULL UNIQUE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );
    CREATE TABLE GrupoCategoria ( id SERIAL PRIMARY KEY, nome_grupo VARCHAR(100) NOT NULL UNIQUE );
    CREATE TABLE MacroCategoria ( id SERIAL PRIMARY KEY, grupo_id INT NOT NULL REFERENCES GrupoCategoria(id) ON DELETE RESTRICT, usuario_id INT REFERENCES Usuarios(id) ON DELETE CASCADE, nome_macro VARCHAR(255) NOT NULL, ordem_macro INT );
    CREATE TABLE SubCategoria ( id SERIAL PRIMARY KEY, macro_id INT NOT NULL REFERENCES MacroCategoria(id) ON DELETE CASCADE, usuario_id INT REFERENCES Usuarios(id) ON DELETE CASCADE, nome_sub VARCHAR(255) NOT NULL, UNIQUE(macro_id, nome_sub, usuario_id) );
    CREATE TABLE Contas ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE, nome_conta VARCHAR(100) NOT NULL, tipo_conta VARCHAR(50) NOT NULL CHECK (tipo_conta IN ('Conta Corrente', 'Conta Poupança', 'Investimento', 'Cartão de Crédito', 'Dinheiro', 'Outro')), saldo_inicial NUMERIC(15, 2) NOT NULL DEFAULT 0.00, dia_vencimento INT NULL CHECK (dia_vencimento >= 1 AND dia_vencimento <= 31), dia_fechamento INT NULL CHECK (dia_fechamento >= 1 AND dia_fechamento <= 31), created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, UNIQUE(usuario_id, nome_conta), CONSTRAINT chk_cartao_credito CHECK ( (tipo_conta = 'Cartão de Crédito') OR (dia_vencimento IS NULL AND dia_fechamento IS NULL) ) );
    CREATE TABLE Faturas ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, conta_id INT NOT NULL REFERENCES Contas(id) ON DELETE CASCADE, data_vencimento DATE NOT NULL, data_fechamento DATE NOT NULL, status VARCHAR(20) NOT NULL DEFAULT 'Aberta' CHECK (status IN ('Aberta', 'Fechada', 'Paga')), UNIQUE(conta_id, data_vencimento) );
    CREATE TABLE Transacoes ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE, conta_id INT NOT NULL REFERENCES Contas(id) ON DELETE RESTRICT, subcategoria_id INT NOT NULL REFERENCES SubCategoria(id) ON DELETE RESTRICT, fatura_id INT NULL REFERENCES Faturas(id) ON DELETE SET NULL, transferencia_par_id INT NULL REFERENCES Transacoes(id) ON DELETE SET NULL, descricao VARCHAR(255) NOT NULL, valor NUMERIC(15, 2) NOT NULL, data_transacao DATE NOT NULL DEFAULT CURRENT_DATE, tipo_transacao VARCHAR(20) NOT NULL CHECK (tipo_transacao IN ('Renda', 'Despesa', 'Transferência', 'Pagamento Fatura')), consolidada BOOLEAN NOT NULL DEFAULT TRUE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP );
    CREATE TABLE Agendamentos ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE, conta_id INT NOT NULL REFERENCES Contas(id) ON DELETE CASCADE, subcategoria_id INT NOT NULL REFERENCES SubCategoria(id) ON DELETE RESTRICT, descricao VARCHAR(255) NOT NULL, valor_previsto NUMERIC(15, 2), tipo_agendamento VARCHAR(20) NOT NULL CHECK (tipo_agendamento IN ('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')), periodicidade VARCHAR(20) NOT NULL CHECK (periodicidade IN ('DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL')), data_inicio DATE NOT NULL, dia_execucao INT NOT NULL CHECK (dia_execucao >= 1 AND dia_execucao <= 31), total_parcelas INT, parcelas_executadas INT DEFAULT 0, notificar_antes_dias INT DEFAULT 3, ativo BOOLEAN NOT NULL DEFAULT TRUE, created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP, CONSTRAINT chk_parcelado CHECK ( (tipo_agendamento = 'PARCELADO') OR (total_parcelas IS NULL AND parcelas_executadas = 0) ) );
    CREATE TABLE PotesDeGastos ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE, nome_pote VARCHAR(100) NOT NULL, valor_limite NUMERIC(15, 2) NOT NULL, periodicidade VARCHAR(20) NOT NULL DEFAULT 'MENSAL' CHECK (periodicidade IN ('SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL')), data_inicio DATE NOT NULL DEFAULT CURRENT_DATE, ativo BOOLEAN NOT NULL DEFAULT TRUE, UNIQUE(usuario_id, nome_pote) );
    CREATE TABLE PoteSubCategorias ( id INT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, pote_id INT NOT NULL REFERENCES PotesDeGastos(id) ON DELETE CASCADE, subcategoria_id INT NOT NULL REFERENCES SubCategoria(id) ON DELETE CASCADE, UNIQUE(pote_id, subcategoria_id) );
    CREATE TABLE IF NOT EXISTS baileys_auth (session_id VARCHAR(100), data_key VARCHAR(100), data_value TEXT, PRIMARY KEY (session_id, data_key));
    CREATE INDEX IF NOT EXISTS idx_usuarios_api_key_automate ON Usuarios(api_key_automate); CREATE INDEX IF NOT EXISTS idx_macro_usuario_id ON MacroCategoria(usuario_id); CREATE INDEX IF NOT EXISTS idx_sub_usuario_id ON SubCategoria(usuario_id); CREATE INDEX IF NOT EXISTS idx_contas_usuario_id ON Contas(usuario_id); CREATE INDEX IF NOT EXISTS idx_faturas_conta_id ON Faturas(conta_id); CREATE INDEX IF NOT EXISTS idx_transacoes_usuario_id ON Transacoes(usuario_id); CREATE INDEX IF NOT EXISTS idx_transacoes_fatura_id ON Transacoes(fatura_id); CREATE INDEX IF NOT EXISTS idx_transacoes_transfer_par_id ON Transacoes(transferencia_par_id); CREATE INDEX IF NOT EXISTS idx_agendamentos_usuario_id ON Agendamentos(usuario_id); CREATE INDEX IF NOT EXISTS idx_potes_usuario_id ON PotesDeGastos(usuario_id); CREATE INDEX IF NOT EXISTS idx_pote_subcat_pote_id ON PoteSubCategorias(pote_id); CREATE INDEX IF NOT EXISTS idx_pote_subcat_subcat_id ON PoteSubCategorias(subcategoria_id);
    """)
    
    try:
        with db_engine.connect() as conn:
            conn.execute(sql_script_ddl) 
            conn.commit() 
    except Exception as e:
        print(f"Erro ao criar estrutura do banco: {e}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        raise e

def populate_global_categories():
    """ Executa o DML (script de inserção de categorias globais). """
    if not db_engine: 
        raise Exception("Banco de dados não configurado")

    # (DML global, movido do app.py)
    sql_populate_dml = text("""
    DELETE FROM SubCategoria WHERE usuario_id IS NULL; DELETE FROM MacroCategoria WHERE usuario_id IS NULL; DELETE FROM GrupoCategoria;
    INSERT INTO GrupoCategoria (nome_grupo) VALUES ('Renda'), ('Despesa Essencial'), ('Meta Financeira'), ('Despesa Discricionária'), ('Geral');
    INSERT INTO MacroCategoria (grupo_id, nome_macro, ordem_macro) VALUES
        ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Renda'), 'Renda Principal', 1), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Renda'), 'Renda Extra e Passiva', 2), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Renda'), 'Investimentos e Reembolsos', 3), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Renda'), 'Outras Entradas', 4),
        ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Essencial'), 'Moradia e Contas', 1), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Essencial'), 'Alimentação Essencial', 2), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Essencial'), 'Saúde e Bem-Estar Básico', 3), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Essencial'), 'Educação Essencial', 4), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Essencial'), 'Transporte Essencial', 5),
        ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Meta Financeira'), 'Construção de Riqueza', 1), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Meta Financeira'), 'Gerenciamento de Dívidas', 2), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Meta Financeira'), 'Educação de Alto Custo', 3),
        ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Discricionária'), 'Lazer e Entretenimento', 1), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Discricionária'), 'Assinaturas e Serviços', 2), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Discricionária'), 'Cuidados Pessoais', 3), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Despesa Discricionária'), 'Compras Pessoais', 4),
        ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Geral'), 'Despesas Gerais', NULL), ((SELECT id FROM GrupoCategoria WHERE nome_grupo = 'Geral'), 'Receitas Gerais', NULL);
    INSERT INTO SubCategoria (macro_id, nome_sub) VALUES
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Renda Principal' AND usuario_id IS NULL), 'Salário Fixo / Pró-labore'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Renda Principal' AND usuario_id IS NULL), 'Remuneração Variável / Comissões'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Renda Extra e Passiva' AND usuario_id IS NULL), 'Aluguéis Recebidos'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Renda Extra e Passiva' AND usuario_id IS NULL), 'Freelances / Projetos Avulsos'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Investimentos e Reembolsos' AND usuario_id IS NULL), 'Dividendos / Juros (Rendimentos)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Investimentos e Reembolsos' AND usuario_id IS NULL), 'Restituições / Reembolsos'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Outras Entradas' AND usuario_id IS NULL), 'Presentes em Dinheiro / Doações'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Outras Entradas' AND usuario_id IS NULL), 'Venda de Bens (Pontual)'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Moradia e Contas' AND usuario_id IS NULL), 'Aluguel / Financiamento'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Moradia e Contas' AND usuario_id IS NULL), 'Condomínio / IPTU / Taxas'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Moradia e Contas' AND usuario_id IS NULL), 'Eletricidade / Água / Gás'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Moradia e Contas' AND usuario_id IS NULL), 'Internet / Telefone Fixo'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Moradia e Contas' AND usuario_id IS NULL), 'Manutenção da Casa'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Alimentação Essencial' AND usuario_id IS NULL), 'Supermercado / Mercearia'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Alimentação Essencial' AND usuario_id IS NULL), 'Feira / Hortifrúti'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Alimentação Essencial' AND usuario_id IS NULL), 'Suplementos e Itens Básicos'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Saúde e Bem-Estar Básico' AND usuario_id IS NULL), 'Plano de Saúde / Convênio'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Saúde e Bem-Estar Básico' AND usuario_id IS NULL), 'Medicamentos de Uso Contínuo'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Saúde e Bem-Estar Básico' AND usuario_id IS NULL), 'Consultas e Exames (não cobertos)'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Educação Essencial' AND usuario_id IS NULL), 'Mensalidades Escolares / Faculdade'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Educação Essencial' AND usuario_id IS NULL), 'Material Didático e Livros'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Transporte Essencial' AND usuario_id IS NULL), 'Combustível / Recarga (Carro/Moto)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Transporte Essencial' AND usuario_id IS NULL), 'Transporte Público (Bilhetes/Passe)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Transporte Essencial' AND usuario_id IS NULL), 'Aplicativos de Transporte (Necessidade)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Transporte Essencial' AND usuario_id IS NULL), 'IPVA / Seguro do Veículo'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Construção de Riqueza' AND usuario_id IS NULL), 'Aporte à Reserva de Emergência'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Construção de Riqueza' AND usuario_id IS NULL), 'Investimentos para Aposentadoria'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Construção de Riqueza' AND usuario_id IS NULL), 'Investimentos de Curto Prazo'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Gerenciamento de Dívidas' AND usuario_id IS NULL), 'Quitação de Empréstimos (Principal)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Gerenciamento de Dívidas' AND usuario_id IS NULL), 'Juros e Multas (Dívidas)'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Educação de Alto Custo' AND usuario_id IS NULL), 'Pós-graduação, Intercâmbio'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Lazer e Entretenimento' AND usuario_id IS NULL), 'Restaurantes, Bares e Delivery'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Lazer e Entretenimento' AND usuario_id IS NULL), 'Passeios e Eventos Culturais'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Lazer e Entretenimento' AND usuario_id IS NULL), 'Viagens e Hospedagem'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Assinaturas e Serviços' AND usuario_id IS NULL), 'Streaming (Netflix, Spotify, etc.)'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Assinaturas e Serviços' AND usuario_id IS NULL), 'Softwares / Apps Pagos'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Assinaturas e Serviços' AND usuario_id IS NULL), 'Academia / Clube'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Cuidados Pessoais' AND usuario_id IS NULL), 'Salão de Beleza / Barbearia'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Cuidados Pessoais' AND usuario_id IS NULL), 'Cosméticos e Higiene (não básica)'),
        ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Compras Pessoais' AND usuario_id IS NULL), 'Roupas, Calçados e Acessórios'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Compras Pessoais' AND usuario_id IS NULL), 'Eletrônicos e Gadgets'), ((SELECT id FROM MacroCategoria WHERE nome_macro = 'Compras Pessoais' AND usuario_id IS NULL), 'Presentes (Aniversários, Datas)');
    INSERT INTO SubCategoria (macro_id, nome_sub) SELECT id, 'Outros' FROM MacroCategoria WHERE usuario_id IS NULL;
    """)
    
    try:
        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql_populate_dml) 
            conn.commit()
    except Exception as e:
        print(f"Erro ao inserir templates globais: {e}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        raise e

def setup_user_data(numero_whatsapp, dia_venc_cartao, dia_fech_cartao):
    """ Insere/Atualiza os dados de um usuário e suas contas padrão. """
    if not db_engine: 
        raise Exception("Banco de dados não configurado")
        
    nova_api_key = secrets.token_hex(20) 

    sql_user = text(f"""
    INSERT INTO Usuarios (nome, numero_whatsapp, api_key_automate) 
    VALUES ('Rafael', :num_wpp, :api_key) 
    ON CONFLICT (numero_whatsapp) 
    DO UPDATE SET 
        nome = EXCLUDED.nome,
        api_key_automate = EXCLUDED.api_key_automate 
    RETURNING id, api_key_automate;
    """)
    
    try:
        user_id = None
        api_key_retornada = None
        
        with db_engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql_user, {
                "num_wpp": numero_whatsapp,
                "api_key": nova_api_key
            })
            user_data = result.fetchone() 
            
            if user_data:
                user_id = user_data[0]
                api_key_retornada = user_data[1]
            else:
                # Se o ON CONFLICT não retornou (pode acontecer em algumas configs de PG)
                sql_find = text("SELECT id, api_key_automate FROM Usuarios WHERE numero_whatsapp = :num")
                user_data = conn.execute(sql_find, {"num": numero_whatsapp}).fetchone()
                user_id = user_data[0]
                api_key_retornada = user_data[1]
            
            sql_accounts_com_id = text(f"""
                INSERT INTO Contas (usuario_id, nome_conta, tipo_conta, dia_vencimento, dia_fechamento) VALUES 
                    (:uid, 'Banco Inter', 'Conta Corrente', NULL, NULL),         
                    (:uid, 'Cartão Inter', 'Cartão de Crédito', :dia_venc, :dia_fech),
                    (:uid, 'Nubank', 'Conta Corrente', NULL, NULL),             
                    (:uid, 'Carteira', 'Dinheiro', NULL, NULL)
                ON CONFLICT (usuario_id, nome_conta) DO UPDATE SET
                    tipo_conta = EXCLUDED.tipo_conta,
                    dia_vencimento = EXCLUDED.dia_vencimento,
                    dia_fechamento = EXCLUDED.dia_fechamento; 
            """)
            
            conn.execute(sql_accounts_com_id, {
                "uid": user_id,
                "dia_venc": dia_venc_cartao,
                "dia_fech": dia_fech_cartao
            }) 
            conn.commit()
        
        return user_id, api_key_retornada

    except Exception as e:
        print(f"Erro ao inserir dados do usuário: {e}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        raise e
    
def get_user_by_api_key(api_key):
    """ Encontra um usuário pela sua API key do Automate. """
    if not db_engine: raise Exception("Banco não configurado")
    sql = text("SELECT id, numero_whatsapp FROM Usuarios WHERE api_key_automate = :api_key")
    with db_engine.connect() as conn:
        return conn.execute(sql, {"api_key": api_key}).fetchone() # Retorna (id, numero) ou None

def get_user_by_whatsapp(numero_whatsapp):
    """ Encontra um usuário pelo seu número de WhatsApp. """
    if not db_engine: raise Exception("Banco não configurado")
    sql = text("SELECT id FROM Usuarios WHERE numero_whatsapp = :num")
    with db_engine.connect() as conn:
        return conn.execute(sql, {"num": numero_whatsapp}).scalar_one_or_none() # Retorna id ou None

def get_user_accounts(conn, usuario_id):
    """ Busca todas as contas de um usuário. (Requer conexão). """
    sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid")
    return conn.execute(sql, {"uid": usuario_id}).fetchall()

def get_user_categories(conn, usuario_id, tipo_transacao):
    """ Busca categorias globais + do usuário por tipo (Renda/Despesa). (Requer conexão). """
    grupo_filtro_sql = "g.nome_grupo = 'Renda'"
    if tipo_transacao == 'Despesa': 
        grupo_filtro_sql = "g.nome_grupo IN ('Despesa Essencial', 'Despesa Discricionária', 'Meta Financeira', 'Geral')"
    
    sql = text(f"""
        SELECT s.id, s.nome_sub, m.nome_macro 
        FROM SubCategoria s 
        JOIN MacroCategoria m ON s.macro_id = m.id 
        JOIN GrupoCategoria g ON m.grupo_id = g.id 
        WHERE (s.usuario_id IS NULL OR s.usuario_id = :uid) AND ({grupo_filtro_sql})
    """)
    result = conn.execute(sql, {"uid": usuario_id}).fetchall()
    # Converte para o formato JSON que o Gemini espera
    return [{"id": row[0], "nome_sub": row[1], "nome_macro": row[2]} for row in result]

def get_fallback_category_id(conn, tipo_transacao):
    """ Pega o ID da subcategoria 'Outros' (Renda ou Despesa). (Requer conexão). """
    nome_macro_outros = 'Receitas Gerais' if tipo_transacao == 'Renda' else 'Despesas Gerais'
    sql = text("""
        SELECT s.id FROM SubCategoria s 
        JOIN MacroCategoria m ON s.macro_id = m.id 
        WHERE m.nome_macro = :nome_macro AND s.nome_sub = 'Outros' AND s.usuario_id IS NULL LIMIT 1
    """)
    return conn.execute(sql, {"nome_macro": nome_macro_outros}).scalar_one_or_none()

def get_account_by_name(conn, usuario_id, nome_conta, fallback=False):
    """ Busca um ID de conta pelo nome (exato ou ILIKE). (Requer conexão). """
    sql_exact = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta = :nome")
    conta_id = conn.execute(sql_exact, {"uid": usuario_id, "nome": nome_conta}).scalar_one_or_none()

    if conta_id: return conta_id

    sql_like = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome_like")
    conta_id = conn.execute(sql_like, {"uid": usuario_id, "nome_like": f"%{nome_conta}%"}).scalar_one_or_none()

    if conta_id: return conta_id

    if fallback:
        sql_fallback = text("SELECT id FROM Contas WHERE usuario_id = :uid LIMIT 1")
        return conn.execute(sql_fallback, {"uid": usuario_id}).scalar_one()

    return None

def get_account_details_by_name(conn, usuario_id, nome_conta):
    """ Busca detalhes completos de uma conta pelo nome (id, nome, tipo). (Requer conexão). """
    sql_exact = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid AND nome_conta = :nome")
    conta = conn.execute(sql_exact, {"uid": usuario_id, "nome": nome_conta}).fetchone()

    if conta:
        return {"id": conta[0], "nome": conta[1], "tipo": conta[2]}

    # Tentar busca parcial (ILIKE)
    sql_like = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome_like")
    conta = conn.execute(sql_like, {"uid": usuario_id, "nome_like": f"%{nome_conta}%"}).fetchone()

    if conta:
        return {"id": conta[0], "nome": conta[1], "tipo": conta[2]}

    return None

def get_category_name_by_id(conn, subcategoria_id):
    """ Busca o nome formatado 'Macro -> Sub' pelo ID. (Requer conexão). """
    sql = text("SELECT s.nome_sub, m.nome_macro FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id WHERE s.id = :scid")
    info = conn.execute(sql, {"scid": subcategoria_id}).fetchone()
    return f"{info[1]} -> {info[0]}" if info else "Categoria Desconhecida"

def create_transaction(conn, usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao):
    """ Insere uma transação simples (Renda/Despesa). (Requer conexão). """
    sql = text("""
        INSERT INTO Transacoes 
        (usuario_id, conta_id, subcategoria_id, fatura_id, transferencia_par_id, descricao, valor, tipo_transacao, data_transacao) 
        VALUES (:uid, :cid, :scid, :fid, NULL, :desc, :val, :tipo, :data)
    """)
    conn.execute(sql, {
        "uid": usuario_id, "cid": conta_id, "scid": subcategoria_id, "fid": fatura_id,
        "desc": descricao, "val": valor, "tipo": tipo_transacao, "data": data_transacao
    })

def create_transfer_pair(conn, usuario_id, conta_id_origem, conta_id_destino, valor, data_transacao):
    """ Cria o par de transações (entrada/saída) para uma transferência. (Requer conexão). """
    valor_saida = (float(valor) * -1)
    valor_entrada = float(valor)
    
    sql_get_subcat = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE g.nome_grupo = 'Meta Financeira' AND s.nome_sub = 'Investimentos de Curto Prazo' AND (s.usuario_id IS NULL OR s.usuario_id = :uid) LIMIT 1")
    id_subcat_transfer = conn.execute(sql_get_subcat, {"uid": usuario_id}).scalar_one_or_none()
    if not id_subcat_transfer:
        raise Exception("Subcategoria 'Investimentos de Curto Prazo' não encontrada.")

    sql_get_nomes = text("SELECT nome_conta FROM Contas WHERE id = :id")
    nome_origem = conn.execute(sql_get_nomes, {"id": conta_id_origem}).scalar()
    nome_destino = conn.execute(sql_get_nomes, {"id": conta_id_destino}).scalar()

    sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :desc, :val, 'Transferência', :data) RETURNING id")
    
    desc_saida = f"Transferência para {nome_destino}"
    result_saida = conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_origem, "scid": id_subcat_transfer, "desc": desc_saida, "val": valor_saida, "data": data_transacao})
    id_transacao_saida = result_saida.scalar_one()
    
    desc_entrada = f"Transferência de {nome_origem}"
    result_entrada = conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_destino, "scid": id_subcat_transfer, "desc": desc_entrada, "val": valor_entrada, "data": data_transacao})
    id_transacao_entrada = result_entrada.scalar_one()
    
    sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo")
    conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida})
    conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})
    
    return nome_origem, nome_destino

def create_fatura_payment(conn, usuario_id, conta_id_origem, conta_id_cartao, valor, data_transacao):
    """ Cria o par de transações (pagamento/recebimento) para uma fatura. (Requer conexão). """
    valor_saida = (float(valor) * -1)
    valor_entrada = float(valor)
    
    fatura_id_pagar = get_or_create_fatura(conn, conta_id_cartao, data_transacao, usuario_id)
    
    sql_get_subcat = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE g.nome_grupo = 'Meta Financeira' AND s.nome_sub = 'Quitação de Empréstimos (Principal)' AND (s.usuario_id IS NULL OR s.usuario_id = :uid) LIMIT 1")
    id_subcat_pagto = conn.execute(sql_get_subcat, {"uid": usuario_id}).scalar_one_or_none()
    if not id_subcat_pagto:
        raise Exception("Subcategoria 'Quitação de Empréstimos (Principal)' não encontrada.")

    sql_get_nomes = text("SELECT nome_conta FROM Contas WHERE id = :id")
    nome_origem = conn.execute(sql_get_nomes, {"id": conta_id_origem}).scalar()
    nome_cartao = conn.execute(sql_get_nomes, {"id": conta_id_cartao}).scalar()
    
    sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, 'Pagamento Fatura', :data) RETURNING id")
    
    desc_saida = f"Pagamento Fatura {nome_cartao}"
    result_saida = conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_origem, "scid": id_subcat_pagto, "fid": fatura_id_pagar, "desc": desc_saida, "val": valor_saida, "data": data_transacao})
    id_transacao_saida = result_saida.scalar_one()
    
    desc_entrada = f"Pagamento Recebido (de {nome_origem})"
    result_entrada = conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_cartao, "scid": id_subcat_pagto, "fid": fatura_id_pagar, "desc": desc_entrada, "val": valor_entrada, "data": data_transacao})
    id_transacao_entrada = result_entrada.scalar_one()
    
    sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo")
    conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida})
    conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})
    
    sql_update_fatura = text("UPDATE Faturas SET status = 'Paga' WHERE id = :fid")
    conn.execute(sql_update_fatura, {"fid": fatura_id_pagar})
    
    return nome_cartao

def get_fatura_valor(conn, usuario_id, conta_id_cartao=None):
    """
    Consulta o valor atual da(s) fatura(s) em aberto.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id_cartao: ID do cartão (opcional). Se None, retorna todas as faturas.

    Returns:
        List de dicts com informações das faturas:
        [{
            "nome_cartao": "Nubank",
            "valor_fatura": 1500.50,
            "data_vencimento": date(2025, 12, 15),
            "status": "Aberta"
        }]
    """
    if conta_id_cartao:
        # Consultar fatura específica de um cartão
        sql = text("""
            SELECT
                c.nome_conta,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
                f.data_vencimento,
                f.status
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
                AND c.id = :cid
                AND f.status = 'Aberta'
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC
            LIMIT 1
        """)
        result = conn.execute(sql, {"uid": usuario_id, "cid": conta_id_cartao}).fetchall()
    else:
        # Consultar todas as faturas abertas
        sql = text("""
            SELECT
                c.nome_conta,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
                f.data_vencimento,
                f.status
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
                AND f.status = 'Aberta'
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC
        """)
        result = conn.execute(sql, {"uid": usuario_id}).fetchall()

    faturas = []
    for row in result:
        faturas.append({
            "nome_cartao": row[0],
            "valor_fatura": float(row[1]),
            "data_vencimento": row[2],
            "status": row[3]
        })

    return faturas

def get_saldo_contas(conn, usuario_id, conta_id=None):
    """
    Consulta o saldo atual das contas do usuário.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID da conta específica (opcional). Se None, retorna todas.

    Returns:
        List de dicts com saldos:
        [{
            "nome_conta": "Nubank",
            "tipo_conta": "Conta Corrente",
            "saldo": 1500.50
        }]
    """
    if conta_id:
        # Consultar saldo de uma conta específica
        sql = text("""
            SELECT
                c.nome_conta,
                c.tipo_conta,
                COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
                AND c.id = :cid
            GROUP BY c.nome_conta, c.tipo_conta
        """)
        result = conn.execute(sql, {"uid": usuario_id, "cid": conta_id}).fetchall()
    else:
        # Consultar todas as contas
        sql = text("""
            SELECT
                c.nome_conta,
                c.tipo_conta,
                COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
            GROUP BY c.nome_conta, c.tipo_conta
            ORDER BY c.tipo_conta, c.nome_conta
        """)
        result = conn.execute(sql, {"uid": usuario_id}).fetchall()

    contas = []
    for row in result:
        contas.append({
            "nome_conta": row[0],
            "tipo_conta": row[1],
            "saldo": float(row[2])
        })

    return contas

def create_parcelamento_agendamento(conn, usuario_id, conta_id, categoria_id, descricao, valor_parcela, num_parcelas, data_primeira_parcela):
    """
    Cria agendamentos para as parcelas futuras de uma compra parcelada.

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
        ID do agendamento criado
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

def get_pote_status(conn, usuario_id):
    """ Consulta o status de todos os potes de gasto do mês. (Requer conexão). """
    sql = text("""
        SELECT
            p.nome_pote, p.valor_limite,
            COALESCE(SUM(t.valor), 0) AS valor_gasto_negativo
        FROM PotesDeGastos p
        LEFT JOIN PoteSubCategorias psc ON p.id = psc.pote_id
        LEFT JOIN Transacoes t ON psc.subcategoria_id = t.subcategoria_id
            AND t.tipo_transacao = 'Despesa'
            AND t.usuario_id = :uid
            AND t.data_transacao >= date_trunc('month', CURRENT_DATE)
            AND t.data_transacao < date_trunc('month', CURRENT_DATE) + interval '1 month'
        WHERE
            p.usuario_id = :uid
            AND p.ativo = TRUE
        GROUP BY p.id, p.nome_pote, p.valor_limite
        ORDER BY p.nome_pote;
    """)
    return conn.execute(sql, {"uid": usuario_id}).fetchall()

def get_reserva_status(conn, usuario_id):
    """ Calcula a média de gastos essenciais dos últimos 3 meses. (Requer conexão). """
    sql = text("""
        SELECT 
            COALESCE(SUM(t.valor), 0) AS total_essencial_negativo
        FROM Transacoes t
        JOIN SubCategoria s ON t.subcategoria_id = s.id 
        JOIN MacroCategoria m ON s.macro_id = m.id 
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE t.usuario_id = :uid
          AND g.nome_grupo = 'Despesa Essencial'
          AND t.tipo_transacao = 'Despesa'
          AND t.data_transacao >= date_trunc('month', CURRENT_DATE) - interval '3 month'
          AND t.data_transacao < date_trunc('month', CURRENT_DATE)
    """)
    total_negativo = conn.execute(sql, {"uid": usuario_id}).scalar()
    media_mensal = (float(total_negativo or 0) * -1) / 3
    reserva_ideal = media_mensal * 6
    return media_mensal, reserva_ideal

def get_category_spending(conn, usuario_id, nome_categoria_consulta):
    """ Consulta o gasto total em uma categoria/macro-categoria no mês atual. (Requer conexão). """
    sql = text("""
        WITH CategoriaAlvo AS (
            SELECT id FROM SubCategoria 
            WHERE (usuario_id = :uid OR usuario_id IS NULL) 
              AND nome_sub ILIKE :nome_cat
            
            UNION
            
            SELECT s.id FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            WHERE (m.usuario_id = :uid OR m.usuario_id IS NULL) 
              AND m.nome_macro ILIKE :nome_cat
        )
        SELECT COALESCE(SUM(t.valor), 0) AS valor_gasto_total
        FROM Transacoes t
        WHERE t.usuario_id = :uid
          AND t.tipo_transacao = 'Despesa'
          AND EXTRACT(MONTH FROM t.data_transacao) = EXTRACT(MONTH FROM CURRENT_DATE)
          AND EXTRACT(YEAR FROM t.data_transacao) = EXTRACT(YEAR FROM CURRENT_DATE)
          AND t.subcategoria_id IN (SELECT id FROM CategoriaAlvo);
    """)
    gasto_total_negativo = conn.execute(sql, {"uid": usuario_id, "nome_cat": f"%{nome_categoria_consulta}%"}).scalar()
    return (float(gasto_total_negativo or 0)) * -1

def add_google_calendar_tokens_table():
    """Adiciona tabela para armazenar tokens OAuth2 do Google Calendar"""
    if not db_engine:
        raise Exception("Banco não configurado")
    
    sql = text("""
        CREATE TABLE IF NOT EXISTS GoogleCalendarTokens (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            access_token TEXT NOT NULL,
            refresh_token TEXT,
            token_expiry TIMESTAMP WITH TIME ZONE,
            scopes TEXT,
            created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(usuario_id)
        );
        
        CREATE INDEX IF NOT EXISTS idx_calendar_tokens_usuario 
        ON GoogleCalendarTokens(usuario_id);
    """)
    
    try:
        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql)
            conn.commit()
            print("[DB] ✅ Tabela GoogleCalendarTokens criada/verificada")
    except Exception as e:
        print(f"[DB] Erro ao criar tabela: {e}")
        raise