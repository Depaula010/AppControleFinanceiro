#!/usr/bin/env python3
"""
Processador de Resumo Matinal (Daily Briefing) + Alertas Financeiros
Executado via cron job para enviar resumo inteligente da agenda e alertas de contas/faturas
"""

import os
import sys
from datetime import datetime, time, date

# Adicionar diretório raiz ao path para encontrar o módulo 'app'
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def format_financial_alerts_standalone(alertas_data):
    """
    Formata alertas financeiros para mensagem independente (quando resumo está desativado).
    Inclui saudação e contexto completo.

    Args:
        alertas_data: dict com contas_hoje, contas_amanha, faturas_hoje, faturas_amanha

    Returns:
        str: Mensagem formatada ou None
    """
    contas_hoje = alertas_data.get('contas_hoje', [])
    contas_amanha = alertas_data.get('contas_amanha', [])
    faturas_hoje = alertas_data.get('faturas_hoje', [])
    faturas_amanha = alertas_data.get('faturas_amanha', [])

    tem_alertas = any([contas_hoje, contas_amanha, faturas_hoje, faturas_amanha])

    if not tem_alertas:
        return None

    msg_parts = ["🌅 *Bom dia!*\n", "💰 *ALERTAS FINANCEIROS*\n"]

    # Contas/faturas que vencem HOJE
    if contas_hoje or faturas_hoje:
        msg_parts.append("⚠️ *VENCE HOJE:*")

        for conta in contas_hoje:
            valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
            msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        for fatura in faturas_hoje:
            valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
            msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

    # Contas/faturas que vencem AMANHÃ
    if contas_amanha or faturas_amanha:
        msg_parts.append("\n🔔 *VENCE AMANHÃ:*")

        for conta in contas_amanha:
            valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
            msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        for fatura in faturas_amanha:
            valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
            msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

    return "\n".join(msg_parts)


def montar_mensagem_unificada(resumo_componente, alertas_componente, config):
    """
    Monta mensagem final baseado nos componentes disponíveis.

    Args:
        resumo_componente: Mensagem do resumo matinal (ou None)
        alertas_componente: Mensagem dos alertas financeiros (ou None)
        config: Configurações do usuário

    Returns:
        str: Mensagem final ou None
    """
    resumo_ativo = config['resumo_matinal_ativo']
    alertas_ativos = config['alertas_financeiros_ativos']

    if resumo_ativo and alertas_ativos:
        # CASO 1: Ambos ativos
        if resumo_componente:
            # Alertas já estão incluídos no resumo (prepare_briefing_data)
            return resumo_componente
        elif alertas_componente:
            # Não há eventos, mas há alertas
            return alertas_componente

    elif resumo_ativo and resumo_componente:
        # CASO 2: Apenas resumo ativo
        return resumo_componente

    elif alertas_ativos and alertas_componente:
        # CASO 3: Apenas alertas ativos
        return alertas_componente

    return None


