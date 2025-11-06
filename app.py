import os
import json
import secrets
import requests
import locale # Import para formatação R$
from flask import Flask, request, jsonify
import google.generativeai as genai
from sqlalchemy import create_engine, text, exc as sqlalchemy_exc
from datetime import date, timedelta
from calendar import monthrange

# Importa o motor (com tratamento de erro se não existir)
try:
    from motor_agendamentos import processar_agendamentos
except ImportError:
    print("AVISO: Arquivo 'motor_agendamentos.py' não encontrado. A rota do motor falhará.")
    def processar_agendamentos():
        print("ERRO CRÍTICO: 'motor_agendamentos.py' não encontrado ou com erro de importação.")
        raise ImportError("motor_agendamentos.py não encontrado.")

# ========= 1. CONFIGURAÇÃO INICIAL E CONEXÃO =========

app = Flask(__name__)

# --- ADICIONE ESTA ROTA DE HEALTH CHECK ---
@app.route('/ping')
def ping():
    """ Rota de Health Check para o Render. """
    return "pong", 200
# --- FIM DA ADIÇÃO ---

# Configura o locale para R$ (Padrão Brasileiro)
try:
    locale.setlocale(locale.LC_ALL, 'pt_BR.UTF-8')
except locale.Error:
    try:
        locale.setlocale(locale.LC_ALL, 'Portuguese_Brazil.1252') # Windows
    except Exception as e:
        print(f"[AVISO] Não foi possível definir o locale 'pt_BR'. Usando padrão. Erro: {e}")

def formatar_moeda(valor):
    """ Tenta formatar como R$ (BRL). Se falhar, usa um formato simples. """
    if valor is None:
        return "R$ 0,00"
    try:
        # --- CORREÇÃO ---
        # A chamada original 'return formatar_moeda(valor)' era uma recursão infinita.
        # A chamada correta é para a biblioteca 'locale'.
        return locale.currency(valor, grouping=True)
        # --- FIM DA CORREÇÃO ---
    except Exception:
        # Se o locale 'pt_BR' não estiver disponível no servidor, usa um fallback manual.
        # Formata com 2 casas decimais, troca ',' por 'X', '.' por ',' e 'X' por '.'
        return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")

# Carregar as "senhas"
try:
    GEMINI_API_KEY = os.environ['GEMINI_API_KEY']
    DATABASE_URL = os.environ['DATABASE_URL']
    API_SECRET_KEY = os.environ.get('API_SECRET_KEY', 'uma-senha-bem-forte-12345') 
    BOT_WHATSAPP_URL = os.environ.get('BOT_WHATSAPP_URL', 'https://bot-appfinanceiro-whatsapp.onrender.com') 
except KeyError as e:
    print(f"ERRO CRÍTICO: Variável de ambiente {e} não definida.")
    GEMINI_API_KEY = None; DATABASE_URL = None; API_SECRET_KEY = 'uma-senha-bem-forte-12345'; BOT_WHATSAPP_URL = 'https://bot-appfinanceiro-whatsapp.onrender.com'

# Configurar o cliente do Gemini
if GEMINI_API_KEY:
    genai.configure(api_key=GEMINI_API_KEY)
    model = genai.GenerativeModel('models/gemini-flash-latest') 
else: 
    print("AVISO CRÍTICO: Chave do Gemini (GEMINI_API_KEY) não configurada.")
    model = None

# Configurar a conexão com o Banco de Dados
if DATABASE_URL:
    if DATABASE_URL.startswith("postgres://"):
        DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)
    engine = create_engine(DATABASE_URL)
else: 
    print("AVISO CRÍTICO: URL do Banco de Dados (DATABASE_URL) não configurada.")
    engine = None

# ========= 2. ROTAS ADMINISTRATIVAS (Com Correção 'TextClause') =========

# [COLE ESTE BLOCO DE CÓDIGO NO SEU app.py]

