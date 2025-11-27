# app/services/event_confirmation_service.py
from app.services.redis_service import redis_service
from app.services.calendar_management_service import CalendarManagementService
import uuid
from datetime import date

class EventConfirmationService:
    """Gerencia o fluxo de confirmação de eventos do calendário"""

    @staticmethod
    def create_pending_event(numero_whatsapp, event_data):
        """
        Cria um evento pendente de confirmação no Redis.

        Args:
            numero_whatsapp: Número do usuário
            event_data: Dict com dados do evento {
                "usuario_id": 1,
                "titulo": "Consulta médica",
                "data_evento": "2025-11-21" (string ISO),
                "hora_inicio": "18:00" ou None,
                "hora_fim": "19:00" ou None,
                "descricao": "..." ou None,
                "localizacao": "..." ou None
            }

        Returns:
            event_id: ID único do evento pendente
        """
        # Gera ID único para este evento
        event_id = str(uuid.uuid4())[:8]

        # Chave no Redis: pending_event:{numero}:{event_id}
        redis_key = f"pending_event:{numero_whatsapp}:{event_id}"

        # Salva no Redis com TTL de 5 minutos
        success = redis_service.set_with_ttl(
            redis_key,
            event_data,
            ttl_seconds=300  # 5 minutos
        )

        if success:
            print(f"[EVENT-CONFIRM] Evento pendente criado: {event_id}")
            return event_id
        else:
            print(f"[EVENT-CONFIRM] ERRO ao criar evento pendente")
            return None

    @staticmethod
    def get_pending_event(numero_whatsapp, event_id):
        """Recupera um evento pendente"""
        redis_key = f"pending_event:{numero_whatsapp}:{event_id}"
        return redis_service.get(redis_key)

    @staticmethod
    def delete_pending_event(numero_whatsapp, event_id):
        """Remove um evento pendente"""
        redis_key = f"pending_event:{numero_whatsapp}:{event_id}"
        return redis_service.delete(redis_key)

    @staticmethod
    def get_latest_pending_event(numero_whatsapp):
        """
        Busca o evento pendente mais recente do usuário.
        Útil quando o usuário responde apenas "sim" sem ID.

        Returns:
            (event_id, event_data) ou (None, None)
        """
        # Buscar todas as chaves de eventos pendentes deste usuário
        pattern = f"pending_event:{numero_whatsapp}:*"
        keys = redis_service.get_keys_by_pattern(pattern)

        if not keys:
            return None, None

        # Pegar o último (mais recente)
        # Formato: pending_event:553194001072:abc12345
        latest_key = keys[-1]
        event_id = latest_key.split(":")[-1]
        event_data = redis_service.get(latest_key)

        return event_id, event_data

    @staticmethod
    def confirm_and_create_event(numero_whatsapp, event_id=None):
        """
        Confirma e cria o evento no Google Calendar.

        Args:
            numero_whatsapp: Número do usuário
            event_id: ID do evento (se None, busca o mais recente)

        Returns:
            (sucesso: bool, mensagem: str, google_event_id: str ou None)
        """
        # Se não tem event_id, buscar o mais recente
        if not event_id:
            event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_whatsapp)
        else:
            event_data = EventConfirmationService.get_pending_event(numero_whatsapp, event_id)

        if not event_data:
            return False, "❌ Nenhum evento pendente encontrado ou já expirou (5 minutos).", None

        print(f"[EVENT-CONFIRM] Confirmando evento {event_id}: {event_data}")

        # Converter data de string para date object
        data_str = event_data['data_evento']
        if isinstance(data_str, str):
            data_evento = date.fromisoformat(data_str)
        else:
            data_evento = data_str

        # Criar evento no Google Calendar
        sucesso, mensagem, google_event_id = CalendarManagementService.create_event(
            usuario_id=event_data['usuario_id'],
            titulo=event_data['titulo'],
            data_evento=data_evento,
            hora_inicio=event_data.get('hora_inicio'),
            hora_fim=event_data.get('hora_fim'),
            descricao=event_data.get('descricao'),
            localizacao=event_data.get('localizacao')
        )

        if sucesso:
            # Remover do Redis
            EventConfirmationService.delete_pending_event(numero_whatsapp, event_id)
            print(f"[EVENT-CONFIRM] ✅ Evento criado e removido do Redis")

        return sucesso, mensagem, google_event_id

    @staticmethod
    def cancel_pending_event(numero_whatsapp, event_id=None):
        """
        Cancela um evento pendente.

        Args:
            numero_whatsapp: Número do usuário
            event_id: ID do evento (se None, cancela o mais recente)

        Returns:
            (sucesso: bool, mensagem: str)
        """
        # Se não tem event_id, buscar o mais recente
        if not event_id:
            event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_whatsapp)
        else:
            event_data = EventConfirmationService.get_pending_event(numero_whatsapp, event_id)

        if not event_data:
            return False, "❌ Nenhum evento pendente encontrado."

        # Remover do Redis
        EventConfirmationService.delete_pending_event(numero_whatsapp, event_id)
        print(f"[EVENT-CONFIRM] ❌ Evento {event_id} cancelado")

        return True, "✅ Evento cancelado com sucesso!"

    @staticmethod
    def format_confirmation_message(event_data):
        """
        Formata mensagem de confirmação para enviar ao usuário.

        Args:
            event_data: Dict com dados do evento

        Returns:
            str: Mensagem formatada
        """
        titulo = event_data['titulo']
        data_str = event_data['data_evento']
        hora_inicio = event_data.get('hora_inicio')
        hora_fim = event_data.get('hora_fim')
        descricao = event_data.get('descricao')
        localizacao = event_data.get('localizacao')

        # Formatar data
        if isinstance(data_str, str):
            try:
                data_obj = date.fromisoformat(data_str)
                data_formatada = data_obj.strftime('%d/%m/%Y')
            except:
                data_formatada = data_str
        else:
            data_formatada = data_str.strftime('%d/%m/%Y')

        # Montar mensagem
        msg = f"📅 *Confirmar Evento?*\n\n"
        msg += f"📌 *{titulo}*\n"
        msg += f"📆 Data: {data_formatada}\n"

        if hora_inicio:
            if hora_fim:
                msg += f"⏰ Horário: {hora_inicio} - {hora_fim}\n"
            else:
                msg += f"⏰ Horário: {hora_inicio}\n"
        else:
            msg += f"⏰ Dia inteiro\n"

        if localizacao:
            msg += f"📍 Local: {localizacao}\n"

        if descricao:
            msg += f"📝 Descrição: {descricao}\n"

        msg += f"\n✅ Responda *'sim'* ou *'confirmar'* para criar"
        msg += f"\n❌ Responda *'não'* ou *'cancelar'* para desistir"

        # Adicionar pergunta sobre tempo de deslocamento (só se tiver localização)
        if localizacao:
            msg += f"\n\n🚗 *Deseja calcular tempo de deslocamento?*"
            msg += f"\n   Responda *'sim, calcular'* para incluir tempo de viagem"

        return msg
