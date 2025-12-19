#!/usr/bin/env python3
"""
Script de validação de sintaxe da Fase B.2

Testa apenas a sintaxe dos módulos, sem executar imports que dependem de env vars.
"""

import py_compile
import sys
from pathlib import Path


def test_syntax():
    """Verifica se todos os módulos têm sintaxe Python válida."""
    print("=" * 60)
    print("TESTE DE SINTAXE - FASE B.2")
    print("=" * 60 + "\n")

    base_path = Path("app/services/finance")
    modules = [
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

    all_valid = True
    for module_name in modules:
        filepath = base_path / module_name
        try:
            py_compile.compile(str(filepath), doraise=True)
            print(f"[OK] {module_name}")
        except py_compile.PyCompileError as e:
            print(f"[ERRO] {module_name}: {e}")
            all_valid = False

    return all_valid


def test_exports():
    """Verifica se __init__.py exporta as 34 funções esperadas."""
    print("\n" + "=" * 60)
    print("TESTE DE EXPORTS - __init__.py")
    print("=" * 60 + "\n")

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

    # Ler o arquivo __init__.py
    init_path = Path("app/services/finance/__init__.py")
    content = init_path.read_text(encoding='utf-8')

    missing_exports = []
    for func_name in expected_functions:
        if f"'{func_name}'" in content and '__all__' in content:
            # Verificar se está no __all__
            all_section = content[content.find('__all__'):]
            if f"'{func_name}'" in all_section:
                print(f"[OK] {func_name} exportado em __all__")
            else:
                print(f"[AVISO] {func_name} nao esta em __all__")
                missing_exports.append(func_name)
        else:
            print(f"[ERRO] {func_name} nao encontrado")
            missing_exports.append(func_name)

    if missing_exports:
        print(f"\n[ERRO] {len(missing_exports)} funcoes faltando!")
        return False

    print(f"\n[OK] Todas as {len(expected_functions)} funcoes estao exportadas!")
    return True


def test_imports_in_init():
    """Verifica se todos os imports estão corretos no __init__.py."""
    print("\n" + "=" * 60)
    print("TESTE DE IMPORTS - __init__.py")
    print("=" * 60 + "\n")

    init_path = Path("app/services/finance/__init__.py")
    content = init_path.read_text(encoding='utf-8')

    expected_imports = [
        ('user_service', ['get_user_by_api_key', 'get_user_by_whatsapp']),
        ('pot_service', ['get_pote_status']),
        ('emergency_reserve_service', ['get_reserva_status']),
        ('installment_service', ['create_parcelamento_agendamento']),
        ('text_utils', ['extract_mentioned_account']),
        ('category_service', ['get_user_categories', 'get_fallback_category_id', 'get_category_name_by_id', 'get_category_spending']),
        ('account_service', ['get_user_accounts', 'get_account_by_name', 'get_account_details_by_name', 'get_saldo_contas', 'update_saldo_inicial', 'get_user_default_accounts', 'set_user_default_account', 'choose_account_for_transaction']),
        ('transaction_service', ['create_transaction', 'create_transfer_pair', 'create_fatura_payment']),
        ('invoice_service', ['get_or_create_fatura', 'ensure_current_invoice_exists', 'get_fatura_valor']),
        ('bills_service', ['get_upcoming_bills_and_invoices', 'get_vencimentos_periodo', 'format_vencimentos_message']),
        ('setup_service', ['clear_bot_session', 'setup_database_schema', 'populate_global_categories', 'setup_user_data', 'add_google_calendar_tokens_table', 'add_nightly_checkin_config_columns', 'criar_tabelas_chaves_api']),
    ]

    all_imports_ok = True
    for module_name, functions in expected_imports:
        if f"from .{module_name} import" in content:
            print(f"[OK] Import de {module_name}")
            for func in functions:
                if func not in content:
                    print(f"  [ERRO] Funcao {func} nao importada de {module_name}")
                    all_imports_ok = False
        else:
            print(f"[ERRO] Import de {module_name} nao encontrado")
            all_imports_ok = False

    return all_imports_ok


def main():
    """Executa todos os testes de validação."""
    print("\n" + "=" * 60)
    print("VALIDACAO DE SINTAXE - FASE B.2")
    print("=" * 60 + "\n")

    tests = [
        ("Sintaxe dos Modulos", test_syntax),
        ("Exports Completos", test_exports),
        ("Imports Corretos", test_imports_in_init),
    ]

    results = {}
    for test_name, test_func in tests:
        try:
            results[test_name] = test_func()
        except Exception as e:
            print(f"[ERRO] Erro ao executar teste '{test_name}': {e}\n")
            results[test_name] = False

    # Resumo final
    print("\n" + "=" * 60)
    print("RESUMO DOS TESTES")
    print("=" * 60)

    for test_name, passed in results.items():
        status = "[PASSOU]" if passed else "[FALHOU]"
        print(f"{status} - {test_name}")

    all_passed = all(results.values())

    print("\n" + "=" * 60)
    if all_passed:
        print("TODOS OS TESTES PASSARAM!")
        print("Fase B.2 - Sintaxe validada com sucesso")
        print("\nPara testes funcionais completos:")
        print("1. Configure as variaveis de ambiente (.env)")
        print("2. Execute python test_phase_b2.py")
    else:
        print("ALGUNS TESTES FALHARAM")
        print("Verifique os erros acima para corrigir")
    print("=" * 60 + "\n")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
