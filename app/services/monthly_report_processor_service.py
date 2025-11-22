"""
Processador de envio automático de relatórios mensais.
Identifica usuários elegíveis e envia relatórios via WhatsApp.
"""

import logging
from datetime import datetime
from zoneinfo import ZoneInfo
from app.services.monthly_report_config_service import get_users_to_notify
from app.services.monthly_report_service import (
    generate_monthly_report_data,
    generate_monthly_report_chart,
    format_report_message
)
from app.services.notification_service import (
    enviar_notificacao_whatsapp,
    enviar_imagem_whatsapp_bytes
)
from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

# Configurar logger
logger = logging.getLogger(__name__)


def processar_relatorios_mensais(momento_envio: str, janela_minutos: int = 5) -> dict:
    """
    Processa e envia relatórios mensais para usuários elegíveis.

    Args:
        momento_envio: 'INICIO_MES' ou 'FIM_MES'
        janela_minutos: Janela de tolerância em minutos (padrão: 5)

    Returns:
        dict: Resultado do processamento com estatísticas:
            - total_usuarios: int
            - enviados_sucesso: int
            - enviados_erro: int
            - usuarios_processados: list
            - erros: list
    """
    inicio_processamento = datetime.now(TIMEZONE_BR)
    logger.info(f"Iniciando processamento de relatórios mensais: {momento_envio}")

    resultado = {
        'momento_envio': momento_envio,
        'horario_processamento': inicio_processamento.strftime('%Y-%m-%d %H:%M:%S'),
        'total_usuarios': 0,
        'enviados_sucesso': 0,
        'enviados_erro': 0,
        'usuarios_processados': [],
        'erros': []
    }

    try:
        # Buscar usuários elegíveis
        usuarios = get_users_to_notify(momento_envio, janela_minutos)
        resultado['total_usuarios'] = len(usuarios)

        logger.info(f"Encontrados {len(usuarios)} usuários para notificar")

        # Processar cada usuário
        for usuario in usuarios:
            usuario_id = usuario['usuario_id']
            nome = usuario['nome']
            numero_whatsapp = usuario['numero_whatsapp']
            hora_envio = usuario['hora_envio']

            logger.info(f"Processando relatório para usuário {usuario_id} ({nome})")

            try:
                # Gerar dados do relatório
                report_data = generate_monthly_report_data(usuario_id, momento_envio)

                # Verificar se há dados suficientes
                if report_data['totais']['total_transacoes'] == 0:
                    logger.warning(f"Usuário {usuario_id} sem transações no período. Pulando envio.")
                    resultado['usuarios_processados'].append({
                        'usuario_id': usuario_id,
                        'nome': nome,
                        'status': 'sem_dados',
                        'mensagem': 'Nenhuma transação no período'
                    })
                    continue

                # Formatar mensagem
                mensagem = format_report_message(report_data, nome)

                # Gerar gráfico
                chart_bytes = generate_monthly_report_chart(report_data)

                # Enviar mensagem de texto
                sucesso_texto = enviar_notificacao_whatsapp(
                    numero_whatsapp,
                    mensagem,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                if not sucesso_texto:
                    raise Exception("Falha ao enviar mensagem de texto")

                # Enviar gráfico
                mes_nome = [
                    '', 'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
                    'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro'
                ][report_data['mes']]

                caption = f"📊 Relatório de {mes_nome}/{report_data['ano']}"

                sucesso_grafico = enviar_imagem_whatsapp_bytes(
                    numero_whatsapp,
                    chart_bytes,
                    caption,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                if not sucesso_grafico:
                    logger.warning(f"Falha ao enviar gráfico para usuário {usuario_id}")

                # Registrar sucesso
                resultado['enviados_sucesso'] += 1
                resultado['usuarios_processados'].append({
                    'usuario_id': usuario_id,
                    'nome': nome,
                    'hora_configurada': str(hora_envio),
                    'status': 'enviado',
                    'mensagem': 'Relatório enviado com sucesso',
                    'grafico_enviado': sucesso_grafico
                })

                logger.info(f"Relatório enviado com sucesso para usuário {usuario_id}")

            except Exception as e:
                # Registrar erro
                erro_msg = f"Erro ao processar usuário {usuario_id}: {str(e)}"
                logger.error(erro_msg, exc_info=True)

                resultado['enviados_erro'] += 1
                resultado['erros'].append({
                    'usuario_id': usuario_id,
                    'nome': nome,
                    'erro': str(e)
                })

                resultado['usuarios_processados'].append({
                    'usuario_id': usuario_id,
                    'nome': nome,
                    'status': 'erro',
                    'mensagem': str(e)
                })

    except Exception as e:
        erro_msg = f"Erro geral no processamento: {str(e)}"
        logger.error(erro_msg, exc_info=True)
        resultado['erros'].append({
            'tipo': 'erro_geral',
            'erro': str(e)
        })

    # Finalizar
    fim_processamento = datetime.now(TIMEZONE_BR)
    duracao = (fim_processamento - inicio_processamento).total_seconds()
    resultado['duracao_segundos'] = duracao

    logger.info(
        f"Processamento finalizado: {resultado['enviados_sucesso']} sucessos, "
        f"{resultado['enviados_erro']} erros em {duracao:.2f}s"
    )

    return resultado


def enviar_relatorio_manual(usuario_id: int, momento_envio: str = 'INICIO_MES') -> dict:
    """
    Envia relatório mensal manualmente para um usuário específico.
    Útil para testes e envios sob demanda.

    Args:
        usuario_id: ID do usuário
        momento_envio: 'INICIO_MES' (mês anterior) ou 'FIM_MES' (mês atual)

    Returns:
        dict: Resultado do envio
    """
    from app.services.user_service import get_user_by_id

    logger.info(f"Envio manual de relatório para usuário {usuario_id}")

    try:
        # Buscar dados do usuário
        usuario = get_user_by_id(usuario_id)
        if not usuario:
            return {
                'sucesso': False,
                'erro': 'Usuário não encontrado'
            }

        nome = usuario['nome']
        numero_whatsapp = usuario['numero_whatsapp']

        # Gerar relatório
        report_data = generate_monthly_report_data(usuario_id, momento_envio)

        if report_data['totais']['total_transacoes'] == 0:
            return {
                'sucesso': False,
                'erro': 'Nenhuma transação encontrada no período',
                'dados': report_data
            }

        # Formatar mensagem
        mensagem = format_report_message(report_data, nome)

        # Gerar gráfico
        chart_bytes = generate_monthly_report_chart(report_data)

        # Enviar mensagem
        sucesso_texto = enviar_notificacao_whatsapp(
            numero_whatsapp,
            mensagem,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        if not sucesso_texto:
            return {
                'sucesso': False,
                'erro': 'Falha ao enviar mensagem de texto'
            }

        # Enviar gráfico
        mes_nome = [
            '', 'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
            'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro'
        ][report_data['mes']]

        caption = f"📊 Relatório de {mes_nome}/{report_data['ano']}"

        sucesso_grafico = enviar_imagem_whatsapp_bytes(
            numero_whatsapp,
            chart_bytes,
            caption,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        return {
            'sucesso': True,
            'usuario_id': usuario_id,
            'nome': nome,
            'mensagem_enviada': sucesso_texto,
            'grafico_enviado': sucesso_grafico,
            'dados': report_data
        }

    except Exception as e:
        logger.error(f"Erro no envio manual: {str(e)}", exc_info=True)
        return {
            'sucesso': False,
            'erro': str(e)
        }
