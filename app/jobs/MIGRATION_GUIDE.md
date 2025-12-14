# 📘 Guia de Migração para BaseJob

Este guia mostra como refatorar cron jobs existentes para usar a nova `BaseJob` class.

---

## 🎯 Benefícios da BaseJob

✅ **Elimina ~50 linhas de código duplicado** por job
✅ **Logging consistente** em todos os jobs
✅ **Error handling padronizado** com stack traces
✅ **Template Method Pattern** - focue apenas na lógica de negócio
✅ **Facilita testes** - pode mockar apenas o método `execute()`

---

## 📋 Checklist de Migração

Para cada cron job:

- [ ] Herdar de `BaseJob`
- [ ] Implementar `get_job_name()` - retorna nome em MAIÚSCULAS
- [ ] Implementar `execute()` - move lógica para dentro deste método
- [ ] Remover código boilerplate (path setup, app context, prints)
- [ ] Atualizar `if __name__ == "__main__"` para usar `job.run()`
- [ ] Testar execução manual: `python app/jobs/seu_job.py`
- [ ] Validar logs e error handling

---

## 🔄 Exemplo Prático: Migração do task_alerts.py

### ❌ ANTES (Código Original - 85 linhas)

```python
#!/usr/bin/env python3
"""
Script de cronjob para processar alertas de tarefas do Google Calendar.
"""

import sys
import os

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from datetime import datetime
from zoneinfo import ZoneInfo

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

def processar_alertas_tarefas():
    """
    Processa alertas de tarefas do Google Calendar.
    """
    print("\n" + "="*60)
    print("INICIANDO PROCESSAMENTO DE ALERTAS DE TAREFAS")
    print(f"Data/Hora: {datetime.now(TIMEZONE_BR).strftime('%d/%m/%Y %H:%M:%S')}")
    print("="*60)

    try:
        # IMPORTANTE: Criar instância da aplicação
        from app import create_app
        app = create_app()

        # Entrar no contexto da aplicação
        with app.app_context():
            # Importar serviços
            from app.services.calendar_alert_config_service import CalendarAlertConfigService
            from app.services.calendar_alert_service import CalendarAlertService

            # Buscar usuários
            usuarios = CalendarAlertConfigService.get_users_with_alerts_active()

            if not usuarios:
                print("[ALERTAS-TAREFAS] ℹ️  Nenhum usuário com alertas ativos")
                return

            print(f"[ALERTAS-TAREFAS] Processando {len(usuarios)} usuário(s)")

            total_alertas = 0

            for usuario_id, numero_whatsapp, minutos_antes in usuarios:
                print(f"\n[ALERTAS-TAREFAS] Processando usuário {usuario_id}")

                alertas_enviados = CalendarAlertService.process_alerts_for_user(
                    usuario_id=usuario_id,
                    numero_whatsapp=numero_whatsapp,
                    minutos_antes=minutos_antes
                )

                total_alertas += alertas_enviados
                print(f"[ALERTAS-TAREFAS] {alertas_enviados} alerta(s) enviado(s)")

            print(f"\n[ALERTAS-TAREFAS] ✅ Total: {total_alertas} alerta(s) processado(s)")

    except Exception as e:
        print(f"\n[ALERTAS-TAREFAS] ❌ ERRO: {str(e)}")
        import traceback
        traceback.print_exc()
        raise

    finally:
        print("\n" + "="*60)
        print("PROCESSAMENTO FINALIZADO")
        print("="*60 + "\n")


if __name__ == "__main__":
    processar_alertas_tarefas()
```

---

### ✅ DEPOIS (Código Refatorado - 45 linhas)

```python
#!/usr/bin/env python3
"""
Cron job para processar alertas de tarefas do Google Calendar.
Executado a cada minuto pelo Ofelia.
"""

from app.jobs.base_job import BaseJob
from datetime import datetime
from zoneinfo import ZoneInfo

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


class TaskAlertsJob(BaseJob):
    """
    Processa alertas de eventos do Google Calendar.

    Fluxo:
    1. Busca usuários com alertas ativos
    2. Para cada usuário, verifica eventos próximos
    3. Envia alertas via WhatsApp
    """

    def get_job_name(self) -> str:
        return "ALERTAS-TAREFAS"

    def execute(self):
        """
        Lógica de processamento de alertas.
        Executado dentro do Flask app context.
        """
        # Importar serviços (já estamos dentro do app context)
        from app.services.calendar_alert_config_service import CalendarAlertConfigService
        from app.services.calendar_alert_service import CalendarAlertService

        # Buscar usuários com alertas ativos
        usuarios = CalendarAlertConfigService.get_users_with_alerts_active()

        if not usuarios:
            self._log("Nenhum usuário com alertas ativos")
            return

        self._log(f"Processando {len(usuarios)} usuário(s)")

        total_alertas = 0

        for usuario_id, numero_whatsapp, minutos_antes in usuarios:
            self._log(f"Processando usuário {usuario_id} ({numero_whatsapp})")
            self._log(f"Configuração: {minutos_antes} minuto(s) antes")

            # Processar alertas
            alertas_enviados = CalendarAlertService.process_alerts_for_user(
                usuario_id=usuario_id,
                numero_whatsapp=numero_whatsapp,
                minutos_antes=minutos_antes
            )

            total_alertas += alertas_enviados
            self._log(f"{alertas_enviados} alerta(s) enviado(s) para usuário {usuario_id}")

        self._log(f"Total: {total_alertas} alerta(s) processado(s)")


if __name__ == "__main__":
    job = TaskAlertsJob()
    exit_code = job.run()
    exit(exit_code)
```

