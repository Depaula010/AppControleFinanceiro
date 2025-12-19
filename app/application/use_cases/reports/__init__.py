# app/application/use_cases/reports/__init__.py
"""
Use Cases de Relatórios.

Módulos:
- GenerateMonthlyReportUseCase: Relatório mensal
- GenerateCategoryReportUseCase: Relatório por categoria
"""

from .monthly_report import (
    MonthlyReportInput,
    CategorySummary,
    MonthlyReportOutput,
    GenerateMonthlyReportUseCase,
)

from .category_report import (
    CategoryReportInput,
    SubcategorySummary,
    CategoryDetail,
    GenerateCategoryReportOutput,
    GenerateCategoryReportUseCase,
)


__all__ = [
    # Monthly Report
    'MonthlyReportInput',
    'CategorySummary',
    'MonthlyReportOutput',
    'GenerateMonthlyReportUseCase',
    # Category Report
    'CategoryReportInput',
    'SubcategorySummary',
    'CategoryDetail',
    'GenerateCategoryReportOutput',
    'GenerateCategoryReportUseCase',
]
