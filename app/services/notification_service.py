# app/services/notification_service.py
"""
Serviço centralizado para envio de notificações
"""
import requests
import base64
import os

def enviar_notificacao_whatsapp(numero, mensagem, bot_url, api_key):
    """
    Envia notificação via bot do WhatsApp.

    Args:
        numero: Número do WhatsApp (ex: 553194001072)
        mensagem: Texto da mensagem
        bot_url: URL do bot (ex: https://bot.onrender.com)
        api_key: Chave de autenticação

    Returns:
        bool: True se enviou com sucesso
    """
    try:
        headers = {'x-api-key': api_key}
        payload = {'numero': numero, 'mensagem': mensagem}

        response = requests.post(
            f"{bot_url}/enviar-mensagem",
            json=payload,
            headers=headers,
            timeout=10
        )

        if response.status_code == 200:
            print(f"[NOTIF] ✅ Enviado para {numero}")
            return True
        else:
            print(f"[NOTIF] ❌ Erro {response.status_code} para {numero}")
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
        api_key: Chave de autenticação

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

        headers = {'x-api-key': api_key}
        payload = {
            'numero': numero,
            'imagem': image_base64,
            'legenda': caption
        }

        response = requests.post(
            f"{bot_url}/enviar-imagem",
            json=payload,
            headers=headers,
            timeout=30  # Timeout maior para upload de imagem
        )

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
            print(f"[NOTIF-IMG] ❌ Erro {response.status_code} para {numero}")
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
        api_key: Chave de autenticação

    Returns:
        bool: True se enviou com sucesso
    """
    try:
        # Converter bytes para base64
        image_base64 = base64.b64encode(image_bytes).decode('utf-8')

        headers = {'x-api-key': api_key}
        payload = {
            'numero': numero,
            'imagem': image_base64,
            'legenda': caption
        }

        response = requests.post(
            f"{bot_url}/enviar-imagem",
            json=payload,
            headers=headers,
            timeout=30  # Timeout maior para upload de imagem
        )

        if response.status_code == 200:
            print(f"[NOTIF-IMG] ✅ Imagem enviada para {numero}")
            return True
        else:
            print(f"[NOTIF-IMG] ❌ Erro {response.status_code} para {numero}")
            return False

    except Exception as e:
        print(f"[NOTIF-IMG] ❌ Erro ao enviar imagem para {numero}: {e}")
        return False