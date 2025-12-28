# app/routes/webhooks/intents/__init__.py
"""
Intent Registry e Factory para roteamento de mensagens WhatsApp.

Este módulo implementa o Factory Pattern para criar e executar
intent handlers baseado na classificação do Gemini AI.

Arquitetura:
1. Gemini classifica intent da mensagem
2. Registry mapeia intent name → handler class
3. Factory cria instância do handler
4. Handler executa e retorna resposta

Uso:
    from app.routes.webhooks.intents import route_intent

    result = route_intent(
        intent_name="Consulta Saldo",
        usuario_id=123,
        mensagem="quanto tenho no nubank?",
        conn=connection
    )

    if result["success"]:
        send_whatsapp(result["message"])
"""

from typing import Dict, Any, Type, Optional
from sqlalchemy.engine import Connection
import logging

from .base_intent import BaseIntent

logger = logging.getLogger(__name__)


# =============================================================================
# INTENT REGISTRY
# =============================================================================
# Mapeamento: Intent Name (retornado pelo Gemini) → Handler Class
#
# Intents implementados são importados e registrados aqui.
# =============================================================================

# Import dos intents implementados
from .query_intents import (
    ConsultaSaldoIntent,
    ConsultaReservaIntent,
    ConsultaPotesIntent,
)
from .transaction_intents import (
    RendaIntent,
    DespesaIntent,
    TransferenciaIntent,
    PagamentoFaturaIntent,
)
from .calendar_intents import (
    CriarEventoIntent,
    DeletarEventoIntent,
    ConsultarAgendaIntent,
    HorariosLivresIntent,
)
from .notification_intents import (
    ConfigurarNotificacoesIntent,
    VencimentosHojeIntent,
    VencimentosAmanhaIntent,
    VencimentosSemanaIntent,
    ContasAtrasadasIntent,
)
from .analytics_intents import (
    AnaliseInteligenteIntent,
    ComparacaoMensalIntent,
    PrevisaoGastosIntent,
    GraficoGastosIntent,
    ConsultaPeriodoIntent,
    ConsultaCategoriaIntent,
)
from .admin_intents import (
    SolicitarApiKeyIntent,
    ConfigurarLocalizacaoIntent,
    ConfigurarRelatorioMensalIntent,
    ListarContasIntent,
    AjustarSaldoIntent,
    ConsultaContasFixasIntent,
    ConsultaFaturaIntent,
)

INTENT_REGISTRY: Dict[str, Type[BaseIntent]] = {
    # Transaction Intents (Implementados)
    'Renda': RendaIntent,
    'Despesa': DespesaIntent,
    'Transferência': TransferenciaIntent,
    'Pagamento Fatura': PagamentoFaturaIntent,

    # Query Intents (Implementados)
    'Consulta Reserva': ConsultaReservaIntent,
    'Consulta Saldo': ConsultaSaldoIntent,
    'Consulta Potes': ConsultaPotesIntent,
    'Consulta Período': ConsultaPeriodoIntent,
    'Consulta Categoria Específica': ConsultaCategoriaIntent,
    'Consulta Contas Fixas': ConsultaContasFixasIntent,
    'Consulta Valor Fatura': ConsultaFaturaIntent,
    'Listar Contas': ListarContasIntent,
    'Ajustar Saldo Inicial': AjustarSaldoIntent,

    # Calendar Intents (Implementados como placeholders)
    'Criar Evento': CriarEventoIntent,
    'Deletar Evento': DeletarEventoIntent,
    'Consultar Agenda': ConsultarAgendaIntent,
    'Horários Livres': HorariosLivresIntent,

    # Notification Intents (Implementados)
    'Configurar Notificações': ConfigurarNotificacoesIntent,
    'Vencimentos Hoje': VencimentosHojeIntent,
    'Vencimentos Amanhã': VencimentosAmanhaIntent,
    'Vencimentos Essa Semana': VencimentosSemanaIntent,
    'Contas Atrasadas': ContasAtrasadasIntent,

    # Analytics Intents (Implementados como placeholders)
    'Análise Inteligente': AnaliseInteligenteIntent,
    'Comparação Mensal': ComparacaoMensalIntent,
    'Previsão de Gastos': PrevisaoGastosIntent,
    'Gráfico de Gastos': GraficoGastosIntent,

    # Admin Intents (Implementados)
    'Solicitar API Key': SolicitarApiKeyIntent,
    'Configurar Localização': ConfigurarLocalizacaoIntent,
    'Configurar Relatório Mensal': ConfigurarRelatorioMensalIntent,
}


