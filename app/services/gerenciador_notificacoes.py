# app/services/gerenciador_notificacoes.py
"""
Gerenciador de notificações WhatsApp para sistema de chaves de API (SaaS).
Envia alertas sobre:
- Uso de chaves de API
- Limites próximos ao máximo
- Relatórios mensais
- Alertas de segurança
"""

from datetime import datetime
from typing import Dict, Optional
from sqlalchemy import text
from app import db_engine
from app.services.notification_service import enviar_notificacao_whatsapp
import os


class GerenciadorNotificacoes:
    """
    Gerenciador centralizado para notificações WhatsApp relacionadas a chaves de API.
    """

    # Configurações do bot WhatsApp
    BOT_URL = os.environ.get('WHATSAPP_BOT_URL')
    BOT_API_KEY = os.environ.get('WHATSAPP_BOT_API_KEY')

    # Emojis para notificações
    EMOJI_SUCESSO = '✅'
    EMOJI_ALERTA = '⚠️'
    EMOJI_ERRO = '❌'
    EMOJI_INFO = 'ℹ️'
    EMOJI_CHAVE = '🔑'
    EMOJI_GRAFICO = '📊'
    EMOJI_DINHEIRO = '💰'

    @staticmethod
    def _obter_numero_whatsapp_usuario(usuario_id: int) -> Optional[str]:
        """
        Busca número de WhatsApp do usuário no banco.

        Args:
            usuario_id: ID do usuário

        Returns:
            str: Número do WhatsApp ou None se não encontrado
        """
        try:
            with db_engine.connect() as conn:
                # Assumindo que existe uma tabela Usuarios com campo whatsapp_numero
                query = text("""
                    SELECT whatsapp_numero
                    FROM Usuarios
                    WHERE id = :usuario_id
                """)

                result = conn.execute(query, {'usuario_id': usuario_id})
                row = result.fetchone()

                if row and row[0]:
                    return row[0]

                print(f"[NOTIF] ⚠️ Número WhatsApp não encontrado para usuário {usuario_id}")
                return None

        except Exception as e:
            print(f"[NOTIF] ❌ Erro ao buscar número WhatsApp: {e}")
            return None

    @staticmethod
    def _verificar_consentimento_comunicacao(usuario_id: int) -> bool:
        """
        Verifica se usuário consentiu receber notificações WhatsApp.

        Args:
            usuario_id: ID do usuário

        Returns:
            bool: True se consentiu, False caso contrário
        """
        try:
            from app.services.servico_consentimento_lgpd import ServicoConsentimentoLGPD

            return ServicoConsentimentoLGPD.verificar_consentimento(
                usuario_id=usuario_id,
                tipo_consentimento='comunicacao_whatsapp'
            )

        except Exception as e:
            print(f"[NOTIF] ⚠️ Erro ao verificar consentimento: {e}")
            # Em caso de erro, assumir que não consentiu (seguro)
            return False

    @staticmethod
    def _enviar_notificacao(usuario_id: int, mensagem: str) -> bool:
        """
        Envia notificação WhatsApp para usuário (wrapper interno).

        Args:
            usuario_id: ID do usuário
            mensagem: Texto da mensagem

        Returns:
            bool: True se enviou com sucesso
        """
        # Verificar se bot está configurado
        if not GerenciadorNotificacoes.BOT_URL or not GerenciadorNotificacoes.BOT_API_KEY:
            print("[NOTIF] ⚠️ Bot WhatsApp não configurado (BOT_URL ou BOT_API_KEY ausente)")
            return False

        # Verificar consentimento LGPD
        if not GerenciadorNotificacoes._verificar_consentimento_comunicacao(usuario_id):
            print(f"[NOTIF] ⚠️ Usuário {usuario_id} não consentiu receber notificações WhatsApp")
            return False

        # Buscar número do usuário
        numero = GerenciadorNotificacoes._obter_numero_whatsapp_usuario(usuario_id)
        if not numero:
            return False

        # Enviar notificação
        return enviar_notificacao_whatsapp(
            numero=numero,
            mensagem=mensagem,
            bot_url=GerenciadorNotificacoes.BOT_URL,
            api_key=GerenciadorNotificacoes.BOT_API_KEY
        )

    @staticmethod
    def notificar_cadastro_chave(usuario_id: int, provedor: str) -> bool:
        """
        Notifica usuário sobre cadastro de nova chave de API.

        Args:
            usuario_id: ID do usuário
            provedor: Provedor da chave (gemini, weather, openroute)

        Returns:
            bool: True se enviou com sucesso
        """
        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_SUCESSO} *Nova chave cadastrada!*

{GerenciadorNotificacoes.EMOJI_CHAVE} Provedor: *{provedor.upper()}*

Sua chave foi cadastrada com sucesso e já está disponível para uso.

