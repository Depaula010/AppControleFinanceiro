# app/routes/webhooks/intents/admin_intents.py
"""
Intent handlers para operações administrativas e configurações.

Permite usuários gerenciarem:
- API keys
- Configurações de localização
- Relatórios mensais automáticos
- Outras preferências de conta
"""

from typing import Dict, Any, Optional
from collections import defaultdict
import re
from sqlalchemy import text
from .base_intent import BaseIntent
from app.services import finance_service, gemini_service
from app.shared.formatters.currency_formatter import formatar_moeda


class SolicitarApiKeyIntent(BaseIntent):
    """
    Handler para intent 'Solicitar API Key'.

    Recupera a API key do usuário para uso em integrações.

    Exemplo de mensagem:
    - "Qual minha API key?"
    - "Minha chave de API"
    - "API key"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários - apenas recupera a key."""
        return {}

    def validate(self) -> Optional[str]:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca API key do usuário no banco."""
        sql_api_key = text("SELECT api_key_automate, nome FROM Usuarios WHERE id = :uid")
        result = self.conn.execute(sql_api_key, {"uid": self.usuario_id}).fetchone()

        if result and result[0]:
            return {
                "api_key": result[0],
                "nome_usuario": result[1],
                "encontrada": True
            }
        else:
            return {"encontrada": False}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta com API key e instruções."""
        if not data.get("encontrada"):
            return "❌ Não encontrei sua API Key. Entre em contato com o suporte."

        api_key = data["api_key"]
        nome = data.get("nome_usuario", "")

        msg = f"🔑 *Sua API Key*\n\n"
        if nome:
            msg += f"Olá {nome}!\n\n"
        msg += f"Sua chave de acesso:\n"
        msg += f"`{api_key}`\n\n"
        msg += f"⚠️ *Importante:*\n"
        msg += f"• Não compartilhe esta chave com ninguém\n"
        msg += f"• Use-a para configurar automações no iPhone\n"
        msg += f"• Esta chave dá acesso total à sua conta\n\n"
        msg += f"📱 *Para usar no iPhone:*\n"
        msg += f"1. Copie a chave acima\n"
        msg += f"2. No atalho, cole no campo `user_api_key`\n"
        msg += f"3. Teste enviando um gasto!\n\n"
        msg += f"💡 *Endpoint:*\n"
        msg += f"`POST /api/transacao`"

        return msg


