# app/routes/webhooks/intents/calendar_intents.py
"""
Intent handlers para operações de calendário Google.

Implementa integração com Google Calendar API para:
- Criar eventos
- Deletar eventos
- Consultar agenda
- Verificar horários livres
"""

from typing import Dict, Any, Optional
from datetime import datetime, date, timedelta
from zoneinfo import ZoneInfo
from .base_intent import BaseIntent, ConfirmationRequiredIntent
from app.services import gemini_service


class CriarEventoIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Criar Evento'.

    Cria um evento pendente no Redis e aguarda confirmação do usuário.
    O evento só é criado no Google Calendar após confirmação.

    Exemplo de mensagem:
    - "Criar reunião amanhã às 14h"
    - "Agendar dentista dia 25 às 10h30"
    - "Criar evento academia amanhã 7h"
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros do evento usando Gemini."""
        event_data = gemini_service.extract_event_creation_details(self.mensagem, self.usuario_id)

        return {
            "titulo": event_data.get('titulo'),
            "data_str": event_data.get('data'),
            "hora_inicio": event_data.get('hora_inicio'),
            "hora_fim": event_data.get('hora_fim'),
            "descricao": event_data.get('descricao'),
            "localizacao": event_data.get('localizacao')
        }

    def validate(self) -> Optional[str]:
        """Valida parâmetros do evento."""
        if not self.params.get("titulo") or not self.params.get("data_str"):
            return "❌ Não consegui identificar o título ou data do evento. Tente algo como: 'Criar evento Academia amanhã às 7h'"
        return None

    def execute(self) -> Dict[str, Any]:
        """Cria evento pendente no Redis para confirmação."""
        from app.services.event_confirmation_service import EventConfirmationService

        # Processar data
        hoje_br = datetime.now(self.TIMEZONE_BR).date()
        data_str = self.params["data_str"]

        if data_str == 'hoje':
            data_evento = hoje_br
        elif data_str == 'amanha':
            data_evento = hoje_br + timedelta(days=1)
        else:
            try:
                data_evento = date.fromisoformat(data_str)
            except:
                return {"erro": True, "mensagem": f"❌ Data inválida: {data_str}"}

        # Preparar dados do evento
        event_data = {
            "usuario_id": self.usuario_id,
            "titulo": self.params["titulo"],
            "data_evento": data_evento.isoformat(),
            "hora_inicio": self.params["hora_inicio"],
            "hora_fim": self.params["hora_fim"],
            "descricao": self.params["descricao"],
            "localizacao": self.params["localizacao"]
        }

        # Criar evento pendente no Redis
        event_id = EventConfirmationService.create_pending_event(self.numero_whatsapp, event_data)

        if event_id:
            return {
                "erro": False,
                "event_data": event_data,
                "event_id": event_id
            }
        else:
            return {"erro": True, "mensagem": "❌ Erro ao processar evento. Tente novamente."}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if data.get("erro"):
            return data.get("mensagem", "❌ Erro ao processar evento.")

        from app.services.event_confirmation_service import EventConfirmationService
        return EventConfirmationService.format_confirmation_message(data["event_data"])


class DeletarEventoIntent(BaseIntent):
    """
    Handler para intent 'Deletar Evento'.

    Busca e deleta eventos do Google Calendar por título.
    Se encontrar múltiplos eventos, pede confirmação.

    Exemplo de mensagem:
    - "Cancelar reunião de amanhã"
    - "Deletar evento dentista"
    - "Deletar academia de hoje"
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros do evento a deletar."""
        delete_data = gemini_service.extract_event_deletion_query(self.mensagem, self.usuario_id)

        return {
            "titulo_busca": delete_data.get('titulo_busca'),
            "quando": delete_data.get('quando')
        }

    def validate(self) -> Optional[str]:
        """Valida parâmetros."""
        if not self.params.get("titulo_busca"):
            return "❌ Não consegui identificar qual evento deletar. Tente algo como: 'Deletar academia de hoje'"
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca e deleta evento do calendário."""
        from app.services.calendar_management_service import CalendarManagementService

        titulo_busca = self.params["titulo_busca"]
        quando = self.params.get("quando")

        # Buscar eventos
        eventos_encontrados = CalendarManagementService.find_events_by_title(
            self.usuario_id, titulo_busca, max_results=5
        )

        # Filtrar por quando se fornecido
        if quando and eventos_encontrados:
            hoje_br = datetime.now(self.TIMEZONE_BR).date()
            data_alvo = hoje_br if quando == 'hoje' else hoje_br + timedelta(days=1)
            eventos_encontrados = [
                e for e in eventos_encontrados
                if date.fromisoformat(e['start'].split('T')[0]) == data_alvo
            ]

        if not eventos_encontrados:
            return {
                "tipo": "nao_encontrado",
                "titulo_busca": titulo_busca,
                "quando": quando
            }

        if len(eventos_encontrados) == 1:
            # Deletar automaticamente
            evento = eventos_encontrados[0]
            sucesso, mensagem = CalendarManagementService.delete_event(
                self.usuario_id,
                evento['id'],
                evento['calendar_id']
            )
            return {
                "tipo": "deletado",
                "sucesso": sucesso,
                "mensagem": mensagem
            }
        else:
            # Múltiplos eventos
            return {
                "tipo": "multiplos",
                "eventos": eventos_encontrados
            }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta baseada no resultado."""
        tipo = data.get("tipo")

        if tipo == "nao_encontrado":
            msg = f"🤔 Não encontrei eventos com '{data['titulo_busca']}'"
            if data.get("quando"):
                msg += f" para {data['quando']}"
            msg += ".\n\nTente buscar com outras palavras."
            return msg

        elif tipo == "deletado":
            if data["sucesso"]:
                return f"✅ {data['mensagem']}"
            else:
                return f"❌ {data['mensagem']}"

        elif tipo == "multiplos":
            eventos = data["eventos"]
            msg = f"📋 Encontrei {len(eventos)} eventos:\n\n"

            for idx, evento in enumerate(eventos, 1):
                if 'T' in evento['start']:
                    data_evento = datetime.fromisoformat(evento['start']).strftime('%d/%m às %H:%M')
                else:
                    data_evento = date.fromisoformat(evento['start']).strftime('%d/%m')

                msg += f"{idx}. *{evento['summary']}*\n"
                msg += f"   📅 {data_evento}\n"
                msg += f"   📂 {evento['calendar_name']}\n"
                msg += f"   _ID: {evento['id']}_\n\n"

            msg += "Para deletar um específico, envie:\n"
            msg += f"'Deletar evento {eventos[0]['id']}'"
            return msg

        return "❌ Erro ao processar deleção."


class ConsultarAgendaIntent(BaseIntent):
    """
    Handler para intent 'Consultar Agenda'.

    Consulta eventos do Google Calendar para um período.
    Suporta filtro de horário (manhã, tarde, noite).

    Exemplo de mensagem:
    - "Qual minha agenda de hoje?"
    - "O que tenho amanhã?"
    - "Compromissos da semana"
    - "O que tenho de manhã amanhã?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de período e filtro de horário."""
        # Extrair período
        calendar_data = gemini_service.extract_calendar_query(self.mensagem, self.usuario_id)
        period_type = calendar_data.get('period_type', 'hoje')

        # Extrair filtro de horário (opcional)
        time_data = gemini_service.extract_time_filter_query(self.mensagem, self.usuario_id)
        time_filter = time_data.get('time_filter')

        return {
            "period_type": period_type,
            "time_filter": time_filter
        }

    def validate(self) -> Optional[str]:
        """Sem validação necessária - fallback para 'hoje'."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca eventos do calendário."""
        from app.services.calendar_query_service import CalendarQueryService

        period_type = self.params["period_type"]
        time_filter = self.params.get("time_filter")

        if time_filter:
            mensagem = CalendarQueryService.query_agenda_with_time_filter(
                self.usuario_id, period_type, time_filter
            )
        else:
            mensagem = CalendarQueryService.query_agenda(self.usuario_id, period_type)

        return {"mensagem_formatada": mensagem}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna a mensagem já formatada pelo serviço."""
        return data.get("mensagem_formatada", "❌ Erro ao consultar agenda.")


class HorariosLivresIntent(BaseIntent):
    """
    Handler para intent 'Horários Livres'.

    Verifica horários livres no calendário considerando eventos e
    horários de trabalho do usuário.

    Exemplo de mensagem:
    - "Tenho horário livre amanhã?"
    - "Quando estou livre essa semana?"
    - "Quero 2 horas livres para estudar"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de período, duração e contexto."""
        free_time_data = gemini_service.extract_free_time_query(self.mensagem, self.usuario_id)

        return {
            "period_type": free_time_data.get('period_type', 'hoje'),
            "duracao_minutos": free_time_data.get('duracao_minutos', 60),
            "contexto": free_time_data.get('contexto')
        }

    def validate(self) -> Optional[str]:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca horários livres."""
        from app.services.free_time_finder_service import FreeTimeFinderService
        from app import db_engine

        period_type = self.params["period_type"]
        duracao_minutos = self.params["duracao_minutos"]
        contexto = self.params.get("contexto")

        # Buscar horários livres
        result = FreeTimeFinderService.find_free_slots(
            db_engine, self.usuario_id, period_type, duracao_minutos
        )

        return {
            "result": result,
            "contexto": contexto
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem com horários livres e sugestão de IA."""
        from app.services.free_time_finder_service import FreeTimeFinderService

        result = data["result"]
        contexto = data.get("contexto")

        # Formatar mensagem principal
        mensagem = FreeTimeFinderService.format_free_slots_message(result, contexto)

        # Adicionar sugestão da IA se houver contexto e slots disponíveis
        if contexto and result.get("slots_livres"):
            sugestao_ai = FreeTimeFinderService.suggest_best_slot_with_ai(
                result, contexto, result.get("insights_usuario", "")
            )
            if sugestao_ai:
                mensagem += f"\n\n{sugestao_ai}"

        return mensagem


__all__ = [
    'CriarEventoIntent',
    'DeletarEventoIntent',
    'ConsultarAgendaIntent',
    'HorariosLivresIntent',
]
