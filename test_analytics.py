#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script de Teste para Analytics Service
Testa as funcionalidades de análise inteligente de gastos
"""

import sys
import os
import io

# Configurar encoding para UTF-8 no Windows
if sys.platform == 'win32':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')

# Adicionar o diretório do app ao path
sys.path.insert(0, os.path.abspath(os.path.dirname(__file__)))

def test_analytics_service():
    """Testa o serviço de analytics"""
    print("=" * 60)
    print("TESTE: Analytics Service")
    print("=" * 60)

    from app.services.analytics_service import (
        get_spending_analysis,
        generate_ai_insights,
        get_category_comparison,
        get_monthly_comparison
    )

    # ID do usuário para teste (ajuste conforme necessário)
    usuario_id = 1

    try:
        # 1. Testar coleta de dados
        print("\n1️⃣ Testando coleta de dados de gastos...")
        dados = get_spending_analysis(usuario_id, meses_analise=3)

        print(f"✅ Período de análise: {dados['periodo_analise']}")
        print(f"✅ Mês atual: {dados['mes_atual']}")
        print(f"✅ Gastos mensais encontrados: {len(dados['gastos_mensais'])} meses")
        print(f"✅ Categorias analisadas: {len(dados['gastos_por_categoria'])}")
        print(f"✅ Potes configurados: {len(dados['potes'])}")
        print(f"✅ Maiores gastos: {len(dados['maiores_gastos'])}")

        # Mostrar resumo dos gastos mensais
        if dados['gastos_mensais']:
            print("\n📊 Gastos por mês:")
            for gasto in dados['gastos_mensais']:
                print(f"   {gasto['mes']}: R$ {gasto['total']:,.2f}")

        # Mostrar top 3 categorias
        if dados['gastos_por_categoria']:
            print("\n💰 Top 3 Categorias:")
            for i, cat in enumerate(dados['gastos_por_categoria'][:3], 1):
                print(f"   {i}. {cat['categoria']} / {cat['subcategoria']}: R$ {cat['total']:,.2f}")

        print("\n" + "=" * 60)

        # 2. Testar geração de insights com IA
        print("\n2️⃣ Testando geração de insights com IA...")
        print("⏳ Aguarde, consultando o Gemini...")

        insights = generate_ai_insights(usuario_id)

        print("\n✅ Insights gerados com sucesso!")
        print("\n" + "=" * 60)
        print("📊 RELATÓRIO DE INSIGHTS:")
        print("=" * 60)
        print(insights)
        print("=" * 60)

        # 3. Testar comparação mensal
        print("\n3️⃣ Testando comparação mensal...")
        comparacao = get_monthly_comparison(usuario_id)

        print("\n✅ Comparação gerada!")
        print("\n" + "=" * 60)
        print("📈 COMPARAÇÃO MENSAL:")
        print("=" * 60)
        print(comparacao)
        print("=" * 60)

        # 4. Testar comparação de categoria específica
        print("\n4️⃣ Testando comparação de categoria (exemplo: 'alimentação')...")
        cat_comparacao = get_category_comparison(usuario_id, "alimentação", meses=3)

        print("\n✅ Comparação de categoria gerada!")
        print("\n" + "=" * 60)
        print("📊 COMPARAÇÃO POR CATEGORIA:")
        print("=" * 60)
        print(cat_comparacao)
        print("=" * 60)

        print("\n✅ TODOS OS TESTES CONCLUÍDOS COM SUCESSO! ✅")

    except Exception as e:
        print(f"\n❌ ERRO durante o teste: {e}")
        import traceback
        traceback.print_exc()
        return False

    return True


def test_intent_recognition():
    """Testa o reconhecimento de intenção para análise"""
    print("\n" + "=" * 60)
    print("TESTE: Reconhecimento de Intenção")
    print("=" * 60)

    from app.services.gemini_service import get_message_intent

    mensagens_teste = [
        "analisar meus gastos",
        "quero ver insights",
        "análise inteligente",
        "me mostre um relatório financeiro",
        "padrões de consumo",
        "comparar este mês com o anterior",
        "evolução mensal",
    ]

    print("\n🔍 Testando reconhecimento de intenções:\n")

    for msg in mensagens_teste:
        try:
            intent = get_message_intent(msg)
            emoji = "✅" if intent in ["Análise Inteligente", "Comparação Mensal"] else "⚠️"
            print(f'{emoji} "{msg}" → {intent}')
        except Exception as e:
            print(f'❌ "{msg}" → ERRO: {e}')

    print("\n" + "=" * 60)


if __name__ == "__main__":
    print("\n🚀 INICIANDO TESTES DO ANALYTICS SERVICE\n")

    # Verificar se as variáveis de ambiente estão configuradas
    from app.config import GEMINI_API_KEY, DATABASE_URL

    if not GEMINI_API_KEY:
        print("❌ ERRO: GEMINI_API_KEY não configurada!")
        sys.exit(1)

    if not DATABASE_URL:
        print("❌ ERRO: DATABASE_URL não configurada!")
        sys.exit(1)

    print("✅ Variáveis de ambiente OK")

    # Executar testes
    print("\n" + "=" * 60)
    test_intent_recognition()

    print("\n" + "=" * 60)
    sucesso = test_analytics_service()

    if sucesso:
        print("\n🎉 SUCESSO: Todos os testes passaram!")
        sys.exit(0)
    else:
        print("\n💥 FALHA: Alguns testes falharam!")
        sys.exit(1)
