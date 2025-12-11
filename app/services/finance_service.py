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
    """
    Encontra um usuário pela sua API key do Automate.

    NOTA: Busca todas as API keys e descriptografa para comparar.
    Em produção com muitos usuários, considere indexar hash da chave.
    """
    if not db_engine:
        raise Exception("Banco não configurado")

    from app.services.encryption_service import encryption_service

    # Buscar todos os usuários com API key
    sql = text("SELECT id, numero_whatsapp, api_key_automate FROM Usuarios WHERE api_key_automate IS NOT NULL")

    with db_engine.connect() as conn:
        results = conn.execute(sql).fetchall()

        # Comparar descriptografando cada chave
        for row in results:
            stored_key = row.api_key_automate

            try:
                # Tentar descriptografar
                decrypted_key = encryption_service.decrypt(stored_key)

                if decrypted_key == api_key:
                    # Retornar no mesmo formato que antes
                    return (row.id, row.numero_whatsapp)
            except:
                # Chave pode estar em plain text (dados antigos)
                # Comparação direta como fallback
                if stored_key == api_key:
                    return (row.id, row.numero_whatsapp)

        return None  # Nenhuma chave correspondente encontrada

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
    """ Insere uma transação simples (Renda/Despesa). (Requer conexão). Retorna o ID da transação criada. """
    sql = text("""
        INSERT INTO Transacoes
        (usuario_id, conta_id, subcategoria_id, fatura_id, transferencia_par_id, descricao, valor, tipo_transacao, data_transacao)
        VALUES (:uid, :cid, :scid, :fid, NULL, :desc, :val, :tipo, :data)
        RETURNING id
    """)
    result = conn.execute(sql, {
        "uid": usuario_id, "cid": conta_id, "scid": subcategoria_id, "fid": fatura_id,
        "desc": descricao, "val": valor, "tipo": tipo_transacao, "data": data_transacao
    })
    return result.scalar_one()

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
                c.saldo_inicial + COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
                AND c.id = :cid
            GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
        """)
        result = conn.execute(sql, {"uid": usuario_id, "cid": conta_id}).fetchall()
    else:
        # Consultar todas as contas
        sql = text("""
            SELECT
                c.nome_conta,
                c.tipo_conta,
                c.saldo_inicial + COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
            GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
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

def update_saldo_inicial(conn, usuario_id, conta_id, novo_saldo_inicial):
    """
    Atualiza o saldo_inicial de uma conta.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID da conta
        novo_saldo_inicial: Novo valor do saldo inicial

    Returns:
        bool: True se atualizou com sucesso
    """
    sql = text("""
        UPDATE Contas
        SET saldo_inicial = :novo_saldo
        WHERE id = :cid AND usuario_id = :uid
    """)

    result = conn.execute(sql, {
        "novo_saldo": novo_saldo_inicial,
        "cid": conta_id,
        "uid": usuario_id
    })

    conn.commit()
    return result.rowcount > 0

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
    """
    Calcula a reserva de emergência com base em agendamentos de TODAS as periodicidades.

    Versão 4.0: Quantidade de meses CONFIGURÁVEL por usuário (coluna meses_reserva_emergencia).
    ANUAIS são incluídos com valor INTEGRAL (mais conservador).

    Lógica de normalização DINÂMICA:
    - MENSAL: valor × N meses
    - ANUAL: valor INTEGRAL (ex: IPTU R$ 1200/ano → R$ 1200 - mais conservador)
    - SEMANAL: valor × (N meses × 4.33 semanas/mês)
    - QUINZENAL: valor × (N meses × 2 quinzenas/mês)
    - DIARIA: valor × (N meses × 30 dias/mês)

    Returns:
        (gasto_mensal_equivalente, reserva_ideal_N_meses, quantidade_meses_configurada)
    """
    # Buscar quantos meses o usuário configurou (padrão: 6 meses)
    sql_meses = text("""
        SELECT COALESCE(meses_reserva_emergencia, 6) as meses
        FROM Usuarios
        WHERE id = :uid
    """)
    meses_row = conn.execute(sql_meses, {"uid": usuario_id}).fetchone()
    meses = int(meses_row.meses) if meses_row else 6

    sql = text("""
        SELECT
            a.periodicidade,
            COALESCE(SUM(a.valor_previsto), 0) AS total_periodo
        FROM Agendamentos a
        WHERE a.usuario_id = :uid
          AND a.ativo = TRUE
          AND a.incluir_na_reserva = TRUE
          AND (a.tipo_agendamento = 'FIXO' OR a.tipo_agendamento = 'LEMBRETE_VARIAVEL')
        GROUP BY a.periodicidade
    """)

    results = conn.execute(sql, {"uid": usuario_id}).fetchall()

    # Normalizar cada periodicidade para N meses (configurado pelo usuário)
    reserva_total_n_meses = 0.0

    for row in results:
        periodicidade = row.periodicidade
        valor_periodo = float(row.total_periodo or 0)

        if periodicidade == 'MENSAL':
            # Valor mensal × N meses
            reserva_total_n_meses += valor_periodo * meses

        elif periodicidade == 'ANUAL':
            # Valor anual INTEGRAL (mais conservador - IPTU/IPVA podem ter parcelas nos N meses)
            reserva_total_n_meses += valor_periodo

        elif periodicidade == 'SEMANAL':
            # Valor semanal × (N meses × 4.33 semanas/mês)
            semanas = meses * 4.33
            reserva_total_n_meses += valor_periodo * semanas

        elif periodicidade == 'QUINZENAL':
            # Valor quinzenal × (N meses × 2 quinzenas/mês)
            quinzenas = meses * 2
            reserva_total_n_meses += valor_periodo * quinzenas

        elif periodicidade == 'DIARIA':
            # Valor diário × (N meses × 30 dias/mês)
            dias = meses * 30
            reserva_total_n_meses += valor_periodo * dias

    # Calcular equivalente mensal (reserva / N)
    gasto_mensal_equivalente = reserva_total_n_meses / meses if meses > 0 else 0

    return gasto_mensal_equivalente, reserva_total_n_meses, meses

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

