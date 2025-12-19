"""
Utilit

ários de data e hora para Brasil (America/Sao_Paulo).

Elimina código duplicado de timezone handling presente em 10+ lugares.
"""

from datetime import datetime, date, timedelta
from zoneinfo import ZoneInfo
from typing import Optional


class DateUtils:
    """
    Utilitários de data e hora com timezone do Brasil.

    Elimina código duplicado como:
        from zoneinfo import ZoneInfo
        TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
        hoje_br = datetime.now(TIMEZONE_BR).date()

    Usage:
        from app.shared.utils.date_utils import DateUtils

        # Data/hora atual no Brasil
        agora = DateUtils.now_brazil()
        hoje = DateUtils.today_brazil()

        # Parsing de datas relativas
        data = DateUtils.parse_relative_date('hoje')      # date.today()
        data = DateUtils.parse_relative_date('amanha')    # date.today() + 1 day
        data = DateUtils.parse_relative_date('2025-12-25')  # date(2025, 12, 25)
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    @classmethod
    def now_brazil(cls) -> datetime:
        """
        Retorna datetime atual no timezone do Brasil.

        Returns:
            datetime com timezone America/Sao_Paulo

        Example:
            >>> DateUtils.now_brazil()
            datetime.datetime(2025, 12, 16, 15, 30, 0, tzinfo=ZoneInfo('America/Sao_Paulo'))
        """
        return datetime.now(cls.TIMEZONE_BR)

    @classmethod
    def today_brazil(cls) -> date:
        """
        Retorna date de hoje no timezone do Brasil.

        Returns:
            date de hoje no Brasil

        Example:
            >>> DateUtils.today_brazil()
            datetime.date(2025, 12, 16)
        """
        return cls.now_brazil().date()

    @classmethod
    def parse_relative_date(cls, date_str: str) -> date:
        """
        Parse string de data relativa ou ISO.

        Suporta:
        - 'hoje' -> date de hoje
        - 'amanha' ou 'amanhã' -> date de amanhã
        - '2025-12-25' -> date(2025, 12, 25)

        Args:
            date_str: String representando data

        Returns:
            date parsed

        Raises:
            ValueError: Se formato inválido

        Example:
            >>> DateUtils.parse_relative_date('hoje')
            datetime.date(2025, 12, 16)

            >>> DateUtils.parse_relative_date('amanha')
            datetime.date(2025, 12, 17)

            >>> DateUtils.parse_relative_date('2025-12-25')
            datetime.date(2025, 12, 25)
        """
        date_str_lower = date_str.lower().strip()
        hoje = cls.today_brazil()

        if date_str_lower == 'hoje':
            return hoje
        elif date_str_lower in ('amanha', 'amanhã'):
            return hoje + timedelta(days=1)
        elif date_str_lower == 'ontem':
            return hoje - timedelta(days=1)
        else:
            # Tentar parse ISO (YYYY-MM-DD)
            try:
                return date.fromisoformat(date_str)
            except ValueError:
                raise ValueError(
                    f"Data inválida: '{date_str}'. "
                    f"Use 'hoje', 'amanha' ou formato ISO (YYYY-MM-DD)"
                )

    @classmethod
    def get_week_range(cls, reference_date: Optional[date] = None) -> tuple[date, date]:
        """
        Retorna range de 7 dias a partir de uma data de referência.

        Args:
            reference_date: Data de referência (padrão: hoje)

        Returns:
            Tuple (data_inicio, data_fim) com 7 dias de diferença

        Example:
            >>> DateUtils.get_week_range()
            (datetime.date(2025, 12, 16), datetime.date(2025, 12, 23))
        """
        if reference_date is None:
            reference_date = cls.today_brazil()

        data_fim = reference_date + timedelta(days=7)
        return (reference_date, data_fim)

    @classmethod
    def get_month_range(cls, year: Optional[int] = None, month: Optional[int] = None) -> tuple[date, date]:
        """
        Retorna primeiro e último dia de um mês.

        Args:
            year: Ano (padrão: ano atual)
            month: Mês (padrão: mês atual)

        Returns:
            Tuple (primeiro_dia, ultimo_dia) do mês

        Example:
            >>> DateUtils.get_month_range(2025, 12)
            (datetime.date(2025, 12, 1), datetime.date(2025, 12, 31))
        """
        hoje = cls.today_brazil()

        if year is None:
            year = hoje.year
        if month is None:
            month = hoje.month

        primeiro_dia = date(year, month, 1)

        # Último dia do mês
        if month == 12:
            ultimo_dia = date(year + 1, 1, 1) - timedelta(days=1)
        else:
            ultimo_dia = date(year, month + 1, 1) - timedelta(days=1)

        return (primeiro_dia, ultimo_dia)

    @classmethod
    def format_date_pt(cls, data: date) -> str:
        """
        Formata data em português (DD/MM/YYYY).

        Args:
            data: Data para formatar

        Returns:
            String formatada (ex: "16/12/2025")

        Example:
            >>> DateUtils.format_date_pt(date(2025, 12, 16))
            '16/12/2025'
        """
        return data.strftime('%d/%m/%Y')

    @classmethod
    def format_datetime_pt(cls, data_hora: datetime) -> str:
        """
        Formata datetime em português (DD/MM/YYYY HH:MM).

        Args:
            data_hora: Datetime para formatar

        Returns:
            String formatada (ex: "16/12/2025 15:30")

        Example:
            >>> DateUtils.format_datetime_pt(datetime(2025, 12, 16, 15, 30))
            '16/12/2025 15:30'
        """
        return data_hora.strftime('%d/%m/%Y %H:%M')

    @classmethod
    def is_weekend(cls, data: date) -> bool:
        """
        Verifica se data é fim de semana (sábado ou domingo).

        Args:
            data: Data para verificar

        Returns:
            True se for sábado ou domingo

        Example:
            >>> DateUtils.is_weekend(date(2025, 12, 20))  # Sábado
            True
        """
        return data.weekday() >= 5  # 5=Sábado, 6=Domingo

    @classmethod
    def add_business_days(cls, start_date: date, days: int) -> date:
        """
        Adiciona dias úteis (pula fins de semana).

        Args:
            start_date: Data inicial
            days: Número de dias úteis para adicionar

        Returns:
            Data após adicionar dias úteis

        Example:
            >>> DateUtils.add_business_days(date(2025, 12, 19), 3)  # Sexta
            datetime.date(2025, 12, 24)  # Quarta (pula sábado/domingo)
        """
        current_date = start_date
        days_added = 0

        while days_added < days:
            current_date += timedelta(days=1)
            if not cls.is_weekend(current_date):
                days_added += 1

        return current_date

    @classmethod
    def days_until(cls, target_date: date) -> int:
        """
        Calcula quantos dias faltam até uma data.

        Args:
            target_date: Data alvo

        Returns:
            Número de dias (positivo se futuro, negativo se passado)

        Example:
            >>> DateUtils.days_until(date(2025, 12, 25))  # Hoje: 16/12
            9
        """
        hoje = cls.today_brazil()
        return (target_date - hoje).days


# Aliases para compatibilidade e conveniência
now_brazil = DateUtils.now_brazil
today_brazil = DateUtils.today_brazil
parse_relative_date = DateUtils.parse_relative_date
