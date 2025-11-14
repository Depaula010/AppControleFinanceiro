# app/__init__.py
import locale
import google.generativeai as genai
from flask import Flask
from sqlalchemy import create_engine
from .config import GEMINI_API_KEY, DATABASE_URL
from app.services.redis_service import redis_service

# Variáveis globais que serão "injetadas" (acessíveis) em outros módulos
# (Similar a registrar singletons no DI do .NET)
db_engine = None
gemini_model = None

def create_app():
    """
    Esta é a "Application Factory", o equivalente ao seu Program.cs / Startup.cs
    """
    app = Flask(__name__)
    
    # 1. Carregar a Configuração
    app.config.from_object('app.config')
    
    # 2. Configurar o Locale (Movido do app.py)
    try:
        locale.setlocale(locale.LC_ALL, 'pt_BR.UTF-8')
    except locale.Error:
        try:
            locale.setlocale(locale.LC_ALL, 'Portuguese_Brazil.1252') # Windows
        except Exception as e:
            print(f"[AVISO] Não foi possível definir o locale 'pt_BR'. Usando padrão. Erro: {e}")

    # 3. Configurar Clientes (Injeção de Dependência)
    global db_engine, gemini_model
    
    if DATABASE_URL:
        db_engine = create_engine(DATABASE_URL)
    else: 
        print("AVISO CRÍTICO: URL do Banco de Dados (DATABASE_URL) não configurada.")
        
    if GEMINI_API_KEY:
        genai.configure(api_key=GEMINI_API_KEY)
        gemini_model = genai.GenerativeModel('models/gemini-flash-latest')
    else: 
        print("AVISO CRÍTICO: Chave do Gemini (GEMINI_API_KEY) não configurada.")
        
    # Verificar conexão Redis na inicialização
    if not redis_service.is_connected():
        print("AVISO: Redis não conectado. Sistema de confirmação desabilitado.")
    
    # 4. Registrar os "Controllers" (Blueprints)
    # Vamos criar esses arquivos no próximo passo
    with app.app_context():
        from .routes import admin
        from .routes import webhooks
        
        app.register_blueprint(admin.admin_bp)
        app.register_blueprint(webhooks.webhooks_bp)

    
    # Rota de verificação (movida do app.py)
    @app.route('/')
    def home():
        print("TESTE DE DEPLOY v99 FUNCIONOU!") 
        return "API DO BOT FINANCEIRO - DEPLOY v99 - FUNCIONOU!"
        
    return app