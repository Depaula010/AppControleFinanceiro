#!/usr/bin/env python3
"""
Base class abstrata para todos os cron jobs do sistema.

Este módulo elimina código duplicado fornecendo:
- Setup automático do Flask app context
- Logging consistente
- Error handling padronizado
- Path configuration
- Template method pattern para execução

Usage:
    from app.jobs.base_job import BaseJob

    class MeuJob(BaseJob):
        def get_job_name(self) -> str:
            return "MEU-JOB"

        def execute(self):
            # Lógica específica do job aqui
            # Você já está dentro do app.app_context()
            # Pode importar e usar serviços livremente
            from app.services.meu_service import processar
            processar()

    if __name__ == "__main__":
        job = MeuJob()
        job.run()
"""

import sys
import os
from abc import ABC, abstractmethod
from datetime import datetime
from typing import Optional

# Adicionar diretório raiz ao path para encontrar o módulo 'app'
# Isso permite que os jobs funcionem tanto via cron quanto executados manualmente
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)


class BaseJob(ABC):
    """
    Classe base abstrata para cron jobs.

    Implementa o Template Method Pattern:
    - run() orquestra a execução (método template)
    - execute() deve ser implementado pelas subclasses (método abstrato)

    Benefícios:
    - Elimina duplicação de ~50 linhas por job
    - Garante logging consistente
    - Facilita debug e troubleshooting
    - Centraliza error handling
    """

    def __init__(self):
        """Inicializa o job. Pode ser sobrescrito se necessário."""
        self._start_time: Optional[datetime] = None
        self._end_time: Optional[datetime] = None

    @abstractmethod
    def get_job_name(self) -> str:
        """
        Retorna o nome do job para logging.

        Returns:
            str: Nome do job em MAIÚSCULAS (ex: "RESUMO-MATINAL")

        Example:
            def get_job_name(self) -> str:
                return "DAILY-BRIEFING"
        """
        pass

    @abstractmethod
    def execute(self):
        """
        Lógica específica do job.

        IMPORTANTE: Este método é executado DENTRO do app.app_context(),
        então você pode importar e usar serviços livremente.

        Example:
            def execute(self):
                from app.services.notification_service import enviar
                from app import db_engine

                with db_engine.connect() as conn:
                    # ... sua lógica
                    pass
        """
        pass

    def _log(self, message: str, level: str = "INFO"):
        """
        Log padronizado com timestamp e nome do job.

        Args:
            message: Mensagem a ser logada
            level: Nível do log (INFO, WARNING, ERROR)
        """
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        job_name = self.get_job_name()
        print(f"[{timestamp}] [{job_name}] {level}: {message}")

    def _log_separator(self):
        """Imprime separador visual para facilitar leitura dos logs."""
        print("\n" + "=" * 70)

    def _log_start(self):
        """Log de início da execução."""
        self._start_time = datetime.now()
        self._log_separator()
        self._log(f"INICIANDO PROCESSAMENTO")
        self._log(f"Timestamp: {self._start_time.strftime('%d/%m/%Y %H:%M:%S')}")
        self._log_separator()

    def _log_end(self, success: bool = True):
        """
        Log de fim da execução.

        Args:
            success: Se a execução foi bem-sucedida
        """
        self._end_time = datetime.now()
        duration = (self._end_time - self._start_time).total_seconds()

        self._log_separator()
        if success:
            self._log(f"PROCESSAMENTO CONCLUÍDO COM SUCESSO")
        else:
            self._log(f"PROCESSAMENTO FINALIZADO COM ERROS", level="ERROR")
        self._log(f"Duração: {duration:.2f} segundos")
        self._log_separator()

    def _create_app_context(self):
        """
        Cria e retorna o contexto da aplicação Flask.

        Returns:
            Flask app context manager
        """
        try:
            from app import create_app
            app = create_app()
            return app.app_context()
        except Exception as e:
            self._log(f"ERRO CRÍTICO ao criar Flask app: {e}", level="ERROR")
            raise

    def run(self):
        """
        Método template que orquestra a execução do job.

        Este método:
        1. Faz log de início
        2. Cria Flask app context
        3. Executa a lógica específica (execute())
        4. Trata erros de forma consistente
        5. Faz log de fim

        NÃO sobrescreva este método. Sobrescreva execute() ao invés.
        """
        success = False

        try:
            # 1. Log de início
            self._log_start()

            # 2. Criar Flask app context
            app_context = self._create_app_context()

            # 3. Executar dentro do contexto
            with app_context:
                try:
                    # Executar lógica específica do job
                    self.execute()
                    success = True

                except Exception as e:
                    self._log(f"ERRO durante execução: {str(e)}", level="ERROR")
                    self._log(f"Tipo do erro: {type(e).__name__}", level="ERROR")

                    # Imprimir stack trace completo para debug
                    import traceback
                    self._log("Stack trace completo:", level="ERROR")
                    traceback.print_exc()

                    raise  # Re-raise para ser capturado pelo except externo

        except KeyboardInterrupt:
            self._log("Job interrompido pelo usuário (Ctrl+C)", level="WARNING")
            success = False

        except Exception as e:
            self._log(f"ERRO CRÍTICO: {str(e)}", level="ERROR")
            success = False
            # Não propaga o erro para permitir log de fim
            # Em produção, você pode querer propagar para o Ofelia detectar falha

        finally:
            # 4. Log de fim (sempre executado)
            self._log_end(success=success)

        # Retornar código de saída apropriado
        return 0 if success else 1


# ============================================================
# EXEMPLO DE USO
# ============================================================

class ExampleJob(BaseJob):
    """
    Exemplo de job concreto.
    DELETE ESTA CLASSE quando todos os jobs forem migrados.
    """

    def get_job_name(self) -> str:
        return "EXAMPLE-JOB"

    def execute(self):
        """Implementação de exemplo."""
        self._log("Executando lógica de exemplo...")

        # Simular importação de serviços
        # from app.services.example_service import processar
        # processar()

        self._log("Exemplo concluído!")


# Para testes manuais
if __name__ == "__main__":
    print("Executando BaseJob em modo de teste...")
    job = ExampleJob()
    exit_code = job.run()
    sys.exit(exit_code)