@app.route('/admin/clear-bot-session', methods=['POST'])
def clear_bot_session():
    """
    ENDPOINT DE EMERGÊNCIA: Limpa a tabela 'baileys_auth' para forçar o bot
    a gerar um novo QR code e limpar sessões corrompidas.
    """
   
    if not engine: 
        return jsonify({"status": "erro", "mensagem": "Banco não configurado"}), 500

    # 2. SQL para limpar a sessão do bot (com base no seu index.js)
    sql_clear = text("DELETE FROM baileys_auth WHERE session_id = 'baileys_session';")

    try:
        with engine.connect() as conn:
            conn.begin()
            # Executa o delete
            result = conn.execute(sql_clear)
            conn.commit()
        
        deleted_rows = result.rowcount
        mensagem_sucesso = f"Sessão do bot ('baileys_auth') limpa com sucesso. {deleted_rows} linhas deletadas."
        print(f"[ADMIN-FIX] {mensagem_sucesso}")
        
        return jsonify({
            "status": "sucesso", 
            "mensagem": mensagem_sucesso
        }), 200

    except Exception as e:
        print(f"[ADMIN-FIX] Erro ao limpar a sessão do bot: {e}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return jsonify({"status": "erro", "mensagem": f"Erro ao limpar sessão: {str(e)}"}), 500

@app.route('/admin/setup-database', methods=['GET'])
def setup_database():
    """ 
    Cria/Recria a ESTRUTURA final do banco (v12). 
    Execute PRIMEIRO.
    """
    if not engine: return "Erro: Banco não configurado.", 500
    
    # (DDL v12 completo)
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
    """) # Fim do DDL
    
    try:
        with engine.connect() as conn:
            # *** CORREÇÃO: Removido o 'text()' duplicado ***
            conn.execute(sql_script_ddl) 
            conn.commit() 
        return "Estrutura final do banco (v12) criada/recriada com sucesso!", 200
    except Exception as e:
        print(f"Erro ao criar estrutura do banco: {e}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return f"Erro ao criar estrutura do banco: {str(e)}", 500

@app.route('/admin/populate-global-categories', methods=['GET'])
def populate_global_categories():
    """ Insere os TEMPLATES GLOBAIS de categorias. Execute SEGUNDO. """
    if not engine: return "Erro: Banco não configurado.", 500
    
    # (DML global, removido 'BEGIN' e 'COMMIT' do SQL)
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
    """) # Fim do DML
    
    try:
        with engine.connect() as conn:
            conn.begin()
            # *** CORREÇÃO: Removido o 'text()' duplicado ***
            conn.execute(sql_populate_dml) 
            conn.commit()
        return "Templates globais de categorias (v12) inseridos com sucesso!", 200
    except Exception as e:
        print(f"Erro ao inserir templates globais: {e}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return f"Erro ao inserir templates globais: {str(e)}", 500

@app.route('/admin/setup-user-data', methods=['GET']) 
def setup_user_data():
    """ 
    Roda para inserir/atualizar o usuário e contas. 
    Execute TERCEIRO.
    """
    if not engine: return "Erro: Banco não configurado.", 500
    
    numero_whatsapp_usuario = '553194001072' 
    dia_vencimento_inter = 20 
    dia_fechamento_inter = 13  
    
    nova_api_key = secrets.token_hex(20) 

    sql_user = text(f"""
    INSERT INTO Usuarios (nome, numero_whatsapp, api_key_automate) 
    VALUES ('Rafael', '{numero_whatsapp_usuario}', '{nova_api_key}') 
    ON CONFLICT (numero_whatsapp) 
    DO UPDATE SET 
        nome = EXCLUDED.nome,
        api_key_automate = EXCLUDED.api_key_automate 
    RETURNING id, api_key_automate;
    """)
    
    try:
        user_id = None; api_key_retornada = None
        with engine.connect() as conn:
            conn.begin()
            result = conn.execute(sql_user)
            user_data = result.fetchone() 
            if user_data:
                user_id = user_data[0]; api_key_retornada = user_data[1]
            else:
                sql_find = text("SELECT id, api_key_automate FROM Usuarios WHERE numero_whatsapp = :num")
                user_data = conn.execute(sql_find, {"num": numero_whatsapp_usuario}).fetchone()
                user_id = user_data[0]; api_key_retornada = user_data[1]
            
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
                "dia_venc": dia_vencimento_inter,
                "dia_fech": dia_fechamento_inter
            }) 
            conn.commit()
        
        return jsonify({
            "status": "sucesso",
            "mensagem": f"Usuário e Contas inseridos/atualizados (Usuário ID: {user_id})!",
            "user_api_key_para_automate": api_key_retornada 
        }), 200

    except Exception as e:
        print(f"Erro ao inserir dados do usuário: {e}")
        # --- CORREÇÃO DE SINTAXE ---
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        # --- FIM DA CORREÇÃO ---
        return f"Erro ao inserir dados do usuário: {str(e)}", 500

