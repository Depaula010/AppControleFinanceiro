# app/services/notification_service.py
"""
Serviço centralizado para envio de notificações via WhatsApp Bot (API v1 SaaS).
"""
import requests
import base64
import os

from app.config import BOT_SESSION_ID, BOT_SESSION_NAME, BOT_API_KEY
from app.services.redis_service import redis_service

_SESSION_CACHE_KEY = f"bot_session_id:{BOT_SESSION_NAME}"
_SESSION_CACHE_TTL = 600  # 10 minutos


def _resolve_session_id(bot_url, api_key, force_refresh=False):
    """
    Resolve o session_id atual do bot pelo nome estável (BOT_SESSION_NAME),
    em vez de depender de um UUID fixo em variável de ambiente que fica
    desatualizado sempre que a sessão é recriada no bot.

    BOT_SESSION_ID continua funcionando como override manual, se configurado.
    """
    if BOT_SESSION_ID:
        return BOT_SESSION_ID

    if not force_refresh:
        cached = redis_service.get(_SESSION_CACHE_KEY)
        if cached:
            return cached

    try:
        response = requests.get(
            f"{bot_url}/api/v1/sessions",
            headers={'X-API-Key': api_key},
            timeout=10
        )
        response.raise_for_status()
        sessions = response.json().get('data', [])
        match = next((s for s in sessions if s.get('session_name') == BOT_SESSION_NAME), None)

        if not match:
            print(f"[NOTIF] ❌ Nenhuma sessão com session_name='{BOT_SESSION_NAME}' encontrada no bot")
            return None

        session_id = match['session_id']
        redis_service.set_with_ttl(_SESSION_CACHE_KEY, session_id, _SESSION_CACHE_TTL)
        return session_id
    except Exception as e:
        print(f"[NOTIF] ❌ Erro ao resolver session_id do bot: {e}")
        return None


def _build_url_and_headers(bot_url, api_key, endpoint_legacy, endpoint_v1_suffix, force_refresh=False):
    """
    Constrói URL e headers baseado na configuração.
    Se conseguir resolver um session_id (override manual ou via BOT_SESSION_NAME),
    usa API v1 com BOT_API_KEY. Senão, usa endpoint legado com a api_key passada (fallback).
    """
    session_id = _resolve_session_id(bot_url, BOT_API_KEY, force_refresh=force_refresh)
    if session_id:
        url = f"{bot_url}/api/v1/sessions/{session_id}/{endpoint_v1_suffix}"
        headers = {'X-API-Key': BOT_API_KEY, 'Content-Type': 'application/json'}
    else:
        url = f"{bot_url}/{endpoint_legacy}"
        headers = {'x-api-key': api_key}
    return url, headers


def _post_to_bot(bot_url, api_key, endpoint_legacy, endpoint_v1_suffix, payload, timeout):
    """
    POST para o bot com retry automático: se a sessão resolvida estiver desatualizada
    (bot responde 404 "Session not found" - ex: sessão foi recriada no bot), invalida
    o cache e resolve o session_id de novo antes de desistir.
    """
    url, headers = _build_url_and_headers(bot_url, api_key, endpoint_legacy, endpoint_v1_suffix)
    response = requests.post(url, json=payload, headers=headers, timeout=timeout)

    if response.status_code == 404 and not BOT_SESSION_ID:
        redis_service.delete(_SESSION_CACHE_KEY)
        url, headers = _build_url_and_headers(
            bot_url, api_key, endpoint_legacy, endpoint_v1_suffix, force_refresh=True
        )
        response = requests.post(url, json=payload, headers=headers, timeout=timeout)

    return response


def enviar_notificacao_whatsapp(numero, mensagem, bot_url, api_key):
    """
    Envia notificação via bot do WhatsApp.

    Args:
        numero: Número do WhatsApp (ex: 553194001072)
        mensagem: Texto da mensagem
        bot_url: URL do bot (ex: https://bot.onrender.com)
        api_key: Chave de autenticação (usada apenas no fallback legado)

    Returns:
        bool: True se enviou com sucesso
    """
    try:
        payload = {'numero': numero, 'mensagem': mensagem}
        response = _post_to_bot(bot_url, api_key, 'enviar-mensagem', 'send-message', payload, timeout=10)

        if response.status_code == 200:
            print(f"[NOTIF] ✅ Enviado para {numero}")
            return True
        else:
            print(f"[NOTIF] ❌ Erro {response.status_code} para {numero}: {response.text}")
            return False

    except Exception as e:
        print(f"[NOTIF] ❌ Erro ao enviar para {numero}: {e}")
        return False


def enviar_imagem_whatsapp(numero, image_path, caption, bot_url, api_key):
    """
    Envia imagem via bot do WhatsApp.

    Args:
        numero: Número do WhatsApp (ex: 553194001072)
        image_path: Caminho do arquivo de imagem
        caption: Legenda da imagem
        bot_url: URL do bot (ex: https://bot.onrender.com)
        api_key: Chave de autenticação (usada apenas no fallback legado)

    Returns:
        bool: True se enviou com sucesso
    """
    try:
        # Verificar se arquivo existe
        if not os.path.exists(image_path):
            print(f"[NOTIF-IMG] ❌ Arquivo não encontrado: {image_path}")
            return False

        # Ler arquivo e converter para base64
        with open(image_path, 'rb') as img_file:
            image_data = img_file.read()
            image_base64 = base64.b64encode(image_data).decode('utf-8')

        payload = {
            'numero': numero,
            'imagem': image_base64,
            'legenda': caption
        }
        response = _post_to_bot(bot_url, api_key, 'enviar-imagem', 'send-image', payload, timeout=30)

        if response.status_code == 200:
            print(f"[NOTIF-IMG] ✅ Imagem enviada para {numero}")

            # Remover arquivo temporário após envio
            try:
                os.remove(image_path)
                print(f"[NOTIF-IMG] 🗑️ Arquivo temporário removido: {image_path}")
            except:
                pass

            return True
        else:
            print(f"[NOTIF-IMG] ❌ Erro {response.status_code} para {numero}: {response.text}")
            return False

    except Exception as e:
        print(f"[NOTIF-IMG] ❌ Erro ao enviar imagem para {numero}: {e}")
        return False


def enviar_imagem_whatsapp_bytes(numero, image_bytes, caption, bot_url, api_key):
    """
    Envia imagem via bot do WhatsApp a partir de bytes.

    Args:
        numero: Número do WhatsApp (ex: 553194001072)
        image_bytes: Bytes da imagem
        caption: Legenda da imagem
        bot_url: URL do bot (ex: https://bot.onrender.com)
        api_key: Chave de autenticação (usada apenas no fallback legado)

    Returns:
        bool: True se enviou com sucesso
    """
    try:
        # Converter bytes para base64
        image_base64 = base64.b64encode(image_bytes).decode('utf-8')

        payload = {
            'numero': numero,
            'imagem': image_base64,
            'legenda': caption
        }
        response = _post_to_bot(bot_url, api_key, 'enviar-imagem', 'send-image', payload, timeout=30)

        if response.status_code == 200:
            print(f"[NOTIF-IMG] ✅ Imagem enviada para {numero}")
            return True
        else:
            print(f"[NOTIF-IMG] ❌ Erro {response.status_code} para {numero}: {response.text}")
            return False

    except Exception as e:
        print(f"[NOTIF-IMG] ❌ Erro ao enviar imagem para {numero}: {e}")
        return False
