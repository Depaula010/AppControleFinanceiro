# app/services/gmail_email_processing_service.py

import re
import base64
from bs4 import BeautifulSoup
from datetime import datetime

class GmailEmailProcessingService:
    """Serviço para processar emails do Gmail (Mercado Pago PIX)"""

    @staticmethod
    def extract_email_data(gmail_message):
        """
        Extrai dados estruturados de um objeto Gmail API message.

        Args:
            gmail_message: Objeto message da Gmail API

        Returns:
            dict: {
                'id': 'message_id',
                'thread_id': 'thread_id',
                'from': 'info@mercadopago.com',
                'subject': 'Seu Pix de R$ 59,50 foi enviado',
                'date': '2025-11-29T07:24:11Z',
                'body_html': '<html>...',
                'body_text': 'plain text...'
            }
        """
        payload = gmail_message['payload']
        headers = {h['name']: h['value'] for h in payload['headers']}

        # Extrair corpo (HTML e texto)
        body_html = ''
        body_text = ''

        if 'parts' in payload:
            # Email multiparte
            for part in payload['parts']:
                mime_type = part.get('mimeType', '')

                if 'data' in part['body']:
                    data = part['body']['data']
                    decoded = base64.urlsafe_b64decode(data).decode('utf-8', errors='ignore')

                    if mime_type == 'text/html':
                        body_html = decoded
                    elif mime_type == 'text/plain':
                        body_text = decoded

                # Verificar sub-partes (nested)
                if 'parts' in part:
                    for subpart in part['parts']:
                        if 'data' in subpart['body']:
                            data = subpart['body']['data']
                            decoded = base64.urlsafe_b64decode(data).decode('utf-8', errors='ignore')

                            mime_type = subpart.get('mimeType', '')
                            if mime_type == 'text/html':
                                body_html = decoded
                            elif mime_type == 'text/plain':
                                body_text = decoded
        else:
            # Email de parte única
            if 'data' in payload['body']:
                data = payload['body']['data']
                body_text = base64.urlsafe_b64decode(data).decode('utf-8', errors='ignore')

        return {
            'id': gmail_message['id'],
            'thread_id': gmail_message['threadId'],
            'from': headers.get('From', ''),
            'subject': headers.get('Subject', ''),
            'date': headers.get('Date', ''),
            'body_html': body_html,
            'body_text': body_text
        }

    @staticmethod
    def is_mercado_pago_email(email_data):
        """
        Verifica se o email é do Mercado Pago.

        Args:
            email_data: Dict retornado por extract_email_data()

        Returns:
            bool: True se for Mercado Pago
        """
        sender = email_data['from'].lower()

        mercadopago_domains = [
            '@mercadopago.com',
            '@mercadopago.com.br',
            '@email.mercadopago.com',
            '@mercadolibre.com'
        ]

        return any(domain in sender for domain in mercadopago_domains)

    @staticmethod
    def is_pix_transaction_email(email_data):
        """
        Verifica se o email é uma notificação de transação PIX.

        Args:
            email_data: Dict retornado por extract_email_data()

        Returns:
            bool: True se for PIX
        """
        subject = email_data['subject'].lower()

        # Keywords que indicam transação PIX
        pix_keywords = [
            'pix',
            'enviado',
            'recebeu',
            'recebido',
            'pagamento',
            'transferência',
            'transferencia'
        ]

        return any(kw in subject for kw in pix_keywords)

    @staticmethod
    def parse_pix_email_basic(email_data):
        """
        Parse básico de email PIX Mercado Pago (regex).
        Usado como fallback se Gemini falhar.

        Args:
            email_data: Dict retornado por extract_email_data()

        Returns:
            dict: {
                'valor': 59.50,
                'descricao': 'LOJAS REDE - COMERCIAL LTDA',
                'tipo': 'Despesa',  # ou 'Renda'
                'data': '2025-11-29',
                'tipo_pagamento': 'pix'
            }
        """
        subject = email_data['subject']
        body = email_data['body_html'] or email_data['body_text']

        # Parse HTML para texto limpo
        if email_data['body_html']:
            soup = BeautifulSoup(body, 'html.parser')
            text = soup.get_text()
        else:
            text = body

        # Determinar tipo (enviado = Despesa, recebeu = Renda)
        tipo = 'Despesa'
        subject_lower = subject.lower()
        if any(kw in subject_lower for kw in ['recebeu', 'recebido', 'você recebeu']):
            tipo = 'Renda'

        # Extrair valor (R$ 59,50)
        valor = 0.0
        valor_patterns = [
            r'R\$\s*([0-9]{1,3}(?:\.[0-9]{3})*,[0-9]{2})',  # R$ 1.234,56
            r'R\$\s*([0-9]+,[0-9]{2})',                      # R$ 123,45
        ]

        for pattern in valor_patterns:
            match = re.search(pattern, text)
            if match:
                valor_str = match.group(1).replace('.', '').replace(',', '.')
                valor = float(valor_str)
                break

        # Extrair destinatário/remetente
        descricao = 'PIX'
        descricao_patterns = [
            r'(?:Destinatário|Para|De|Remetente):\s*([^\n]+)',
            r'(?:Destino|Origem):\s*([^\n]+)',
        ]

        for pattern in descricao_patterns:
            match = re.search(pattern, text, re.IGNORECASE)
            if match:
                descricao = match.group(1).strip()
                # Limpar (remover CPF/CNPJ entre parênteses)
                descricao = re.sub(r'\s*\(.*?\)', '', descricao)
                # Limpar dados bancários
                descricao = re.sub(r'(?:CPF|CNPJ|Agência|Conta).*', '', descricao, flags=re.IGNORECASE)
                descricao = descricao.strip()
                if descricao:
                    break

        return {
            'valor': valor,
            'descricao': descricao,
            'tipo': tipo,
            'data': email_data.get('date', ''),
            'tipo_pagamento': 'pix'
        }

    @staticmethod
    def clean_description(raw_description):
        """
        Limpa descrição extraída (remove CPF, CNPJ, dados bancários).

        Args:
            raw_description: String bruta

        Returns:
            str: String limpa
        """
        if not raw_description:
            return 'PIX'

        # Remover textos entre parênteses
        cleaned = re.sub(r'\s*\([^)]*\)', '', raw_description)

        # Remover CPF/CNPJ patterns
        cleaned = re.sub(r'\d{3}\.\d{3}\.\d{3}-\d{2}', '', cleaned)  # CPF
        cleaned = re.sub(r'\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}', '', cleaned)  # CNPJ

        # Remover dados bancários
        cleaned = re.sub(r'(?:Agência|Ag|Conta|Cc):?\s*\d+', '', cleaned, flags=re.IGNORECASE)
        cleaned = re.sub(r'Banco\s+[A-Z\s]+', '', cleaned, flags=re.IGNORECASE)

        # Remover múltiplos espaços
        cleaned = re.sub(r'\s+', ' ', cleaned)

        cleaned = cleaned.strip()

        return cleaned if cleaned else 'PIX'
