#!/usr/bin/env python3
"""
Testes ISOLADOS da Fase E - Eliminar Duplicações
Não depende do módulo app completo, testa apenas os arquivos criados/modificados.
"""

import sys
import os

# Simular o código do formatter diretamente para teste isolado
def test_financial_alert_formatter():
    """Testa FinancialAlertFormatter copiando a lógica do arquivo diretamente."""
    print("=" * 60)
    print("🧪 TESTE ISOLADO: FinancialAlertFormatter")
    print("=" * 60)
    
    # Ler e executar o código do formatter
    formatter_path = os.path.join(
        os.path.dirname(__file__),
        "app", "shared", "formatters", "financial_alert_formatter.py"
    )
    
    if not os.path.exists(formatter_path):
        print(f"❌ Arquivo não encontrado: {formatter_path}")
        return False
    
    print(f"✅ Arquivo existe: {formatter_path}")
    
    # Ler conteúdo do arquivo
    with open(formatter_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Executar o código em um namespace isolado
    namespace = {'__name__': '__main__'}
    try:
        exec(content, namespace)
        print("✅ Código executa sem erros de sintaxe")
    except Exception as e:
        print(f"❌ Erro ao executar código: {e}")
        return False
    
    # Verificar se classe foi criada
    if 'FinancialAlertFormatter' not in namespace:
        print("❌ Classe FinancialAlertFormatter não encontrada")
        return False
    
    print("✅ Classe FinancialAlertFormatter criada")
    
    # Testar formatação
    formatter = namespace['FinancialAlertFormatter']
    
    # Teste 1: Alertas vazios
    result = formatter.format({
        'contas_hoje': [], 'contas_amanha': [],
        'faturas_hoje': [], 'faturas_amanha': []
    })
    
    if result is None:
        print("✅ Retorna None para alertas vazios")
    else:
        print(f"❌ Deveria retornar None, retornou: {result}")
        return False
    
    # Teste 2: Com alertas
    alertas = {
        'contas_hoje': [
            {'descricao': 'Conta de Luz', 'valor': 150.50, 'tipo': 'Despesa'},
            {'descricao': 'Salário', 'valor': 5000.00, 'tipo': 'Receita'}
        ],
        'contas_amanha': [
            {'descricao': 'Internet', 'valor': 99.90, 'tipo': 'Despesa'}
        ],
        'faturas_hoje': [
            {'cartao': 'Nubank', 'valor': 1500.00}
        ],
        'faturas_amanha': []
    }
    
    result = formatter.format(alertas, include_greeting=False)
    
    if result is None:
        print("❌ Não deveria retornar None com alertas")
        return False
    
    print("\nResultado da formatação (sem saudação):")
    print("-" * 40)
    print(result)
    print("-" * 40)
    
    checks = [
        ("💰 *ALERTAS FINANCEIROS*" in result, "Cabeçalho de alertas"),
        ("VENCE HOJE" in result, "Seção vence hoje"),
        ("VENCE AMANHÃ" in result, "Seção vence amanhã"),
        ("Conta de Luz" in result, "Conta de luz"),
        ("Salário" in result, "Salário"),
        ("Internet" in result, "Internet"),
        ("Fatura Nubank" in result, "Fatura Nubank"),
        ("150,50" in result, "Valor formatado"),
        ("🌅" not in result, "Sem saudação"),
    ]
    
    all_ok = True
    for check, desc in checks:
        if check:
            print(f"✅ {desc}")
        else:
            print(f"❌ {desc}")
            all_ok = False
    
    # Teste 3: Com saudação
    result_greeting = formatter.format(alertas, include_greeting=True)
    
    print("\nResultado da formatação (com saudação):")
    print("-" * 40)
    print(result_greeting)
    print("-" * 40)
    
    if "🌅 *Bom dia!*" in result_greeting:
        print("✅ Saudação presente com include_greeting=True")
    else:
        print("❌ Saudação deveria estar presente")
        all_ok = False
    
    return all_ok


def test_invoice_service_helper():
    """Testa se o helper get_fatura_id_if_credit_card foi adicionado."""
    print("\n" + "=" * 60)
    print("🧪 TESTE ISOLADO: get_fatura_id_if_credit_card")
    print("=" * 60)
    
    invoice_path = os.path.join(
        os.path.dirname(__file__),
        "app", "services", "finance", "invoice_service.py"
    )
    
    if not os.path.exists(invoice_path):
        print(f"❌ Arquivo não encontrado: {invoice_path}")
        return False
    
    print(f"✅ Arquivo existe: {invoice_path}")
    
    with open(invoice_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verificar se a função foi adicionada
    if "def get_fatura_id_if_credit_card(" in content:
        print("✅ Função get_fatura_id_if_credit_card encontrada")
    else:
        print("❌ Função get_fatura_id_if_credit_card NÃO encontrada")
        return False
    
    # Verificar export em __all__
    if "'get_fatura_id_if_credit_card'" in content:
        print("✅ Função exportada em __all__")
    else:
        print("❌ Função NÃO exportada em __all__")
        return False
    
    # Verificar parâmetros
    params = ["conn", "conta_id", "conta_tipo", "data_transacao", "usuario_id"]
    all_params = all(p in content for p in params)
    
    if all_params:
        print(f"✅ Todos os parâmetros presentes: {params}")
    else:
        print(f"❌ Parâmetros incompletos")
        return False
    
    # Verificar lógica
    if "if conta_tipo != 'Cartão de Crédito':" in content:
        print("✅ Verificação de tipo de conta presente")
    else:
        print("❌ Verificação de tipo de conta não encontrada")
        return False
    
    if "ensure_current_invoice_exists" in content and "get_or_create_fatura" in content:
        print("✅ Chamadas a ensure_current_invoice_exists e get_or_create_fatura presentes")
    else:
        print("❌ Lógica interna incompleta")
        return False
    
    return True


def test_daily_briefing_service_refactored():
    """Testa se daily_briefing_service.py foi refatorado corretamente."""
    print("\n" + "=" * 60)
    print("🧪 TESTE ISOLADO: daily_briefing_service.py refatorado")
    print("=" * 60)
    
    service_path = os.path.join(
        os.path.dirname(__file__),
        "app", "services", "daily_briefing_service.py"
    )
    
    if not os.path.exists(service_path):
        print(f"❌ Arquivo não encontrado: {service_path}")
        return False
    
    print(f"✅ Arquivo existe: {service_path}")
    
    with open(service_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verificar que usa o FinancialAlertFormatter
    if "from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter" in content:
        print("✅ Import do FinancialAlertFormatter presente")
    else:
        print("❌ Import do FinancialAlertFormatter NÃO encontrado")
        return False
    
    if "FinancialAlertFormatter.format(" in content:
        print("✅ Uso do FinancialAlertFormatter.format() presente")
    else:
        print("❌ Uso do FinancialAlertFormatter.format() NÃO encontrado")
        return False
    
    # Verificar que o método foi simplificado (não tem a lógica antiga inline)
    if "msg_parts = [\"💰 *ALERTAS FINANCEIROS*\"]" not in content:
        print("✅ Lógica inline removida (código simplicado)")
    else:
        print("❌ Lógica inline ainda presente (não foi refatorado)")
        return False
    
    return True


def test_daily_briefing_job_refactored():
    """Testa se daily_briefing.py (job) foi refatorado corretamente."""
    print("\n" + "=" * 60)
    print("🧪 TESTE ISOLADO: daily_briefing.py (job) refatorado")
    print("=" * 60)
    
    job_path = os.path.join(
        os.path.dirname(__file__),
        "app", "jobs", "daily_briefing.py"
    )
    
    if not os.path.exists(job_path):
        print(f"❌ Arquivo não encontrado: {job_path}")
        return False
    
    print(f"✅ Arquivo existe: {job_path}")
    
    with open(job_path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Verificar que usa o FinancialAlertFormatter
    if "from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter" in content:
        print("✅ Import do FinancialAlertFormatter presente")
    else:
        print("❌ Import do FinancialAlertFormatter NÃO encontrado")
        return False
    
    if "FinancialAlertFormatter.format(" in content:
        print("✅ Uso do FinancialAlertFormatter.format() presente")
    else:
        print("❌ Uso do FinancialAlertFormatter.format() NÃO encontrado")
        return False
    
    # Verificar que a função ainda existe para backward compatibility
    if "def format_financial_alerts_standalone(" in content:
        print("✅ Função format_financial_alerts_standalone ainda existe (backward compatible)")
    else:
        print("❌ Função format_financial_alerts_standalone removida (quebra compatibilidade)")
        return False
    
    # Verificar que lógica inline foi removida
    if "msg_parts = [\"🌅 *Bom dia!*\\n\", \"💰 *ALERTAS FINANCEIROS*\\n\"]" not in content:
        print("✅ Lógica inline removida")
    else:
        print("❌ Lógica inline ainda presente")
        return False
    
    return True


def run_all_tests():
    """Executa todos os testes isolados."""
    print("\n" + "=" * 60)
    print("🧪 TESTES ISOLADOS DA FASE E - ELIMINAR DUPLICAÇÕES")
    print("=" * 60 + "\n")
    
    tests = [
        ("FinancialAlertFormatter", test_financial_alert_formatter),
        ("get_fatura_id_if_credit_card", test_invoice_service_helper),
        ("daily_briefing_service.py refatorado", test_daily_briefing_service_refactored),
        ("daily_briefing.py (job) refatorado", test_daily_briefing_job_refactored),
    ]
    
    results = []
    for name, test_func in tests:
        try:
            passed = test_func()
            results.append((name, passed))
        except Exception as e:
            print(f"\n❌ ERRO em '{name}': {e}")
            import traceback
            traceback.print_exc()
            results.append((name, False))
    
    # Resumo
    print("\n" + "=" * 60)
    print("📊 RESUMO DOS TESTES")
    print("=" * 60)
    
    passed = sum(1 for _, p in results if p)
    total = len(results)
    
    for name, p in results:
        status = "✅ PASS" if p else "❌ FAIL"
        print(f"  {status}: {name}")
    
    print(f"\n  Total: {passed}/{total} testes passaram")
    
    if passed == total:
        print("\n🎉 TODOS OS TESTES PASSARAM! FASE E VALIDADA!")
        return 0
    else:
        print(f"\n⚠️  {total - passed} teste(s) falharam")
        return 1


if __name__ == "__main__":
    sys.exit(run_all_tests())