Para começar a usar, configure suas preferências:
👉 Escolha se quer usar sua chave (grátis) ou chave do sistema (pago)

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def notificar_limite_proximo(usuario_id: int, provedor: str, uso_atual: int, limite: int) -> bool:
        """
        Notifica usuário quando está próximo do limite mensal.

        Args:
            usuario_id: ID do usuário
            provedor: Provedor da chave
            uso_atual: Uso atual (quantidade de chamadas)
            limite: Limite do plano

        Returns:
            bool: True se enviou com sucesso
        """
        percentual = (uso_atual / limite * 100) if limite > 0 else 0

        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_ALERTA} *Alerta de Limite!*

{GerenciadorNotificacoes.EMOJI_CHAVE} Provedor: *{provedor.upper()}*
{GerenciadorNotificacoes.EMOJI_GRAFICO} Uso atual: *{uso_atual}/{limite}* ({percentual:.1f}%)

Você está próximo do limite mensal do seu plano.

💡 *Opções:*
• Use sua própria chave (grátis e ilimitado)
• Faça upgrade do seu plano

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def notificar_limite_excedido(usuario_id: int, provedor: str) -> bool:
        """
        Notifica usuário quando limite mensal foi excedido.

        Args:
            usuario_id: ID do usuário
            provedor: Provedor da chave

        Returns:
            bool: True se enviou com sucesso
        """
        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_ERRO} *Limite Excedido!*

{GerenciadorNotificacoes.EMOJI_CHAVE} Provedor: *{provedor.upper()}*

Você atingiu o limite mensal do seu plano. O serviço de {provedor} está temporariamente bloqueado.

💡 *Soluções imediatas:*
1. Cadastre sua própria chave (grátis e ilimitado)
2. Faça upgrade do seu plano

👉 Acesse o dashboard para configurar

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def enviar_relatorio_mensal(usuario_id: int, mes_ano: str) -> bool:
        """
        Envia relatório mensal de uso de chaves de API.

        Args:
            usuario_id: ID do usuário
            mes_ano: Mês/ano no formato "YYYY-MM"

        Returns:
            bool: True se enviou com sucesso
        """
        try:
            # Buscar dados de uso do mês
            with db_engine.connect() as conn:
                query = text("""
                    SELECT
                        provedor,
                        tipo_chave,
                        quantidade_chamadas
                    FROM RastreamentoUsoApi
                    WHERE usuario_id = :usuario_id
                      AND mes_ano = :mes_ano
                    ORDER BY provedor, tipo_chave
                """)

                result = conn.execute(query, {
                    'usuario_id': usuario_id,
                    'mes_ano': mes_ano
                })
                rows = result.fetchall()

                if not rows:
                    print(f"[NOTIF] ℹ️ Sem uso registrado para usuário {usuario_id} em {mes_ano}")
                    return False

                # Montar relatório
                linhas = [
                    f"{GerenciadorNotificacoes.EMOJI_GRAFICO} *Relatório Mensal - {mes_ano}*",
                    "",
                    "📊 *Uso de APIs:*",
                    ""
                ]

                total_proprio = 0
                total_sistema = 0

                for row in rows:
                    provedor = row[0]
                    tipo_chave = row[1]
                    quantidade = row[2]

                    emoji = "🆓" if tipo_chave == 'propria' else "💳"
                    tipo_texto = "Chave própria" if tipo_chave == 'propria' else "Chave sistema"

                    linhas.append(f"{emoji} *{provedor.upper()}* ({tipo_texto}): {quantidade}")

                    if tipo_chave == 'propria':
                        total_proprio += quantidade
                    else:
                        total_sistema += quantidade

                linhas.extend([
                    "",
                    "📈 *Totais:*",
                    f"🆓 Chave própria: {total_proprio} chamadas",
                    f"💳 Chave sistema: {total_sistema} chamadas",
                    "",
                    f"*Total geral: {total_proprio + total_sistema} chamadas*"
                ])

                # Adicionar informação de custo se usou chave do sistema
                if total_sistema > 0:
                    linhas.extend([
                        "",
                        f"{GerenciadorNotificacoes.EMOJI_DINHEIRO} _Chamadas com chave sistema são cobradas conforme seu plano_"
                    ])

                linhas.extend([
                    "",
                    "_Meu Secretário - Sistema de Chaves API_"
                ])

                mensagem = "\n".join(linhas)

                return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

        except Exception as e:
            print(f"[NOTIF] ❌ Erro ao enviar relatório mensal: {e}")
            return False

    @staticmethod
    def notificar_falha_chave(usuario_id: int, provedor: str, erro: str) -> bool:
        """
        Notifica usuário sobre falha ao usar chave de API.

        Args:
            usuario_id: ID do usuário
            provedor: Provedor da chave
            erro: Mensagem de erro

        Returns:
            bool: True se enviou com sucesso
        """
        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_ERRO} *Erro ao usar chave API!*

{GerenciadorNotificacoes.EMOJI_CHAVE} Provedor: *{provedor.upper()}*

Houve um erro ao tentar usar sua chave de API:

⚠️ _{erro}_

💡 *Possíveis causas:*
• Chave inválida ou expirada
• Limite de uso do provedor atingido
• Problemas de conectividade