# =============================================================================
# INTENT FACTORY
# =============================================================================

def route_intent(
    intent_name: str,
    usuario_id: int,
    mensagem: str,
    conn: Connection,
    numero_whatsapp: Optional[str] = None
) -> Dict[str, Any]:
    """
    Factory Pattern - Cria e executa o intent handler apropriado.

    Args:
        intent_name: Nome do intent retornado pelo Gemini
        usuario_id: ID do usuário
        mensagem: Texto original da mensagem
        conn: Conexão ativa com banco de dados
        numero_whatsapp: Número WhatsApp do usuário (opcional)

    Returns:
        {
            "success": bool,
            "message": str,  # Mensagem formatada para envio
            "data": Optional[Dict]  # Dados adicionais
        }

    Exemplo:
        >>> result = route_intent("Consulta Saldo", 123, "saldo?", conn)
        >>> print(result["message"])
        💰 Saldo Nubank: R$ 1.234,56
    """
    logger.info(
        f"Roteando intent '{intent_name}' para usuario_id={usuario_id}"
    )

    # Buscar handler class no registry
    handler_class = INTENT_REGISTRY.get(intent_name)

    if not handler_class:
        logger.warning(
            f"Intent '{intent_name}' não encontrado no registry. "
            f"Intents disponíveis: {list(INTENT_REGISTRY.keys())}"
        )
        return {
            "success": False,
            "message": (
                "❓ Desculpe, não entendi sua mensagem.\n\n"
                "Você pode tentar:\n"
                "• Consultar seu saldo\n"
                "• Registrar uma despesa ou renda\n"
                "• Ver sua agenda\n"
                "• Configurar notificações"
            ),
            "data": {"unknown_intent": intent_name}
        }

    # Criar instância do handler
    try:
        handler = handler_class(
            usuario_id=usuario_id,
            mensagem=mensagem,
            conn=conn,
            numero_whatsapp=numero_whatsapp
        )
    except Exception as e:
        logger.error(
            f"Erro ao criar handler para intent '{intent_name}': {e}",
            exc_info=True
        )
        return {
            "success": False,
            "message": "❌ Erro interno ao processar sua mensagem.",
            "data": None
        }

    # Executar handler (Template Method)
    result = handler.handle()

    logger.info(
        f"Intent '{intent_name}' processado. Success={result['success']}"
    )

    return result


def register_intent(intent_name: str, handler_class: Type[BaseIntent]) -> None:
    """
    Registra um novo intent handler no registry.

    Útil para plugins ou intents dinâmicos.

    Args:
        intent_name: Nome do intent (deve bater com classificação do Gemini)
        handler_class: Classe que herda de BaseIntent

    Exemplo:
        >>> register_intent("Meu Intent Custom", MeuCustomIntent)
    """
    if not issubclass(handler_class, BaseIntent):
        raise TypeError(
            f"Handler class deve herdar de BaseIntent, "
            f"recebeu {handler_class}"
        )

    if intent_name in INTENT_REGISTRY:
        logger.warning(
            f"Intent '{intent_name}' já existe no registry. "
            f"Sobrescrevendo {INTENT_REGISTRY[intent_name]} com {handler_class}"
        )

    INTENT_REGISTRY[intent_name] = handler_class
    logger.info(f"Intent '{intent_name}' registrado: {handler_class}")


def list_registered_intents() -> list:
    """
    Lista todos os intents registrados.

    Returns:
        Lista de nomes de intents disponíveis

    Exemplo:
        >>> intents = list_registered_intents()
        >>> print(f"{len(intents)} intents disponíveis")
    """
    return list(INTENT_REGISTRY.keys())


__all__ = [
    'BaseIntent',
    'INTENT_REGISTRY',
    'route_intent',
    'register_intent',
    'list_registered_intents',
]
