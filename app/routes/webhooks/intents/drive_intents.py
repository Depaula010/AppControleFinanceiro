# app/routes/webhooks/intents/drive_intents.py
"""
Intent handlers para operações com Google Drive.

Processa uploads de arquivos enviados via WhatsApp para o Google Drive,
permitindo que o usuário especifique a pasta de destino na mensagem.

Exemplo de uso via WhatsApp:
    [Usuário envia imagem com caption]
    "salvar no drive pasta Notas Fiscais"

    [Bot responde]
    ✅ Arquivo enviado para o Drive!
    📁 Pasta: Notas Fiscais
    🔗 [Abrir no Drive](link)
"""

import re
import base64
import logging
from typing import Dict, Any, Optional

from .base_intent import BaseIntent

logger = logging.getLogger(__name__)


class UploadDriveIntent(BaseIntent):
    """
    Intent para upload de arquivos no Google Drive via WhatsApp.

    Espera receber dados de mídia injetados pelo handler do webhook:
    - media_data: bytes do arquivo
    - media_type: tipo MIME (ex: image/jpeg)
    - media_filename: nome do arquivo

    A pasta destino é extraída do texto da mensagem (caption).
    """

    # Pasta padrão caso não especificada
    DEFAULT_FOLDER = "WhatsApp Uploads"

    # Padrões para extrair nome da pasta da mensagem
    FOLDER_PATTERNS = [
        r'(?:salvar|guardar|enviar|subir|upload)\s+(?:no|para|pro|pra|em)\s+(?:google\s+)?drive\s+(?:na\s+)?pasta\s+["\']?([^"\']+)["\']?',
        r'pasta\s+["\']?([^"\']+)["\']?',
        r'(?:em|na|para)\s+["\']?([^"\']+)["\']?$',
    ]

    def __init__(self, *args, **kwargs):
        """
        Inicializa o intent.

        Args extras esperados (injetados pelo handler):
            media_data: bytes do arquivo (ou base64 string)
            media_type: tipo MIME do arquivo
            media_filename: nome do arquivo
        """
        # Extrair dados de mídia antes de chamar super()
        self.media_data = kwargs.pop('media_data', None)
        self.media_type = kwargs.pop('media_type', None)
        self.media_filename = kwargs.pop('media_filename', None)

        super().__init__(*args, **kwargs)

    def extract_params(self) -> Dict[str, Any]:
        """
        Extrai nome da pasta da mensagem do usuário.

        Tenta extrair de padrões como:
        - "salvar no drive pasta Notas Fiscais"
        - "guardar no drive em Documentos"
        - "pasta Comprovantes"

        Returns:
            Dict com folder_name
        """
        folder_name = self._extract_folder_name(self.mensagem)

        logger.info(f"[UploadDriveIntent] Pasta extraída: '{folder_name}'")

        return {
            "folder_name": folder_name
        }

    def _extract_folder_name(self, text: str) -> str:
        """
        Extrai nome da pasta do texto usando regex.

        Args:
            text: Texto da mensagem

        Returns:
            Nome da pasta ou DEFAULT_FOLDER se não encontrar
        """
        if not text:
            return self.DEFAULT_FOLDER

        text_lower = text.lower().strip()

        for pattern in self.FOLDER_PATTERNS:
            match = re.search(pattern, text_lower, re.IGNORECASE)
            if match:
                folder_name = match.group(1).strip()
                # Limpar e capitalizar
                folder_name = self._sanitize_folder_name(folder_name)
                if folder_name:
                    return folder_name

        return self.DEFAULT_FOLDER

    def _sanitize_folder_name(self, name: str) -> str:
        """
        Sanitiza nome da pasta removendo caracteres inválidos.

        Args:
            name: Nome da pasta

        Returns:
            Nome sanitizado
        """
        if not name:
            return ""

        # Remover caracteres não permitidos no Google Drive
        # Permitidos: letras, números, espaços, hífen, underscore, ponto
        sanitized = re.sub(r'[^\w\s\-_.]', '', name, flags=re.UNICODE)

        # Remover espaços extras
        sanitized = ' '.join(sanitized.split())

        # Limitar tamanho
        sanitized = sanitized[:100]

        # Capitalizar primeira letra de cada palavra
        sanitized = sanitized.title()

        return sanitized

    def validate(self) -> Optional[str]:
        """
        Valida se temos os dados necessários para o upload.

        Returns:
            None se válido, mensagem de erro caso contrário
        """
        # Verificar se recebemos dados de mídia
        if not self.media_data:
            return (
                "📎 Nenhum arquivo recebido.\n\n"
                "Para salvar no Google Drive, envie uma *imagem* ou *documento* "
                "junto com a mensagem.\n\n"
                "Exemplo:\n"
                "[Envie uma foto]\n"
                "_salvar no drive pasta Notas Fiscais_"
            )

        # Verificar tipo MIME
        if not self.media_type:
            return "Não foi possível identificar o tipo do arquivo."

        # Verificar se a pasta foi especificada (já tem padrão, então só log)
        if self.params.get("folder_name") == self.DEFAULT_FOLDER:
            logger.info(
                f"[UploadDriveIntent] Pasta não especificada, usando padrão: {self.DEFAULT_FOLDER}"
            )

        return None

    def execute(self) -> Dict[str, Any]:
        """
        Faz upload do arquivo para o Google Drive.

        Returns:
            Dict com resultado do upload
        """
        from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
        from app.services.google_drive_service import GoogleDriveService

        folder_name = self.params.get("folder_name", self.DEFAULT_FOLDER)

        # 1. Verificar se usuário tem Drive conectado
        logger.info(f"[UploadDriveIntent] Verificando conexão Drive para usuario_id={self.usuario_id}")

        if not GoogleCalendarOAuthService.has_drive_scope(self.usuario_id):
            logger.warning(f"[UploadDriveIntent] Usuário {self.usuario_id} não tem escopo do Drive")
            return {
                "success": False,
                "error": "drive_not_connected",
                "message": (
                    "🔗 *Google Drive não conectado*\n\n"
                    "Para usar esta funcionalidade, você precisa conectar sua conta Google.\n\n"
                    "Acesse as configurações e clique em *Conectar Google*."
                )
            }

        # 2. Obter serviço do Drive
        try:
            service = GoogleCalendarOAuthService.get_drive_service(self.usuario_id)
        except Exception as e:
            logger.error(f"[UploadDriveIntent] Erro ao obter serviço Drive: {e}")
            return {
                "success": False,
                "error": "drive_auth_error",
                "message": (
                    "⚠️ *Erro de autenticação*\n\n"
                    "Não foi possível acessar o Google Drive.\n"
                    "Por favor, reconecte sua conta Google nas configurações."
                )
            }

        # 3. Preparar dados do arquivo
        file_data = self._prepare_file_data()
        if not file_data:
            return {
                "success": False,
                "error": "invalid_file_data",
                "message": "Não foi possível processar o arquivo enviado."
            }

        # 4. Validar arquivo
        is_valid, error_msg = GoogleDriveService.validate_file(
            file_data,
            self.media_filename or "arquivo",
            self.media_type
        )

        if not is_valid:
            return {
                "success": False,
                "error": "validation_error",
                "message": f"❌ {error_msg}"
            }

        # 5. Fazer upload
        logger.info(
            f"[UploadDriveIntent] Fazendo upload: {self.media_filename} -> {folder_name}"
        )

        result = GoogleDriveService.upload_file(
            service=service,
            file_data=file_data,
            filename=self.media_filename or f"arquivo_{self.usuario_id}",
            folder_name=folder_name,
            mime_type=self.media_type
        )

        if result.get("success"):
            logger.info(f"[UploadDriveIntent] ✅ Upload concluído: {result.get('file_id')}")
        else:
            logger.error(f"[UploadDriveIntent] ❌ Upload falhou: {result.get('error')}")

        return result

    def _prepare_file_data(self) -> Optional[bytes]:
        """
        Prepara dados do arquivo para upload.

        Converte de base64 se necessário.

        Returns:
            bytes do arquivo ou None se inválido
        """
        if not self.media_data:
            return None

        # Se já é bytes, retorna direto
        if isinstance(self.media_data, bytes):
            return self.media_data

        # Se é string, pode ser base64
        if isinstance(self.media_data, str):
            try:
                # Remover prefixo data:image/...;base64, se presente
                if ',' in self.media_data:
                    self.media_data = self.media_data.split(',', 1)[1]

                return base64.b64decode(self.media_data)
            except Exception as e:
                logger.error(f"[UploadDriveIntent] Erro ao decodificar base64: {e}")
                return None

        return None

    def format_response(self, data: Dict[str, Any]) -> str:
        """
        Formata resposta para o WhatsApp.

        Args:
            data: Dados retornados por execute()

        Returns:
            Mensagem formatada
        """
        # Se houve erro, a mensagem já vem formatada
        if not data.get("success"):
            return data.get("message", "❌ Erro ao enviar arquivo para o Drive.")

        # Sucesso - formatar mensagem bonita
        folder_name = data.get("folder_name", self.DEFAULT_FOLDER)
        filename = data.get("filename", "arquivo")
        web_link = data.get("web_view_link", "")

        message = "✅ *Arquivo enviado para o Google Drive!*\n\n"
        message += f"📁 *Pasta:* {folder_name}\n"
        message += f"📄 *Arquivo:* {filename}\n"

        if web_link:
            message += f"\n🔗 *Abrir no Drive:*\n{web_link}"

        return message


__all__ = [
    'UploadDriveIntent',
]
