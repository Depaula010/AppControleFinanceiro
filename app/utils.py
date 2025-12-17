# app/utils.py
"""
DEPRECATED: Este arquivo mantém compatibilidade retroativa.
Novas implementações devem importar diretamente de app.shared.*

As funções agora estão organizadas em:
- app.shared.formatters: formatar_moeda, formatar_mes_pt, etc.
- app.shared.validators: sanitize_for_log, sanitize_input
- app.shared.security: verify_hmac_signature, generate_hmac_signature, compare_keys_safe
- app.shared.database: with_db_retry, check_db_connection, ensure_db_connection, db_transaction, db_connection
- app.shared.decorators: require_api_key, validate_required_fields, handle_errors, etc.
- app.shared.responses: ApiResponse, success_response, error_response
- app.shared.utils: DateUtils, now_brazil, today_brazil, parse_relative_date
"""

# Re-exportar todas as funções dos novos módulos para manter compatibilidade
from app.shared.formatters import (
    formatar_moeda,
    formatar_mes_pt,
    formatar_mes_ano_pt,
    formatar_dia_semana_pt,
    MESES_PT_BR,
    DIAS_SEMANA_PT_BR
)

from app.shared.validators import (
    sanitize_for_log,
    sanitize_input
)

from app.shared.security import (
    verify_hmac_signature,
    generate_hmac_signature,
    compare_keys_safe
)

from app.shared.database import (
    with_db_retry,
    check_db_connection,
    ensure_db_connection,
    db_transaction,
    db_connection,
    execute_in_transaction
)

from app.shared.decorators import (
    require_api_key,
    validate_required_fields,
    handle_errors,
    require_user_auth,
    require_db_connection,
    combine_decorators,
    admin_endpoint,
    webhook_endpoint
)

from app.shared.responses import (
    ApiResponse,
    success_response,
    error_response
)

from app.shared.utils import (
    DateUtils,
    now_brazil,
    today_brazil,
    parse_relative_date
)

__all__ = [
    # Formatters
    'formatar_moeda',
    'formatar_mes_pt',
    'formatar_mes_ano_pt',
    'formatar_dia_semana_pt',
    'MESES_PT_BR',
    'DIAS_SEMANA_PT_BR',
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
    'db_transaction',
    'db_connection',
    'execute_in_transaction',
    # Decorators
    'require_api_key',
    'validate_required_fields',
    'handle_errors',
    'require_user_auth',
    'require_db_connection',
    'combine_decorators',
    'admin_endpoint',
    'webhook_endpoint',
    # Responses
    'ApiResponse',
    'success_response',
    'error_response',
    # Utils
    'DateUtils',
    'now_brazil',
    'today_brazil',
    'parse_relative_date',
]
