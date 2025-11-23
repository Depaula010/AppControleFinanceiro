# app/__init__.py (VERSÃO CORRIGIDA COM POOL RESILIENTE + SEGURANÇA)
import locale
import logging
import os
from datetime import datetime
import google.generativeai as genai
from flask import Flask, request, jsonify
from sqlalchemy import create_engine, event, text
from sqlalchemy.pool import NullPool, QueuePool
from .config import GEMINI_API_KEY, DATABASE_URL
from app.services.redis_service import redis_service

# Imports de segurança
from flask_limiter import Limiter
from flask_limiter.util import get_remote_address
from flask_talisman import Talisman
from flask_cors import CORS
from app.middleware.security import security_filter, get_security_stats
from app import security_config

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

    # ========== CONFIGURAÇÃO DE SEGURANÇA ==========

    # 1.1. Configurar logging de segurança
    os.makedirs('logs', exist_ok=True)
    security_handler = logging.FileHandler(security_config.SECURITY_LOG_FILE)
    security_handler.setLevel(getattr(logging, security_config.SECURITY_LOG_LEVEL))
    security_handler.setFormatter(logging.Formatter(
        '[%(asctime)s] %(levelname)s: %(message)s',
        datefmt='%Y-%m-%d %H:%M:%S'
    ))

    security_logger = logging.getLogger('security')
    security_logger.addHandler(security_handler)
    security_logger.addHandler(logging.StreamHandler())  # Também no console

    # 1.2. Rate Limiting
    if security_config.RATELIMIT_ENABLED:
        limiter = Limiter(
            app=app,
            key_func=get_remote_address,
            storage_uri=security_config.RATELIMIT_STORAGE_URI,
            strategy=security_config.RATELIMIT_STRATEGY,
            default_limits=[security_config.RATELIMIT_DEFAULTS['default']]
        )
        print("[SECURITY] ✅ Rate Limiting ativado")

    # 1.3. CORS (apenas se configurado)
    if security_config.CORS_ENABLED:
        CORS(app, origins=security_config.CORS_ORIGINS)
        print(f"[SECURITY] ✅ CORS ativado | Origins: {security_config.CORS_ORIGINS}")

    # 1.4. Security Headers (Talisman)
    if security_config.SECURITY_HEADERS_ENABLED:
        # Configurar Talisman com política moderada
        Talisman(
            app,
            force_https=False,  # Deixar HTTPS para o proxy reverso (Render.com)
            strict_transport_security=True,
            strict_transport_security_max_age=31536000,
            content_security_policy=security_config.CSP_CONFIG,
            content_security_policy_nonce_in=['script-src'],
            frame_options='DENY',
            frame_options_allow_from=None,
            referrer_policy='strict-origin-when-cross-origin',
        )
        print("[SECURITY] ✅ Security Headers ativados")

    # 1.5. Middleware customizado de proteção contra bots
    if security_config.BOT_PROTECTION_ENABLED:
        @app.before_request
        def apply_security_filter():
            return security_filter()

        print("[SECURITY] ✅ Bot Protection ativado")

    # ========== FIM CONFIGURAÇÃO DE SEGURANÇA ==========
    
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