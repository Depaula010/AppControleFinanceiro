# tests/unit/test_use_cases_reports.py
"""
Testes unitários para Use Cases de Reports.

Testa:
- GenerateMonthlyReportUseCase
- GenerateCategoryReportUseCase
"""

import pytest
from unittest.mock import patch, MagicMock
from datetime import date
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestMonthlyReportInput:
    """Testes para MonthlyReportInput DTO."""
    
    def test_create_input(self):
        """Deve criar input de relatório mensal."""
        from app.application.use_cases.reports import MonthlyReportInput
        
        input_data = MonthlyReportInput(
            usuario_id=1,
            ano=2025,
            mes=12
        )
        
        assert input_data.usuario_id == 1
        assert input_data.ano == 2025
        assert input_data.mes == 12
    
    def test_valid_month_range(self):
        """Deve aceitar meses de 1 a 12."""
        from app.application.use_cases.reports import MonthlyReportInput
        
        for mes in range(1, 13):
            input_data = MonthlyReportInput(usuario_id=1, ano=2025, mes=mes)
            assert input_data.mes == mes


class TestCategorySummary:
    """Testes para CategorySummary DTO."""
    
    def test_create_category_summary(self):
        """Deve criar resumo de categoria."""
        from app.application.use_cases.reports import CategorySummary
        
        summary = CategorySummary(
            categoria_nome='Alimentação',
            total=500.0,
            percentual=25.5
        )
        
        assert summary.categoria_nome == 'Alimentação'
        assert summary.total == 500.0
        assert summary.percentual == 25.5


class TestMonthlyReportOutput:
    """Testes para MonthlyReportOutput DTO."""
    
    def test_create_output(self):
        """Deve criar output de relatório."""
        from app.application.use_cases.reports import (
            MonthlyReportOutput,
            CategorySummary
        )
        
        output = MonthlyReportOutput(
            success=True,
            periodo="Dezembro 2025",
            total_receitas=5000.0,
            total_despesas=3000.0,
            saldo_periodo=2000.0,
            categorias_despesas=[
                CategorySummary(categoria_nome='A', total=1000, percentual=33.3)
            ],
            message="OK"
        )
        
        assert output.success is True
        assert output.periodo == "Dezembro 2025"
        assert output.saldo_periodo == 2000.0
    
    def test_calculate_saldo_periodo(self):
        """Saldo deve ser receitas - despesas."""
        from app.application.use_cases.reports import MonthlyReportOutput
        
        output = MonthlyReportOutput(
            success=True,
            total_receitas=5000.0,
            total_despesas=3000.0,
            saldo_periodo=5000.0 - 3000.0
        )
        
        assert output.saldo_periodo == output.total_receitas - output.total_despesas


class TestCategoryReportInput:
    """Testes para CategoryReportInput DTO."""
    
    def test_create_input_defaults(self):
        """Deve criar input com defaults."""
        from app.application.use_cases.reports import CategoryReportInput
        
        input_data = CategoryReportInput(usuario_id=1)
        
        assert input_data.usuario_id == 1
        assert input_data.categoria_id is None
        assert input_data.data_inicio is None
        assert input_data.data_fim is None
    
    def test_create_input_with_filters(self):
        """Deve aceitar filtros opcionais."""
        from app.application.use_cases.reports import CategoryReportInput
        
        input_data = CategoryReportInput(
            usuario_id=1,
            categoria_id=5,
            data_inicio=date(2025, 1, 1),
            data_fim=date(2025, 12, 31)
        )
        
        assert input_data.categoria_id == 5
        assert input_data.data_inicio == date(2025, 1, 1)


class TestSubcategorySummary:
    """Testes para SubcategorySummary DTO."""
    
    def test_create_subcategory_summary(self):
        """Deve criar resumo de subcategoria."""
        from app.application.use_cases.reports import SubcategorySummary
        
        summary = SubcategorySummary(
            subcategoria_nome='Supermercado',
            total=300.0,
            quantidade=15
        )
        
        assert summary.subcategoria_nome == 'Supermercado'
        assert summary.total == 300.0
        assert summary.quantidade == 15


class TestCategoryDetail:
    """Testes para CategoryDetail DTO."""
    
    def test_create_category_detail(self):
        """Deve criar detalhe de categoria."""
        from app.application.use_cases.reports import (
            CategoryDetail,
            SubcategorySummary
        )
        
        detail = CategoryDetail(
            categoria_id=1,
            categoria_nome='Alimentação',
            total=500.0,
            quantidade=20,
            subcategorias=[
                SubcategorySummary(subcategoria_nome='Mercado', total=300, quantidade=10),
                SubcategorySummary(subcategoria_nome='Restaurante', total=200, quantidade=10)
            ]
        )
        
        assert detail.categoria_id == 1
        assert len(detail.subcategorias) == 2
        assert sum(s.total for s in detail.subcategorias) == detail.total


class TestGenerateCategoryReportOutput:
    """Testes para GenerateCategoryReportOutput DTO."""
    
    def test_create_output(self):
        """Deve criar output de relatório."""
        from app.application.use_cases.reports import (
            GenerateCategoryReportOutput,
            CategoryDetail
        )
        
        output = GenerateCategoryReportOutput(
            success=True,
            categorias=[
                CategoryDetail(categoria_id=1, categoria_nome='A', total=1000, quantidade=10)
            ],
            total_geral=1000.0,
            message="OK"
        )
        
        assert output.success is True
        assert len(output.categorias) == 1
        assert output.total_geral == 1000.0
