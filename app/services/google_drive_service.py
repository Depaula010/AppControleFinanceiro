# app/services/google_drive_service.py
"""
Serviço para upload de arquivos no Google Drive.

Este serviço gerencia operações de upload de arquivos para o Google Drive,
incluindo criação de pastas e validação de arquivos.

Uso:
    from app.services.google_drive_service import GoogleDriveService
    from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService

    # Obter serviço do Drive
    service = GoogleCalendarOAuthService.get_drive_service(usuario_id)

    # Fazer upload
    result = GoogleDriveService.upload_file(
        service=service,
        file_data=bytes_do_arquivo,
        filename="nota_fiscal.pdf",
        folder_name="Notas Fiscais",
        mime_type="application/pdf"
    )

    if result['success']:
        print(f"Link: {result['web_view_link']}")
"""

from io import BytesIO
from typing import Dict, Any, Optional, Tuple
from googleapiclient.http import MediaIoBaseUpload


class GoogleDriveService:
    """Serviço para upload de arquivos no Google Drive."""

    # Tipos MIME permitidos para upload
    ALLOWED_MIME_TYPES = {
        # Imagens
        'image/jpeg': '.jpg',
        'image/png': '.png',
        'image/gif': '.gif',
        'image/webp': '.webp',
        'image/bmp': '.bmp',
        # Documentos
        'application/pdf': '.pdf',
        'application/msword': '.doc',
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document': '.docx',
        'application/vnd.ms-excel': '.xls',
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': '.xlsx',
        'application/vnd.ms-powerpoint': '.ppt',
        'application/vnd.openxmlformats-officedocument.presentationml.presentation': '.pptx',
        'text/plain': '.txt',
        'text/csv': '.csv',
        # Áudio (comum no WhatsApp)
        'audio/mpeg': '.mp3',
        'audio/ogg': '.ogg',
        'audio/opus': '.opus',
    }

    # Limite de tamanho (10MB)
    MAX_FILE_SIZE_MB = 10
    MAX_FILE_SIZE = MAX_FILE_SIZE_MB * 1024 * 1024

    @staticmethod
    def validate_file(
        file_data: bytes,
        filename: str,
        content_type: str
    ) -> Tuple[bool, Optional[str]]:
        """
        Valida arquivo antes do upload.

        Args:
            file_data: Bytes do arquivo
            filename: Nome do arquivo
            content_type: Tipo MIME do arquivo

        Returns:
            Tuple[bool, Optional[str]]: (is_valid, error_message)
        """
        # Validar se tem conteúdo
        if not file_data or len(file_data) == 0:
            return False, "Arquivo vazio"

        # Validar tamanho
        if len(file_data) > GoogleDriveService.MAX_FILE_SIZE:
            return False, f"Arquivo muito grande. Máximo permitido: {GoogleDriveService.MAX_FILE_SIZE_MB}MB"

        # Validar tipo MIME
        if content_type not in GoogleDriveService.ALLOWED_MIME_TYPES:
            tipos_amigaveis = [
                "imagens (JPG, PNG, GIF)",
                "documentos (PDF, DOC, DOCX)",
                "planilhas (XLS, XLSX)",
                "texto (TXT, CSV)",
                "áudio (MP3, OGG)"
            ]
            return False, f"Tipo de arquivo não permitido. Aceitos: {', '.join(tipos_amigaveis)}"

        return True, None

    @staticmethod
    def find_folder_by_name(
        service,
        folder_name: str,
        parent_id: Optional[str] = None
    ) -> Optional[str]:
        """
        Busca pasta por nome no Google Drive.

        Args:
            service: Google Drive API service
            folder_name: Nome da pasta a buscar
            parent_id: ID da pasta pai (opcional, busca na raiz se None)

        Returns:
            str ou None: folder_id se encontrar, None caso contrário
        """
        # Escapar aspas no nome da pasta
        folder_name_escaped = folder_name.replace("'", "\\'")

        query = (
            f"name = '{folder_name_escaped}' and "
            f"mimeType = 'application/vnd.google-apps.folder' and "
            f"trashed = false"
        )

        if parent_id:
            query += f" and '{parent_id}' in parents"

        try:
            results = service.files().list(
                q=query,
                spaces='drive',
                fields='files(id, name)',
                pageSize=1
            ).execute()

            files = results.get('files', [])

            if files:
                folder_id = files[0]['id']
                print(f"[DRIVE] Pasta '{folder_name}' encontrada: {folder_id}")
                return folder_id

            return None

        except Exception as e:
            print(f"[DRIVE] Erro ao buscar pasta '{folder_name}': {e}")
            return None

    @staticmethod
    def create_folder(
        service,
        folder_name: str,
        parent_id: Optional[str] = None
    ) -> str:
        """
        Cria pasta no Google Drive.

        Args:
            service: Google Drive API service
            folder_name: Nome da pasta a criar
            parent_id: ID da pasta pai (opcional, cria na raiz se None)

        Returns:
            str: folder_id da pasta criada
        """
        file_metadata = {
            'name': folder_name,
            'mimeType': 'application/vnd.google-apps.folder'
        }

        if parent_id:
            file_metadata['parents'] = [parent_id]

        folder = service.files().create(
            body=file_metadata,
            fields='id'
        ).execute()

        folder_id = folder.get('id')
        print(f"[DRIVE] Pasta '{folder_name}' criada com ID: {folder_id}")

        return folder_id

    @staticmethod
    def find_or_create_folder(
        service,
        folder_name: str,
        parent_id: Optional[str] = None
    ) -> str:
        """
        Busca pasta por nome, cria se não existir.

        Args:
            service: Google Drive API service
            folder_name: Nome da pasta
            parent_id: ID da pasta pai (opcional)

        Returns:
            str: folder_id da pasta (existente ou nova)
        """
        folder_id = GoogleDriveService.find_folder_by_name(service, folder_name, parent_id)

        if folder_id:
            return folder_id

        return GoogleDriveService.create_folder(service, folder_name, parent_id)

    @staticmethod
    def upload_file(
        service,
        file_data: bytes,
        filename: str,
        folder_name: str,
        mime_type: str
    ) -> Dict[str, Any]:
        """
        Faz upload de arquivo para pasta específica no Google Drive.

        Args:
            service: Google Drive API service
            file_data: Bytes do arquivo
            filename: Nome do arquivo
            folder_name: Nome da pasta destino
            mime_type: Tipo MIME do arquivo

        Returns:
            dict: {
                "success": bool,
                "file_id": str,
                "filename": str,
                "folder_name": str,
                "folder_id": str,
                "web_view_link": str,
                "web_content_link": str (opcional, pode ser None),
                "error": str (se success=False)
            }
        """
        try:
            # 1. Validar arquivo
            is_valid, error_msg = GoogleDriveService.validate_file(
                file_data, filename, mime_type
            )

            if not is_valid:
                return {
                    "success": False,
                    "error": error_msg
                }

            # 2. Buscar/criar pasta
            print(f"[DRIVE] Buscando/criando pasta '{folder_name}'...")
            folder_id = GoogleDriveService.find_or_create_folder(service, folder_name)

            # 3. Preparar metadata do arquivo
            file_metadata = {
                'name': filename,
                'parents': [folder_id]
            }

            # 4. Preparar media para upload
            media = MediaIoBaseUpload(
                BytesIO(file_data),
                mimetype=mime_type,
                resumable=True
            )

            # 5. Fazer upload
            print(f"[DRIVE] Fazendo upload de '{filename}' ({len(file_data)} bytes)...")

            file = service.files().create(
                body=file_metadata,
                media_body=media,
                fields='id, name, webViewLink, webContentLink'
            ).execute()

            print(f"[DRIVE] ✅ Upload concluído!")
            print(f"[DRIVE] File ID: {file.get('id')}")
            print(f"[DRIVE] Link: {file.get('webViewLink')}")

            return {
                "success": True,
                "file_id": file.get('id'),
                "filename": file.get('name'),
                "folder_name": folder_name,
                "folder_id": folder_id,
                "web_view_link": file.get('webViewLink'),
                "web_content_link": file.get('webContentLink')  # Pode ser None para alguns tipos
            }

        except Exception as e:
            print(f"[DRIVE] ❌ Erro no upload: {e}")
            import traceback
            traceback.print_exc()

            # Tratamento de erros específicos
            error_str = str(e).lower()

            if 'insufficient permission' in error_str or 'forbidden' in error_str:
                return {
                    "success": False,
                    "error": "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google."
                }

            if 'quota' in error_str:
                return {
                    "success": False,
                    "error": "Limite de armazenamento do Google Drive atingido."
                }

            if 'invalid' in error_str and 'credentials' in error_str:
                return {
                    "success": False,
                    "error": "Credenciais do Google expiraram. Por favor, reconecte sua conta."
                }

            return {
                "success": False,
                "error": f"Erro ao fazer upload: {str(e)}"
            }

    @staticmethod
    def get_file_extension(mime_type: str) -> str:
        """
        Retorna extensão de arquivo baseado no tipo MIME.

        Args:
            mime_type: Tipo MIME do arquivo

        Returns:
            str: Extensão do arquivo (ex: '.jpg')
        """
        return GoogleDriveService.ALLOWED_MIME_TYPES.get(mime_type, '')
