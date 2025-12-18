# app/services/finance/__init__.py
"""
Pacote de serviços financeiros (REFATORADO - Fase B.2).

✅ PROGRESSO: 12/12 módulos concluídos (100%)

Módulos refatorados (34/34 funções - 100%):
✅ user_service (2 funções)
✅ pot_service (1 função)
✅ emergency_reserve_service (1 função)
✅ installment_service (1 função)
✅ text_utils (1 função)
✅ category_service (4 funções)
✅ account_service (8 funções)
✅ transaction_service (3 funções)
✅ invoice_service (3 funções)
✅ bills_service (3 funções)
✅ setup_service (7 funções)

Todos os serviços são re-exportados para manter compatibilidade 100%.
"""

# Re-exportar serviços refatorados
from .user_service import (
    get_user_by_api_key,
    get_user_by_whatsapp,
)

from .pot_service import (
    get_pote_status,
)

from .emergency_reserve_service import (
    get_reserva_status,
)

from .installment_service import (
    create_parcelamento_agendamento,
)

from .text_utils import (
    extract_mentioned_account,
)

from .category_service import (
    get_user_categories,
    get_fallback_category_id,
    get_category_name_by_id,
    get_category_spending,
)

from .account_service import (
    get_user_accounts,
    get_account_by_name,
    get_account_details_by_name,
    get_saldo_contas,
    update_saldo_inicial,
    get_user_default_accounts,
    set_user_default_account,
    choose_account_for_transaction,
)

from .transaction_service import (
    create_transaction,
    create_transfer_pair,
    create_fatura_payment,
)

from .invoice_service import (
    get_or_create_fatura,
    ensure_current_invoice_exists,
    get_fatura_valor,
)

from .bills_service import (
    get_upcoming_bills_and_invoices,
    get_vencimentos_periodo,
    format_vencimentos_message,
)

from .setup_service import (
    clear_bot_session,
    setup_database_schema,
    populate_global_categories,
    setup_user_data,
    add_google_calendar_tokens_table,
    add_nightly_checkin_config_columns,
    criar_tabelas_chaves_api,
)

__all__ = [
    # User
    'get_user_by_api_key',
    'get_user_by_whatsapp',
    # Pot
    'get_pote_status',
    # Emergency Reserve
    'get_reserva_status',
    # Installment
    'create_parcelamento_agendamento',
    # Text Utils
    'extract_mentioned_account',
    # Category
    'get_user_categories',
    'get_fallback_category_id',
    'get_category_name_by_id',
    'get_category_spending',
    # Account
    'get_user_accounts',
    'get_account_by_name',
    'get_account_details_by_name',
    'get_saldo_contas',
    'update_saldo_inicial',
    'get_user_default_accounts',
    'set_user_default_account',
    'choose_account_for_transaction',
    # Transaction
    'create_transaction',
    'create_transfer_pair',
    'create_fatura_payment',
    # Invoice
    'get_or_create_fatura',
    'ensure_current_invoice_exists',
    'get_fatura_valor',
    # Bills
    'get_upcoming_bills_and_invoices',
    'get_vencimentos_periodo',
    'format_vencimentos_message',
    # Setup
    'clear_bot_session',
    'setup_database_schema',
    'populate_global_categories',
    'setup_user_data',
    'add_google_calendar_tokens_table',
    'add_nightly_checkin_config_columns',
    'criar_tabelas_chaves_api',
]
