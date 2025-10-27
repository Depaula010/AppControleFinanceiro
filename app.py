import os
import json
from flask import Flask, request, jsonify
import google.generativeai as genai
from sqlalchemy import create_engine, text

# ========= 1. CONFIGURAÇÃO INICIAL =========

app = Flask(__name__)

# Carregar as "senhas"
try:
    GEMINI_API_KEY = os.environ['GEMINI_API_KEY']
    DATABASE_URL = os.environ['DATABASE_URL']
except KeyError:
    print("Erro: Variáveis de Ambiente não encontradas.")
    GEMINI_API_KEY = None
    DATABASE_URL = None

# Configurar o cliente do Gemini
if GEMINI_API_KEY:
    genai.configure(api_key=GEMINI_API_KEY)
    # NÃO VAMOS MAIS ESCOLHER O MODELO AQUI
else:
    print("AVISO: Chave do Gemini não encontrada.")

# Configurar a conexão com o Banco de Dados
if DATABASE_URL:
    if DATABASE_URL.startswith("postgres://"):
        DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)
    engine = create_engine(DATABASE_URL)
else:
    engine = None


# ========= 2. ROTA DE DEPURAÇÃO (NOVO!) =========

@app.route('/admin/list-models')
def list_available_models():
    """
    Esta rota vai nos dizer quais modelos a nossa API key pode usar.
    """
    if not GEMINI_API_KEY:
        return "Erro: Chave do Gemini não configurada.", 500
        
    try:
        models_list = []
        for m in genai.list_models():
            # Vamos focar apenas nos modelos que podem "gerar conteúdo"
            if 'generateContent' in m.supported_generation_methods:
                models_list.append(m.name)
        
        return jsonify({"modelos_disponiveis": models_list})
        
    except Exception as e:
        return f"Erro ao listar modelos: {e}", 500


# ========= 3. OUTRAS ROTAS (DESATIVADAS TEMPORARIAMENTE) =========

@app.route('/')
def home():
    return "API do Bot Financeiro v3 (Modo de Depuração de Modelo)"

@app.route('/admin/setup-database')
def setup_database():
    return "Rota de setup já foi executada. (Desativada por segurança)"

@app.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    return jsonify({"status": "erro", "mensagem": "API em modo de depuração. Rota desativada."}), 503

@app.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    return jsonify({"status": "Ainda não implementado"}), 200