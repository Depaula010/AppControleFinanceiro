# app/services/encryption_service.py
"""
Serviço de criptografia para dados sensíveis (API keys, tokens, etc.)
Usa Fernet (criptografia simétrica) do pacote cryptography
"""
import os
import base64
from cryptography.fernet import Fernet, InvalidToken
from app.config import API_SECRET_KEY

class EncryptionService:
    """
    Singleton service para criptografia/descriptografia de dados sensíveis
    """
    _instance = None
    _cipher = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(EncryptionService, cls).__new__(cls)
            cls._instance._initialize_cipher()
        return cls._instance

    def _initialize_cipher(self):
        """
        Inicializa o cipher Fernet usando a API_SECRET_KEY como base
        """
        # Deriva uma chave de 32 bytes (URL-safe base64) a partir da API_SECRET_KEY
        # IMPORTANTE: Em produção, use uma chave dedicada (variável ENCRYPTION_KEY)
        encryption_key = os.getenv('ENCRYPTION_KEY')

        if not encryption_key:
            # Fallback: derivar da API_SECRET_KEY
            # Pega os primeiros 32 bytes e converte para base64
            key_bytes = (API_SECRET_KEY + '=' * 32)[:32].encode('utf-8')
            encryption_key = base64.urlsafe_b64encode(key_bytes)
            print("[ENCRYPTION] ⚠️  Usando chave derivada de API_SECRET_KEY. "
                  "Configure ENCRYPTION_KEY em produção!")
        else:
            encryption_key = encryption_key.encode('utf-8')

        try:
            self._cipher = Fernet(encryption_key)
            print("[ENCRYPTION] ✅ Serviço de criptografia inicializado")
        except Exception as e:
            print(f"[ENCRYPTION] ❌ Erro ao inicializar cipher: {e}")
            self._cipher = None

    def encrypt(self, plaintext: str) -> str:
        """
        Criptografa uma string

        Args:
            plaintext: Texto em claro (plain text)

        Returns:
            String criptografada (base64)

        Raises:
            ValueError: Se o serviço não foi inicializado corretamente
        """
        if not self._cipher:
            raise ValueError("Encryption service não inicializado")

        if not plaintext:
            return plaintext

        try:
            # Converte para bytes, criptografa, retorna como string
            encrypted_bytes = self._cipher.encrypt(plaintext.encode('utf-8'))
            return encrypted_bytes.decode('utf-8')
        except Exception as e:
            print(f"[ENCRYPTION] ❌ Erro ao criptografar: {e}")
            raise

    def decrypt(self, ciphertext: str) -> str:
        """
        Descriptografa uma string

        Args:
            ciphertext: Texto criptografado (base64)

        Returns:
            String descriptografada (plain text)

        Raises:
            ValueError: Se o serviço não foi inicializado ou token inválido
        """
        if not self._cipher:
            raise ValueError("Encryption service não inicializado")

        if not ciphertext:
            return ciphertext

        try:
            # Converte para bytes, descriptografa, retorna como string
            decrypted_bytes = self._cipher.decrypt(ciphertext.encode('utf-8'))
            return decrypted_bytes.decode('utf-8')
        except InvalidToken:
            print(f"[ENCRYPTION] ❌ Token inválido ao descriptografar")
            raise ValueError("Dados criptografados corrompidos ou chave incorreta")
        except Exception as e:
            print(f"[ENCRYPTION] ❌ Erro ao descriptografar: {e}")
            raise

    def is_encrypted(self, value: str) -> bool:
        """
        Verifica se uma string parece estar criptografada (heurística)

        Args:
            value: String a verificar

        Returns:
            True se parece criptografada, False caso contrário
        """
        if not value or len(value) < 40:
            return False

        # Tokens Fernet começam com 'gAAAAA' após encoding
        # (é uma heurística, não é 100% precisa)
        try:
            return value.startswith('gAAAAA')
        except:
            return False

    def encrypt_if_needed(self, value: str) -> str:
        """
        Criptografa apenas se o valor ainda não estiver criptografado

        Args:
            value: String a criptografar

        Returns:
            String criptografada
        """
        if self.is_encrypted(value):
            return value
        return self.encrypt(value)

    def decrypt_safe(self, value: str, default: str = None) -> str:
        """
        Descriptografa com fallback seguro

        Se a descriptografia falhar (dados corrompidos, chave errada),
        retorna o valor padrão ou o próprio valor original

        Args:
            value: String a descriptografar
            default: Valor padrão em caso de erro

        Returns:
            String descriptografada ou valor padrão
        """
        try:
            return self.decrypt(value)
        except Exception as e:
            print(f"[ENCRYPTION] ⚠️  Falha ao descriptografar (retornando default): {e}")
            return default if default is not None else value


# Singleton global
encryption_service = EncryptionService()
