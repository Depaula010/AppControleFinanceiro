"""
Queries SQL centralizadas e reutilizáveis.

Este pacote contém todas as queries SQL usadas no sistema, organizadas por domínio.

Importações rápidas:
    from app.services.queries import AgendamentosQueries, FaturasQueries

Vantagens:
    - Elimina duplicação de código SQL
    - Facilita manutenção (alterar em 1 lugar afeta todos os usos)
    - Documenta parâmetros necessários
    - Permite testes unitários das queries
"""

from .agendamentos_queries import AgendamentosQueries
from .faturas_queries import FaturasQueries

__all__ = [
    'AgendamentosQueries',
    'FaturasQueries',
]
