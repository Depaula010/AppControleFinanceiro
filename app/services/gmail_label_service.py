# app/services/gmail_label_service.py

import time
from datetime import datetime

class GmailLabelService:
    """Gerencia labels do Gmail para emails processados"""

    LABEL_NAME = 'AppFinanceiro/Processado'

    @staticmethod
    def get_or_create_label(gmail_service):
        """
        Obtém ou cria o label de processamento.

        Args:
            gmail_service: Gmail API service object

        Returns:
            str: Label ID
        """
        try:
            # 1. Listar labels existentes
            results = gmail_service.users().labels().list(userId='me').execute()
            labels = results.get('labels', [])

            # 2. Verificar se label já existe
            for label in labels:
                if label['name'] == GmailLabelService.LABEL_NAME:
                    print(f"[GMAIL-LABEL] Label já existe: {label['id']}")
                    return label['id']

            # 3. Criar label
            label_body = {
                'name': GmailLabelService.LABEL_NAME,
                'labelListVisibility': 'labelShow',
                'messageListVisibility': 'show',
                'color': {
                    'textColor': '#ffffff',
                    'backgroundColor': '#16a765'  # Verde
                }
            }

            label = gmail_service.users().labels().create(
                userId='me',
                body=label_body
            ).execute()

            print(f"[GMAIL-LABEL] ✅ Label criado: {label['id']}")
            return label['id']

        except Exception as e:
            print(f"[GMAIL-LABEL] ❌ Erro ao obter/criar label: {e}")
            raise

    @staticmethod
    def mark_email_processed(usuario_id, email_id):
        """
        Adiciona label "AppFinanceiro/Processado" e marca como lido.

        Args:
            usuario_id: ID do usuário
            email_id: Gmail message ID

        Returns:
            bool: True se sucesso
        """
        try:
            # Importação local para evitar circular import
            from app.services.google_calendar_oauth_service import GoogleOAuthService

            # Obter Gmail service
            gmail_service = GoogleOAuthService.get_gmail_service(usuario_id)

            # Obter ou criar label
            label_id = GmailLabelService.get_or_create_label(gmail_service)

            # Modificar mensagem: adicionar label + marcar como lido
            gmail_service.users().messages().modify(
                userId='me',
                id=email_id,
                body={
                    'addLabelIds': [label_id],
                    'removeLabelIds': ['UNREAD']
                }
            ).execute()

            print(f"[GMAIL-LABEL] ✅ Email {email_id} marcado como processado e lido")
            return True

        except Exception as e:
            print(f"[GMAIL-LABEL] ❌ Erro ao marcar email {email_id}: {e}")
            import traceback
            traceback.print_exc()
            return False

    @staticmethod
    def mark_email_processed_with_retry(usuario_id, email_id, max_retries=3):
        """
        Marca email com retry logic (exponential backoff).

        Args:
            usuario_id: ID do usuário
            email_id: Gmail message ID
            max_retries: Número máximo de tentativas

        Returns:
            bool: True se sucesso
        """
        for attempt in range(max_retries):
            success = GmailLabelService.mark_email_processed(usuario_id, email_id)

            if success:
                return True

            if attempt < max_retries - 1:
                wait_time = 2 ** attempt  # 1s, 2s, 4s
                print(f"[GMAIL-LABEL] Retry {attempt + 1}/{max_retries} em {wait_time}s...")
                time.sleep(wait_time)

        # Falhou após todas as tentativas
        print(f"[GMAIL-LABEL] ❌ Falha após {max_retries} tentativas")

        # Logar no Redis para revisão manual
        try:
            from app.services.redis_service import redis_service

            redis_key = f"gmail_label_failed:{email_id}"
            redis_service.set_with_ttl(
                redis_key,
                {
                    'usuario_id': usuario_id,
                    'email_id': email_id,
                    'timestamp': datetime.now().isoformat()
                },
                ttl_seconds=7 * 24 * 3600  # 7 dias
            )
            print(f"[GMAIL-LABEL] Falha registrada no Redis: {redis_key}")
        except Exception as e:
            print(f"[GMAIL-LABEL] ❌ Erro ao registrar falha no Redis: {e}")

        return False