def get_upcoming_bills_and_invoices(conn, usuario_id, target_date=None):
    """
    Busca contas fixas e faturas que vão vencer hoje ou amanhã.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        target_date: Data de referência (padrão: hoje)

    Returns:
        dict: {
            'contas_hoje': [...],
            'contas_amanha': [...],
            'faturas_hoje': [...],
            'faturas_amanha': [...]
        }
    """
    from datetime import timedelta

    if target_date is None:
        target_date = date.today()

    amanha = target_date + timedelta(days=1)

    # Buscar contas fixas pendentes
    # IMPORTANTE: Executar queries separadas para HOJE e AMANHÃ para corrigir bug de virada de mês
    # (Ex: 31/12 busca dezembro, 01/01 busca janeiro)
    sql_contas = text("""
        SELECT
            a.id,
            a.descricao,
            a.valor_previsto,
            a.dia_execucao,
            s.nome_sub as categoria,
            c.nome_conta
        FROM Agendamentos a
        JOIN SubCategoria s ON a.subcategoria_id = s.id
        JOIN Contas c ON a.conta_id = c.id
        WHERE a.usuario_id = :uid
          AND a.ativo = TRUE
          AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
          AND a.dia_execucao = :dia
          -- Verificar se ainda não foi executado este mês
          AND NOT EXISTS (
              SELECT 1 FROM Transacoes t
              WHERE t.descricao = a.descricao
                AND t.usuario_id = a.usuario_id
                AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
                AND t.tipo_transacao = 'Despesa'
          )
        ORDER BY a.dia_execucao ASC
    """)

    # Query 1: Contas que vencem HOJE
    contas_hoje_result = conn.execute(sql_contas, {
        "uid": usuario_id,
        "dia": target_date.day,
        "mes_ref": target_date.month,
        "ano_ref": target_date.year
    }).fetchall()

    # Query 2: Contas que vencem AMANHÃ (usa mês/ano de amanhã - corrige virada de mês)
    contas_amanha_result = conn.execute(sql_contas, {
        "uid": usuario_id,
        "dia": amanha.day,
        "mes_ref": amanha.month,
        "ano_ref": amanha.year
    }).fetchall()

    # Processar resultados
    contas_hoje = []
    contas_amanha = []

    for conta in contas_hoje_result:
        contas_hoje.append({
            "id": conta.id,
            "descricao": conta.descricao,
            "valor": float(conta.valor_previsto or 0),
            "categoria": conta.categoria,
            "conta": conta.nome_conta
        })

    for conta in contas_amanha_result:
        contas_amanha.append({
            "id": conta.id,
            "descricao": conta.descricao,
            "valor": float(conta.valor_previsto or 0),
            "categoria": conta.categoria,
            "conta": conta.nome_conta
        })

    # Buscar faturas de cartão de crédito
    sql_faturas = text("""
        SELECT
            c.nome_conta,
            f.data_vencimento,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
            f.status
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = :uid
            AND f.status = 'Aberta'
            AND (f.data_vencimento = :hoje OR f.data_vencimento = :amanha)
        GROUP BY c.nome_conta, f.data_vencimento, f.status
        ORDER BY f.data_vencimento ASC
    """)

    faturas_result = conn.execute(sql_faturas, {
        "uid": usuario_id,
        "hoje": target_date,
        "amanha": amanha
    }).fetchall()

    # Separar faturas por dia
    faturas_hoje = []
    faturas_amanha = []

    for fatura in faturas_result:
        fatura_dict = {
            "cartao": fatura.nome_conta,
            "valor": float(fatura.valor_fatura),
            "vencimento": fatura.data_vencimento
        }

        if fatura.data_vencimento == target_date:
            faturas_hoje.append(fatura_dict)
        elif fatura.data_vencimento == amanha:
            faturas_amanha.append(fatura_dict)

    return {
        'contas_hoje': contas_hoje,
        'contas_amanha': contas_amanha,
        'faturas_hoje': faturas_hoje,
        'faturas_amanha': faturas_amanha
    }

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