@app.route('/admin/run-motor-agendamentos', methods=['POST'])
def run_motor_agendamentos():
    """ Rota secreta que o Bot chama para rodar os agendamentos. """
    secret_key_recebida = request.headers.get('x-api-key')
    if secret_key_recebida != API_SECRET_KEY: 
        print(f"[MOTOR] Acesso negado à rota /run-motor-agendamentos. Chave errada.")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401
    
    try:
        print("[MOTOR] Rota /run-motor-agendamentos chamada com sucesso! Iniciando processamento...")
        processar_agendamentos() # Chama a função do arquivo motor_agendamentos.py
        return jsonify({"status": "sucesso", "mensagem": "Agendamentos processados."}), 200
    except Exception as e:
        print(f"[MOTOR] ERRO CRÍTICO ao rodar /run-motor-agendamentos: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500

# ========= 3. LÓGICA DE FATURA (Função Auxiliar - Sem Alteração) =========
def get_or_create_fatura(conn, conta_id, data_transacao, usuario_id):
    # (Lógica completa de cálculo de fatura, como antes)
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
        print(f"[DATABASE] Fatura ID {fatura_id} (Venc: {data_fatura_vencimento}) sendo usada/criada para Cartão ID {conta_id}")
    return fatura_id

# ========= 4. ENDPOINTS PRINCIPAIS (Com 'except' e 'Gemini response' Corrigido) =========

@app.route('/')
def home():
    return "API do Bot Financeiro v14 (Bug Fixes Finais) está no ar!"

# (Helper para checar a resposta do Gemini)
def get_gemini_text_response(response):
    """ Tenta extrair 'response.text'. Se falhar (bloqueio), levanta uma exceção. """
    try:
        return response.text
    except ValueError as e:
        # Isso acontece se a resposta foi bloqueada (ex: safety_ratings)
        print(f"[GEMINI-ERRO] Resposta do Gemini bloqueada ou inválida. {e}")
        print(f"[GEMINI-ERRO] Feedback: {response.prompt_feedback}")
        raise Exception(f"Falha na API: Resposta do Gemini bloqueada. {response.prompt_feedback}")

@app.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """ Rota do Gatilho Android (Notificações Automáticas) """
    if not engine or not model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503
    try:
        data = request.json
        texto_notificacao = data.get('texto')
        user_api_key = data.get('user_api_key') 
        if not texto_notificacao or not user_api_key:
            return jsonify({"status": "erro", "mensagem": "Chave 'texto' ou 'user_api_key' faltando"}), 400
        print(f"[AUTOMATE] Recebido: {texto_notificacao}")
        
        usuario_id = None
        numero_whatsapp_usuario = None
        with engine.connect() as conn:
            sql_find_user = text("SELECT id, numero_whatsapp FROM Usuarios WHERE api_key_automate = :api_key")
            result = conn.execute(sql_find_user, {"api_key": user_api_key}).fetchone()
            if not result:
                return jsonify({"status": "erro", "mensagem": "Chave de API de usuário inválida"}), 401
            usuario_id = result[0]; numero_whatsapp_usuario = result[1] 
        print(f"[AUTOMATE] Usuário autenticado (ID: {usuario_id}). Processando...")

        prompt_1 = f"""
        Analise a notificação: "{texto_notificacao}"
        Retorne APENAS JSON com: "valor_decimal" (sempre positivo), "descricao_bruta", "tipo_fluxo" ("Renda" ou "Despesa").
        """; 
        response_1 = model.generate_content(prompt_1); 
        
        # --- CORREÇÃO: Verifica se Gemini retornou texto ---
        response_text_1 = get_gemini_text_response(response_1)
        if not response_text_1:
            raise Exception("Falha na extração (Automate): Resposta vazia do Gemini.")

        json_response_text_1 = response_text_1.strip().replace("```json", "").replace("```", ""); transacao_gemini = json.loads(json_response_text_1)
        print(f"[GEMINI-1] Extração: {json_response_text_1}")
        
        tipo_transacao_db = transacao_gemini.get('tipo_fluxo', 'Despesa'); transacao_descricao = transacao_gemini.get('descricao_bruta'); data_hoje = date.today(); id_categoria_final = None; id_outros_fallback = None
        valor_decimal = float(transacao_gemini.get('valor_decimal', 0)) # Garante que é float

        with engine.connect() as conn:
            conn.begin() 
            sql_get_contas = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid"); contas_usuario = conn.execute(sql_get_contas, {"uid": usuario_id}).fetchall()
            conta_id_transacao = None
            if tipo_transacao_db == 'Despesa' and any(kw in texto_notificacao.lower() for kw in ['cartão', 'compra', 'credit']):
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Cartão de Crédito'), None)
            if not conta_id_transacao:
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Conta Corrente'), contas_usuario[0][0]) 

            grupo_filtro_sql = "g.nome_grupo = 'Renda'"
            if tipo_transacao_db == 'Despesa': grupo_filtro_sql = "g.nome_grupo IN ('Despesa Essencial', 'Despesa Discricionária', 'Meta Financeira', 'Geral')"
            sql_get_cats = text(f"SELECT s.id, s.nome_sub, m.nome_macro FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE (s.usuario_id IS NULL OR s.usuario_id = :uid) AND ({grupo_filtro_sql})")
            categories_list_result = conn.execute(sql_get_cats, {"uid": usuario_id}).fetchall()
            
            categories_json_list = []
            for row in categories_list_result: categories_json_list.append({"id": row[0], "nome_sub": row[1], "nome_macro": row[2]})
            
            nome_macro_outros = 'Receitas Gerais' if tipo_transacao_db == 'Renda' else 'Despesas Gerais'; sql_get_outros_id = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id WHERE m.nome_macro = :nome_macro AND s.nome_sub = 'Outros' AND s.usuario_id IS NULL LIMIT 1"); id_outros_fallback = conn.execute(sql_get_outros_id, {"nome_macro": nome_macro_outros}).scalar_one_or_none()
            if id_outros_fallback is None: conn.rollback(); return jsonify({"status": "erro", "mensagem": "Erro interno: Categoria 'Outros' não encontrada"}), 500

            prompt_2 = f"""
            Minhas subcategorias são: {json.dumps(categories_json_list)}
            A transação teve a descrição: "{transacao_descricao}"; O tipo é: "{tipo_transacao_db}"
            Qual é o "id" da subcategoria que melhor corresponde? Se for genérico, use o "id" de "Outros" (que é {id_outros_fallback}).
            Responda APENAS com o número do ID.
            """; 
            response_2 = model.generate_content(prompt_2); 

            # --- CORREÇÃO: Verifica se Gemini retornou texto ---
            try:
                response_text_2 = response_2.text
            except ValueError as e:
                print(f"[GEMINI-2] ERRO: Resposta do Gemini bloqueada ou inválida. {e} / {response_2.prompt_feedback}. Usando 'Outros'.")
                response_text_2 = "" 
            
            if not response_text_2:
                print(f"[GEMINI-2] ERRO: Gemini (Call 2 - Automate) retornou uma resposta vazia. Usando 'Outros'.")
                id_categoria_final = id_outros_fallback
            else:
                id_categoria_str = response_text_2.strip().replace("`", "")
                try:
                    id_categoria_final = int(id_categoria_str)
                    if id_categoria_final not in [cat['id'] for cat in categories_json_list]:
                        id_categoria_final = id_outros_fallback
                except ValueError:
                    id_categoria_final = id_outros_fallback
            
            print(f"[GEMINI-2] ID de Categoria escolhido: {id_categoria_final}")

            fatura_id_transacao = None
            sql_check_conta_tipo = text("SELECT tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid"); conta_tipo_result = conn.execute(sql_check_conta_tipo, {"cid": conta_id_transacao, "uid": usuario_id}).scalar_one_or_none()
            if conta_tipo_result == 'Cartão de Crédito':
                fatura_id_transacao = get_or_create_fatura(conn, conta_id_transacao, data_hoje, usuario_id)
            
            valor_para_db = valor_decimal if tipo_transacao_db == 'Renda' else (valor_decimal * -1)

            sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, transferencia_par_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, NULL, :desc, :val, :tipo, :data) "); 
            conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_transacao, "scid": id_categoria_final, "fid": fatura_id_transacao, "desc": transacao_descricao, "val": valor_para_db, "tipo": tipo_transacao_db, "data": data_hoje})
            
            sql_get_cat_nome = text("SELECT s.nome_sub, m.nome_macro FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id WHERE s.id = :scid");
            cat_info = conn.execute(sql_get_cat_nome, {"scid": id_categoria_final}).fetchone(); nome_categoria_salva = f"{cat_info[1]} -> {cat_info[0]}"
            conn.commit()
            
        valor_formatado = formatar_moeda(valor_decimal)
        mensagem_notificacao = f"✅ Transação Automática Salva!\n\nDescrição: {transacao_descricao}\nValor: {valor_formatado} ({tipo_transacao_db})\nCategoria: {nome_categoria_salva}"
        if id_categoria_final == id_outros_fallback:
            mensagem_notificacao += f"\n\n*Atenção:* Não soube categorizar esta despesa. Salvei em 'Outros'."
        
        try:
            headers = {'x-api-key': API_SECRET_KEY}; payload = {'numero': numero_whatsapp_usuario, 'mensagem': mensagem_notificacao}
            response_bot = requests.post(f"{BOT_WHATSAPP_URL}/enviar-mensagem", json=payload, headers=headers)
            if response_bot.status_code == 200: print(f"[BOT-WPP] Notificação enviada com sucesso para {numero_whatsapp_usuario}.")
            else: print(f"[BOT-WPP] ERRO: Bot respondeu com status {response_bot.status_code}")
        except Exception as bot_err:
            print(f"[BOT-WPP] ERRO: Falha ao chamar a API do Bot: {bot_err}")

        return jsonify({"status": "sucesso", "transacao_salva": transacao_gemini, "categoria_id_escolhida": id_categoria_final}), 200

    # --- CORREÇÃO DE SINTAXE ---
    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"Erro de Banco de Dados: {db_err}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return jsonify({"status": "erro", "mensagem": f"Erro de Banco de Dados: {str(db_err)}"}), 500
    except Exception as e:
        print(f"Erro geral: {e}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return jsonify({"status": "erro", "mensagem": str(e)}), 500


@app.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """
    Recebe uma mensagem manual, classifica a INTENÇÃO, processa e salva/consulta.
    *** CORRIGIDA: Verificação de resposta vazia do Gemini E sintaxe do 'except' ***
    """
    if not engine or not model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503
    
    secret_key_recebida = request.headers.get('x-api-key')
    
    # --- MUDANÇA 1: DEBUG MELHORADO (USE repr()) ---
    if secret_key_recebida:
        print(f"[DEBUG-KEY] Chave Recebida: {repr(secret_key_recebida)} | Chave Esperada: {repr(API_SECRET_KEY)}")
    else:
        print("[DEBUG-KEY] NENHUMA CHAVE RECEBIDA (x-api-key está vazia)")
    # --- FIM DA MUDANÇA 1 ---

    # --- MUDANÇA 2: A CORREÇÃO (USE .strip()) ---
    if not secret_key_recebida or secret_key_recebida.strip() != API_SECRET_KEY.strip():
        print("[DEBUG-KEY] COMPARAÇÃO FALHOU! (Verifique acima)")
        return jsonify({"status": "erro", "resposta": "Chave de API inválida."}), 401
    # --- FIM DA MUDANÇA 2 ---
    
    try:
        data = request.json
        texto_msg = data.get('texto')
        numero_remetente = data.get('numero_remetente')
        if not texto_msg or not numero_remetente:
            return jsonify({"status": "erro", "mensagem": "Faltando 'texto' ou 'numero_remetente'"}), 400

        numero_limpo = numero_remetente.split('@')[0]
        print(f"[WHATSAPP] Mensagem recebida de {numero_limpo}: {texto_msg}")

        usuario_id = None
        with engine.connect() as conn:
            sql_find_user = text("SELECT id FROM Usuarios WHERE numero_whatsapp = :num")
            result = conn.execute(sql_find_user, {"num": numero_limpo})
            usuario_id = result.scalar_one_or_none()

        if usuario_id is None:
            return jsonify({"status": "erro", "resposta": "Usuário não autorizado."}), 401
        
        print(f"[WHATSAPP] Usuário autenticado (ID: {usuario_id}). Processando...")

        prompt_intent = f"""Analise a mensagem do usuário: "{texto_msg}"
        Classifique a intenção principal como "Renda", "Despesa", "Transferência", "Pagamento Fatura", "Consulta Potes", "Consulta Reserva", ou "Consulta Categoria Específica".
        Responda APENAS com um JSON contendo a chave "intent".
        Exemplos:
        - "gastei 50 na padaria" -> {{"intent": "Despesa"}}
        - "recebi 100 do freela" -> {{"intent": "Renda"}}
        - "transferi 500 do Inter para o Nubank" -> {{"intent": "Transferência"}}
        - "paguei a fatura de 1500 do cartão inter" -> {{"intent": "Pagamento Fatura"}}
        - "como estão meus potes?" -> {{"intent": "Consulta Potes"}}
        - "qual minha reserva de emergência?" -> {{"intent": "Consulta Reserva"}}
        - "quanto gastei com supermercado este mês?" -> {{"intent": "Consulta Categoria Específica"}}""";
        response_intent = model.generate_content(prompt_intent)
        
        # --- CORREÇÃO (Bug 1): Verifica se Gemini retornou texto ---
        try:
            response_text_intent = response_intent.text
        except ValueError as e:
            print(f"[GEMINI-INTENT] ERRO: Resposta do Gemini bloqueada ou inválida. {e}")
            print(f"[GEMINI-INTENT] Feedback: {response_intent.prompt_feedback}")
            raise Exception(f"Falha na classificação da intenção: Resposta do Gemini bloqueada. {response_intent.prompt_feedback}")

        if not response_text_intent:
            raise Exception("Falha na classificação da intenção: Resposta vazia do Gemini.")
            
        json_intent_text = response_text_intent.strip().replace("```json", "").replace("```", "")
        intent_data = json.loads(json_intent_text)
        intent = intent_data.get('intent')
        
        print(f"[GEMINI-INTENT] Intenção detectada: {intent}")

        with engine.connect() as conn:
            conn.begin() 
            data_hoje = date.today()
            resposta_para_usuario = "" 

            if intent == 'Renda' or intent == 'Despesa':
                prompt_extract = f"""Analise a mensagem: "{texto_msg}"
                O tipo é: "{intent}".
                Extraia "valor_decimal" (sempre positivo) e "descricao_bruta".
                Responda APENAS com JSON.
                Ex: {{"valor_decimal": 50.00, "descricao_bruta": "Padaria"}}"""; 
                response_extract = model.generate_content(prompt_extract); 
                
                try: response_text_extract = response_extract.text
                except ValueError as e: raise Exception(f"Falha na extração (R/D): Resposta do Gemini bloqueada. {response_extract.prompt_feedback}")
                if not response_text_extract: raise Exception(f"Falha na extração (R/D): Resposta vazia do Gemini.")
                
                json_extract_text = response_text_extract.strip().replace("```json", "").replace("```", ""); transacao_gemini = json.loads(json_extract_text)
                print(f"[GEMINI-EXTRACT] Extração (R/D): {json_extract_text}")
                transacao_descricao = transacao_gemini.get('descricao_bruta'); valor_decimal = float(transacao_gemini.get('valor_decimal', 0))
                grupo_filtro_sql = "g.nome_grupo = 'Renda'"
                if intent == 'Despesa': grupo_filtro_sql = "g.nome_grupo IN ('Despesa Essencial', 'Despesa Discricionária', 'Meta Financeira', 'Geral')"
                sql_get_cats = text(f"SELECT s.id, s.nome_sub, m.nome_macro FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE (s.usuario_id IS NULL OR s.usuario_id = :uid) AND ({grupo_filtro_sql})")
                categories_list_result = conn.execute(sql_get_cats, {"uid": usuario_id}).fetchall()
                categories_json_list = [{"id": row[0], "nome_sub": row[1], "nome_macro": row[2]} for row in categories_list_result]
                nome_macro_outros = 'Receitas Gerais' if intent == 'Renda' else 'Despesas Gerais'; sql_get_outros_id = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id WHERE m.nome_macro = :nome_macro AND s.nome_sub = 'Outros' AND s.usuario_id IS NULL LIMIT 1"); id_outros_fallback = conn.execute(sql_get_outros_id, {"nome_macro": nome_macro_outros}).scalar_one_or_none()
                
                prompt_categorize = f"""Minhas subcategorias são: {json.dumps(categories_json_list)}
                A transação foi: "{transacao_descricao}" (Tipo: "{intent}")
                Qual é o "id" da subcategoria que melhor corresponde?
                Se for genérico, use o "id" de "Outros" (que é {id_outros_fallback}).
                Responda APENAS com o número do ID."""; 
                response_cat = model.generate_content(prompt_categorize); 

                try: response_text_cat = response_cat.text
                except ValueError as e: 
                    print(f"[GEMINI-CAT] ERRO: Resposta do Gemini bloqueada. Usando 'Outros'. {e}"); 
                    id_categoria_final = id_outros_fallback
                else:
                    if not response_text_cat: 
                        print(f"[GEMINI-CAT] ERRO: Gemini (Call 3 - R/D) retornou uma resposta vazia. Usando 'Outros'.")
                        id_categoria_final = id_outros_fallback
                    else:
                        id_categoria_str = response_text_cat.strip().replace("`", "")
                        try:
                            id_categoria_final = int(id_categoria_str)
                            if id_categoria_final not in [cat['id'] for cat in categories_json_list]: id_categoria_final = id_outros_fallback
                        except ValueError: id_categoria_final = id_outros_fallback
                
                print(f"[GEMINI-CAT] ID de Categoria (Manual) escolhido: {id_categoria_final}")
                conta_nome_padrao = 'Banco Inter' if intent == 'Renda' else 'Carteira'; sql_get_conta_id = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta = :nome"); conta_id_transacao = conn.execute(sql_get_conta_id, {"uid": usuario_id, "nome": conta_nome_padrao}).scalar_one_or_none()
                if conta_id_transacao is None: sql_get_conta_id = text("SELECT id FROM Contas WHERE usuario_id = :uid LIMIT 1"); conta_id_transacao = conn.execute(sql_get_conta_id, {"uid": usuario_id}).scalar_one()
                valor_para_db = valor_decimal if intent == 'Renda' else (valor_decimal * -1)
                sql_insert = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, transferencia_par_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, NULL, NULL, :desc, :val, :tipo, :data) "); 
                conn.execute(sql_insert, {"uid": usuario_id, "cid": conta_id_transacao, "scid": id_categoria_final, "desc": transacao_descricao, "val": valor_para_db, "tipo": intent, "data": data_hoje})
                sql_get_cat_nome = text("SELECT s.nome_sub, m.nome_macro FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id WHERE s.id = :scid");
                cat_info = conn.execute(sql_get_cat_nome, {"scid": id_categoria_final}).fetchone(); nome_categoria_salva = f"{cat_info[1]} -> {cat_info[0]}"
                valor_formatado = formatar_moeda(valor_decimal)
                resposta_para_usuario = f"✅ {intent} manual salva!\nDescrição: {transacao_descricao}\nValor: {valor_formatado}\nCategoria: {nome_categoria_salva}"

            elif intent == 'Transferência':
                print(f"[WHATSAPP] Processando Lógica de Transferência Pura...")
                sql_get_contas = text("SELECT nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid"); contas_usuario = conn.execute(sql_get_contas, {"uid": usuario_id}).fetchall(); contas_json_list = [{"nome": row[0], "tipo": row[1]} for row in contas_usuario]
                prompt_extract_transfer = f"""Analise a mensagem de transferência: "{texto_msg}"
                Minhas contas são: {json.dumps(contas_json_list)}
                Extraia "valor_decimal", "conta_origem", e "conta_destino". Responda APENAS com JSON."""; 
                response_extract = model.generate_content(prompt_extract_transfer);
                
                try: response_text_extract = response_extract.text
                except ValueError as e: raise Exception(f"Falha na extração (Transfer): Resposta do Gemini bloqueada. {response_extract.prompt_feedback}")
                if not response_text_extract: raise Exception(f"Falha na extração (Transfer): Resposta vazia do Gemini.")
                
                json_extract_text = response_text_extract.strip().replace("```json", "").replace("```", ""); transfer_data = json.loads(json_extract_text)
                print(f"[GEMINI-EXTRACT] Extração (Transf): {json_extract_text}")
                valor_decimal = float(transfer_data.get('valor_decimal', 0)); conta_origem_nome = transfer_data.get('conta_origem'); conta_destino_nome = transfer_data.get('conta_destino')
                if not valor_decimal or not conta_origem_nome or not conta_destino_nome: raise Exception("Gemini não conseguiu extrair os dados da transferência (valor, origem, destino).")
                sql_get_conta_id = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome"); conta_id_origem = conn.execute(sql_get_conta_id, {"uid": usuario_id, "nome": f"%{conta_origem_nome}%"}).scalar_one_or_none(); conta_id_destino = conn.execute(sql_get_conta_id, {"uid": usuario_id, "nome": f"%{conta_destino_nome}%"}).scalar_one_or_none()
                sql_get_subcat_transfer = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE g.nome_grupo = 'Meta Financeira' AND s.nome_sub = 'Investimentos de Curto Prazo' AND (s.usuario_id IS NULL OR s.usuario_id = :uid) LIMIT 1")
                id_subcat_transfer = conn.execute(sql_get_subcat_transfer, {"uid": usuario_id}).scalar_one_or_none()
                if not conta_id_origem or not conta_id_destino or not id_subcat_transfer: raise Exception(f"Não foi possível encontrar as contas ({conta_origem_nome} -> {conta_destino_nome}) ou a subcategoria de transferência.")
                sql_insert_transf = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :desc, :val, 'Transferência', :data) RETURNING id");
                desc_saida = f"Transferência para {conta_destino_nome}"; result_saida = conn.execute(sql_insert_transf, {"uid": usuario_id, "cid": conta_id_origem, "scid": id_subcat_transfer, "desc": desc_saida, "val": (valor_decimal * -1), "data": data_hoje}); id_transacao_saida = result_saida.scalar_one()
                desc_entrada = f"Transferência de {conta_origem_nome}"; result_entrada = conn.execute(sql_insert_transf, {"uid": usuario_id, "cid": conta_id_destino, "scid": id_subcat_transfer, "desc": desc_entrada, "val": valor_decimal, "data": data_hoje}); id_transacao_entrada = result_entrada.scalar_one()
                sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo"); conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida}); conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})
                valor_formatado = formatar_moeda(valor_decimal)
                resposta_para_usuario = f"✅ Transferência salva!\n\nValor: {valor_formatado}\nDe: {conta_origem_nome}\nPara: {conta_destino_nome}"

            elif intent == 'Pagamento Fatura':
                print(f"[WHATSAPP] Processando Lógica de Pagamento de Fatura...")
                sql_get_contas = text("SELECT nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid"); contas_usuario = conn.execute(sql_get_contas, {"uid": usuario_id}).fetchall(); contas_json_list = [{"nome": row[0], "tipo": row[1]} for row in contas_usuario]
                prompt_extract_fatura = f"""Analise a mensagem de pagamento de fatura: "{texto_msg}"
                Minhas contas são: {json.dumps(contas_json_list)}
                Extraia "valor_decimal", "conta_origem", e "conta_cartao". Responda APENAS com JSON."""; 
                response_extract = model.generate_content(prompt_extract_fatura);
                
                try: response_text_extract_fatura = response_extract.text
                except ValueError as e: raise Exception(f"Falha na extração (Pagto Fatura): Resposta do Gemini bloqueada. {response_extract.prompt_feedback}")
                if not response_text_extract_fatura: raise Exception(f"Falha na extração (Pagto Fatura): Resposta vazia do Gemini.")
                
                json_extract_text = response_text_extract_fatura.strip().replace("```json", "").replace("```", ""); fatura_data = json.loads(json_extract_text)
                print(f"[GEMINI-EXTRACT] Extração (Pagto Fatura): {json_extract_text}")
                valor_decimal = float(fatura_data.get('valor_decimal', 0)); conta_origem_nome = fatura_data.get('conta_origem'); conta_cartao_nome = fatura_data.get('conta_cartao')
                if not valor_decimal or not conta_origem_nome or not conta_cartao_nome: raise Exception("Gemini não conseguiu extrair os dados do pagamento (valor, origem, cartão).")
                sql_get_conta_id = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome"); conta_id_origem = conn.execute(sql_get_conta_id, {"uid": usuario_id, "nome": f"%{conta_origem_nome}%"}).scalar_one_or_none(); conta_id_cartao = conn.execute(sql_get_conta_id, {"uid": usuario_id, "nome": f"%{conta_cartao_nome}%"}).scalar_one_or_none()
                fatura_id_pagar = get_or_create_fatura(conn, conta_id_cartao, data_hoje, usuario_id)
                sql_get_subcat_pagto = text("SELECT s.id FROM SubCategoria s JOIN MacroCategoria m ON s.macro_id = m.id JOIN GrupoCategoria g ON m.grupo_id = g.id WHERE g.nome_grupo = 'Meta Financeira' AND s.nome_sub = 'Quitação de Empréstimos (Principal)' AND (s.usuario_id IS NULL OR s.usuario_id = :uid) LIMIT 1")
                id_subcat_pagto = conn.execute(sql_get_subcat_pagto, {"uid": usuario_id}).scalar_one_or_none()
                if not conta_id_origem or not conta_id_cartao or not fatura_id_pagar or not id_subcat_pagto: raise Exception(f"Não foi possível encontrar as contas, uma fatura, ou a subcategoria de pagamento.")
                sql_insert_transf = text("INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao) VALUES (:uid, :cid, :scid, :fid, :desc, :val, 'Pagamento Fatura', :data) RETURNING id")
                desc_saida = f"Pagamento Fatura {conta_cartao_nome}"; result_saida = conn.execute(sql_insert_transf, {"uid": usuario_id, "cid": conta_id_origem, "scid": id_subcat_pagto, "fid": fatura_id_pagar, "desc": desc_saida, "val": (valor_decimal * -1), "data": data_hoje}); id_transacao_saida = result_saida.scalar_one()
                desc_entrada = f"Pagamento Recebido (de {conta_origem_nome})"; result_entrada = conn.execute(sql_insert_transf, {"uid": usuario_id, "cid": conta_id_cartao, "scid": id_subcat_pagto, "fid": fatura_id_pagar, "desc": desc_entrada, "val": valor_decimal, "data": data_hoje}); id_transacao_entrada = result_entrada.scalar_one()
                sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo"); conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida}); conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})
                sql_update_fatura = text("UPDATE Faturas SET status = 'Paga' WHERE id = :fid"); conn.execute(sql_update_fatura, {"fid": fatura_id_pagar})
                valor_formatado = formatar_moeda(valor_decimal)
                resposta_para_usuario = f"✅ Pagamento da fatura '{conta_cartao_nome}' ({valor_formatado}) registrado com sucesso!"

            elif intent == 'Consulta Potes':
                print(f"[WHATSAPP] Processando Lógica de Consulta de Potes...")
                # *** CORREÇÃO: SQL estava com '...' e foi corrigida ***
                sql_get_potes = text("""
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
                potes_result = conn.execute(sql_get_potes, {"uid": usuario_id}).fetchall()
                resposta_para_usuario = "📊 *Status dos Seus Potes (Este Mês)* 📊\n\n"
                if not potes_result: resposta_para_usuario = "Você ainda não configurou nenhum 'Pote de Gasto' (Orçamento)."
                else:
                    for pote in potes_result:
                        valor_limite = float(pote[1]); valor_gasto = float(pote[2] or 0) * -1; valor_restante = valor_limite - valor_gasto
                        resposta_para_usuario += f"🍯 *{pote[0]}*:\n"
                        resposta_para_usuario += f"   - Gasto: *{formatar_moeda(valor_gasto)}*\n"
                        resposta_para_usuario += f"   - Limite: {formatar_moeda(valor_limite)}\n"
                        resposta_para_usuario += f"   - Restante: {formatar_moeda(valor_restante)}\n\n"

            elif intent == 'Consulta Reserva':
                print(f"[WHATSAPP] Processando Lógica de Consulta de Reserva...")
                # *** CORREÇÃO: SQL estava com '...' e foi corrigida ***
                sql_get_essenciais = text("""
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
                total_essencial_3_meses = conn.execute(sql_get_essenciais, {"uid": usuario_id}).scalar()
                media_mensal_essencial = (float(total_essencial_3_meses or 0) * -1) / 3; reserva_ideal_6_meses = media_mensal_essencial * 6
                resposta_para_usuario = "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
                resposta_para_usuario += f"Sua média de gastos essenciais (últimos 3 meses) é: *{formatar_moeda(media_mensal_essencial)}* / mês\n"
                resposta_para_usuario += f"Sua reserva ideal (6x) é: *{formatar_moeda(reserva_ideal_6_meses)}*"

            elif intent == 'Consulta Categoria Específica':
                print(f"[WHATSAPP] Processando Lógica de Consulta de Categoria...")
                prompt_extract_cat = f"""Analise a pergunta: "{texto_msg}"
                Extraia o nome da categoria ou subcategoria que o usuário quer consultar.
                Responda APENAS com JSON: {{"nome_categoria": "..."}}
                Ex1: "quanto gastei com supermercado" -> {{"nome_categoria": "Supermercado / Mercearia"}}
                Ex2: "qual foi meu gasto com lazer?" -> {{"nome_categoria": "Lazer e Entretenimento"}}"""; 
                response_extract = model.generate_content(prompt_extract_cat)
                
                try: response_text_extract_cat = response_extract.text
                except ValueError as e: raise Exception(f"Falha na extração (Consulta Cat): Resposta do Gemini bloqueada. {response_extract.prompt_feedback}")
                if not response_text_extract_cat: raise Exception(f"Falha na extração (Consulta Cat): Resposta vazia do Gemini.")
                
                json_extract_text = response_text_extract_cat.strip().replace("```json", "").replace("```", ""); cat_data = json.loads(json_extract_text)
                nome_categoria_consulta = cat_data.get('nome_categoria')
                if not nome_categoria_consulta: raise Exception("Gemini não conseguiu extrair o nome da categoria da consulta.")
                print(f"[GEMINI-EXTRACT] Categoria para consulta: {nome_categoria_consulta}")

                # *** CORREÇÃO: SQL estava com '...' e foi corrigida ***
                sql_find_gasto = text("""
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
                gasto_total = conn.execute(sql_find_gasto, {"uid": usuario_id, "nome_cat": f"%{nome_categoria_consulta}%"}).scalar()
                valor_gasto = (float(gasto_total or 0)) * -1
                resposta_para_usuario = f"ℹ️ *Consulta de Categoria (Este Mês)*\n\n"
                resposta_para_usuario += f"Você gastou *{formatar_moeda(valor_gasto)}* com '{nome_categoria_consulta}'."

            else:
                resposta_para_usuario = f"🤔 Desculpe, não entendi. Tente 'gastei 50', 'transferi 100', 'paguei a fatura', 'meus potes' ou 'minha reserva'."

            conn.commit() 
        
        print(f"[DATABASE] Processamento MANUAL concluído (Usuário: {usuario_id})!")
        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

    # === CORREÇÃO DA SINTAXE DO 'except' ===
    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"[ERRO] Erro de Banco de Dados: {db_err}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return jsonify({"status": "erro", "mensagem": f"Erro de Banco de Dados: {str(db_err)}"}), 500
    except Exception as e:
        print(f"[ERRO] Erro geral: {e}")
        try: 
            with engine.connect() as conn: 
                conn.rollback()
        except: 
            pass
        return jsonify({"status": "erro", "mensagem": str(e)}), 500