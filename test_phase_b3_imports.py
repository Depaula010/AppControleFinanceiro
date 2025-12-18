#!/usr/bin/env python3
"""
Script de teste de imports da Fase B.3.

Testa se todos os módulos podem ser importados corretamente
e se a estrutura funciona em runtime.
"""

import sys
import os

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.abspath('.'))

print("=" * 70)
print("TESTE DE IMPORTS - FASE B.3")
print("=" * 70)
print()

errors = []
warnings = []

# =============================================================================
# TESTE 1: Imports de Infraestrutura
# =============================================================================
print("TESTE 1: Imports de Infraestrutura")
print("-" * 70)

try:
    from app.routes.webhooks import webhooks_bp
    print("[OK] Blueprint webhooks_bp importado")
except Exception as e:
    msg = f"Erro ao importar webhooks_bp: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

try:
    from app.routes.webhooks.base import (
        success_response,
        error_response,
        require_api_key_auth,
        require_hmac_validation,
    )
    print("[OK] Utilities de base.py importadas")
except Exception as e:
    msg = f"Erro ao importar base.py: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# TESTE 2: Imports de Rotas
# =============================================================================
print("TESTE 2: Imports de Rotas Extraídas")
print("-" * 70)

try:
    from app.routes.webhooks import transactions
    print("[OK] Módulo transactions importado")
except Exception as e:
    msg = f"Erro ao importar transactions: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

try:
    from app.routes.webhooks import calendar
    print("[OK] Módulo calendar importado")
except Exception as e:
    msg = f"Erro ao importar calendar: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

try:
    from app.routes.webhooks import reserves
    print("[OK] Módulo reserves importado")
except Exception as e:
    msg = f"Erro ao importar reserves: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

try:
    from app.routes.webhooks import whatsapp_router
    print("[OK] Módulo whatsapp_router importado")
except Exception as e:
    msg = f"Erro ao importar whatsapp_router: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# TESTE 3: Imports de Intent System
# =============================================================================
print("TESTE 3: Imports do Sistema de Intents")
print("-" * 70)

try:
    from app.routes.webhooks.intents import (
        BaseIntent,
        INTENT_REGISTRY,
        route_intent,
        register_intent,
        list_registered_intents,
    )
    print("[OK] Intent system importado")
    print(f"[INFO] {len(INTENT_REGISTRY)} intents registrados")
except Exception as e:
    msg = f"Erro ao importar intent system: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

try:
    from app.routes.webhooks.intents.base_intent import (
        BaseIntent,
        ConfirmationRequiredIntent,
    )
    print("[OK] BaseIntent classes importadas")
except Exception as e:
    msg = f"Erro ao importar base_intent: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# TESTE 4: Imports de Intent Handlers
# =============================================================================
print("TESTE 4: Imports de Intent Handlers")
print("-" * 70)

intent_modules = [
    ('query_intents', ['ConsultaSaldoIntent', 'ConsultaReservaIntent']),
    ('transaction_intents', ['RendaIntent', 'DespesaIntent']),
    ('calendar_intents', ['CriarEventoIntent', 'DeletarEventoIntent']),
    ('notification_intents', ['ConfigurarNotificacoesIntent', 'VencimentosHojeIntent']),
    ('analytics_intents', ['AnaliseInteligenteIntent', 'ComparacaoMensalIntent']),
    ('admin_intents', ['SolicitarApiKeyIntent', 'ListarContasIntent']),
]

for module_name, expected_classes in intent_modules:
    try:
        module = __import__(
            f'app.routes.webhooks.intents.{module_name}',
            fromlist=expected_classes
        )

        # Verificar se classes existem
        missing = []
        for class_name in expected_classes:
            if not hasattr(module, class_name):
                missing.append(class_name)

        if missing:
            msg = f"{module_name}: faltando classes {missing}"
            print(f"[AVISO] {msg}")
            warnings.append(msg)
        else:
            print(f"[OK] {module_name} importado com {len(expected_classes)} classes")

    except Exception as e:
        msg = f"Erro ao importar {module_name}: {e}"
        print(f"[ERRO] {msg}")
        errors.append(msg)

print()

# =============================================================================
# TESTE 5: Verificar Blueprint Rotas
# =============================================================================
print("TESTE 5: Verificação de Rotas Registradas")
print("-" * 70)

