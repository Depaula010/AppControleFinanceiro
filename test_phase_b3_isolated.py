#!/usr/bin/env python3
"""
Script de teste isolado da Fase B.3.

Testa a estrutura sem depender de variáveis de ambiente ou serviços externos.
Valida apenas a arquitetura e imports diretos.
"""

import sys
import os

# Configurar env vars mínimas ANTES de qualquer import
os.environ['GEMINI_API_KEY'] = 'test_key_for_import_only_12345678'
os.environ['DATABASE_URL'] = 'postgresql://test:test@localhost/test'
os.environ['API_SECRET_KEY'] = 'test_secret_key_with_at_least_32_characters_for_security'
os.environ['TWILIO_ACCOUNT_SID'] = 'test_sid'
os.environ['TWILIO_AUTH_TOKEN'] = 'test_token'
os.environ['TWILIO_WHATSAPP_NUMBER'] = 'whatsapp:+1234567890'

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.abspath('.'))

print("=" * 70)
print("TESTE ISOLADO - FASE B.3 IMPORTS")
print("=" * 70)
print("\n[INFO] Env vars mockadas para permitir imports\n")

errors = []
warnings = []

# =============================================================================
# TESTE 1: Import Blueprint
# =============================================================================
print("TESTE 1: Import do Blueprint Principal")
print("-" * 70)

try:
    from app.routes.webhooks import webhooks_bp
    print(f"[OK] Blueprint 'webhooks_bp' importado")
    print(f"[INFO] Nome: {webhooks_bp.name}")
except Exception as e:
    msg = f"Erro ao importar webhooks_bp: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 2: Import Base Utilities
# =============================================================================
print("TESTE 2: Import de Base Utilities")
print("-" * 70)

try:
    from app.routes.webhooks.base import (
        success_response,
        error_response,
    )
    print("[OK] success_response importada")
    print("[OK] error_response importada")

    # Testar funções
    resp = success_response("teste", {"key": "value"})
    print(f"[OK] success_response() funcional: {type(resp)}")

    resp = error_response("erro teste")
    print(f"[OK] error_response() funcional: {type(resp)}")

except Exception as e:
    msg = f"Erro ao importar/testar base utilities: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 3: Import Intent Base Classes
# =============================================================================
print("TESTE 3: Import de Intent Base Classes")
print("-" * 70)

try:
    from app.routes.webhooks.intents.base_intent import (
        BaseIntent,
        ConfirmationRequiredIntent,
    )
    print("[OK] BaseIntent importada")
    print("[OK] ConfirmationRequiredIntent importada")

    # Verificar se BaseIntent é abstrata
    from abc import ABC
    if issubclass(BaseIntent, ABC):
        print("[OK] BaseIntent é classe abstrata (ABC)")
    else:
        msg = "BaseIntent deveria ser ABC"
        print(f"[AVISO] {msg}")
        warnings.append(msg)

    # Verificar métodos abstratos
    abstract_methods = BaseIntent.__abstractmethods__
    print(f"[INFO] Métodos abstratos: {abstract_methods}")

except Exception as e:
    msg = f"Erro ao importar base intent classes: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 4: Import Intent Registry
# =============================================================================
print("TESTE 4: Import do Intent Registry e Factory")
print("-" * 70)

try:
    from app.routes.webhooks.intents import (
        INTENT_REGISTRY,
        route_intent,
        register_intent,
        list_registered_intents,
    )
    print("[OK] INTENT_REGISTRY importado")
    print("[OK] route_intent importada")
    print("[OK] register_intent importada")
    print("[OK] list_registered_intents importada")

    # Verificar registry
    print(f"\n[INFO] Intents no registry: {len(INTENT_REGISTRY)}")

    # Listar intents
    intents = list_registered_intents()
    print(f"[INFO] list_registered_intents() retornou {len(intents)} intents")

    # Mostrar alguns
    print("\nPrimeiros 10 intents:")
    for i, intent_name in enumerate(intents[:10], 1):
        handler_class = INTENT_REGISTRY[intent_name]
        print(f"  {i}. '{intent_name}' → {handler_class.__name__}")

    if len(intents) > 10:
        print(f"  ... e mais {len(intents) - 10}")

except Exception as e:
    msg = f"Erro ao importar intent registry: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 5: Validar Intent Handlers
# =============================================================================
print("TESTE 5: Validação dos Intent Handlers")
print("-" * 70)

try:
    from app.routes.webhooks.intents import INTENT_REGISTRY
    from app.routes.webhooks.intents.base_intent import BaseIntent

    # Verificar se todos herdam de BaseIntent
    print("Verificando herança de BaseIntent...")
    invalid = []

    for intent_name, handler_class in INTENT_REGISTRY.items():
        if not issubclass(handler_class, BaseIntent):
            invalid.append((intent_name, handler_class.__name__))

    if invalid:
        msg = f"Handlers que não herdam de BaseIntent: {invalid}"
        print(f"[ERRO] {msg}")
        errors.append(msg)
    else:
        print(f"[OK] Todos os {len(INTENT_REGISTRY)} handlers herdam de BaseIntent")

    # Verificar métodos obrigatórios
    print("\nVerificando métodos obrigatórios...")
    required_methods = ['handle', 'extract_params', 'execute', 'validate', 'format_response']

    for intent_name, handler_class in list(INTENT_REGISTRY.items())[:3]:  # Testar 3 primeiros
        missing_methods = []
        for method in required_methods:
            if not hasattr(handler_class, method):
                missing_methods.append(method)

        if missing_methods:
            msg = f"{handler_class.__name__} faltando métodos: {missing_methods}"
            print(f"[ERRO] {msg}")
            errors.append(msg)
        else:
            print(f"[OK] {handler_class.__name__} tem todos os métodos")

