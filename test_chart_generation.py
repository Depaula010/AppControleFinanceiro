#!/usr/bin/env python3
"""
Script de teste para geração de gráficos localmente (sem WhatsApp).
Útil para desenvolvimento e debugging.

Uso:
    python test_chart_generation.py --user-id 1 --chart-type pizza
    python test_chart_generation.py --user-id 1 --chart-type barras --months 12
    python test_chart_generation.py --user-id 1 --chart-type linha --months 6
"""

import sys
import argparse
from datetime import datetime

# Importar contexto da aplicação
from app import create_app, db_engine
from app.services import chart_service

def test_pie_chart(usuario_id, periodo_dias=30):
    """Testa geração de gráfico de pizza."""
    print(f"\n{'='*60}")
    print(f"🍕 Testando Gráfico de Pizza")
    print(f"{'='*60}")
    print(f"Usuário ID: {usuario_id}")
    print(f"Período: {periodo_dias} dias")
    print(f"{'='*60}\n")

    try:
        chart_bytes = chart_service.generate_pie_chart(usuario_id, periodo_dias)

        if chart_bytes is None:
            print("❌ FALHA: Nenhum dado retornado")
            print("   - Verifique se o usuário tem transações de despesas")
            print(f"   - Verifique se há dados nos últimos {periodo_dias} dias")
            return False

        # Salvar arquivo de teste
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f"test_pie_chart_{timestamp}.png"

        with open(filename, 'wb') as f:
            f.write(chart_bytes)

        file_size = len(chart_bytes) / 1024  # KB

        print(f"✅ SUCESSO!")
        print(f"   - Tamanho: {file_size:.2f} KB")
        print(f"   - Arquivo: {filename}")
        print(f"   - Abra o arquivo para verificar visualmente")

        return True

    except Exception as e:
        print(f"❌ ERRO: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_bar_chart(usuario_id, num_meses=6):
    """Testa geração de gráfico de barras."""
    print(f"\n{'='*60}")
    print(f"📊 Testando Gráfico de Barras")
    print(f"{'='*60}")
    print(f"Usuário ID: {usuario_id}")
    print(f"Período: {num_meses} meses")
    print(f"{'='*60}\n")

    try:
        chart_bytes = chart_service.generate_bar_chart(usuario_id, num_meses)

        if chart_bytes is None:
            print("❌ FALHA: Nenhum dado retornado")
            print("   - Verifique se o usuário tem transações")
            print(f"   - Verifique se há dados nos últimos {num_meses} meses")
            return False

        # Salvar arquivo de teste
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f"test_bar_chart_{timestamp}.png"

        with open(filename, 'wb') as f:
            f.write(chart_bytes)

        file_size = len(chart_bytes) / 1024  # KB

        print(f"✅ SUCESSO!")
        print(f"   - Tamanho: {file_size:.2f} KB")
        print(f"   - Arquivo: {filename}")
        print(f"   - Abra o arquivo para verificar visualmente")

        return True

    except Exception as e:
        print(f"❌ ERRO: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_line_chart(usuario_id, num_meses=6):
    """Testa geração de gráfico de linha."""
    print(f"\n{'='*60}")
    print(f"📈 Testando Gráfico de Linha")
    print(f"{'='*60}")
    print(f"Usuário ID: {usuario_id}")
    print(f"Período: {num_meses} meses")
    print(f"{'='*60}\n")

    try:
        chart_bytes = chart_service.generate_line_chart(usuario_id, num_meses)

        if chart_bytes is None:
            print("❌ FALHA: Nenhum dado retornado")
            print("   - Verifique se o usuário tem transações")
            print(f"   - Verifique se há dados nos últimos {num_meses} meses")
            return False

        # Salvar arquivo de teste
        timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
        filename = f"test_line_chart_{timestamp}.png"

        with open(filename, 'wb') as f:
            f.write(chart_bytes)

        file_size = len(chart_bytes) / 1024  # KB

        print(f"✅ SUCESSO!")
        print(f"   - Tamanho: {file_size:.2f} KB")
        print(f"   - Arquivo: {filename}")
        print(f"   - Abra o arquivo para verificar visualmente")

        return True

    except Exception as e:
        print(f"❌ ERRO: {e}")
        import traceback
        traceback.print_exc()
        return False


def test_all_charts(usuario_id):
    """Testa todos os tipos de gráficos."""
    print(f"\n{'#'*60}")
    print(f"# TESTE COMPLETO DE GRÁFICOS")
    print(f"# Usuário ID: {usuario_id}")
    print(f"{'#'*60}")

    results = {
        'pizza': test_pie_chart(usuario_id, 30),
        'barras': test_bar_chart(usuario_id, 6),
        'linha': test_line_chart(usuario_id, 6)
    }

    print(f"\n{'='*60}")
    print(f"📋 RESUMO DOS TESTES")
    print(f"{'='*60}")

    for chart_type, success in results.items():
        status = "✅ PASSOU" if success else "❌ FALHOU"
        print(f"  {chart_type.upper()}: {status}")

    print(f"{'='*60}\n")

    all_passed = all(results.values())
    if all_passed:
        print("🎉 Todos os testes passaram!")
    else:
        print("⚠️  Alguns testes falharam. Verifique os logs acima.")

    return all_passed


def verify_database_connection():
    """Verifica conexão com banco de dados."""
    print("🔍 Verificando conexão com banco de dados...")

    try:
        from sqlalchemy import text
        with db_engine.connect() as conn:
            result = conn.execute(text("SELECT 1"))
            result.fetchone()
        print("✅ Conexão com banco de dados OK")
        return True
    except Exception as e:
        print(f"❌ Erro ao conectar com banco de dados: {e}")
        return False


def check_user_has_data(usuario_id):
    """Verifica se usuário tem dados para gráficos."""
    print(f"\n🔍 Verificando dados do usuário {usuario_id}...")

    try:
        from sqlalchemy import text
        with db_engine.connect() as conn:
            # Contar transações
            sql = text("SELECT COUNT(*) as total FROM Transacoes WHERE usuario_id = :uid")
            result = conn.execute(sql, {"uid": usuario_id}).fetchone()
            total_transacoes = result.total

            # Contar despesas
            sql = text("""
                SELECT COUNT(*) as total
                FROM Transacoes
                WHERE usuario_id = :uid AND tipo_fluxo = 'Despesa'
            """)
            result = conn.execute(sql, {"uid": usuario_id}).fetchone()
            total_despesas = result.total

            # Contar rendas
            sql = text("""
                SELECT COUNT(*) as total
                FROM Transacoes
                WHERE usuario_id = :uid AND tipo_fluxo = 'Renda'
            """)
            result = conn.execute(sql, {"uid": usuario_id}).fetchone()
            total_rendas = result.total

            print(f"   Total de transações: {total_transacoes}")
            print(f"   Despesas: {total_despesas}")
            print(f"   Rendas: {total_rendas}")

            if total_transacoes == 0:
                print(f"⚠️  AVISO: Usuário {usuario_id} não tem transações!")
                print("   Os testes de gráficos provavelmente falharão.")
                return False

            print(f"✅ Usuário tem dados suficientes")
            return True

    except Exception as e:
        print(f"❌ Erro ao verificar dados: {e}")
        return False


def main():
    """Função principal."""
    parser = argparse.ArgumentParser(
        description='Script de teste para geração de gráficos'
    )
    parser.add_argument(
        '--user-id',
        type=int,
        required=True,
        help='ID do usuário para teste'
    )
    parser.add_argument(
        '--chart-type',
        choices=['pizza', 'barras', 'linha', 'all'],
        default='all',
        help='Tipo de gráfico a testar (padrão: all)'
    )
    parser.add_argument(
        '--days',
        type=int,
        default=30,
        help='Período em dias para gráfico de pizza (padrão: 30)'
    )
    parser.add_argument(
        '--months',
        type=int,
        default=6,
        help='Período em meses para gráficos de barras/linha (padrão: 6)'
    )
    parser.add_argument(
        '--skip-checks',
        action='store_true',
        help='Pular verificações de banco e dados'
    )

    args = parser.parse_args()

    # Banner
    print("\n" + "="*60)
    print("  📊 TESTE DE GERAÇÃO DE GRÁFICOS")
    print("="*60 + "\n")

    # Criar contexto da aplicação
    app = create_app()

    with app.app_context():
        # Verificações iniciais
        if not args.skip_checks:
            if not verify_database_connection():
                print("\n❌ Abortando: Falha na conexão com banco de dados")
                sys.exit(1)

            if not check_user_has_data(args.user_id):
                print("\n⚠️  Continuando mesmo sem dados suficientes...")

        # Executar testes
        if args.chart_type == 'pizza':
            success = test_pie_chart(args.user_id, args.days)
        elif args.chart_type == 'barras':
            success = test_bar_chart(args.user_id, args.months)
        elif args.chart_type == 'linha':
            success = test_line_chart(args.user_id, args.months)
        else:  # all
            success = test_all_charts(args.user_id)

        # Retornar código de saída
        sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