def add_nightly_checkin_config_columns():
    """
    Adiciona colunas de check-in noturno na tabela NotificationConfigs.
    Função idempotente - pode ser executada múltiplas vezes sem erros.
    """
    if not db_engine:
        raise Exception("Banco não configurado")

    sql = text("""
        -- Adicionar colunas
        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS checkin_noturno_ativo BOOLEAN NOT NULL DEFAULT TRUE,
        ADD COLUMN IF NOT EXISTS checkin_noturno_hora TIME NOT NULL DEFAULT '20:00:00';

        -- Adicionar constraint (usando DO para evitar erro se já existe)
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_constraint WHERE conname = 'chk_checkin_hora'
            ) THEN
                ALTER TABLE NotificationConfigs
                ADD CONSTRAINT chk_checkin_hora CHECK (
                    checkin_noturno_hora >= '18:00:00'::TIME
                    AND checkin_noturno_hora <= '23:00:00'::TIME
                );
            END IF;
        END $$;

        -- Adicionar comentários
        COMMENT ON COLUMN NotificationConfigs.checkin_noturno_ativo IS
        'Se TRUE, envia check-in noturno com contas pendentes (D-0 até D-7)';

        COMMENT ON COLUMN NotificationConfigs.checkin_noturno_hora IS
        'Horário para envio do check-in noturno (18:00-23:00)';
    """)

    try:
        with db_engine.connect() as conn:
            with conn.begin():
                conn.execute(sql)
            print("[MIGRATION] ✅ Colunas de check-in noturno adicionadas com sucesso")
            return True
    except Exception as e:
        print(f"[MIGRATION] ❌ Erro ao adicionar colunas: {e}")
        import traceback
        traceback.print_exc()
        return False


