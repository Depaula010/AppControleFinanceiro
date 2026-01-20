"""Testes para correção de bugs do finance_service."""
import pytest
from unittest.mock import MagicMock, patch


class TestGetAccountDetailsByName:
    """Testes para get_account_details_by_name - Bug tipo_conta."""

    @patch('app.services.finance_service.db_engine')
    def test_should_find_account_by_tipo_conta_when_name_not_found(self, mock_engine):
        """
        Bug: Ao passar 'crédito', deve encontrar conta com tipo_conta='Cartão de Crédito'
        mesmo que nome_conta seja diferente (ex: 'Nubank').
        """
        from app.services.finance_service import get_account_details_by_name

        # Mock da conexão
        mock_conn = MagicMock()

        # Simula: não encontra por nome exato, não encontra por nome parcial
        # MAS existe uma conta com tipo_conta='Cartão de Crédito'
        mock_result_none = MagicMock()
        mock_result_none.fetchone.return_value = None

        mock_result_found = MagicMock()
        mock_result_found.fetchone.return_value = (1, 'Nubank', 'Cartão de Crédito')

        # Primeira chamada (nome exato) -> None
        # Segunda chamada (nome parcial) -> None
        # Terceira chamada (tipo_conta) -> Found
        mock_conn.execute.side_effect = [
            mock_result_none,  # busca exata
            mock_result_none,  # busca parcial por nome
            mock_result_found  # busca por tipo_conta
        ]

        # Executar
        result = get_account_details_by_name(mock_conn, usuario_id=1, nome_conta="crédito")

        # Verificar - DEVE encontrar a conta pelo tipo_conta
        assert result is not None, "Deve encontrar conta pelo tipo_conta"
        assert result['id'] == 1
        assert result['nome'] == 'Nubank'
        assert result['tipo'] == 'Cartão de Crédito'


class TestGetUserByApiKey:
    """Testes para get_user_by_api_key - Bug poluição de logs."""

    @patch('app.services.encryption_service.encryption_service')
    @patch('app.services.finance_service.db_engine')
    def test_should_not_try_decrypt_plaintext_keys(self, mock_engine, mock_enc):
        """
        Bug: Não deve tentar descriptografar chaves em texto plano,
        evitando logs de erro desnecessários.
        """
        from app.services.finance_service import get_user_by_api_key

        # Mock connection
        mock_conn = MagicMock()
        mock_engine.connect.return_value.__enter__ = lambda s: mock_conn
        mock_engine.connect.return_value.__exit__ = lambda s, *args: None

        # Simula uma chave em texto plano (não criptografada)
        plaintext_key = "minha-api-key-simples"
        mock_row = MagicMock()
        mock_row.id = 1
        mock_row.numero_whatsapp = "5511999999999"
        mock_row.api_key_automate = plaintext_key

        mock_result = MagicMock()
        mock_result.fetchall.return_value = [mock_row]
        mock_conn.execute.return_value = mock_result

        # Configura is_encrypted para retornar False (texto plano)
        mock_enc.is_encrypted.return_value = False

        result = get_user_by_api_key(plaintext_key)

        # Verificar que is_encrypted foi chamado
        mock_enc.is_encrypted.assert_called_once_with(plaintext_key)

        # Verificar que decrypt NÃO foi chamado (chave em texto plano)
        mock_enc.decrypt.assert_not_called()

        # Deve encontrar o usuário
        assert result is not None
        assert result[0] == 1
        assert result[1] == "5511999999999"