class ConfigurarLocalizacaoIntent(BaseIntent):
    """
    Handler para intent 'Configurar Localização'.

    Configura cidade e estado do usuário para informações de clima.

    Exemplo de mensagem:
    - "Configurar localização São Paulo, SP"
    - "Estou em Rio de Janeiro"
    - "Minha cidade é Belo Horizonte"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai cidade e estado da mensagem."""
        from app.services.gemini_service import extract_location_config

        location_data = extract_location_config(self.mensagem, self.usuario_id)
        return {
            "cidade": location_data.get('cidade'),
            "estado": location_data.get('estado')
        }

    def validate(self) -> Optional[str]:
        """Valida se cidade foi identificada."""
        if not self.params.get("cidade"):
            return ("❌ Não consegui identificar a cidade.\n\n"
                    "Por favor, envie no formato:\n"
                    '"Configurar localização: São Paulo, SP"')
        return None

    def execute(self) -> Dict[str, Any]:
        """Atualiza localização do usuário no banco."""
        from app.services.location_service import LocationService

        sucesso, mensagem = LocationService.update_user_location(
            self.usuario_id,
            self.params["cidade"],
            self.params.get("estado")
        )

        return {
            "sucesso": sucesso,
            "mensagem": mensagem,
            "cidade": self.params["cidade"],
            "estado": self.params.get("estado")
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata confirmação."""
        if data.get("sucesso"):
            msg = f"✅ {data['mensagem']}\n\n"
            msg += "Agora você receberá informações de clima nos resumos matinais!"
            return msg
        else:
            return f"❌ {data['mensagem']}"


class ConfigurarRelatorioMensalIntent(BaseIntent):
    """
    Handler para intent 'Configurar Relatório Mensal'.

    Configura envio automático de relatórios mensais via WhatsApp.
    Suporta ações: consultar, ativar, desativar, configurar.

    Exemplo de mensagem:
    - "Quero receber relatório mensal"
    - "Desativar relatório mensal"
    - "Configurar relatório mensal às 10h"
    - "Como está configurado meu relatório?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de configuração via Gemini."""
        config_info = gemini_service.extract_monthly_report_config(self.mensagem, self.usuario_id)
        return {
            "acao": config_info.get('acao'),
            "momento_envio": config_info.get('momento_envio'),
            "hora_envio": config_info.get('hora_envio')
        }

    def validate(self) -> Optional[str]:
        """Sem validação especial - ação inválida é tratada no execute."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Executa ação de configuração do relatório."""
        from app.services.monthly_report_config_service import (
            get_or_create_config,
            update_config,
            desativar_config
        )

        acao = self.params.get('acao')
        momento_envio = self.params.get('momento_envio')
        hora_envio = self.params.get('hora_envio')

        # Buscar configuração atual
        config_atual = get_or_create_config(self.usuario_id)

        if acao == 'consultar':
            return {
                "tipo": "consultar",
                "config": config_atual
            }

        elif acao == 'desativar':
            desativar_config(self.usuario_id)
            return {"tipo": "desativar"}

        elif acao == 'ativar':
            params = {'ativo': True}
            if momento_envio:
                params['momento_envio'] = momento_envio
            if hora_envio:
                params['hora_envio'] = hora_envio

            config_nova = update_config(self.usuario_id, **params)
            return {
                "tipo": "ativar",
                "config": config_nova
            }

        elif acao == 'configurar':
            params = {}
            if momento_envio:
                params['momento_envio'] = momento_envio
            if hora_envio:
                params['hora_envio'] = hora_envio

            if not params:
                return {"tipo": "erro_config"}

            config_nova = update_config(self.usuario_id, **params)
            return {
                "tipo": "configurar",
                "config": config_nova
            }

        else:
            return {"tipo": "erro_acao"}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta baseada no tipo de ação."""
        tipo = data.get("tipo")

        if tipo == "consultar":
            config = data["config"]
            status = "✅ Ativo" if config['ativo'] else "❌ Desativado"
            momento = "Início do mês (dia 1)" if config['momento_envio'] == 'INICIO_MES' else "Fim do mês (último dia)"
            hora = config['hora_envio'].strftime('%H:%M') if config['hora_envio'] else "08:00"

            msg = "📊 *CONFIGURAÇÃO DO RELATÓRIO MENSAL*\n\n"
            msg += f"Status: {status}\n"
            msg += f"Momento: {momento}\n"
            msg += f"Horário: {hora}\n\n"
            msg += "_Para alterar, envie: 'configurar relatório mensal no início do mês às 10h'_"
            return msg

        elif tipo == "desativar":
            return "✅ Relatório mensal desativado com sucesso!\n\n_Para reativar, envie: 'ativar relatório mensal'_"

        elif tipo == "ativar":
            config = data["config"]
            momento_texto = "início do mês (dia 1)" if config['momento_envio'] == 'INICIO_MES' else "fim do mês (último dia)"
            hora_texto = config['hora_envio'].strftime('%H:%M')

            msg = "✅ *Relatório mensal ativado!*\n\n"
            msg += f"📅 Momento: {momento_texto}\n"
            msg += f"🕐 Horário: {hora_texto}\n\n"
            msg += "📊 *O que você vai receber:*\n"
            msg += "• Gastos totais do mês\n"
            msg += "• Top 5 categorias\n"
            msg += "• Comparação com mês anterior\n"
            msg += "• Status dos potes de gastos\n"
            msg += "• Contas pagas vs pendentes\n"
            msg += "• Gráfico de pizza com categorias\n\n"
            msg += "_Você receberá automaticamente no horário configurado!_"
            return msg

        elif tipo == "configurar":
            config = data["config"]
            momento_texto = "início do mês (dia 1)" if config['momento_envio'] == 'INICIO_MES' else "fim do mês (último dia)"
            hora_texto = config['hora_envio'].strftime('%H:%M')

            msg = "✅ *Configuração atualizada!*\n\n"
            msg += f"📅 Momento: {momento_texto}\n"
            msg += f"🕐 Horário: {hora_texto}\n\n"
            msg += "O relatório será enviado automaticamente no horário configurado."
            return msg

        elif tipo == "erro_config":
            msg = "❌ Não entendi o que você quer configurar.\n\n"
            msg += "Exemplos:\n"
            msg += "• 'quero receber no início do mês às 8h'\n"
            msg += "• 'mudar hora do relatório para 14:00'\n"
            msg += "• 'receber no fim do mês'"
            return msg

        else:
            msg = "❌ Não entendi a ação desejada.\n\n"
            msg += "Exemplos:\n"
            msg += "• 'ativar relatório mensal'\n"
            msg += "• 'desativar relatório mensal'\n"
            msg += "• 'configurar relatório mensal às 10h'\n"
            msg += "• 'como está configurado meu relatório?'"
            return msg


class ListarContasIntent(BaseIntent):
    """
    Handler para intent 'Listar Contas'.

    Lista todas as contas financeiras do usuário agrupadas por tipo.

    Exemplo de mensagem:
    - "Quais minhas contas?"
    - "Listar contas cadastradas"
    - "Mostrar minhas contas bancárias"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca contas do usuário."""
        contas_raw = finance_service.get_user_accounts(self.conn, self.usuario_id)
        return {"contas": contas_raw}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de contas agrupadas por tipo."""
        contas_raw = data.get("contas", [])

        if not contas_raw:
            return "❌ Você não tem contas cadastradas."

        msg = "📋 *Suas Contas Cadastradas:*\n\n"

        # Agrupar por tipo
        contas_por_tipo = {}
        for conta in contas_raw:
            conta_id, nome, tipo = conta[0], conta[1], conta[2]
            if tipo not in contas_por_tipo:
                contas_por_tipo[tipo] = []
            contas_por_tipo[tipo].append(nome)

        # Formatar resposta
        for tipo, nomes in contas_por_tipo.items():
            icone = "💳" if tipo == "Cartão de Crédito" else "🏦" if tipo == "Conta Corrente" else "💰"
            msg += f"{icone} *{tipo}*\n"
            for nome in nomes:
                msg += f"   • {nome}\n"
            msg += "\n"

        msg += f"_Total: {len(contas_raw)} conta(s)_"

        return msg


class AjustarSaldoIntent(BaseIntent):
    """
    Handler para intent 'Ajustar Saldo Inicial'.

    Permite ajustar o saldo inicial de uma conta.

    Exemplo de mensagem:
    - "Ajustar saldo inicial Nubank 1500"
    - "Corrigir saldo do banco inter para 5000"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai valor e identifica conta na mensagem."""
        # Extrair valor usando regex
        # Remove pontos de milhar e troca vírgula por ponto
        texto_limpo = self.mensagem.replace('.', '').replace(',', '.')
        match_valor = re.search(r'(\d+(?:\.\d+)?)', texto_limpo)

        valor = None
        if match_valor:
            valor = float(match_valor.group(1))

        # Buscar contas do usuário para identificar na mensagem
        contas_raw = finance_service.get_user_accounts(self.conn, self.usuario_id)
        conta_encontrada = None

        for conta in contas_raw:
            conta_id, nome_conta, tipo_conta = conta[0], conta[1], conta[2]
            # Busca case-insensitive
            if nome_conta.lower() in self.mensagem.lower():
                conta_encontrada = {
                    "id": conta_id,
                    "nome": nome_conta,
                    "tipo": tipo_conta
                }
                break

        return {
            "valor": valor,
            "conta": conta_encontrada,
            "contas_disponiveis": contas_raw
        }

    def validate(self) -> Optional[str]:
        """Valida parâmetros."""
        if self.params.get("valor") is None:
            return "🤔 Não consegui identificar o valor. Exemplo: 'ajustar saldo inicial Banco Inter 5000'"

        if not self.params.get("conta"):
            contas = self.params.get("contas_disponiveis", [])
            msg = "🤔 Não consegui identificar qual conta. Contas disponíveis:\n\n"
            for conta in contas:
                msg += f"• {conta[1]}\n"
            msg += "\nTente: 'ajustar saldo inicial [nome da conta] [valor]'"
            return msg

        return None

    def execute(self) -> Dict[str, Any]:
        """Ajusta saldo inicial da conta."""
        conta = self.params["conta"]
        valor = self.params["valor"]

        sucesso = finance_service.update_saldo_inicial(
            self.conn, self.usuario_id, conta["id"], valor
        )

        return {
            "sucesso": sucesso,
            "conta_nome": conta["nome"],
            "tipo_conta": conta["tipo"],
            "novo_valor": valor
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata confirmação."""
        if not data.get("sucesso"):
            return "❌ Erro ao atualizar o saldo inicial. Tente novamente."

        tipo = data["tipo_conta"]
        icone = "💳" if tipo == "Cartão de Crédito" else "🏦" if tipo == "Conta Corrente" else "💰"

        msg = f"✅ *SALDO INICIAL ATUALIZADO* ✅\n\n"
        msg += f"{icone} *{data['conta_nome']}*\n"
        msg += f"💵 Novo saldo inicial: *{formatar_moeda(data['novo_valor'])}*\n\n"
        msg += f"_O saldo atual já reflete esta mudança._"

        return msg


class ConsultaContasFixasIntent(BaseIntent):
    """
    Handler para intent 'Consulta Contas Fixas'.

    Lista contas fixas/agendamentos pendentes do usuário.

    Exemplo de mensagem:
    - "Minhas contas fixas"
    - "Quais agendamentos tenho?"
    - "Contas fixas pendentes"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca contas fixas pendentes via FixedBillsService."""
        from app.services.fixed_bills_service import FixedBillsService

        # O serviço já retorna a mensagem formatada
        mensagem = FixedBillsService.list_pending_bills_formatted(self.conn, self.usuario_id)
        return {"mensagem_formatada": mensagem}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna a mensagem já formatada pelo serviço."""
        return data.get("mensagem_formatada", "❌ Erro ao buscar contas fixas.")


class ConsultaFaturaIntent(BaseIntent):
    """
    Handler para intent 'Consulta Valor Fatura'.

    Consulta valor da fatura do cartão de crédito (resumido ou detalhado).

    Exemplo de mensagem:
    - "Quanto tá a fatura do Nubank?"
    - "Valor da fatura esse mês"
    - "Detalhar fatura do cartão"
    """

    PALAVRAS_DETALHES = ['detalhe', 'detalhar', 'detalhada', 'itens', 'lista', 'transações', 'transacoes', 'compras']

    def extract_params(self) -> Dict[str, Any]:
        """Extrai cartão e detecta se quer detalhes."""
        # Buscar contas para identificar cartão
        contas_raw = finance_service.get_user_accounts(self.conn, self.usuario_id)
        contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

        # Usar Gemini para extrair nome do cartão
        fatura_query = gemini_service.extract_fatura_query(self.mensagem, contas_list, self.usuario_id)
        nome_cartao = fatura_query.get('conta_cartao')

        # Detectar se quer detalhes
        texto_lower = self.mensagem.lower()
        quer_detalhes = any(palavra in texto_lower for palavra in self.PALAVRAS_DETALHES)

        # Buscar ID do cartão
        conta_id_cartao = None
        if nome_cartao:
            conta_id_cartao = finance_service.get_account_by_name(self.conn, self.usuario_id, nome_cartao)

        return {
            "nome_cartao": nome_cartao,
            "conta_id_cartao": conta_id_cartao,
            "quer_detalhes": quer_detalhes
        }

    def validate(self) -> Optional[str]:
        """Valida se cartão foi encontrado (se especificado)."""
        nome_cartao = self.params.get("nome_cartao")
        conta_id = self.params.get("conta_id_cartao")

        if nome_cartao and not conta_id:
            return f"🤔 Não encontrei um cartão chamado '{nome_cartao}'."

        return None

    def execute(self) -> Dict[str, Any]:
        """Busca fatura (resumida ou detalhada)."""
        conta_id_cartao = self.params.get("conta_id_cartao")
        nome_cartao = self.params.get("nome_cartao")
        quer_detalhes = self.params.get("quer_detalhes", False)

        if quer_detalhes and conta_id_cartao:
            # Buscar fatura detalhada
            fatura_detalhada = finance_service.get_fatura_detalhada(
                self.conn, self.usuario_id, conta_id_cartao
            )
            return {
                "modo": "detalhado",
                "fatura": fatura_detalhada,
                "nome_cartao": nome_cartao
            }
        else:
            # Buscar fatura(s) resumida(s)
            faturas = finance_service.get_fatura_valor(self.conn, self.usuario_id, conta_id_cartao)
            return {
                "modo": "resumido",
                "faturas": faturas,
                "nome_cartao": nome_cartao
            }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta (detalhada ou resumida)."""
        modo = data.get("modo")
        nome_cartao = data.get("nome_cartao")

        if modo == "detalhado":
            return self._format_detalhado(data.get("fatura"), nome_cartao)
        else:
            return self._format_resumido(data.get("faturas", []), nome_cartao)

    def _format_detalhado(self, fatura: Optional[Dict], nome_cartao: str) -> str:
        """Formata fatura detalhada com transações por data."""
        if not fatura:
            if nome_cartao:
                return f"✅ Você não tem faturas em aberto no cartão '{nome_cartao}'! 🎉"
            return "✅ Você não tem nenhuma fatura em aberto! 🎉"

        data_venc = fatura['data_vencimento'].strftime('%d/%m/%Y')
        msg = f"💳 *Fatura {fatura['nome_cartao']}*\n\n"
        msg += f"💰 Valor atual: *{formatar_moeda(fatura['valor_total'])}*\n"
        msg += f"📅 Vencimento: {data_venc}\n"
        msg += f"📊 Status: {fatura['status']}\n\n"

        if fatura.get('transacoes'):
            # Agrupar transações por data
            transacoes_por_data = defaultdict(list)
            for t in fatura['transacoes']:
                transacoes_por_data[t['data']].append(t)

            # Ordenar datas (mais recente primeiro)
            datas_ordenadas = sorted(transacoes_por_data.keys(), reverse=True)

            msg += "📅 *Transações por Data:*\n\n"

            for data in datas_ordenadas:
                data_formatada = data.strftime('%d/%m/%Y')
                transacoes_do_dia = transacoes_por_data[data]
                total_dia = sum(t['valor'] for t in transacoes_do_dia)

                msg += f"*{data_formatada}* - {formatar_moeda(total_dia)}\n"

                for t in transacoes_do_dia:
                    emoji = ""
                    if t.get('tipo_agendamento') == 'FIXO':
                        emoji = "🔁 "
                    elif t.get('tipo_agendamento') == 'PARCELADO':
                        emoji = "📊 "

                    info_extra = ""
                    if t.get('parcela_info'):
                        info_extra = f" ({t['parcela_info']})"

                    msg += f"  {emoji}{t['descricao']}{info_extra}: {formatar_moeda(t['valor'])}\n"

                msg += "\n"
        else:
            msg += "✅ Nenhuma transação registrada nesta fatura."

        return msg

    def _format_resumido(self, faturas: list, nome_cartao: str) -> str:
        """Formata fatura(s) resumida(s)."""
        if not faturas:
            if nome_cartao:
                return f"✅ Você não tem faturas em aberto no cartão '{nome_cartao}'! 🎉"
            return "✅ Você não tem nenhuma fatura em aberto! 🎉"

        if len(faturas) == 1:
            fatura = faturas[0]
            data_venc = fatura['data_vencimento'].strftime('%d/%m/%Y')
            msg = f"💳 *Fatura {fatura['nome_cartao']}*\n\n"
            msg += f"💰 Valor atual: *{formatar_moeda(fatura['valor_fatura'])}*\n"
            msg += f"📅 Vencimento: {data_venc}\n"
            msg += f"📊 Status: {fatura['status']}"
            return msg

        # Múltiplas faturas
        msg = "💳 *Suas Faturas em Aberto:*\n\n"
        total_geral = 0

        for fatura in faturas:
            data_venc = fatura['data_vencimento'].strftime('%d/%m')
            msg += f"🔹 *{fatura['nome_cartao']}*\n"
            msg += f"   💰 {formatar_moeda(fatura['valor_fatura'])} (Venc: {data_venc})\n\n"
            total_geral += fatura['valor_fatura']

        msg += f"💵 *Total Geral:* {formatar_moeda(total_geral)}"

        return msg


class ConsultaTodasContasIntent(BaseIntent):
    """
    Handler para intent 'Consulta Todas Contas'.

    Lista TODAS as contas fixas do mês — pagas e pendentes.

    Exemplo de mensagem:
    - "todas as minhas contas do mês"
    - "listar todas as contas"
    - "quais contas tenho esse mês"
    - "me mostra todas as contas"
    """

    def extract_params(self) -> Dict[str, Any]:
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        from app.services.fixed_bills_service import FixedBillsService
        mensagem = FixedBillsService.list_all_bills_formatted(self.conn, self.usuario_id)
        return {"mensagem_formatada": mensagem}

    def format_response(self, data: Dict[str, Any]) -> str:
        return data.get("mensagem_formatada", "❌ Erro ao buscar contas.")


class MenuAjudaIntent(BaseIntent):
    """
    Handler para intent 'Menu de Ajuda'.

    Exibe o menu completo de funcionalidades disponíveis.

    Exemplo de mensagem:
    - "ajuda"
    - "o que você pode fazer?"
    - "comandos disponíveis"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Retorna o menu de ajuda."""
        return {"mostrar_menu": True}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna o menu de funcionalidades formatado."""
        return """📚 *MENU DE FUNCIONALIDADES* 📚

*💰 GESTÃO FINANCEIRA*
• _"gastei 50 em comida"_ - Registrar despesa
• _"recebi 500"_ - Registrar renda
• _"qual meu saldo?"_ - Consultar saldo das contas
• _"quanto gastei hoje/semana/mês?"_ - Gastos por período
• _"meus potes"_ - Ver limite e gasto dos potes
• _"todas as minhas contas"_ - Listar TODAS as contas recorrentes (pagas ou não)
• _"minhas contas fixas"_ - Ver contas ainda não pagas no mês
• _"paguei água"_ - Quitar conta fixa
• _"paguei a internet e comprei pizza de 50"_ - Quitar múltiplas (híbrido)
• _"transferir 100 da carteira para banco"_ - Transferência
• _"paguei 500 da fatura do nubank"_ - Pagar fatura
• _"qual valor da fatura?"_ - Consultar valor da fatura
• _"quanto gastei com comida?"_ - Gasto por categoria
• _"quais contas tenho?"_ - Listar contas cadastradas
• _"quanto de reserva?"_ - Reserva de emergência (6x média)
• _"tenho conta que vence hoje?"_ - Vencimentos de hoje
• _"tenho conta que vence amanhã?"_ - Vencimentos de amanhã
• _"contas que vencem essa semana?"_ - Vencimentos dos próximos 7 dias

*📅 CALENDÁRIO & AGENDA*
• _"minha agenda amanhã"_ - Ver compromissos
• _"criar evento academia amanhã 7h"_ - Criar evento
• _"deletar reunião de hoje"_ - Remover evento
• _"quando estou livre amanhã?"_ - Horários disponíveis

*📁 GOOGLE DRIVE*
• _[Envie foto/doc] "salvar no drive"_ - Upload para pasta padrão
• _[Envie foto/doc] "salvar no drive pasta Notas"_ - Upload para pasta específica
• _Formatos aceitos:_ Imagens, PDFs, Docs, Planilhas, Áudio

*📊 ANÁLISES & RELATÓRIOS*
• _"analisar meus gastos"_ - Análise inteligente com IA
• _"comparar com mês anterior"_ - Comparação mensal
• _"quanto vou gastar este mês"_ - Previsão de gastos
• _"gráfico de gastos"_ - Gerar gráfico visual

*⚙️ CONFIGURAÇÕES*
• _"ativar resumo matinal"_ - Configurar notificações
• _"configurar localização São Paulo"_ - Definir cidade
• _"configurar relatório mensal"_ - Agendar relatório
• _"configurar endereço casa"_ - Adicionar endereço
• _"meus endereços"_ - Listar endereços
• _"remover endereço casa"_ - Deletar endereço

*🔑 ACESSO*
• _"minha api key"_ - Exibir chave de integração

💡 *Dica:* Use linguagem natural! Eu entendo suas mensagens."""


__all__ = [
    'SolicitarApiKeyIntent',
    'ConfigurarLocalizacaoIntent',
    'ConfigurarRelatorioMensalIntent',
    'ListarContasIntent',
    'AjustarSaldoIntent',
    'ConsultaContasFixasIntent',
    'ConsultaTodasContasIntent',
    'ConsultaFaturaIntent',
    'MenuAjudaIntent',
]