def criar_tabelas_chaves_api():
    """
    Cria tabelas para sistema de chaves de API por usuário (SaaS).

    NOVIDADES:
    - ChavesApiUsuario: Armazena chaves do usuário (criptografadas)
    - PreferenciasChaveApi: Escolha explícita (chave própria ou sistema)
    - LogAcessoChaveApi: Auditoria de segurança
    - RastreamentoUsoApi: Tracking para billing
    - ConsentimentoUsuario: LGPD compliance
    - Planos + AssinaturasUsuario: Sistema de planos (Bronze, Prata, Ouro)

    Função idempotente - pode ser executada múltiplas vezes.
    """
    if not db_engine:
        raise Exception("Banco não configurado")

    sql = text("""
        -- Tabela 1: Armazenamento de chaves do usuário
        CREATE TABLE IF NOT EXISTS ChavesApiUsuario (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            provedor VARCHAR(50) NOT NULL,
            chave_api_criptografada TEXT NOT NULL,
            ativo BOOLEAN DEFAULT TRUE,
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            atualizado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            ultimo_uso_em TIMESTAMP WITH TIME ZONE,
            consentimento_dado BOOLEAN DEFAULT TRUE,
            consentimento_data TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(usuario_id, provedor)
        );

        CREATE INDEX IF NOT EXISTS idx_chaves_api_usuario ON ChavesApiUsuario(usuario_id);
        CREATE INDEX IF NOT EXISTS idx_chaves_api_ativo
            ON ChavesApiUsuario(usuario_id, provedor) WHERE ativo = TRUE;

        -- Tabela 2: Preferências (escolha explícita do usuário)
        CREATE TABLE IF NOT EXISTS PreferenciasChaveApi (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            provedor VARCHAR(50) NOT NULL,
            usar_chave_propria BOOLEAN NOT NULL DEFAULT FALSE,
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            atualizado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(usuario_id, provedor)
        );

        CREATE INDEX IF NOT EXISTS idx_preferencias_usuario ON PreferenciasChaveApi(usuario_id);

        COMMENT ON COLUMN PreferenciasChaveApi.usar_chave_propria IS
        'FALSE = usa chave do sistema (PAGA), TRUE = usa chave própria (GRÁTIS)';

        -- Tabela 3: Logs de acesso (auditoria)
        CREATE TABLE IF NOT EXISTS LogAcessoChaveApi (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            provedor VARCHAR(50) NOT NULL,
            tipo_chave VARCHAR(20) NOT NULL,
            operacao VARCHAR(50) NOT NULL,
            sucesso BOOLEAN NOT NULL,
            mensagem_erro TEXT,
            timestamp_acesso TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            endereco_ip VARCHAR(45),
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS idx_log_acesso_usuario
            ON LogAcessoChaveApi(usuario_id, timestamp_acesso DESC);
        CREATE INDEX IF NOT EXISTS idx_log_acesso_provedor
            ON LogAcessoChaveApi(provedor, timestamp_acesso DESC);

        -- Tabela 4: Rastreamento de uso (billing)
        CREATE TABLE IF NOT EXISTS RastreamentoUsoApi (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            provedor VARCHAR(50) NOT NULL,
            tipo_chave VARCHAR(20) NOT NULL,
            quantidade_chamadas INT DEFAULT 0,
            mes_ano VARCHAR(7) NOT NULL,
            valor_cobrado NUMERIC(10, 2) DEFAULT 0.00,
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            atualizado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            UNIQUE(usuario_id, provedor, tipo_chave, mes_ano)
        );

        CREATE INDEX IF NOT EXISTS idx_rastreamento_mes ON RastreamentoUsoApi(mes_ano, provedor);
        CREATE INDEX IF NOT EXISTS idx_rastreamento_usuario ON RastreamentoUsoApi(usuario_id, mes_ano DESC);

        -- Tabela 5: Consentimentos LGPD
        CREATE TABLE IF NOT EXISTS ConsentimentoUsuario (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            tipo_consentimento VARCHAR(50) NOT NULL,
            versao_consentimento VARCHAR(20) NOT NULL,
            consentimento_dado BOOLEAN NOT NULL,
            data_consentimento TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            ip_consentimento VARCHAR(45),
            texto_consentimento TEXT,
            revogado_em TIMESTAMP WITH TIME ZONE,
            UNIQUE(usuario_id, tipo_consentimento, versao_consentimento)
        );

        CREATE INDEX IF NOT EXISTS idx_consentimento_ativo
            ON ConsentimentoUsuario(usuario_id, tipo_consentimento)
            WHERE consentimento_dado = TRUE AND revogado_em IS NULL;

        -- Tabela 6: Planos (sistema de assinaturas)
        CREATE TABLE IF NOT EXISTS Planos (
            id SERIAL PRIMARY KEY,
            nome_plano VARCHAR(50) NOT NULL UNIQUE,
            descricao TEXT,
            preco_mensal NUMERIC(10, 2) NOT NULL,
            limite_gemini INT,
            limite_weather INT,
            limite_openroute INT,
            custo_por_chamada_gemini NUMERIC(10, 4) DEFAULT 0.01,
            custo_por_chamada_weather NUMERIC(10, 4) DEFAULT 0.005,
            custo_por_chamada_openroute NUMERIC(10, 4) DEFAULT 0.02,
            permite_chaves_proprias BOOLEAN DEFAULT TRUE,
            ativo BOOLEAN DEFAULT TRUE,
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            atualizado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        -- Inserir planos padrão (idempotente)
        INSERT INTO Planos (nome_plano, descricao, preco_mensal, limite_gemini, limite_weather, limite_openroute)
        VALUES
            ('Bronze', 'Plano básico gratuito', 0.00, 100, 50, 10),
            ('Prata', 'Plano intermediário', 29.90, 500, 200, 50),
            ('Ouro', 'Plano premium ilimitado', 79.90, NULL, NULL, NULL)
        ON CONFLICT (nome_plano) DO NOTHING;

        -- Tabela 7: Assinaturas dos usuários
        CREATE TABLE IF NOT EXISTS AssinaturasUsuario (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,
            plano_id INT NOT NULL REFERENCES Planos(id) ON DELETE RESTRICT,
            data_inicio TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            data_fim TIMESTAMP WITH TIME ZONE,
            status VARCHAR(20) NOT NULL DEFAULT 'ativo',
            criado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            atualizado_em TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS idx_assinaturas_usuario ON AssinaturasUsuario(usuario_id, status);
        CREATE INDEX IF NOT EXISTS idx_assinaturas_ativas ON AssinaturasUsuario(usuario_id)
            WHERE status = 'ativo' AND (data_fim IS NULL OR data_fim > CURRENT_TIMESTAMP);
    """)

    try:
        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql)
            conn.commit()
            print("[DB] ✅ Tabelas de chaves de API criadas com sucesso (PT-BR)")
            print("[DB] ℹ️ Tabelas criadas: ChavesApiUsuario, PreferenciasChaveApi, LogAcessoChaveApi,")
            print("[DB] ℹ️                    RastreamentoUsoApi, ConsentimentoUsuario, Planos, AssinaturasUsuario")
            return True
    except Exception as e:
        print(f"[DB] ❌ Erro ao criar tabelas: {e}")
        import traceback
        traceback.print_exc()
        return False

