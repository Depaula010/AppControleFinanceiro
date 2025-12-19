# app/shared/__init__.py
"""
Modulo shared - Utilitarios reutilizaveis organizados por funcao.

Submodulos:
- formatters: Formatacao de moedas, datas, etc.
- validators: Validacao e sanitizacao de inputs
- security: HMAC, assinaturas, etc.
- database: Retry patterns para banco de dados
"""

from .formatters import (
    formatar_moeda,
    formatar_mes_pt,
    formatar_mes_ano_pt,
    formatar_dia_semana_pt,
    FinancialAlertFormatter
)
from .validators import sanitize_for_log, sanitize_input
from .security import verify_hmac_signature, generate_hmac_signature, compare_keys_safe
from .database import with_db_retry, check_db_connection, ensure_db_connection

__all__ = [
    # Formatters
    'formatar_moeda',
    'formatar_mes_pt',
    'formatar_mes_ano_pt',
    'formatar_dia_semana_pt',
    'FinancialAlertFormatter',
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
