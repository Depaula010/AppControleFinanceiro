# app/services/google_calendar_oauth_service.py (VERSÃO CORRIGIDA FINAL)

import json
import base64
from datetime import datetime, timezone
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import Flow
from google.auth.transport.requests import Request
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError
from sqlalchemy import text

from app import db_engine
from app.config import GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, GOOGLE_REDIRECT_URI

class GoogleCalendarOAuthService:
    """Gerencia autenticação OAuth2 para múltiplos usuários"""
    
    SCOPES = [
        'https://www.googleapis.com/auth/calendar.readonly',
        'https://www.googleapis.com/auth/calendar.events.readonly'
    ]
    
    @staticmethod
    def create_flow():
        """Cria objeto Flow para OAuth"""
        if not GOOGLE_CLIENT_ID or not GOOGLE_CLIENT_SECRET:
            raise Exception("Credenciais OAuth2 não configuradas (GOOGLE_CLIENT_ID/SECRET)")
        
        client_config = {
            "web": {
                "client_id": GOOGLE_CLIENT_ID,
                "client_secret": GOOGLE_CLIENT_SECRET,
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": "https://oauth2.googleapis.com/token",
                "redirect_uris": [GOOGLE_REDIRECT_URI]
            }
        }
        
        flow = Flow.from_client_config(
            client_config,
            scopes=GoogleCalendarOAuthService.SCOPES,
            redirect_uri=GOOGLE_REDIRECT_URI
        )
        
        return flow
    
    @staticmethod
    def get_authorization_url(usuario_id):
        """
        Gera URL para usuário autorizar acesso ao Google Calendar.
        
        Returns:
            str: URL de autorização
        """
        flow = GoogleCalendarOAuthService.create_flow()
        
        # State = usuario_id codificado (para segurança)
        state = base64.urlsafe_b64encode(str(usuario_id).encode()).decode()
        
        authorization_url, _ = flow.authorization_url(
            access_type='offline',  # Requisita refresh_token
            include_granted_scopes='true',
            state=state,
            prompt='consent'  # Força tela de consentimento
        )
        
        print(f"[OAUTH] URL gerada para usuário {usuario_id}")
        return authorization_url
    
    @staticmethod
    def exchange_code_for_tokens(code, state):
        """
        Troca código de autorização por tokens de acesso.
        
        Args:
            code: Código retornado pelo Google
            state: State contendo usuario_id
            
        Returns:
            int: usuario_id
        """
        # Decodificar usuario_id do state
        usuario_id = int(base64.urlsafe_b64decode(state.encode()).decode())
        
        # Criar flow e trocar código
        flow = GoogleCalendarOAuthService.create_flow()
        flow.fetch_token(code=code)
        
        credentials = flow.credentials
        
        # Salvar no banco
        GoogleCalendarOAuthService.save_credentials(usuario_id, credentials)
        
        print(f"[OAUTH] ✅ Tokens salvos para usuário {usuario_id}")
        return usuario_id
    
    @staticmethod
    def save_credentials(usuario_id, credentials):
        """Salva credenciais OAuth2 no banco de dados"""
        if not db_engine:
            raise Exception("Banco não configurado")
        
        # Serializar scopes
        scopes_str = json.dumps(credentials.scopes) if credentials.scopes else None
        
        # CORREÇÃO: Garantir que expiry seja timezone-aware antes de salvar
        token_expiry = None
        if credentials.expiry:
            if credentials.expiry.tzinfo is None:
                # Se naive, adicionar UTC
                token_expiry = credentials.expiry.replace(tzinfo=timezone.utc)
            else:
                token_expiry = credentials.expiry
        
        sql = text("""
            INSERT INTO GoogleCalendarTokens 
            (usuario_id, access_token, refresh_token, token_expiry, scopes, updated_at)
            VALUES (:uid, :access, :refresh, :expiry, :scopes, CURRENT_TIMESTAMP)
            ON CONFLICT (usuario_id) 
            DO UPDATE SET 
                access_token = EXCLUDED.access_token,
                refresh_token = COALESCE(EXCLUDED.refresh_token, GoogleCalendarTokens.refresh_token),
                token_expiry = EXCLUDED.token_expiry,
                scopes = EXCLUDED.scopes,
                updated_at = CURRENT_TIMESTAMP
        """)
        
        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql, {
                "uid": usuario_id,
                "access": credentials.token,
                "refresh": credentials.refresh_token,
                "expiry": token_expiry,
                "scopes": scopes_str
            })
            conn.commit()
        
        print(f"[OAUTH] Credenciais salvas. Expiry: {token_expiry}")
    
    @staticmethod
    def get_credentials(usuario_id):
        """
        Recupera e valida credenciais do usuário.
        Renova automaticamente se expiradas.
        
        CORREÇÃO FINAL: Garante timezone-aware em TODAS as situações.
        
        Returns:
            Credentials ou None
        """
        print(f"[OAUTH] Recuperando credenciais para usuário {usuario_id}...")
        
        if not db_engine:
            raise Exception("Banco não configurado")
        
        sql = text("""
            SELECT access_token, refresh_token, token_expiry, scopes
            FROM GoogleCalendarTokens
            WHERE usuario_id = :uid
        """)
        
        # Criar um flow para obter URL do token
        try:
            flow_temp = GoogleCalendarOAuthService.create_flow()
            token_url = flow_temp.oauth2session.token_url
        except:
            token_url = "https://oauth2.googleapis.com/token"
        
        with db_engine.connect() as conn:
            result = conn.execute(sql, {"uid": usuario_id}).fetchone()
        
        if not result:
            print(f"[OAUTH] ❌ Nenhuma credencial encontrada para usuário {usuario_id}")
            return None
        
        print(f"[OAUTH] ✅ Credenciais encontradas. Token expiry (raw): {result.token_expiry} (tipo: {type(result.token_expiry)})")
        
        # --- CORREÇÃO CRÍTICA E COMPLETA DO TIMEZONE ---
        expiry_dt = result.token_expiry

        # 1. Se vier como string (SQLite ou serialização)
        if expiry_dt and isinstance(expiry_dt, str):
            try:
                # Remove 'Z' se presente
                if expiry_dt.endswith('Z'):
                    expiry_dt = expiry_dt[:-1]
                # Tenta parse ISO
                expiry_dt = datetime.fromisoformat(expiry_dt)
            except ValueError:
                try:
                    # Tenta outro formato comum
                    expiry_dt = datetime.strptime(expiry_dt, "%Y-%m-%d %H:%M:%S.%f")
                except ValueError:
                    # Formato sem microssegundos
                    expiry_dt = datetime.strptime(expiry_dt, "%Y-%m-%d %H:%M:%S")

        # 2. Se for datetime, SEMPRE verificar e garantir timezone
        if expiry_dt and isinstance(expiry_dt, datetime):
            if expiry_dt.tzinfo is None:
                # NAIVE - adicionar UTC explicitamente
                print(f"[OAUTH] ⚠️ Token expiry é NAIVE. Convertendo para UTC.")
                expiry_dt = expiry_dt.replace(tzinfo=timezone.utc)
            else:
                # JÁ É AWARE - garantir que está em UTC
                print(f"[OAUTH] ✅ Token expiry já é AWARE ({expiry_dt.tzinfo})")
                if expiry_dt.tzinfo != timezone.utc:
                    expiry_dt = expiry_dt.astimezone(timezone.utc)
        
        print(f"[OAUTH] Token expiry (processado): {expiry_dt} (tzinfo: {expiry_dt.tzinfo if expiry_dt else None})")
        # --- FIM DA CORREÇÃO ---
            
        # Deserializar scopes
        scopes = json.loads(result.scopes) if result.scopes else GoogleCalendarOAuthService.SCOPES
        
        # Recriar objeto Credentials
        credentials = Credentials(
            token=result.access_token,
            refresh_token=result.refresh_token,
            token_uri=token_url,
            client_id=GOOGLE_CLIENT_ID,
            client_secret=GOOGLE_CLIENT_SECRET,
            scopes=scopes,
            expiry=expiry_dt  # Passa a data GARANTIDAMENTE aware
        )
        
        # Verificar expiração de forma segura MANUALMENTE
        # CORREÇÃO: Não usar credentials.expired pois ele usa datetime.now() naive
        # e compara com expiry aware, causando erro de comparação
        try:
            # Fazer comparação manual com datetimes timezone-aware
            now_utc = datetime.now(timezone.utc)
            is_expired = expiry_dt and expiry_dt <= now_utc

            if is_expired:
                print(f"[OAUTH] ⏰ Token expirado. Renovando...")
                if credentials.refresh_token:
                    credentials.refresh(Request())
                    GoogleCalendarOAuthService.save_credentials(usuario_id, credentials)
                    print(f"[OAUTH] ✅ Token renovado com sucesso")
                else:
                    print(f"[OAUTH] ❌ Token expirado sem refresh_token")
                    return None
            else:
                print(f"[OAUTH] ✅ Token ainda válido")
        except Exception as e:
            print(f"[OAUTH] ❌ Erro ao verificar expiração: {e}")
            # Se falhar, tentar renovar
            if credentials.refresh_token:
                try:
                    print(f"[OAUTH] Tentando renovar mesmo assim...")
                    credentials.refresh(Request())
                    GoogleCalendarOAuthService.save_credentials(usuario_id, credentials)
                    print(f"[OAUTH] ✅ Token renovado (fallback)")
                except Exception as e2:
                    print(f"[OAUTH] ❌ Falha ao renovar: {e2}")
                    return None
            else:
                return None
        
        return credentials
        
    @staticmethod
    def is_user_connected(usuario_id):
        """Verifica se usuário já conectou Google Calendar"""
        credentials = GoogleCalendarOAuthService.get_credentials(usuario_id)
        is_connected = credentials is not None
        print(f"[OAUTH] Usuário {usuario_id} conectado? {is_connected}")
        return is_connected
    
    @staticmethod
    def revoke_access(usuario_id):
        """Revoga acesso e remove tokens do banco"""
        if not db_engine:
            raise Exception("Banco não configurado")
        
        credentials = GoogleCalendarOAuthService.get_credentials(usuario_id)
        
        if credentials:
            try:
                # Revogar no Google
                import requests
                requests.post(
                    'https://oauth2.googleapis.com/revoke',
                    params={'token': credentials.token},
                    headers={'content-type': 'application/x-www-form-urlencoded'}
                )
            except Exception as e:
                print(f"[OAUTH] Erro ao revogar no Google: {e}")
        
        # Remover do banco
        sql = text("DELETE FROM GoogleCalendarTokens WHERE usuario_id = :uid")
        with db_engine.connect() as conn:
            conn.begin()
            conn.execute(sql, {"uid": usuario_id})
            conn.commit()
        
        print(f"[OAUTH] ✅ Acesso revogado para usuário {usuario_id}")
    
    @staticmethod
    def get_calendar_service(usuario_id):
        """
        Cria serviço do Google Calendar para um usuário.
        
        Returns:
            Google Calendar API Resource
        """
        print(f"[OAUTH] Criando serviço Calendar para usuário {usuario_id}")
        
        credentials = GoogleCalendarOAuthService.get_credentials(usuario_id)
        
        if not credentials:
            raise Exception("Usuário não conectou Google Calendar")
        
        try:
            service = build('calendar', 'v3', credentials=credentials)
            print(f"[OAUTH] ✅ Serviço Calendar criado com sucesso")
            return service
        except Exception as e:
            print(f"[OAUTH] ❌ Erro ao criar serviço: {e}")
            raise
    
    @staticmethod
    def test_connection(usuario_id):
        """Testa se conexão está funcionando"""
        try:
            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
            
            # Testar listando calendários
            calendars = service.calendarList().list().execute()
            
            print(f"[OAUTH] ✅ Conexão OK. {len(calendars.get('items', []))} calendários encontrados")
            return True
        except Exception as e:
            print(f"[OAUTH] ❌ Teste de conexão falhou: {e}")
            return False