# ====================================
# Consultas de Vencimentos
# ====================================

def get_vencimentos_periodo(conn, usuario_id, data_inicio, data_fim):
    """
    Busca contas fixas e faturas que vencem em um período específico.

    Args:
        conn: Conexão do banco
        usuario_id: ID do usuário
        data_inicio: Data inicial (date)
        data_fim: Data final (date)

    Returns:
        dict com contas_fixas, faturas e totais
    """
    from calendar import monthrange

    # 1. BUSCAR CONTAS FIXAS PENDENTES
    # Precisa considerar virada de mês
    dia_inicio = data_inicio.day
    dia_fim = data_fim.day
    mes_ref = data_inicio.month
    ano_ref = data_inicio.year

    # Se período cruza virada de mês, buscar em dois meses
    if data_inicio.month != data_fim.month:
        # Buscar do início até fim do primeiro mês
        sql_contas_mes1 = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao >= :dia_inicio
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_mes1 = conn.execute(sql_contas_mes1, {
            "uid": usuario_id,
            "dia_inicio": dia_inicio,
            "mes_ref": data_inicio.month,
            "ano_ref": data_inicio.year
        }).fetchall()

        # Buscar do início do mês seguinte até dia_fim
        sql_contas_mes2 = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao <= :dia_fim
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_mes2 = conn.execute(sql_contas_mes2, {
            "uid": usuario_id,
            "dia_fim": dia_fim,
            "mes_ref": data_fim.month,
            "ano_ref": data_fim.year
        }).fetchall()

        contas_fixas = list(contas_mes1) + list(contas_mes2)
    else:
        # Mesmo mês, busca simples
        sql_contas = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao BETWEEN :dia_inicio AND :dia_fim
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_fixas = conn.execute(sql_contas, {
            "uid": usuario_id,
            "dia_inicio": dia_inicio,
            "dia_fim": dia_fim,
            "mes_ref": mes_ref,
            "ano_ref": ano_ref
        }).fetchall()

    # 2. BUSCAR FATURAS ABERTAS
    sql_faturas = text("""
        SELECT c.nome_conta, f.data_vencimento, f.status,
               COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = :uid
          AND f.status = 'Aberta'
          AND f.data_vencimento BETWEEN :data_inicio AND :data_fim
        GROUP BY c.nome_conta, f.data_vencimento, f.status
        ORDER BY f.data_vencimento ASC
    """)

    faturas = conn.execute(sql_faturas, {
        "uid": usuario_id,
        "data_inicio": data_inicio,
        "data_fim": data_fim
    }).fetchall()

    # 3. CALCULAR TOTAIS
    total_contas = sum(row.valor_previsto or 0 for row in contas_fixas)
    total_faturas = sum(row.valor_fatura or 0 for row in faturas)

    return {
        "contas_fixas": contas_fixas,
        "faturas": faturas,
        "total_contas": total_contas,
        "total_faturas": total_faturas,
        "valor_total": total_contas + total_faturas
    }

