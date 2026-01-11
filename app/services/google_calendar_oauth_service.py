# app/services/google_calendar_oauth_service.py (VERSÃO CORRIGIDA FINAL)

import json
import base64
from datetime import datetime, timezone
from google.oauth2.credentials import Credentials
from google_auth_oauthlib.flow import Flow
from google.auth.transport.requests import Request
from google.auth.exceptions import RefreshError
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError
from sqlalchemy import text

from app import db_engine
from app.config import GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, GOOGLE_REDIRECT_URI

class GoogleCalendarOAuthService:
    """Gerencia autenticação OAuth2 para múltiplos usuários"""
    
    SCOPES = [
        'https://www.googleapis.com/auth/calendar'
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

        # CORREÇÃO: Salvar como timezone-aware no banco
        # A biblioteca Google retorna expiry como NAIVE UTC
        token_expiry = None
        if credentials.expiry:
            if credentials.expiry.tzinfo is None:
                # É naive UTC - adicionar tzinfo para salvar no banco
                token_expiry = credentials.expiry.replace(tzinfo=timezone.utc)
            else:
                # Já tem tzinfo (caso raro)
                token_expiry = credentials.expiry
        
        sql = text("""
            INSERT INTO GoogleCalendarTokens
            (usuario_id, access_token, refresh_token, token_expiry, scopes, needs_reconnect, updated_at)
            VALUES (:uid, :access, :refresh, :expiry, :scopes, FALSE, CURRENT_TIMESTAMP)
            ON CONFLICT (usuario_id)
            DO UPDATE SET
                access_token = EXCLUDED.access_token,
                refresh_token = COALESCE(EXCLUDED.refresh_token, GoogleCalendarTokens.refresh_token),
                token_expiry = EXCLUDED.token_expiry,
                scopes = EXCLUDED.scopes,
                needs_reconnect = FALSE,
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
    def mark_token_as_invalid(usuario_id, error_message=None):
        """
        Marca token do usuário como necessitando reconexão.

        Chamado quando detectamos invalid_grant ou outros erros de autenticação
        que requerem reconexão manual do usuário.
        """
        if not db_engine:
            raise Exception("Banco não configurado")

        sql = text("""
            UPDATE GoogleCalendarTokens
            SET needs_reconnect = TRUE,
                updated_at = CURRENT_TIMESTAMP
            WHERE usuario_id = :uid
        """)

        try:
            with db_engine.connect() as conn:
                with conn.begin():
                    conn.execute(sql, {"uid": usuario_id})

            msg = f"Token marcado como inválido para usuário {usuario_id}"
            if error_message:
                msg += f" - Razão: {error_message}"
            print(f"[OAUTH] ⚠️ {msg}")

            # Enviar notificação WhatsApp (1x por semana via Redis)
            GoogleCalendarOAuthService._notify_token_revoked(usuario_id)

        except Exception as e:
            print(f"[OAUTH] ❌ Erro ao marcar token como inválido: {e}")

    @staticmethod
    def _notify_token_revoked(usuario_id):
        """
        Notifica usuário que o token do Google Calendar foi revogado/expirado.
        Usa Redis para enviar apenas 1x por semana (throttling).

        Args:
            usuario_id: ID do usuário
        """
        try:
            from app.services.redis_service import redis_service
            from app.services.notification_service import enviar_notificacao_whatsapp
            from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

            # Verificar se já foi notificado recentemente (Redis)
            redis_key = f"calendar_token_revoked_alert:{usuario_id}"

            if redis_service.exists(redis_key):
                print(f"[OAUTH] ℹ️ Usuário {usuario_id} já foi notificado esta semana sobre token revogado")
                return

            # Buscar número de WhatsApp do usuário
            sql_user = text("SELECT numero_whatsapp FROM Usuarios WHERE id = :uid")

            with db_engine.connect() as conn:
                user_result = conn.execute(sql_user, {"uid": usuario_id}).fetchone()

            if not user_result or not user_result.numero_whatsapp:
                print(f"[OAUTH] ⚠️ Usuário {usuario_id} não possui número de WhatsApp cadastrado")
                return

            numero_whatsapp = user_result.numero_whatsapp

            # Preparar mensagem específica para token revogado
            mensagem = """⚠️ *Google Calendar - Reconexão Necessária*

Detectamos que sua conexão com o Google Calendar expirou ou foi revogada.

🔴 *Seus alertas de tarefas estão PAUSADOS* até você reconectar.

✅ *Como resolver:*
1. Acesse as configurações do app
2. Clique em "Conectar Google Calendar"
3. Autorize novamente o acesso

_Esta notificação é enviada 1x por semana enquanto a reconexão for necessária._"""

            # Enviar notificação
            sucesso = enviar_notificacao_whatsapp(
                numero=numero_whatsapp,
                mensagem=mensagem,
                bot_url=BOT_WHATSAPP_URL,
                api_key=API_SECRET_KEY
            )

            if sucesso:
                # Marcar como notificado (TTL: 7 dias = 1 semana)
                redis_service.set_with_ttl(redis_key, "1", ttl_seconds=7*24*60*60)
                print(f"[OAUTH] ✅ Notificação de token revogado enviada para usuário {usuario_id}")
            else:
                print(f"[OAUTH] ❌ Falha ao enviar notificação para usuário {usuario_id}")

        except Exception as e:
            # Não propagar erro - notificação é feature adicional, não deve quebrar o fluxo
            print(f"[OAUTH] ❌ Erro ao enviar notificação de token revogado: {e}")

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
        # IMPORTANTE: A biblioteca Google OAuth2 espera expiry como NAIVE UTC
        # Internamente ela usa datetime.utcnow() (naive) para comparações
        expiry_dt = result.token_expiry
        expiry_aware = None  # Guardar versão aware para comparação manual

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

        # 2. Se for datetime, converter para NAIVE UTC (requerido pela lib Google)
        if expiry_dt and isinstance(expiry_dt, datetime):
            if expiry_dt.tzinfo is None:
                # JÁ É NAIVE - assumir que é UTC
                print(f"[OAUTH] Token expiry é NAIVE (assumindo UTC)")
                expiry_aware = expiry_dt.replace(tzinfo=timezone.utc)
                # Manter naive para Credentials
            else:
                # É AWARE - salvar versão aware e converter para naive UTC
                print(f"[OAUTH] ✅ Token expiry é AWARE ({expiry_dt.tzinfo})")
                expiry_aware = expiry_dt.astimezone(timezone.utc)
                # Converter para NAIVE UTC removendo tzinfo
                expiry_dt = expiry_aware.replace(tzinfo=None)

        print(f"[OAUTH] Token expiry (para Credentials): {expiry_dt} (tzinfo: {expiry_dt.tzinfo if expiry_dt else None})")
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
            expiry=expiry_dt  # Passa datetime NAIVE UTC (requerido pela lib)
        )

        # Verificar expiração usando a propriedade nativa da biblioteca
        try:
            if credentials.expired:
                print(f"[OAUTH] ⏰ Token expirado. Renovando...")
                if credentials.refresh_token:
                    try:
                        credentials.refresh(Request())
                        GoogleCalendarOAuthService.save_credentials(usuario_id, credentials)
                        print(f"[OAUTH] ✅ Token renovado com sucesso")
                    except RefreshError as refresh_err:
                        error_str = str(refresh_err)
                        # Detectar especificamente invalid_grant
                        if 'invalid_grant' in error_str.lower():
                            print(f"[OAUTH] ⚠️ Token revogado/expirado (invalid_grant) para usuário {usuario_id}")
                            GoogleCalendarOAuthService.mark_token_as_invalid(usuario_id, "invalid_grant")
                            return None
                        else:
                            # Outro tipo de RefreshError (pode ser temporário)
                            print(f"[OAUTH] ❌ Erro ao renovar token: {refresh_err}")
                            return None
                else:
                    print(f"[OAUTH] ❌ Token expirado sem refresh_token")
                    GoogleCalendarOAuthService.mark_token_as_invalid(usuario_id, "no_refresh_token")
                    return None
            else:
                print(f"[OAUTH] ✅ Token ainda válido")

        except RefreshError as refresh_err:
            error_str = str(refresh_err)
            if 'invalid_grant' in error_str.lower():
                print(f"[OAUTH] ⚠️ Token revogado/expirado (invalid_grant) para usuário {usuario_id}")
                GoogleCalendarOAuthService.mark_token_as_invalid(usuario_id, "invalid_grant")
                return None
            else:
                print(f"[OAUTH] ❌ Erro ao verificar expiração: {refresh_err}")
                return None

        except Exception as e:
            print(f"[OAUTH] ❌ Erro inesperado ao verificar expiração: {e}")
            # Não marcar como inválido para erros genéricos (podem ser temporários)
            if credentials.refresh_token:
                try:
                    print(f"[OAUTH] Tentando renovar mesmo assim...")
                    credentials.refresh(Request())
                    GoogleCalendarOAuthService.save_credentials(usuario_id, credentials)
                    print(f"[OAUTH] ✅ Token renovado (fallback)")
                except RefreshError as refresh_err:
                    error_str = str(refresh_err)
                    if 'invalid_grant' in error_str.lower():
                        print(f"[OAUTH] ⚠️ Token revogado/expirado (invalid_grant) para usuário {usuario_id}")
                        GoogleCalendarOAuthService.mark_token_as_invalid(usuario_id, "invalid_grant")
                        return None
                    else:
                        print(f"[OAUTH] ❌ Falha ao renovar: {refresh_err}")
                        return None
                except Exception as e2:
                    print(f"[OAUTH] ❌ Falha ao renovar (fallback): {e2}")
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

    @staticmethod
    def get_events_for_date(usuario_id, target_date):
        """
        Busca eventos de um dia específico para um usuário.

        Args:
            usuario_id: ID do usuário
            target_date: date object

        Returns:
            dict: {'success': bool, 'events': list, 'error': str}
        """
        try:
            from datetime import datetime, time
            from zoneinfo import ZoneInfo

            print(f"[OAUTH] Buscando eventos para usuário {usuario_id} na data {target_date}")

            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)

            # Timezone do Brasil
            TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

            # Criar datetime no timezone do Brasil
            start_datetime = datetime.combine(target_date, time.min).replace(tzinfo=TIMEZONE_BR)
            end_datetime = datetime.combine(target_date, time.max).replace(tzinfo=TIMEZONE_BR)

            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()

            print(f"[OAUTH] Buscando de {start_iso} até {end_iso}")

            # Buscar todos os calendários
            calendars_result = service.calendarList().list().execute()
            calendars = calendars_result.get('items', [])

            # Filtrar apenas calendários selecionados
            selected_calendars = [cal for cal in calendars if cal.get('selected', False)]

            print(f"[OAUTH] Buscando em {len(selected_calendars)} calendários")

            # Buscar eventos em todos os calendários
            all_events = []
            for calendar in selected_calendars:
                cal_id = calendar['id']
                cal_name = calendar.get('summary', 'Sem nome')

                try:
                    events_result = service.events().list(
                        calendarId=cal_id,
                        timeMin=start_iso,
                        timeMax=end_iso,
                        singleEvents=True,
                        orderBy='startTime'
                    ).execute()

                    events = events_result.get('items', [])

                    if events:
                        print(f"[OAUTH] Calendário '{cal_name}': {len(events)} eventos")

                    for event in events:
                        event['_calendar_name'] = cal_name
                        all_events.append(event)

                except Exception as e:
                    print(f"[OAUTH] Erro ao buscar em '{cal_name}': {e}")
                    continue

            print(f"[OAUTH] Total de {len(all_events)} eventos encontrados")

            # Formatar eventos
            formatted_events = []
            for event in all_events:
                try:
                    start = event['start'].get('dateTime', event['start'].get('date'))
                    end = event['end'].get('dateTime', event['end'].get('date'))

                    formatted_events.append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start,
                        'end': end,
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start'],
                        'calendar_name': event.get('_calendar_name', '')
                    })
                except Exception as e:
                    print(f"[OAUTH] Erro ao processar evento: {e}")
                    continue

            # Ordenar por horário
            formatted_events.sort(key=lambda x: x['start'])

            return {
                'success': True,
                'events': formatted_events,
                'error': None
            }

        except Exception as e:
            print(f"[OAUTH] ❌ Erro ao buscar eventos: {e}")
            import traceback
            traceback.print_exc()
            return {
                'success': False,
                'events': [],
                'error': str(e)
            }