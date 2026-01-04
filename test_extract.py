#!/usr/bin/env python3
"""Teste rápido para verificar se extract_income_params existe"""

import sys
import os

# Adicionar o diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

try:
    from app.services import gemini_service

    print("=== TESTE DE FUNÇÕES ===")
    print(f"Módulo gemini_service carregado de: {gemini_service.__file__}")

    # Verificar se as funções existem
    has_income = hasattr(gemini_service, 'extract_income_params')
    has_expense = hasattr(gemini_service, 'extract_expense_params')

    print(f"extract_income_params existe: {has_income}")
    print(f"extract_expense_params existe: {has_expense}")

    if has_income:
        print(f"Assinatura: {gemini_service.extract_income_params.__doc__[:100]}")
    else:
        print("ERRO: extract_income_params NÃO EXISTE!")

        # Listar todas as funções que começam com 'extract'
        extract_funcs = [x for x in dir(gemini_service) if x.startswith('extract')]
        print(f"Funções extract disponíveis: {extract_funcs}")

except Exception as e:
    print(f"ERRO ao importar: {e}")
    import traceback
    traceback.print_exc()