def format_vencimentos_message(vencimentos, periodo, data_referencia):
    """
    Formata mensagem de vencimentos para WhatsApp.
    Separa receitas de despesas com subtotais.

    Args:
        vencimentos: Dict retornado por get_vencimentos_periodo()
        periodo: String descritiva (ex: "HOJE", "AMANHÃ", "NOS PRÓXIMOS 7 DIAS")
        data_referencia: Data de referência para exibição

    Returns:
        String formatada para WhatsApp
    """
    from app.utils import formatar_moeda

    contas_fixas = vencimentos["contas_fixas"]
    faturas = vencimentos["faturas"]

    # Se não houver vencimentos
    if not contas_fixas and not faturas:
        return f"✅ Nenhuma conta vence {periodo.lower()}!"

    # Separar contas fixas em receitas e despesas
    receitas = [c for c in contas_fixas if c.nome_grupo == 'Renda']
    despesas = [c for c in contas_fixas if c.nome_grupo != 'Renda']

    # Montar mensagem
    msg = f"📋 *CONTAS QUE VENCEM {periodo}* ({data_referencia.strftime('%d/%m')})\n\n"

    # Receitas Previstas
    if receitas:
        msg += "*💵 Receitas Previstas:*\n"
        for conta in receitas:
            descricao = conta.descricao
            valor = formatar_moeda(conta.valor_previsto or 0)
            dia = conta.dia_execucao
            msg += f"• {descricao} - {valor} (dia {dia})\n"
        msg += "\n"

    # Despesas Fixas
    if despesas:
        msg += "*💰 Despesas Fixas:*\n"
        for conta in despesas:
            descricao = conta.descricao
            valor = formatar_moeda(conta.valor_previsto or 0)
            dia = conta.dia_execucao
            msg += f"• {descricao} - {valor} (dia {dia})\n"
        msg += "\n"

    # Faturas
    if faturas:
        msg += "*💳 Faturas:*\n"
        for fatura in faturas:
            cartao = fatura.nome_conta
            valor = formatar_moeda(fatura.valor_fatura or 0)
            data_venc = fatura.data_vencimento.strftime('%d/%m')
            msg += f"• {cartao} - {valor} (vence {data_venc})\n"
        msg += "\n"

    # Calcular subtotais
    total_receitas = sum(c.valor_previsto or 0 for c in receitas)
    total_despesas = sum(c.valor_previsto or 0 for c in despesas)
    total_faturas = sum(f.valor_fatura or 0 for f in faturas)
    saldo_previsto = total_receitas - total_despesas - total_faturas

    # Totais com separação
    if receitas or despesas or faturas:
        if receitas:
            msg += f"*Receitas:* {formatar_moeda(total_receitas)}\n"
        if despesas:
            msg += f"*Despesas:* {formatar_moeda(total_despesas)}\n"
        if faturas:
            msg += f"*Faturas:* {formatar_moeda(total_faturas)}\n"

        # Mostrar saldo previsto apenas se houver receitas OU despesas+faturas
        if receitas or despesas or faturas:
            msg += f"*Saldo Previsto:* {formatar_moeda(saldo_previsto)}"

    return msg


# --- Funções para Detecção de Conta Mencionada e Contas Padrão ---

