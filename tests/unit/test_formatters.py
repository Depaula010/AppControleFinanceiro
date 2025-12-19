# tests/unit/test_formatters.py
"""
Testes unitários para os formatters da Fase E.

Testa:
- FinancialAlertFormatter
- currency_formatter
- date_formatter
"""

import pytest
import sys
import os

# Adicionar diretório raiz ao path para imports
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestFinancialAlertFormatter:
    """Testes para FinancialAlertFormatter."""
    
    def test_format_returns_none_for_empty_alerts(self, sample_alertas_vazio):
        """Deve retornar None quando não há alertas."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas_vazio)
        
        assert result is None
    
    def test_format_with_alerts_returns_string(self, sample_alertas):
        """Deve retornar string formatada quando há alertas."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert result is not None
        assert isinstance(result, str)
        assert len(result) > 0
    
    def test_format_contains_header(self, sample_alertas):
        """Deve conter cabeçalho de alertas financeiros."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "💰 *ALERTAS FINANCEIROS*" in result
    
    def test_format_contains_today_section(self, sample_alertas):
        """Deve conter seção de vencimentos de hoje."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "VENCE HOJE" in result
    
    def test_format_contains_tomorrow_section(self, sample_alertas):
        """Deve conter seção de vencimentos de amanhã."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "VENCE AMANHÃ" in result
    
    def test_format_separates_expenses_and_income(self, sample_alertas):
        """Deve separar despesas e receitas."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "Despesas" in result
        assert "Receitas" in result
    
    def test_format_includes_account_descriptions(self, sample_alertas):
        """Deve incluir descrições das contas."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "Conta de Luz" in result
        assert "Salário" in result
        assert "Internet" in result
    
    def test_format_includes_invoice_card_name(self, sample_alertas):
        """Deve incluir nome do cartão de fatura."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "Fatura Nubank" in result
    
    def test_format_formats_values_brazilian_style(self, sample_alertas):
        """Deve formatar valores no padrão brasileiro (vírgula)."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas)
        
        assert "150,50" in result  # Virgula como decimal
    
    def test_format_without_greeting_by_default(self, sample_alertas):
        """Não deve incluir saudação por padrão."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas, include_greeting=False)
        
        assert "🌅 *Bom dia!*" not in result
    
    def test_format_with_greeting_when_requested(self, sample_alertas):
        """Deve incluir saudação quando solicitado."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        result = FinancialAlertFormatter.format(sample_alertas, include_greeting=True)
        
        assert "🌅 *Bom dia!*" in result
    
    def test_format_only_today_alerts(self):
        """Deve funcionar só com alertas de hoje."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        alertas = {
            'contas_hoje': [{'descricao': 'Teste', 'valor': 100.00, 'tipo': 'Despesa'}],
            'contas_amanha': [],
            'faturas_hoje': [],
            'faturas_amanha': []
        }
        
        result = FinancialAlertFormatter.format(alertas)
        
        assert result is not None
        assert "VENCE HOJE" in result
    
    def test_format_only_tomorrow_alerts(self):
        """Deve funcionar só com alertas de amanhã."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        
        alertas = {
            'contas_hoje': [],
            'contas_amanha': [{'descricao': 'Teste', 'valor': 100.00, 'tipo': 'Despesa'}],
            'faturas_hoje': [],
            'faturas_amanha': []
        }
        
        result = FinancialAlertFormatter.format(alertas)
        
        assert result is not None
        assert "VENCE AMANHÃ" in result


class TestCurrencyFormatter:
    """Testes para formatar_moeda."""
    
    def test_format_positive_value(self):
        """Deve formatar valor positivo."""
        from app.shared.formatters.currency_formatter import formatar_moeda
        
        result = formatar_moeda(1500.50)
        
        assert "1.500,50" in result or "1500,50" in result
    
    def test_format_negative_value(self):
        """Deve formatar valor negativo."""
        from app.shared.formatters.currency_formatter import formatar_moeda
        
        result = formatar_moeda(-250.00)
        
        assert "250" in result
    
    def test_format_zero(self):
        """Deve formatar zero."""
        from app.shared.formatters.currency_formatter import formatar_moeda
        
        result = formatar_moeda(0)
        
        assert "0" in result