---

## 📊 Comparação Antes vs Depois

| Aspecto | Antes | Depois | Melhoria |
|---------|-------|--------|----------|
| **Linhas de código** | 85 | 45 | -47% 📉 |
| **Boilerplate** | 40 linhas | 0 linhas | -100% |
| **Error handling** | Manual | Automático | ✅ |
| **Logging** | Inconsistente | Padronizado | ✅ |
| **Testabilidade** | Difícil | Fácil (mock execute) | ✅ |
| **Manutenibilidade** | Baixa | Alta | ✅ |

---

## 🔧 Método `execute()` - Pontos de Atenção

### ✅ PODE fazer dentro de execute():

```python
def execute(self):
    # ✅ Importar serviços
    from app.services.meu_service import processar

    # ✅ Acessar db_engine
    from app import db_engine
    with db_engine.connect() as conn:
        # ... queries

    # ✅ Usar self._log() para logging
    self._log("Processando...")
    self._log("Erro encontrado!", level="ERROR")

    # ✅ Lançar exceções (serão capturadas)
    if erro:
        raise ValueError("Algo deu errado")

    # ✅ Usar serviços
    resultado = processar()
```

### ❌ NÃO fazer:

```python
def execute(self):
    # ❌ NÃO criar Flask app (já está criado)
    from app import create_app
    app = create_app()  # ERRADO!

    # ❌ NÃO manipular app context (já está ativo)
    with app.app_context():  # ERRADO!
        ...

    # ❌ NÃO fazer print direto (use self._log)
    print("Mensagem")  # EVITE

    # ❌ NÃO manipular sys.path (já configurado)
    sys.path.insert(0, ...)  # ERRADO!
```

---

## 🧪 Testes

### Teste Manual

```bash
# Executar job manualmente
python app/jobs/task_alerts.py

# Output esperado:
======================================================================
[2025-12-14 15:30:00] [ALERTAS-TAREFAS] INFO: INICIANDO PROCESSAMENTO
[2025-12-14 15:30:00] [ALERTAS-TAREFAS] INFO: Timestamp: 14/12/2025 15:30:00
======================================================================
[2025-12-14 15:30:01] [ALERTAS-TAREFAS] INFO: Processando 2 usuário(s)
...
======================================================================
[2025-12-14 15:30:05] [ALERTAS-TAREFAS] INFO: PROCESSAMENTO CONCLUÍDO COM SUCESSO
[2025-12-14 15:30:05] [ALERTAS-TAREFAS] INFO: Duração: 4.23 segundos
======================================================================
```

### Teste Unitário (Exemplo)

```python
import pytest
from app.jobs.task_alerts import TaskAlertsJob

class TestTaskAlertsJob:
    def test_job_name(self):
        job = TaskAlertsJob()
        assert job.get_job_name() == "ALERTAS-TAREFAS"

    def test_execute_with_mock(self, mocker):
        # Mock dos serviços
        mock_service = mocker.patch('app.services.calendar_alert_service.CalendarAlertService')
        mock_service.get_users_with_alerts_active.return_value = []

        job = TaskAlertsJob()
        # Teste sem criar Flask app real
        job.execute()

        mock_service.get_users_with_alerts_active.assert_called_once()
```

---

## 📝 Jobs Pendentes de Migração

- [ ] `daily_briefing.py` - Resumo matinal
- [ ] `nightly_checkin.py` - Check-in noturno
- [ ] `schedule_processor.py` - Motor de agendamentos
- [ ] `task_alerts.py` - Alertas de tarefas

**Estimativa**: ~30 minutos por job

---

## 🎓 Conceitos Aplicados

### Design Patterns

1. **Template Method Pattern**
   - `run()` é o método template (não sobrescrever)
   - `execute()` é o método abstrato (implementar)

2. **Strategy Pattern**
   - Cada job é uma estratégia diferente de execução
   - BaseJob fornece a estrutura comum

3. **Dependency Injection**
   - Flask app context injetado automaticamente
   - Serviços importados dentro de execute()

### SOLID Principles

- **S**: Single Responsibility - BaseJob cuida apenas de orquestração
- **O**: Open/Closed - Aberto para extensão (herança), fechado para modificação
- **L**: Liskov Substitution - Qualquer BaseJob pode substituir outro
- **I**: Interface Segregation - Interface mínima (2 métodos abstratos)
- **D**: Dependency Inversion - Depende de abstrações (ABC)

---

**Próximo passo**: Migrar os 4 jobs existentes para usar BaseJob! 🚀