def extract_mentioned_account(conn, usuario_id, texto_msg):
    """
    Detecta se o usuário mencionou uma conta na mensagem usando fuzzy matching.

    Procura por padrões: "com o X", "com a X", "usando X", "pelo X", "pela X", "no X", "na X"

    Args:
        conn: Conexão do banco de dados
        usuario_id: ID do usuário
        texto_msg: Texto da mensagem do WhatsApp

    Returns:
        (conta_id, nome_conta, tipo_conta) ou None se não encontrar
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


def get_user_default_accounts(conn, usuario_id):
    """
    Retorna as contas padrão configuradas pelo usuário.

    Returns:
        (conta_renda_id, conta_despesa_id) ou (None, None) se não configurado
    """
    sql = text("""
        SELECT conta_padrao_renda_id, conta_padrao_despesa_id
        FROM Usuarios
        WHERE id = :uid
    """)
    result = conn.execute(sql, {"uid": usuario_id}).fetchone()

    if result:
        return (result.conta_padrao_renda_id, result.conta_padrao_despesa_id)
    return (None, None)


def set_user_default_account(conn, usuario_id, tipo, conta_id):
    """
    Configura a conta padrão do usuário.

    Args:
        tipo: 'renda' ou 'despesa'
        conta_id: ID da conta a ser configurada como padrão
    """
    if tipo == 'renda':
        sql = text("UPDATE Usuarios SET conta_padrao_renda_id = :cid WHERE id = :uid")
    elif tipo == 'despesa':
        sql = text("UPDATE Usuarios SET conta_padrao_despesa_id = :cid WHERE id = :uid")
    else:
        raise ValueError("Tipo deve ser 'renda' ou 'despesa'")

    conn.execute(sql, {"uid": usuario_id, "cid": conta_id})


def choose_account_for_transaction(conn, usuario_id, texto_msg, tipo_transacao):
    """
    Escolhe a conta para uma transação seguindo ordem de prioridade:
    1. Conta mencionada na mensagem (fuzzy matching)
    2. Conta padrão configurada pelo usuário
    3. Fallback: primeira conta disponível

    Args:
        conn: Conexão do banco
        usuario_id: ID do usuário
        texto_msg: Mensagem do WhatsApp
        tipo_transacao: 'Renda' ou 'Despesa'

    Returns:
        (conta_id, conta_nome, conta_tipo, origem)
        origem: 'mencionada' | 'padrao' | 'fallback'
    """
    # 1. Verificar se mencionou conta na mensagem
    conta_mencionada = extract_mentioned_account(conn, usuario_id, texto_msg)
    if conta_mencionada:
        conta_id, nome, tipo = conta_mencionada
        print(f"[ESCOLHA-CONTA] Usando conta MENCIONADA: {nome}")
        return (conta_id, nome, tipo, 'mencionada')

    # 2. Verificar conta padrão configurada
    conta_renda_id, conta_despesa_id = get_user_default_accounts(conn, usuario_id)

    if tipo_transacao == 'Renda' and conta_renda_id:
        # Buscar detalhes da conta padrão de renda
        sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid")
        conta = conn.execute(sql, {"cid": conta_renda_id, "uid": usuario_id}).fetchone()
        if conta:
            print(f"[ESCOLHA-CONTA] Usando conta padrão RENDA: {conta.nome_conta}")
            return (conta.id, conta.nome_conta, conta.tipo_conta, 'padrao')

    if tipo_transacao == 'Despesa' and conta_despesa_id:
        # Buscar detalhes da conta padrão de despesa
        sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid")
        conta = conn.execute(sql, {"cid": conta_despesa_id, "uid": usuario_id}).fetchone()
        if conta:
            print(f"[ESCOLHA-CONTA] Usando conta padrão DESPESA: {conta.nome_conta}")
            return (conta.id, conta.nome_conta, conta.tipo_conta, 'padrao')

    # 3. Fallback: primeira conta disponível
    sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid LIMIT 1")
    conta = conn.execute(sql, {"uid": usuario_id}).fetchone()
    if conta:
        print(f"[ESCOLHA-CONTA] Usando conta FALLBACK: {conta.nome_conta}")
        return (conta.id, conta.nome_conta, conta.tipo_conta, 'fallback')

    # Sem contas disponíveis
    return (None, None, None, None)