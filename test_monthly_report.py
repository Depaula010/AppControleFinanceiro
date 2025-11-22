#!/usr/bin/env python3
"""
Script de teste rápido para o sistema de relatórios mensais automáticos.
Execute: python test_monthly_report.py
"""

import sys
import json
from datetime import datetime, date
from app.services.monthly_report_config_service import (
    criar_tabela_monthly_report_configs,
    get_or_create_config,
    update_config,
    get_users_to_notify
)
from app.services.monthly_report_service import (
    calcular_periodo_relatorio,
    generate_monthly_report_data,
    generate_monthly_report_chart,
    format_report_message
)


def print_header(title):
    """Imprime cabeçalho formatado"""
    print("\n" + "="*60)
    print(f"  {title}")
    print("="*60)


def print_success(message):
    """Imprime mensagem de sucesso"""
    print(f"✅ {message}")


def print_error(message):
    """Imprime mensagem de erro"""
    print(f"❌ {message}")


def print_info(message):
    """Imprime mensagem informativa"""
    print(f"ℹ️  {message}")


def test_1_criar_tabela():
    """Teste 1: Criar tabela MonthlyReportConfigs"""
    print_header("TESTE 1: Criar Tabela")

    try:
        criar_tabela_monthly_report_configs()
        print_success("Tabela MonthlyReportConfigs criada/verificada com sucesso!")
        return True
    except Exception as e:
        print_error(f"Erro ao criar tabela: {e}")
        return False


