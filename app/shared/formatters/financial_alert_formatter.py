# app/shared/formatters/financial_alert_formatter.py
"""
Formatador centralizado de alertas financeiros.

Este módulo consolida a lógica de formatação de alertas de vencimentos,
eliminando duplicação entre daily_briefing_service.py e daily_briefing.py.
"""

from typing import Dict, Optional


class FinancialAlertFormatter:
    """Formatador de alertas financeiros para WhatsApp."""

    @staticmethod
    def format(alertas: Dict, include_greeting: bool = False) -> Optional[str]:
        """
        Formata alertas financeiros para mensagem WhatsApp.

        Args:
            alertas: Dicionário com alertas financeiros:
                - contas_hoje: Lista de contas que vencem hoje
                - contas_amanha: Lista de contas que vencem amanhã
                - faturas_hoje: Lista de faturas que vencem hoje
                - faturas_amanha: Lista de faturas que vencem amanhã
            include_greeting: Se True, adiciona saudação no início

        Returns:
            str: Mensagem formatada ou None se não houver alertas
        """
        contas_hoje = alertas.get('contas_hoje', [])
        contas_amanha = alertas.get('contas_amanha', [])
        faturas_hoje = alertas.get('faturas_hoje', [])
        faturas_amanha = alertas.get('faturas_amanha', [])

        tem_alertas = any([contas_hoje, contas_amanha, faturas_hoje, faturas_amanha])

        if not tem_alertas:
            return None

        msg_parts = []

        # Saudação opcional (para mensagens standalone)
        if include_greeting:
            msg_parts.append("🌅 *Bom dia!*\n")

        msg_parts.append("💰 *ALERTAS FINANCEIROS*")

        # Vencimentos de HOJE
        if contas_hoje or faturas_hoje:
            despesas_hoje = [c for c in contas_hoje if c.get('tipo') == 'Despesa']
            receitas_hoje = [c for c in contas_hoje if c.get('tipo') == 'Receita']

            if despesas_hoje or faturas_hoje:
                msg_parts.append("\n⚠️ *VENCE HOJE (Despesas):*")
                for conta in despesas_hoje:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")
                for fatura in faturas_hoje:
                    valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

            if receitas_hoje:
                msg_parts.append("\n💵 *VENCE HOJE (Receitas):*")
                for conta in receitas_hoje:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        # Vencimentos de AMANHÃ
        if contas_amanha or faturas_amanha:
            despesas_amanha = [c for c in contas_amanha if c.get('tipo') == 'Despesa']
            receitas_amanha = [c for c in contas_amanha if c.get('tipo') == 'Receita']

            if despesas_amanha or faturas_amanha:
                msg_parts.append("\n🔔 *VENCE AMANHÃ (Despesas):*")
                for conta in despesas_amanha:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")
                for fatura in faturas_amanha:
                    valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

            if receitas_amanha:
                msg_parts.append("\n💰 *VENCE AMANHÃ (Receitas):*")
                for conta in receitas_amanha:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        return "\n".join(msg_parts)


__all__ = ['FinancialAlertFormatter']
