# app/routes/webhooks/intents/admin_intents.py
"""
Intent handlers para operações administrativas e configurações.

Permite usuários gerenciarem:
- API keys
- Configurações de localização
- Relatórios mensais automáticos
- Outras preferências de conta

TODO: Implementar lógica completa quando admin_service estiver pronto.
"""

from typing import Dict, Any
from .base_intent import BaseIntent


class SolicitarApiKeyIntent(BaseIntent):
    """
    Handler para intent 'Solicitar API Key'.

    Gera ou recupera a API key do usuário.

    Exemplo de mensagem:
    - "Qual minha API key?"
    - "Gerar nova API key"
    - "Esqueci minha chave de API"

    SEGURANÇA: API key só deve ser enviada via WhatsApp se usuário estiver
    autenticado e número verificado.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da solicitação."""
        # TODO: Implementar detecção de intent com gemini_service
        return {
            "acao": "recuperar",  # recuperar, gerar_nova, revogar
        }

    def validate(self) -> str | None:
        """Valida ação."""
        acoes_validas = ["recuperar", "gerar_nova", "revogar"]
        if self.params.get("acao") not in acoes_validas:
            return f"❌ Ação inválida. Use: {', '.join(acoes_validas)}"
        return None

    def execute(self) -> Dict[str, Any]:
        """Processa solicitação de API key."""
        # TODO: Implementar via admin_service ou user_service
        # if self.params["acao"] == "recuperar":
        #     api_key = user_service.get_user_api_key(self.conn, self.usuario_id)
        # elif self.params["acao"] == "gerar_nova":
        #     api_key = user_service.generate_new_api_key(self.conn, self.usuario_id)
        # elif self.params["acao"] == "revogar":
        #     user_service.revoke_api_key(self.conn, self.usuario_id)
        #     return {"revogada": True}

        raise NotImplementedError(
            "SolicitarApiKeyIntent ainda não implementado. "
            "Aguardando admin_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta com API key."""
        if data.get("revogada"):
            return "🔐 API key revogada com sucesso!"

        api_key = data.get("api_key", "***")

        msg = "🔑 *Sua API Key*\n\n"
        msg += f"`{api_key}`\n\n"
        msg += "⚠️ *IMPORTANTE:*\n"
        msg += "• Não compartilhe esta chave com ninguém\n"
        msg += "• Use apenas em aplicações confiáveis\n"
        msg += "• Revogue se suspeitar de comprometimento"

        return msg


class ConfigurarLocalizacaoIntent(BaseIntent):
    """
    Handler para intent 'Configurar Localização'.

    Configura timezone e localização do usuário para:
    - Agendamentos corretos
    - Formatação de datas
    - Notificações no horário local

    Exemplo de mensagem:
    - "Configurar fuso horário"
    - "Estou em São Paulo"
    - "Alterar localização para Brasília"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de localização."""
        # TODO: Implementar extração com gemini_service
        return {
            "cidade": None,
            "timezone": None,  # Ex: "America/Sao_Paulo"
            "pais": "Brasil",
        }

    def validate(self) -> str | None:
        """Valida localização."""
        if not self.params.get("cidade") and not self.params.get("timezone"):
            return (
                "❌ Não consegui identificar a localização.\n\n"
                "Você pode dizer:\n"
                "• Estou em São Paulo\n"
                "• Configurar fuso horário Brasília\n"
                "• Timezone America/Sao_Paulo"
            )
        return None

    def execute(self) -> Dict[str, Any]:
        """Configura localização do usuário."""
        # TODO: Implementar via user_service ou admin_service
        # 1. Resolver cidade para timezone (se necessário)
        # if self.params.get("cidade"):
        #     timezone = location_service.cidade_para_timezone(self.params["cidade"])
        #
        # 2. Atualizar configuração do usuário
        # user_service.update_user_timezone(
        #     conn=self.conn,
        #     usuario_id=self.usuario_id,
        #     timezone=timezone
        # )

        raise NotImplementedError(
            "ConfigurarLocalizacaoIntent ainda não implementado. "
            "Aguardando admin_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata confirmação."""
        msg = "🌎 *Localização Configurada*\n\n"
        msg += f"📍 Cidade: {data.get('cidade', 'Não especificada')}\n"
        msg += f"🕐 Timezone: {data.get('timezone', 'UTC')}\n\n"
        msg += "✅ Seus agendamentos e notificações agora usarão este horário!"

        return msg


class ConfigurarRelatorioMensalIntent(BaseIntent):
    """
    Handler para intent 'Configurar Relatório Mensal'.

    Configura envio automático de relatórios mensais via WhatsApp.

    Exemplo de mensagem:
    - "Quero receber relatório mensal"
    - "Enviar resumo financeiro todo mês"
    - "Desativar relatório automático"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de configuração."""
        # TODO: Implementar extração com gemini_service
        return {
            "ativo": True,
            "dia_envio": 1,      # Dia do mês (1-28)
            "horario": "09:00",  # Horário de envio
            "detalhes": True,    # Incluir detalhes ou apenas resumo
        }

    def validate(self) -> str | None:
        """Valida configuração."""
        dia = self.params.get("dia_envio", 1)
        if not isinstance(dia, int) or dia < 1 or dia > 28:
            return "❌ Dia inválido. Use um dia entre 1 e 28."
        return None

    def execute(self) -> Dict[str, Any]:
        """Configura relatório mensal."""
        # TODO: Implementar via notification_service
        # notification_service.configure_monthly_report(
        #     usuario_id=self.usuario_id,
        #     ativo=self.params["ativo"],
        #     dia_envio=self.params["dia_envio"],
        #     horario=self.params["horario"],
        #     detalhes=self.params["detalhes"]
        # )

        raise NotImplementedError(
            "ConfigurarRelatorioMensalIntent ainda não implementado. "
            "Aguardando notification_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata confirmação."""
        if not data.get("ativo"):
            return "🔕 Relatório mensal desativado!"

        msg = "📊 *Relatório Mensal Configurado*\n\n"
        msg += f"📅 Dia: {data['dia_envio']} de cada mês\n"
        msg += f"🕐 Horário: {data['horario']}\n"
        msg += f"📋 Tipo: {'Detalhado' if data.get('detalhes') else 'Resumido'}\n\n"
        msg += "✅ Você receberá seu relatório automaticamente!"

        return msg


class ListarContasIntent(BaseIntent):
    """
    Handler para intent 'Listar Contas'.

    Lista todas as contas financeiras do usuário.

    Exemplo de mensagem:
    - "Quais minhas contas?"
    - "Listar contas cadastradas"
    - "Mostrar minhas contas bancárias"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> str | None:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca contas do usuário."""
        # TODO: Implementar via finance_service
        # contas = finance_service.get_user_accounts(self.conn, self.usuario_id)
        # return {"contas": contas}

        raise NotImplementedError(
            "ListarContasIntent ainda não implementado. "
            "Aguardando implementação em finance_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de contas."""
        contas = data.get("contas", [])

        if not contas:
            return "❌ Você não tem contas cadastradas ainda."

        msg = "🏦 *Suas Contas*\n\n"

        for conta in contas:
            tipo_emoji = {
                "corrente": "💳",
                "poupanca": "🏦",
                "investimento": "📈",
                "credito": "💳"
            }.get(conta.get("tipo", ""), "💰")

            msg += f"{tipo_emoji} *{conta['nome_conta']}*\n"
            msg += f"   Saldo: {conta['saldo_formatado']}\n"

            if conta.get("tipo") == "credito":
                msg += f"   Limite: {conta.get('limite_formatado', 'N/A')}\n"

            msg += "\n"

        return msg


class AjustarSaldoIntent(BaseIntent):
    """
    Handler para intent 'Ajustar Saldo Inicial'.

    Permite ajustar o saldo inicial de uma conta.

    Exemplo de mensagem:
    - "Ajustar saldo do Nubank para 1500"
    - "Corrigir saldo inicial da poupança"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai conta e novo saldo."""
        # TODO: Implementar extração com gemini_service
        return {
            "conta_nome": None,
            "novo_saldo": None,
        }

    def validate(self) -> str | None:
        """Valida parâmetros."""
        if not self.params.get("conta_nome"):
            return "❌ Não consegui identificar a conta. Especifique qual conta quer ajustar."

        if self.params.get("novo_saldo") is None:
            return "❌ Não consegui identificar o novo saldo. Especifique o valor."

        return None

    def execute(self) -> Dict[str, Any]:
        """Ajusta saldo da conta."""
        # TODO: Implementar via finance_service
        # finance_service.adjust_account_balance(
        #     conn=self.conn,
        #     usuario_id=self.usuario_id,
        #     conta_nome=self.params["conta_nome"],
        #     novo_saldo=self.params["novo_saldo"]
        # )

        raise NotImplementedError(
            "AjustarSaldoIntent ainda não implementado. "
            "Aguardando implementação em finance_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata confirmação."""
        msg = "✅ *Saldo Ajustado*\n\n"
        msg += f"🏦 Conta: {data['conta_nome']}\n"
        msg += f"💰 Saldo anterior: {data['saldo_anterior']}\n"
        msg += f"💵 Novo saldo: {data['novo_saldo']}"

        return msg


class ConsultaContasFixasIntent(BaseIntent):
    """
    Handler para intent 'Consulta Contas Fixas'.

    Lista agendamentos/contas fixas do usuário.

    Exemplo de mensagem:
    - "Minhas contas fixas"
    - "Quais agendamentos tenho?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca agendamentos."""
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "ConsultaContasFixasIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de contas fixas."""
        agendamentos = data.get("agendamentos", [])

        msg = "📋 *Contas Fixas/Agendamentos*\n\n"

        for ag in agendamentos:
            msg += f"• {ag['descricao']}: {ag['valor']}\n"
            msg += f"  {ag['periodicidade']} - Dia {ag['dia_execucao']}\n\n"

        return msg


class ConsultaFaturaIntent(BaseIntent):
    """
    Handler para intent 'Consulta Valor Fatura'.

    Consulta valor da fatura do cartão de crédito.

    Exemplo de mensagem:
    - "Quanto tá a fatura do Nubank?"
    - "Valor da fatura esse mês"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai conta/cartão."""
        # TODO: Implementar extração com gemini_service
        return {
            "conta_nome": None,
            "mes_referencia": None,  # None = mês atual
        }

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca valor da fatura."""
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "ConsultaFaturaIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata valor da fatura."""
        msg = f"💳 *Fatura {data['conta_nome']}*\n\n"
        msg += f"Período: {data['periodo']}\n"
        msg += f"Valor: {data['valor_fatura']}\n"
        msg += f"Vencimento: {data['data_vencimento']}"

        return msg


__all__ = [
    'SolicitarApiKeyIntent',
    'ConfigurarLocalizacaoIntent',
    'ConfigurarRelatorioMensalIntent',
    'ListarContasIntent',
    'AjustarSaldoIntent',
    'ConsultaContasFixasIntent',
    'ConsultaFaturaIntent',
]
