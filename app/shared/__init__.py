# app/shared/__init__.py
"""
Módulo compartilhado de utilitários

Este módulo centraliza funções reutilizáveis organizadas por responsabilidade:
- formatters: Formatação de moedas, datas, etc.
- validators: Validação e sanitização de inputs
- security: Utilitários de segurança (HMAC, comparação segura, etc.)
- database: Utilitários de conexão e retry de banco de dados
"""

# Importar e re-exportar para facilitar uso
from .formatters import formatar_moeda, formatar_mes_pt, formatar_mes_ano_pt, formatar_dia_semana_pt
from .validators import sanitize_for_log, sanitize_input
from .security import verify_hmac_signature, generate_hmac_signature, compare_keys_safe
from .database import with_db_retry, check_db_connection, ensure_db_connection

__all__ = [
    # Formatters
    'formatar_moeda',
    'formatar_mes_pt',
    'formatar_mes_ano_pt',
    'formatar_dia_semana_pt',
    # Validators
    'sanitize_for_log',
    'sanitize_input',
    # Security
    'verify_hmac_signature',
    'generate_hmac_signature',
    'compare_keys_safe',
    # Database
    'with_db_retry',
    'check_db_connection',
    'ensure_db_connection',
]