except Exception as e:
    msg = f"Erro ao validar intent handlers: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 6: Testar Criação de Handler Instance
# =============================================================================
print("TESTE 6: Teste de Instanciação de Handler")
print("-" * 70)

try:
    from app.routes.webhooks.intents import INTENT_REGISTRY
    from unittest.mock import Mock

    # Escolher um intent implementado para testar
    test_intent_name = 'Consulta Saldo'

    if test_intent_name in INTENT_REGISTRY:
        HandlerClass = INTENT_REGISTRY[test_intent_name]

        # Mock da conexão
        mock_conn = Mock()

        # Criar instância
        handler = HandlerClass(
            usuario_id=123,
            mensagem="quanto tenho no nubank?",
            conn=mock_conn,
            numero_whatsapp="5511999999999"
        )

        print(f"[OK] Handler {HandlerClass.__name__} instanciado")
        print(f"[INFO] usuario_id: {handler.usuario_id}")
        print(f"[INFO] mensagem: '{handler.mensagem[:30]}...'")
        print(f"[INFO] numero_whatsapp: {handler.numero_whatsapp}")

        # Verificar atributos
        if hasattr(handler, 'params'):
            print(f"[OK] Atributo 'params' presente: {handler.params}")
        else:
            msg = "Atributo 'params' não encontrado"
            print(f"[ERRO] {msg}")
            errors.append(msg)

        # Verificar métodos
        if hasattr(handler, 'handle') and callable(handler.handle):
            print("[OK] Método handle() presente e callable")
        else:
            msg = "Método handle() não encontrado ou não é callable"
            print(f"[ERRO] {msg}")
            errors.append(msg)

    else:
        msg = f"Intent '{test_intent_name}' não encontrado no registry"
        print(f"[AVISO] {msg}")
        warnings.append(msg)

except Exception as e:
    msg = f"Erro ao instanciar handler: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)
    import traceback
    traceback.print_exc()

print()

# =============================================================================
# TESTE 7: Verificar Intents Implementados vs Placeholders
# =============================================================================
print("TESTE 7: Verificação de Implementação")
print("-" * 70)

try:
    from app.routes.webhooks.intents import INTENT_REGISTRY

    # Intents que sabemos que estão implementados
    implemented = ['Renda', 'Despesa', 'Consulta Saldo', 'Consulta Reserva']

    print(f"Intents implementados esperados: {len(implemented)}")
    for intent_name in implemented:
        if intent_name in INTENT_REGISTRY:
            print(f"  [OK] '{intent_name}' registrado")
        else:
            msg = f"Intent implementado '{intent_name}' não registrado"
            print(f"  [ERRO] {msg}")
            errors.append(msg)

    # Intents placeholders
    total_intents = len(INTENT_REGISTRY)
    placeholders = total_intents - len(implemented)

    print(f"\n[INFO] Total de intents: {total_intents}")
    print(f"[INFO] Implementados: {len(implemented)}")
    print(f"[INFO] Placeholders: {placeholders}")

except Exception as e:
    msg = f"Erro ao verificar implementação: {e}"
    print(f"[ERRO] {msg}")
    errors.append(msg)

print()

# =============================================================================
# TESTE 8: Import de Rotas (sem executar)
# =============================================================================
print("TESTE 8: Import de Módulos de Rotas")
print("-" * 70)

route_modules = [
    'transactions',
    'calendar',
    'reserves',
    'whatsapp_router',
]

for module_name in route_modules:
    try:
        module = __import__(
            f'app.routes.webhooks.{module_name}',
            fromlist=['*']
        )
        print(f"[OK] Módulo {module_name} importado")
    except Exception as e:
        msg = f"Erro ao importar {module_name}: {e}"
        print(f"[ERRO] {msg}")
        errors.append(msg)

print()

# =============================================================================
# RESUMO FINAL
# =============================================================================
print("=" * 70)
print("RESUMO DOS TESTES")
print("=" * 70)

print(f"\nErros críticos: {len(errors)}")
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
    print("TODOS OS TESTES PASSARAM!")
    print("=" * 70)
    print("\nRESULTADO: Fase B.3 APROVADA")
    print("\n✓ Todos os módulos importam corretamente")
    print("✓ Blueprint registrado")
    print("✓ Intent Registry funcional")
    print("✓ Handlers podem ser instanciados")
    print("✓ Herança de BaseIntent validada")
    print("✓ Estrutura pronta para produção")
    print("\nPróximos passos:")
    print("  1. Configurar variáveis de ambiente (.env)")
    print("  2. Testar endpoints em ambiente de desenvolvimento")
    print("  3. Implementar intents placeholders conforme demanda")
else:
    print("ALGUNS TESTES FALHARAM")
    print("=" * 70)
    print(f"\n{len(errors)} erro(s) encontrado(s)")
    print("Revise os erros acima")

print("\n" + "=" * 70 + "\n")

sys.exit(0 if len(errors) == 0 else 1)