def processar_resumo_matinal():
    """
    Função principal que o Cron Job vai rodar.
    Envia resumo matinal e/ou alertas financeiros para usuários configurados.
    """
    print(f"[RESUMO-MATINAL] Início do processamento - {datetime.now()}")

    try:
        # 1. IMPORTANTE: Criar instância da aplicação para acessar o Banco
        from app import create_app
        app = create_app()

        # 2. Entrar no contexto da aplicação
        with app.app_context():

            # Importar serviços (agora com acesso ao DB garantido)
            from app.services.notification_config_service import NotificationConfigService
            from app.services.daily_briefing_service import DailyBriefingService
            from app.services.gemini_service import generate_daily_briefing
            from app.services.finance_service import get_upcoming_bills_and_invoices
            from app.services.notification_service import enviar_notificacao_whatsapp
            from app import db_engine

            # Obter hora atual (zerando segundos para bater com o banco)
            hora_atual = datetime.now().time().replace(second=0, microsecond=0)

            print(f"[RESUMO-MATINAL] Buscando usuários para notificar às {hora_atual.strftime('%H:%M')}")

            # Buscar usuários com resumo matinal OU alertas financeiros ativos
            usuarios = NotificationConfigService.get_users_with_notifications_active(hora_atual)

            if not usuarios:
                print(f"[RESUMO-MATINAL] Nenhum usuário configurado para este horário ({hora_atual})")
                return

            print(f"[RESUMO-MATINAL] {len(usuarios)} usuário(s) encontrado(s)")

            # Inicializar serviço
            briefing_service = DailyBriefingService()

            # Processar cada usuário
            for usuario_id, numero_whatsapp in usuarios:
                try:
                    print(f"[RESUMO-MATINAL] Processando usuário {usuario_id}...")

                    # Obter configurações do usuário
                    config = NotificationConfigService.get_or_create_config(usuario_id)

                    # Preparar componentes da mensagem
                    resumo_componente = None
                    alertas_componente = None

                    # 1. Buscar resumo matinal (se ativo)
                    if config['resumo_matinal_ativo']:
                        print(f"[RESUMO-MATINAL] Preparando resumo matinal para usuário {usuario_id}...")
                        briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

                        if not briefing_data:
                            print(f"[RESUMO-MATINAL] Erro ao preparar dados para usuário {usuario_id}")
                        elif briefing_data.get('total_eventos', 0) > 0:
                            # Gerar resumo completo com IA
                            print(f"[RESUMO-MATINAL] Gerando resumo com IA para usuário {usuario_id}...")
                            resumo_componente = generate_daily_briefing(briefing_data)
                        else:
                            # Mensagem simples sem eventos (mas pode incluir alertas se config ativa)
                            print(f"[RESUMO-MATINAL] Sem eventos para usuário {usuario_id}. Gerando mensagem básica.")
                            resumo_componente = briefing_service.generate_briefing_message(usuario_id, date.today())

                    # 2. Buscar alertas financeiros (se ativo E resumo não está ativo)
                    # Se resumo está ativo, alertas já estão incluídos no resumo
                    if config['alertas_financeiros_ativos'] and not config['resumo_matinal_ativo']:
                        print(f"[RESUMO-MATINAL] Buscando alertas financeiros para usuário {usuario_id}...")

                        with db_engine.connect() as conn:
                            alertas_data = get_upcoming_bills_and_invoices(conn, usuario_id, date.today())

                        # Verificar se há alertas (hoje ou amanhã)
                        tem_alertas = any([
                            alertas_data['contas_hoje'],
                            alertas_data['contas_amanha'],
                            alertas_data['faturas_hoje'],
                            alertas_data['faturas_amanha']
                        ])

                        if tem_alertas:
                            alertas_componente = format_financial_alerts_standalone(alertas_data)
                        else:
                            print(f"[RESUMO-MATINAL] Sem alertas financeiros para usuário {usuario_id}")

                    # 3. Montar mensagem final
                    mensagem = montar_mensagem_unificada(
                        resumo_componente,
                        alertas_componente,
                        config
                    )

                    if not mensagem:
                        print(f"[RESUMO-MATINAL] Nenhuma mensagem para enviar ao usuário {usuario_id}")
                        continue

                    # Enviar via WhatsApp
                    enviar_notificacao_whatsapp(
                        numero_whatsapp,
                        mensagem,
                        app.config.get('BOT_WHATSAPP_URL'),
                        app.config.get('API_SECRET_KEY')
                    )

                    print(f"[RESUMO-MATINAL] ✅ Mensagem enviada para usuário {usuario_id}")

                except Exception as e_user:
                    print(f"[RESUMO-MATINAL] ❌ Erro ao processar usuário {usuario_id}: {e_user}")
                    import traceback
                    traceback.print_exc()
                    continue

            print(f"[RESUMO-MATINAL] Processamento finalizado - {datetime.now()}")

    except Exception as e:
        print(f"[RESUMO-MATINAL] ❌ ERRO CRÍTICO: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    processar_resumo_matinal()