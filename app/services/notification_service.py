# app/services/notification_service.py
import requests

def enviar_notificacao_whatsapp(numero, mensagem, bot_url, api_key):
    """
    Serviço centralizado para enviar notificações ao bot.
    (Movido do motor_agendamentos.py)
    """
    try:
        headers = {'x-api-key': api_key}
        payload = {'numero': numero, 'mensagem': mensagem}
        # Adicionado timeout
        response = requests.post(f"{bot_url}/enviar-mensagem", json=payload, headers=headers, timeout=10) 
        
        if response.status_code == 200:
            print(f"[SERVICE-NOTIFY] Notificação enviada com sucesso para {numero}.")
            return True
        else:
            print(f"[SERVICE-NOTIFY] ERRO: Bot respondeu com status {response.status_code}")
            return False
            
    except Exception as e:
        print(f"[SERVICE-NOTIFY] ERRO: Falha ao chamar a API do Bot: {e}")
        return False