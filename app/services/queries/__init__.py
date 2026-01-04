"""
Queries SQL centralizadas e reutilizáveis.

Este pacote contém todas as queries SQL usadas no sistema, organizadas por domínio.

## Importações rápidas:

```python
from app.services.queries import (
    AgendamentosQueries,    # Contas a pagar/receber (agendamentos)
    FaturasQueries,         # Faturas de cartão de crédito
    AccountQueries,         # Contas bancárias e cartões
    CategoryQueries,        # Categorias de transações
    UserQueries,            # Usuários
    TransactionQueries,     # Transações financeiras
)
```

## Vantagens:

- ✅ **Zero duplicação** - Cada query SQL existe em apenas 1 lugar
- ✅ **Manutenção fácil** - Alterar em 1 lugar afeta todos os usos
- ✅ **Documentação completa** - Todos os parâmetros necessários documentados
- ✅ **Testável** - Queries podem ser testadas independentemente
- ✅ **Type hints** - Melhor autocomplete no IDE
- ✅ **Versionamento** - Fácil rastrear mudanças em queries críticas

## Estrutura:

```
app/services/queries/
├── agendamentos_queries.py  - Contas a pagar/receber
├── faturas_queries.py       - Faturas de cartão
├── account_queries.py       - Contas bancárias
├── category_queries.py      - Categorias
├── user_queries.py          - Usuários
├── transaction_queries.py   - Transações
└── __init__.py             - Este arquivo
```

## Exemplo de uso:

```python
from app.services.queries import AccountQueries
from datetime import date

# Buscar todas as contas do usuário
sql = AccountQueries.get_all_user_accounts()
params = {"uid": usuario_id}
contas = conn.execute(sql, params).fetchall()

# Buscar conta por nome (com fallback automático)
conta_id = AccountQueries.executar_busca_completa(
    conn, usuario_id, "Nubank", tipo_conta="Cartão de Crédito"
)
```

## Padrão de Nomenclatura:

- `get_*()` - SELECT queries que retornam dados
- `check_*()` - Queries de validação (EXISTS, COUNT)
- `update_*()` - UPDATE queries
- `delete_*()` - DELETE queries
- `get_parametros_*()` - Helpers para montar parâmetros
- `executar_*()` - Helpers que executam lógica complexa
"""

from .agendamentos_queries import AgendamentosQueries
from .faturas_queries import FaturasQueries
from .account_queries import AccountQueries
from .category_queries import CategoryQueries
from .user_queries import UserQueries
from .transaction_queries import TransactionQueries

__all__ = [
    # Queries de domínio
    'AgendamentosQueries',   # Contas a pagar/receber
    'FaturasQueries',        # Faturas de cartão
    'AccountQueries',        # Contas bancárias
    'CategoryQueries',       # Categorias
    'UserQueries',           # Usuários
    'TransactionQueries',    # Transações
]
