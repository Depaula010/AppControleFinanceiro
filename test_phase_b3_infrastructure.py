#!/usr/bin/env python3
"""
Script de validação da infraestrutura da Fase B.3.

Testa:
1. Estrutura de diretórios criada
2. Imports da BaseIntent
3. Intent Registry funcionando
4. Utilitários base disponíveis
"""

import sys
from pathlib import Path

# Adicionar root ao path
root_dir = Path(__file__).parent
sys.path.insert(0, str(root_dir))


def test_directory_structure():
    """Verifica se estrutura de diretórios foi criada."""
    print("=" * 60)
    print("TESTE 1: Estrutura de Diretórios")
    print("=" * 60)

    base_path = Path("app/routes/webhooks")
    expected_dirs = [
        base_path,
        base_path / "intents",
    ]

    expected_files = [
        base_path / "__init__.py",
        base_path / "base.py",
        base_path / "intents" / "__init__.py",
        base_path / "intents" / "base_intent.py",
    ]

    all_ok = True

    # Verificar diretórios
    for dir_path in expected_dirs:
        if dir_path.exists() and dir_path.is_dir():
            print(f"[OK] {dir_path}/")
        else:
            print(f"[ERRO] Diretorio nao encontrado: {dir_path}/")
            all_ok = False

    print()

    # Verificar arquivos
    for file_path in expected_files:
        if file_path.exists() and file_path.is_file():
            print(f"[OK] {file_path}")
        else:
            print(f"[ERRO] Arquivo nao encontrado: {file_path}")
            all_ok = False

    print()
    return all_ok


def test_base_intent_import():
    """Testa import da BaseIntent."""
    print("=" * 60)
    print("TESTE 2: Import da BaseIntent")
    print("=" * 60)

    try:
        from app.routes.webhooks.intents.base_intent import BaseIntent
        print("[OK] BaseIntent importada com sucesso")

        # Verificar métodos abstratos
        abstract_methods = [
            'extract_params',
            'execute',
        ]

        for method in abstract_methods:
            if hasattr(BaseIntent, method):
                print(f"[OK] Metodo abstrato '{method}' presente")
            else:
                print(f"[ERRO] Metodo abstrato '{method}' ausente")
                return False

        # Verificar método template
        if hasattr(BaseIntent, 'handle'):
            print("[OK] Metodo template 'handle' presente")
        else:
            print("[ERRO] Metodo template 'handle' ausente")
            return False

        print()
        return True

    except Exception as e:
        print(f"[ERRO] Falha ao importar BaseIntent: {e}")
        print()
        return False


def test_intent_registry():
    """Testa Intent Registry."""
    print("=" * 60)
    print("TESTE 3: Intent Registry")
    print("=" * 60)

    try:
        from app.routes.webhooks.intents import (
            INTENT_REGISTRY,
            route_intent,
            register_intent,
            list_registered_intents
        )

        print("[OK] Intent registry importado com sucesso")

        # Verificar que registry está vazio (ainda não populado)
        print(f"[OK] Registry inicial: {len(INTENT_REGISTRY)} intents")

        # Testar list_registered_intents
        intents = list_registered_intents()
        print(f"[OK] list_registered_intents() retorna: {intents}")

        # Verificar que route_intent é callable
        if callable(route_intent):
            print("[OK] route_intent é callable")
        else:
            print("[ERRO] route_intent nao é callable")
            return False

        print()
        return True

    except Exception as e:
        print(f"[ERRO] Falha no intent registry: {e}")
        import traceback
        traceback.print_exc()
        print()
        return False


def test_base_utilities():
    """Testa utilitários base."""
    print("=" * 60)
    print("TESTE 4: Utilitarios Base")
    print("=" * 60)

    try:
        from app.routes.webhooks.base import (
            success_response,
            error_response,
            require_api_key_auth,
            require_db_engine,
            MSG_NOT_UNDERSTOOD
        )

        print("[OK] Utilitarios base importados")

        # Verificar helpers
        if callable(success_response):
            print("[OK] success_response é callable")
        else:
            print("[ERRO] success_response nao é callable")
            return False

        if callable(error_response):
            print("[OK] error_response é callable")
        else:
            print("[ERRO] error_response nao é callable")
            return False

        # Verificar decorators
        if callable(require_api_key_auth):
            print("[OK] require_api_key_auth é decorator")
        else:
            print("[ERRO] require_api_key_auth nao é callable")
            return False

        # Verificar constantes
        if isinstance(MSG_NOT_UNDERSTOOD, str) and len(MSG_NOT_UNDERSTOOD) > 0:
            print("[OK] MSG_NOT_UNDERSTOOD definida")
        else:
            print("[ERRO] MSG_NOT_UNDERSTOOD invalida")
            return False

        print()
        return True

    except Exception as e:
        print(f"[ERRO] Falha nos utilitarios base: {e}")
        import traceback
        traceback.print_exc()
        print()
        return False


def test_blueprint_creation():
    """Testa criação do Blueprint."""
    print("=" * 60)
    print("TESTE 5: Blueprint Principal")
    print("=" * 60)

    try:
        from app.routes.webhooks import webhooks_bp

        print("[OK] webhooks_bp importado")

        # Verificar que é um Blueprint
        from flask import Blueprint
        if isinstance(webhooks_bp, Blueprint):
            print(f"[OK] webhooks_bp é um Blueprint (name='{webhooks_bp.name}')")
        else:
            print("[ERRO] webhooks_bp nao é um Blueprint")
            return False

        print()
        return True

    except Exception as e:
        print(f"[ERRO] Falha ao criar blueprint: {e}")
        import traceback
        traceback.print_exc()
        print()
        return False


def main():
    """Executa todos os testes."""
    print("\n" + "=" * 60)
    print("VALIDACAO DA INFRAESTRUTURA - FASE B.3")
    print("=" * 60 + "\n")

    tests = [
        ("Estrutura de Diretorios", test_directory_structure),
        ("Import da BaseIntent", test_base_intent_import),
        ("Intent Registry", test_intent_registry),
        ("Utilitarios Base", test_base_utilities),
        ("Blueprint Principal", test_blueprint_creation),
    ]

    results = {}
    for test_name, test_func in tests:
        try:
            results[test_name] = test_func()
        except Exception as e:
            print(f"[ERRO] Erro ao executar teste '{test_name}': {e}\n")
            results[test_name] = False

    # Resumo
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
        print("Infraestrutura da Fase B.3 esta pronta")
        print("\nProximos passos:")
        print("1. Criar primeiro intent handler (RendaIntent ou ConsultaSaldoIntent)")
        print("2. Registrar no INTENT_REGISTRY")
        print("3. Testar via route_intent()")
    else:
        print("ALGUNS TESTES FALHARAM")
        print("Verifique os erros acima")
    print("=" * 60 + "\n")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
