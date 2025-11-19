# app/services/notification_service.py
"""
Serviço centralizado para envio de notificações
"""
import requests

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