def test_2_configuracao(usuario_id=1):
    """Teste 2: Gerenciar configuração de usuário"""
    print_header("TESTE 2: Gerenciar Configuração")

    try:
        # Obter ou criar configuração padrão
        print_info(f"Obtendo configuração do usuário {usuario_id}...")
        config = get_or_create_config(usuario_id)

        print_success("Configuração obtida:")
        print(f"   - Ativo: {config['ativo']}")
        print(f"   - Momento: {config['momento_envio']}")
        print(f"   - Horário: {config['hora_envio']}")

        # Atualizar configuração
        print_info("Atualizando configuração para FIM_MES às 14:30...")
        config_nova = update_config(
            usuario_id,
            ativo=True,
            momento_envio='FIM_MES',
            hora_envio='14:30'
        )

        print_success("Configuração atualizada:")
        print(f"   - Ativo: {config_nova['ativo']}")
        print(f"   - Momento: {config_nova['momento_envio']}")
        print(f"   - Horário: {config_nova['hora_envio']}")

        return True

    except Exception as e:
        print_error(f"Erro ao gerenciar configuração: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_3_calculo_periodo():
    """Teste 3: Calcular período do relatório"""
    print_header("TESTE 3: Cálculo de Período")

    try:
        # Teste INICIO_MES
        mes, ano, inicio, fim = calcular_periodo_relatorio('INICIO_MES')
        print_success(f"INICIO_MES: {mes}/{ano}")
        print(f"   - Data início: {inicio}")
        print(f"   - Data fim: {fim}")

        # Teste FIM_MES
        mes, ano, inicio, fim = calcular_periodo_relatorio('FIM_MES')
        print_success(f"FIM_MES: {mes}/{ano}")
        print(f"   - Data início: {inicio}")
        print(f"   - Data fim: {fim}")

        return True

    except Exception as e:
        print_error(f"Erro ao calcular período: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_4_geracao_dados(usuario_id=1):
    """Teste 4: Gerar dados do relatório"""
    print_header("TESTE 4: Geração de Dados do Relatório")

    try:
        print_info(f"Gerando dados do relatório para usuário {usuario_id}...")

        # Gerar dados
        dados = generate_monthly_report_data(usuario_id, 'FIM_MES')

        print_success(f"Relatório de {dados['mes']}/{dados['ano']} gerado!")
        print(f"\n📊 ESTATÍSTICAS:")
        print(f"   Total de transações: {dados['totais']['total_transacoes']}")
        print(f"   Despesas: R$ {dados['totais']['total_despesas']:,.2f}")
        print(f"   Rendas: R$ {dados['totais']['total_rendas']:,.2f}")
        print(f"   Saldo: R$ {dados['totais']['saldo_periodo']:,.2f}")

        # Top categorias
        if dados['top_categorias']:
            print(f"\n🏆 TOP CATEGORIAS:")
            for i, cat in enumerate(dados['top_categorias'][:3], 1):
                print(f"   {i}. {cat['categoria']}: R$ {cat['valor']:,.2f} ({cat['percentual']:.1f}%)")

        # Comparação
        comp = dados['comparacao']
        print(f"\n📈 COMPARAÇÃO:")
        print(f"   Mês atual: R$ {comp['mes_atual']:,.2f}")
        print(f"   Mês anterior: R$ {comp['mes_anterior']:,.2f}")
        print(f"   Variação: R$ {comp['variacao_valor']:,.2f} ({comp['variacao_percentual']:+.1f}%)")

        # Potes
        if dados['potes']:
            print(f"\n🎯 POTES ({len(dados['potes'])} ativos):")
            for pote in dados['potes'][:3]:
                print(f"   {pote['nome']}: R$ {pote['usado']:,.2f} / R$ {pote['limite']:,.2f} ({pote['percentual']:.0f}%)")

        # Contas
        contas = dados['contas']
        print(f"\n💳 CONTAS:")
        print(f"   Pagas: {contas['pagas']} (R$ {contas['valor_pago']:,.2f})")
        print(f"   Pendentes: {contas['pendentes']} (R$ {contas['valor_pendente']:,.2f})")

        return True, dados

    except Exception as e:
        print_error(f"Erro ao gerar dados: {e}")
        import traceback
        traceback.print_exc()
        return False, None


def test_5_geracao_grafico(dados_relatorio):
    """Teste 5: Gerar gráfico"""
    print_header("TESTE 5: Geração de Gráfico")

    try:
        if not dados_relatorio:
            print_error("Dados do relatório não fornecidos. Execute teste 4 primeiro.")
            return False

        print_info("Gerando gráfico de pizza...")

        # Gerar gráfico
        chart_bytes = generate_monthly_report_chart(dados_relatorio)

        # Salvar em arquivo
        filename = f"/tmp/relatorio_mensal_{datetime.now().strftime('%Y%m%d_%H%M%S')}.png"
        with open(filename, 'wb') as f:
            f.write(chart_bytes)

        print_success(f"Gráfico gerado com {len(chart_bytes)} bytes")
        print_info(f"Arquivo salvo em: {filename}")

        return True

    except Exception as e:
        print_error(f"Erro ao gerar gráfico: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_6_formatacao_mensagem(dados_relatorio):
    """Teste 6: Formatar mensagem"""
    print_header("TESTE 6: Formatação de Mensagem")

    try:
        if not dados_relatorio:
            print_error("Dados do relatório não fornecidos. Execute teste 4 primeiro.")
            return False

        print_info("Formatando mensagem para WhatsApp...")

        # Formatar mensagem
        mensagem = format_report_message(dados_relatorio, "João Silva (Teste)")

        print_success("Mensagem formatada com sucesso!")
        print("\n" + "-"*60)
        print("PRÉVIA DA MENSAGEM:")
        print("-"*60)
        print(mensagem)
        print("-"*60)

        return True

    except Exception as e:
        print_error(f"Erro ao formatar mensagem: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_7_filtro_usuarios():
    """Teste 7: Filtrar usuários para notificar"""
    print_header("TESTE 7: Filtro de Usuários")

    try:
        print_info("Buscando usuários para notificar (INICIO_MES)...")

        usuarios = get_users_to_notify('INICIO_MES', janela_minutos=60)  # Janela maior para teste

        if usuarios:
            print_success(f"Encontrados {len(usuarios)} usuário(s):")
            for user in usuarios:
                print(f"   - ID: {user['usuario_id']}, Nome: {user['nome']}, Hora: {user['hora_envio']}")
        else:
            print_info("Nenhum usuário encontrado na janela de horário atual")
            print_info("Isso é normal se não houver usuários configurados para este horário")

        return True

    except Exception as e:
        print_error(f"Erro ao filtrar usuários: {e}")
        import traceback
        traceback.print_exc()
        return False


def run_all_tests(usuario_id=1):
    """Executa todos os testes"""
    print_header("INICIANDO BATERIA DE TESTES")
    print_info(f"Usuário de teste: {usuario_id}")
    print_info(f"Data/Hora: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

    resultados = {}
    dados_relatorio = None

    # Teste 1
    resultados['criar_tabela'] = test_1_criar_tabela()

    # Teste 2
    resultados['configuracao'] = test_2_configuracao(usuario_id)

    # Teste 3
    resultados['calculo_periodo'] = test_3_calculo_periodo()

    # Teste 4
    sucesso, dados = test_4_geracao_dados(usuario_id)
    resultados['geracao_dados'] = sucesso
    if sucesso:
        dados_relatorio = dados

    # Teste 5
    resultados['geracao_grafico'] = test_5_geracao_grafico(dados_relatorio)

    # Teste 6
    resultados['formatacao_mensagem'] = test_6_formatacao_mensagem(dados_relatorio)

    # Teste 7
    resultados['filtro_usuarios'] = test_7_filtro_usuarios()

    # Resumo
    print_header("RESUMO DOS TESTES")

    total = len(resultados)
    passou = sum(1 for r in resultados.values() if r)
    falhou = total - passou

    for nome, resultado in resultados.items():
        status = "✅ PASSOU" if resultado else "❌ FALHOU"
        print(f"{status} - {nome}")

    print(f"\nTotal: {total} | Passou: {passou} | Falhou: {falhou}")

    if falhou == 0:
        print_success("\n🎉 TODOS OS TESTES PASSARAM! Sistema pronto para uso.")
        return True
    else:
        print_error(f"\n⚠️  {falhou} teste(s) falharam. Revise os erros acima.")
        return False


if __name__ == '__main__':
    # Verificar argumento de usuário
    usuario_id = int(sys.argv[1]) if len(sys.argv) > 1 else 1

    try:
        sucesso = run_all_tests(usuario_id)
        sys.exit(0 if sucesso else 1)
    except KeyboardInterrupt:
        print("\n\n⚠️  Testes interrompidos pelo usuário.")
        sys.exit(130)
    except Exception as e:
        print_error(f"\n💥 Erro fatal durante os testes: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
