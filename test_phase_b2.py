#!/usr/bin/env python3
"""
Script de validação da Fase B.2 - Refatoração de finance_service.py

Testa:
1. Imports de todos os módulos
2. Facade re-exportando todas as 34 funções
3. Sem erros de sintaxe
4. Funções acessíveis de múltiplas formas
"""

import sys
from pathlib import Path

# Adicionar o diretório raiz ao PYTHONPATH
root_dir = Path(__file__).parent
sys.path.insert(0, str(root_dir))

def test_imports():
    """Testa se todos os módulos podem ser importados sem erros."""
    print("=" * 60)
    print("TESTE 1: Importando módulos individuais")
    print("=" * 60)

    modules_to_test = [
        'app.services.finance._database',
        'app.services.finance.user_service',
        'app.services.finance.pot_service',
        'app.services.finance.emergency_reserve_service',
        'app.services.finance.installment_service',
        'app.services.finance.text_utils',
        'app.services.finance.category_service',
        'app.services.finance.account_service',
        'app.services.finance.transaction_service',
        'app.services.finance.invoice_service',
        'app.services.finance.bills_service',
        'app.services.finance.setup_service',
    ]

    for module_name in modules_to_test:
        try:
            __import__(module_name)
            print(f"[OK] {module_name}")
        except Exception as e:
            print(f"[ERRO] {module_name}: {e}")
            return False

    print()
    return True


def test_facade_exports():
    """Testa se o facade __init__.py re-exporta todas as 34 funções."""
    print("=" * 60)
    print("TESTE 2: Validando facade (app.services.finance)")
    print("=" * 60)

    expected_functions = [
        # User (2)
        'get_user_by_api_key',
        'get_user_by_whatsapp',
        # Pot (1)
        'get_pote_status',
        # Emergency Reserve (1)
        'get_reserva_status',
        # Installment (1)
        'create_parcelamento_agendamento',
        # Text Utils (1)
        'extract_mentioned_account',
        # Category (4)
        'get_user_categories',
        'get_fallback_category_id',
        'get_category_name_by_id',
        'get_category_spending',
        # Account (8)
        'get_user_accounts',
        'get_account_by_name',
        'get_account_details_by_name',
        'get_saldo_contas',
        'update_saldo_inicial',
        'get_user_default_accounts',
        'set_user_default_account',
        'choose_account_for_transaction',
        # Transaction (3)
        'create_transaction',
        'create_transfer_pair',
        'create_fatura_payment',
        # Invoice (3)
        'get_or_create_fatura',
        'ensure_current_invoice_exists',
        'get_fatura_valor',
        # Bills (3)
        'get_upcoming_bills_and_invoices',
        'get_vencimentos_periodo',
        'format_vencimentos_message',
        # Setup (7)
        'clear_bot_session',
        'setup_database_schema',
        'populate_global_categories',
        'setup_user_data',
        'add_google_calendar_tokens_table',
        'add_nightly_checkin_config_columns',
        'criar_tabelas_chaves_api',
    ]

    try:
        import app.services.finance as finance

        missing_functions = []
        for func_name in expected_functions:
            if not hasattr(finance, func_name):
                missing_functions.append(func_name)
                print(f"[ERRO] Funcao ausente: {func_name}")
            else:
                print(f"[OK] {func_name}")

        if missing_functions:
            print(f"\n[ERRO] {len(missing_functions)} funcoes faltando no facade!")
            return False

        print(f"\n[OK] Todas as {len(expected_functions)} funcoes estao acessiveis!")
        print()
        return True

    except Exception as e:
        print(f"[ERRO] Erro ao importar facade: {e}")
        return False


def test_direct_imports():
    """Testa importação direta dos módulos."""
    print("=" * 60)
    print("TESTE 3: Importação direta de funções específicas")
    print("=" * 60)

    test_cases = [
        ('app.services.finance.user_service', 'get_user_by_whatsapp'),
        ('app.services.finance.account_service', 'get_saldo_contas'),
        ('app.services.finance.transaction_service', 'create_transaction'),
        ('app.services.finance.invoice_service', 'get_or_create_fatura'),
        ('app.services.finance.bills_service', 'get_vencimentos_periodo'),
        ('app.services.finance.setup_service', 'setup_database_schema'),
    ]

    for module_name, func_name in test_cases:
        try:
            module = __import__(module_name, fromlist=[func_name])
            func = getattr(module, func_name)
            print(f"[OK] from {module_name} import {func_name}")
        except Exception as e:
            print(f"[ERRO] from {module_name} import {func_name}: {e}")
            return False

    print()
    return True


def test_module_structure():
    """Verifica a estrutura de arquivos criada."""
    print("=" * 60)
    print("TESTE 4: Estrutura de arquivos")
    print("=" * 60)

    base_path = Path("app/services/finance")
    expected_files = [
        '__init__.py',
        '_database.py',
        'user_service.py',
        'pot_service.py',
        'emergency_reserve_service.py',
        'installment_service.py',
        'text_utils.py',
        'category_service.py',
        'account_service.py',
        'transaction_service.py',
        'invoice_service.py',
        'bills_service.py',
        'setup_service.py',
    ]

    all_exist = True
    for filename in expected_files:
        filepath = base_path / filename
        if filepath.exists():
            print(f"[OK] {filepath}")
        else:
            print(f"[ERRO] Arquivo nao encontrado: {filepath}")
            all_exist = False

    print()
    return all_exist


def main():
    """Executa todos os testes de validação."""
    print("\n" + "=" * 60)
    print("VALIDAÇÃO DA FASE B.2 - REFATORAÇÃO DE FINANCE_SERVICE.PY")
    print("=" * 60 + "\n")

    tests = [
        ("Estrutura de Arquivos", test_module_structure),
        ("Imports de Módulos", test_imports),
        ("Facade Completo", test_facade_exports),
        ("Importação Direta", test_direct_imports),
    ]

    results = {}
    for test_name, test_func in tests:
        try:
            results[test_name] = test_func()
        except Exception as e:
            print(f"[ERRO] Erro ao executar teste '{test_name}': {e}\n")
            results[test_name] = False

    # Resumo final
    print("=" * 60)
    print("RESUMO DOS TESTES")
    print("=" * 60)

    for test_name, passed in results.items():
        status = "[PASSOU]" if passed else "[FALHOU]"
        print(f"{status} - {test_name}")

    all_passed = all(results.values())

    print("\n" + "=" * 60)
    if all_passed:
        print("TODOS OS TESTES PASSARAM!")
        print("Fase B.2 esta 100% funcional e validada")
    else:
        print("ALGUNS TESTES FALHARAM")
        print("Verifique os erros acima para corrigir")
    print("=" * 60 + "\n")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