👉 Verifique sua chave no dashboard

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def notificar_troca_preferencia(usuario_id: int, provedor: str, usar_chave_propria: bool) -> bool:
        """
        Notifica usuário sobre mudança de preferência de chave.

        Args:
            usuario_id: ID do usuário
            provedor: Provedor da chave
            usar_chave_propria: True se trocou para chave própria, False para sistema

        Returns:
            bool: True se enviou com sucesso
        """
        if usar_chave_propria:
            tipo = "sua própria chave 🆓"
            detalhe = "Você não será cobrado por usar esta API."
        else:
            tipo = "chave do sistema 💳"
            detalhe = "O uso será cobrado conforme seu plano."

        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_INFO} *Preferência atualizada!*

{GerenciadorNotificacoes.EMOJI_CHAVE} Provedor: *{provedor.upper()}*

A partir de agora você está usando: *{tipo}*

{detalhe}

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def notificar_mudanca_plano(usuario_id: int, plano_antigo: str, plano_novo: str) -> bool:
        """
        Notifica usuário sobre mudança de plano.

        Args:
            usuario_id: ID do usuário
            plano_antigo: Nome do plano antigo
            plano_novo: Nome do plano novo

        Returns:
            bool: True se enviou com sucesso
        """
        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_SUCESSO} *Plano atualizado!*

🔄 *{plano_antigo}* → *{plano_novo}*

Seu plano foi atualizado com sucesso. Os novos limites já estão disponíveis.

👉 Acesse o dashboard para ver detalhes

_Meu Secretário - Sistema de Chaves API_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)

    @staticmethod
    def notificar_boas_vindas_saas(usuario_id: int) -> bool:
        """
        Envia mensagem de boas-vindas ao sistema SaaS.

        Args:
            usuario_id: ID do usuário

        Returns:
            bool: True se enviou com sucesso
        """
        mensagem = f"""
{GerenciadorNotificacoes.EMOJI_SUCESSO} *Bem-vindo ao Meu Secretário!*

Agora você tem acesso ao sistema de gerenciamento de chaves de API.

💡 *Primeiros passos:*

1️⃣ Cadastre suas chaves de API (Gemini, Weather, OpenRoute)
2️⃣ Configure suas preferências para cada provedor
3️⃣ Escolha se quer usar:
   🆓 Suas próprias chaves (grátis e ilimitado)
   💳 Chaves do sistema (cobrado conforme plano)

📊 *Seu plano atual:* Bronze (gratuito)

👉 Acesse o dashboard para começar

_Estamos aqui para ajudar!_
""".strip()

        return GerenciadorNotificacoes._enviar_notificacao(usuario_id, mensagem)


# ============================================================================
# Funções auxiliares para integração com outros serviços
# ============================================================================

def notificar_uso_proximo_ao_limite(usuario_id: int, provedor: str, percentual_uso: float):
    """
    Wrapper para notificar quando uso está próximo ao limite.
    Pode ser chamado pelos serviços de rastreamento.

    Args:
        usuario_id: ID do usuário
        provedor: Provedor da chave
        percentual_uso: Percentual de uso (0-100)
    """
    # Enviar alerta apenas em marcos específicos (80%, 90%, 95%)
    if percentual_uso >= 95:
        nivel = "CRÍTICO"
    elif percentual_uso >= 90:
        nivel = "ALTO"
    elif percentual_uso >= 80:
        nivel = "MÉDIO"
    else:
        return  # Não notificar abaixo de 80%

    print(f"[NOTIF] ⚠️ Alerta de uso {nivel}: Usuário {usuario_id}, {provedor}, {percentual_uso:.1f}%")

    # Buscar uso atual e limite
    try:
        with db_engine.connect() as conn:
            # Buscar uso atual
            query_uso = text("""
                SELECT quantidade_chamadas
                FROM RastreamentoUsoApi
                WHERE usuario_id = :usuario_id
                  AND provedor = :provedor
                  AND tipo_chave = 'sistema'
                  AND mes_ano = TO_CHAR(CURRENT_DATE, 'YYYY-MM')
            """)

            result = conn.execute(query_uso, {
                'usuario_id': usuario_id,
                'provedor': provedor
            })
            row = result.fetchone()
            uso_atual = row[0] if row else 0

            # Buscar limite do plano
            query_limite = text("""
                SELECT p.limite_mensal_gemini, p.limite_mensal_weather, p.limite_mensal_openroute
                FROM AssinaturasUsuario a
                JOIN Planos p ON a.plano_id = p.id
                WHERE a.usuario_id = :usuario_id
                  AND a.ativo = TRUE
                LIMIT 1
            """)

            result = conn.execute(query_limite, {'usuario_id': usuario_id})
            row = result.fetchone()

            if row:
                # Mapear provedor para coluna correta
                if provedor == 'gemini':
                    limite = row[0]
                elif provedor == 'weather':
                    limite = row[1]
                elif provedor == 'openroute':
                    limite = row[2]
                else:
                    limite = 0

                GerenciadorNotificacoes.notificar_limite_proximo(
                    usuario_id=usuario_id,
                    provedor=provedor,
                    uso_atual=uso_atual,
                    limite=limite
                )

    except Exception as e:
        print(f"[NOTIF] ❌ Erro ao processar alerta de limite: {e}")
