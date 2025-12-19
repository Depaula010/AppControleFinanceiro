#!/usr/bin/env python3
"""
Script de validação de sintaxe da Fase B.3.

Testa apenas sintaxe Python, sem executar imports que dependem de env vars.
"""

import py_compile
import sys
from pathlib import Path


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


def test_syntax():
    """Verifica sintaxe Python dos arquivos criados."""
    print("=" * 60)
    print("TESTE 2: Sintaxe Python")
    print("=" * 60)

    files_to_check = [
        "app/routes/webhooks/__init__.py",
        "app/routes/webhooks/base.py",
        "app/routes/webhooks/intents/__init__.py",
        "app/routes/webhooks/intents/base_intent.py",
    ]

    all_valid = True
    for filepath in files_to_check:
        try:
            py_compile.compile(filepath, doraise=True)
            print(f"[OK] {filepath}")
        except py_compile.PyCompileError as e:
            print(f"[ERRO] {filepath}: {e}")
            all_valid = False

    print()
    return all_valid


def test_file_content():
    """Verifica conteúdo esperado nos arquivos."""
    print("=" * 60)
    print("TESTE 3: Conteudo dos Arquivos")
    print("=" * 60)

    checks = [
        # BaseIntent deve ter métodos abstratos
        {
            "file": "app/routes/webhooks/intents/base_intent.py",
            "contains": ["class BaseIntent", "@abstractmethod", "def handle"],
            "name": "BaseIntent class"
        },
        # Intent Registry deve ter INTENT_REGISTRY
        {
            "file": "app/routes/webhooks/intents/__init__.py",
            "contains": ["INTENT_REGISTRY", "route_intent", "register_intent"],
            "name": "Intent Registry"
        },
        # Base utilities deve ter helpers
        {
            "file": "app/routes/webhooks/base.py",
            "contains": ["success_response", "error_response", "require_api_key_auth"],
            "name": "Base utilities"
        },
        # Webhooks __init__ deve ter blueprint
        {
            "file": "app/routes/webhooks/__init__.py",
            "contains": ["webhooks_bp", "Blueprint"],
            "name": "Webhooks blueprint"
        },
    ]

    all_ok = True
    for check in checks:
        filepath = Path(check["file"])
        if not filepath.exists():
            print(f"[ERRO] {check['name']}: arquivo nao encontrado")
            all_ok = False
            continue

        content = filepath.read_text(encoding='utf-8')
        missing = []

        for expected in check["contains"]:
            if expected not in content:
                missing.append(expected)

        if missing:
            print(f"[ERRO] {check['name']}: faltando {missing}")
            all_ok = False
        else:
            print(f"[OK] {check['name']}")

    print()
    return all_ok


def main():
    """Executa todos os testes."""
    print("\n" + "=" * 60)
    print("VALIDACAO DE SINTAXE - FASE B.3 INFRAESTRUTURA")
    print("=" * 60 + "\n")

    tests = [
        ("Estrutura de Diretorios", test_directory_structure),
        ("Sintaxe Python", test_syntax),
        ("Conteudo dos Arquivos", test_file_content),
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
        print("Infraestrutura da Fase B.3 - Fase 1 CONCLUIDA")
        print("\nArquivos criados:")
        print("- app/routes/webhooks/__init__.py")
        print("- app/routes/webhooks/base.py")
        print("- app/routes/webhooks/intents/__init__.py")
        print("- app/routes/webhooks/intents/base_intent.py")
        print("\nProximos passos:")
        print("Fase 2: Extrair rotas simples (calendar, reserves, transactions)")
    else:
        print("ALGUNS TESTES FALHARAM")
        print("Verifique os erros acima")
    print("=" * 60 + "\n")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
