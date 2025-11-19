# app/__init__.py (VERSÃO CORRIGIDA COM POOL RESILIENTE)
import locale
import google.generativeai as genai
from flask import Flask
from sqlalchemy import create_engine, event, text
from sqlalchemy.pool import NullPool, QueuePool
from .config import GEMINI_API_KEY, DATABASE_URL
from app.services.redis_service import redis_service

# Variáveis globais que serão "injetadas" (acessíveis) em outros módulos
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
        # CORREÇÃO: Configurar engine com pool resiliente
        db_engine = create_engine(
            DATABASE_URL,
            # Pool de conexões
            poolclass=QueuePool,
            pool_size=5,              # Máximo de 5 conexões simultâneas
            max_overflow=10,          # Até 10 conexões extras em pico
            pool_timeout=30,          # Timeout de 30s para obter conexão
            pool_recycle=3600,        # Reciclar conexões a cada 1 hora
            pool_pre_ping=True,       # CRÍTICO: Testa conexão antes de usar
            # Configurações extras
            echo=False,               # Não logar SQL (performance)
            connect_args={
                'connect_timeout': 10,  # Timeout de conexão TCP
                'keepalives': 1,        # Ativar TCP keepalive
                'keepalives_idle': 30,  # Enviar keepalive após 30s
                'keepalives_interval': 10,  # Intervalo entre keepalives
                'keepalives_count': 5   # Quantos keepalives antes de desistir
            }
        )
        
        # Adicionar listener para detectar conexões perdidas
        @event.listens_for(db_engine, "connect")
        def receive_connect(dbapi_conn, connection_record):
            print("[DB] Nova conexão estabelecida")
        
        @event.listens_for(db_engine, "checkout")
        def receive_checkout(dbapi_conn, connection_record, connection_proxy):
            # Testa a conexão antes de entregar (usando DBAPI diretamente)
            cursor = dbapi_conn.cursor()
            try:
                # DBAPI usa SQL puro, não precisa de text()
                cursor.execute("SELECT 1")
                cursor.fetchone()
            except Exception as e:
                print(f"[DB] ⚠️ Conexão inválida detectada, descartando: {e}")
                # Força reconexão
                raise Exception("Stale connection")
            finally:
                cursor.close()
        
        print(f"[DB] ✅ Engine configurado com pool resiliente")
        print(f"[DB] Pool size: 5 | Max overflow: 10 | Pre-ping: Ativo")
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
    with app.app_context():
        from .routes import admin
        from .routes import webhooks
        
        app.register_blueprint(admin.admin_bp)
        app.register_blueprint(webhooks.webhooks_bp)

    
    # Rota de verificação (movida do app.py)
    @app.route('/')
    def home():
        # Teste de conexão do banco
        try:
            with db_engine.connect() as conn:
                conn.execute(text("SELECT 1"))
                db_status = "✅ Conectado"
        except Exception as e:
            db_status = f"❌ Erro: {str(e)[:50]}"

        print("TESTE DE DEPLOY v99 FUNCIONOU!")
        return f"API DO BOT FINANCEIRO - DEPLOY v99 - FUNCIONOU!<br>DB: {db_status}"
        
    return app