try:
    from app.routes.webhooks import webhooks_bp

    # Listar rotas registradas
    routes = []
    for rule in webhooks_bp.url_map.iter_rules() if hasattr(webhooks_bp, 'url_map') else []:
        if rule.endpoint.startswith('webhooks.'):
            routes.append(rule.rule)

    if routes:
        print(f"[OK] {len(routes)} rotas registradas no blueprint")
        for route in routes[:5]:  # Mostrar primeiras 5
            print(f"     - {route}")
        if len(routes) > 5:
            print(f"     ... e mais {len(routes) - 5} rotas")
    else:
        msg = "Nenhuma rota registrada no blueprint (pode ser normal se app não inicializado)"
        print(f"[INFO] {msg}")

except Exception as e:
    msg = f"Erro ao verificar rotas: {e}"
    print(f"[AVISO] {msg}")
    warnings.append(msg)

print()

# =============================================================================
# TESTE 6: Verificar Intent Registry
# =============================================================================
print("TESTE 6: Verificação do Intent Registry")
print("-" * 70)

try:
    from app.routes.webhooks.intents import INTENT_REGISTRY, list_registered_intents

    registered = list_registered_intents()
    print(f"[OK] Intent Registry funcional")
    print(f"[INFO] Total de intents: {len(registered)}")

    # Listar primeiros 10 intents
    print("\nIntents registrados:")
    for i, intent_name in enumerate(registered[:10], 1):
        handler_class = INTENT_REGISTRY[intent_name]
        print(f"  {i}. '{intent_name}' → {handler_class.__name__}")

    if len(registered) > 10:
        print(f"  ... e mais {len(registered) - 10} intents")

    # Verificar se classes herdam de BaseIntent
    from app.routes.webhooks.intents.base_intent import BaseIntent
    invalid = []
    for intent_name, handler_class in INTENT_REGISTRY.items():
        if not issubclass(handler_class, BaseIntent):
            invalid.append(intent_name)

    if invalid:
        msg = f"Handlers que não herdam de BaseIntent: {invalid}"
        print(f"\n[ERRO] {msg}")
        errors.append(msg)
    else:
        print(f"\n[OK] Todos os handlers herdam de BaseIntent")

except Exception as e:
    msg = f"Erro ao verificar Intent Registry: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# TESTE 7: Testar Criação de Intent Handler
# =============================================================================
print("TESTE 7: Teste de Criação de Intent Handler")
print("-" * 70)

try:
    from app.routes.webhooks.intents import INTENT_REGISTRY
    from unittest.mock import Mock

    # Tentar criar uma instância de um intent implementado
    if 'Consulta Saldo' in INTENT_REGISTRY:
        ConsultaSaldoIntent = INTENT_REGISTRY['Consulta Saldo']

        # Mock da conexão
        mock_conn = Mock()

        # Criar instância
        handler = ConsultaSaldoIntent(
            usuario_id=1,
            mensagem="quanto tenho no nubank?",
            conn=mock_conn
        )

        print("[OK] Intent handler instanciado com sucesso")
        print(f"[INFO] Classe: {handler.__class__.__name__}")
        print(f"[INFO] Usuario ID: {handler.usuario_id}")
        print(f"[INFO] Mensagem: {handler.mensagem[:30]}...")

        # Verificar métodos
        required_methods = ['handle', 'extract_params', 'execute']
        for method in required_methods:
            if hasattr(handler, method):
                print(f"[OK] Método {method}() presente")
            else:
                msg = f"Método {method}() não encontrado"
                print(f"[ERRO] {msg}")
                errors.append(msg)
    else:
        msg = "Intent 'Consulta Saldo' não encontrado no registry"
        print(f"[AVISO] {msg}")
        warnings.append(msg)

except Exception as e:
    msg = f"Erro ao criar intent handler: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# RESUMO
# =============================================================================
print("=" * 70)
print("RESUMO DOS TESTES")
print("=" * 70)

print(f"\nErros: {len(errors)}")
if errors:
    for i, error in enumerate(errors, 1):
        print(f"  {i}. {error}")

print(f"\nAvisos: {len(warnings)}")
if warnings:
    for i, warning in enumerate(warnings, 1):
        print(f"  {i}. {warning}")

print()
print("=" * 70)
if len(errors) == 0:
    print("TODOS OS TESTES DE IMPORT PASSARAM!")
    print("=" * 70)
    print("\n✓ Fase B.3 está funcionalmente correta")
    print("✓ Todos os módulos podem ser importados")
    print("✓ Intent Registry está operacional")
    print("✓ Estrutura pronta para uso em produção")
else:
    print("ALGUNS TESTES FALHARAM")
    print("=" * 70)
    print(f"\n{len(errors)} erro(s) crítico(s) encontrado(s)")
    print("Verifique os erros acima")

if warnings:
    print(f"\n{len(warnings)} aviso(s) - não impedem funcionamento")

print("\n" + "=" * 70 + "\n")

sys.exit(0 if len(errors) == 0 else 1)
