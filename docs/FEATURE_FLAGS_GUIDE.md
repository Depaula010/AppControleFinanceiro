# Guia de Feature Flags - Migração Gradual SQL → ORM

**Data**: 2025-12-16
**Fase**: D.6 - Feature Flags Implementado

---

## 📋 Visão Geral

Feature flags permitem ativar/desativar o uso do ORM de forma granular, por módulo, sem quebrar funcionalidades existentes. Isso possibilita uma **migração gradual e controlada** de SQL legado para ORM.

### Benefícios

- ✅ **Migração sem downtime**: Habilitar ORM gradualmente em produção
- ✅ **Rollback imediato**: Desativar flag se houver problemas
- ✅ **Testes A/B**: Comparar performance SQL vs ORM
- ✅ **Segurança**: Validar comportamento antes de 100% rollout
- ✅ **Controle granular**: Habilitar por módulo (users, accounts, transactions)

---

## 🔧 Configuração

### Variáveis de Ambiente (.env)

```bash
# Flag global (override todas as outras)
USE_ORM_GLOBALLY=false

# Flags por módulo
USE_ORM_FOR_USERS=false
USE_ORM_FOR_ACCOUNTS=false
USE_ORM_FOR_TRANSACTIONS=false
USE_ORM_FOR_CATEGORIES=false
USE_ORM_FOR_INVOICES=false
USE_ORM_FOR_SCHEDULES=false
USE_ORM_FOR_BUDGETS=false
```

### Valores Aceitos

- **true**: Habilita ORM
- **false**: Usa SQL legado (padrão)
- Também aceita: `1`, `yes`, `on` para habilitar

---

## 💻 Como Usar

### 1. Uso Direto (Verificar Flag)

```python
from app.core import feature_flags

if feature_flags.use_orm_for_users:
    # Usar ORM
    from app.core import get_user_repository
    user_repo = get_user_repository()
    user = user_repo.get_by_whatsapp("+5511999999999")
else:
    # Usar SQL legado
    from app.database import get_db_connection
    sql = text("SELECT * FROM Usuarios WHERE numero_whatsapp = :numero")
    with get_db_connection() as conn:
        result = conn.execute(sql, {"numero": numero})
        user = result.fetchone()
```

### 2. Uso com Adaptadores (Recomendado)

Os adaptadores abstraem a verificação de flags:

```python
from app.infrastructure.database.adapters import (
    get_user_by_whatsapp,
    get_accounts_by_user,
    calculate_financial_summary
)

# Automaticamente roteia para ORM ou SQL baseado em flags
user = get_user_by_whatsapp("+5511999999999")
accounts = get_accounts_by_user(user['id'], active_only=True)
summary = calculate_financial_summary(user['id'], data_inicio, data_fim)
```

### 3. Exemplo Completo em Rota Flask

```python
from flask import Blueprint, jsonify
from app.infrastructure.database.adapters import (
    get_user_by_whatsapp,
    get_accounts_by_user
)

api_bp = Blueprint('api', __name__)

@api_bp.route('/users/<whatsapp>/summary', methods=['GET'])
def get_user_summary(whatsapp):
    """
    Esta rota funciona com ORM ou SQL, dependendo dos feature flags.
    Não precisa mudar nada no código!
    """
    # Buscar usuário (usa flag use_orm_for_users)
    user = get_user_by_whatsapp(whatsapp)

    if not user:
        return jsonify({"error": "Usuário não encontrado"}), 404

    # Buscar contas (usa flag use_orm_for_accounts)
    accounts = get_accounts_by_user(user['id'], active_only=True)

    return jsonify({
        "user": {
            "id": user['id'],
            "nome": user['nome'],
            "email": user['email']
        },
        "accounts": [
            {
                "id": acc['id'],
                "nome": acc['nome_conta'],
                "saldo_inicial": acc['saldo_inicial']
            }
            for acc in accounts
        ],
        "total_accounts": len(accounts)
    })
```

### 4. Verificar Status dos Flags

```python
from app.core import feature_flags

# Ver todos os flags
status = feature_flags.get_status()
print(status)
# {
#     'use_orm_globally': False,
#     'use_orm_for_users': True,
#     'use_orm_for_accounts': True,
#     'use_orm_for_transactions': False,
#     ...
# }

# Verificar flags individuais
if feature_flags.use_orm_for_users:
    print("ORM habilitado para usuários")
```

### 5. Recarregar Flags (Útil para Testes)

```python
import os
from app.core import feature_flags

# Mudar variável de ambiente
os.environ['USE_ORM_FOR_USERS'] = 'true'

# Recarregar flags
feature_flags.reload()

# Agora use_orm_for_users será True
```

---

## 🚀 Estratégia de Rollout

### Fase 1: Desenvolvimento (DEV)

**Objetivo**: Validar implementação ORM

1. **Habilitar ORM por módulo**:
   ```bash
   USE_ORM_FOR_USERS=true
   ```

2. **Testar funcionalidades**:
   - Login/autenticação
   - CRUD de usuários
   - Listagens

3. **Comparar resultados** SQL vs ORM:
   ```python
   # Temporariamente testar ambos
   user_orm = get_user_repository().get_by_whatsapp(numero)
   user_sql = execute_sql_legacy(numero)
   assert user_orm.id == user_sql['id']
   ```

4. **Monitorar logs** de erros

5. **Se OK**: Manter flag habilitada. **Se erro**: Desabilitar e corrigir.

### Fase 2: Staging (STG)

**Objetivo**: Validar em ambiente similar a produção

1. **Habilitar mesmo módulo que DEV**:
   ```bash
   USE_ORM_FOR_USERS=true
   ```

2. **Testes de carga**:
   - Simular tráfego real
   - Medir latência (SQL vs ORM)
   - Verificar uso de memória/CPU

3. **Testes de integração**:
   - Todas as rotas da API
   - Jobs cron
   - Webhooks

4. **Se OK**: Avançar para PROD. **Se erro**: Rollback e investigar.

### Fase 3: Produção (PROD) - Rollout Gradual

**Semana 1: Usuários (10%)**

```bash
USE_ORM_FOR_USERS=true
```

- Monitorar por 3-5 dias
- Verificar logs de erro
- Comparar métricas (latência, throughput)
- **Se OK**: Continuar. **Se erro**: Rollback imediato (`USE_ORM_FOR_USERS=false`)

**Semana 2: Contas (20%)**

```bash
USE_ORM_FOR_USERS=true
USE_ORM_FOR_ACCOUNTS=true
```

- Monitorar interação entre módulos
- Validar transações complexas
- **Se OK**: Continuar. **Se erro**: Rollback.

**Semana 3: Transações (30%)**

```bash
USE_ORM_FOR_USERS=true
USE_ORM_FOR_ACCOUNTS=true
USE_ORM_FOR_TRANSACTIONS=true
```

- **CRÍTICO**: Transações são o módulo mais usado
- Monitorar performance de cálculos (saldos, totais)
- Validar consolidação de transações
- **Se OK**: Continuar. **Se erro**: Rollback.

**Semana 4: Categorias, Faturas, etc. (50%)**

```bash
USE_ORM_FOR_USERS=true
USE_ORM_FOR_ACCOUNTS=true
USE_ORM_FOR_TRANSACTIONS=true
USE_ORM_FOR_CATEGORIES=true
USE_ORM_FOR_INVOICES=true
```

**Semana 5+: Restante (100%)**

```bash
# Opção 1: Habilitar tudo individualmente
USE_ORM_FOR_SCHEDULES=true
USE_ORM_FOR_BUDGETS=true

# Opção 2: Ativar globalmente (após validar todos os módulos)
USE_ORM_GLOBALLY=true
```

### Fase 4: Remoção de Código Legado

**Após 2-4 semanas com 100% ORM estável**:

1. Remover código SQL legado dos adaptadores
2. Remover flags de feature (não mais necessários)
3. Simplificar código (apenas ORM)
4. Atualizar documentação

---

## 📊 Monitoramento

### Métricas a Acompanhar

1. **Latência**:
   - Tempo de resposta das APIs
   - Comparar SQL vs ORM
   - Meta: ORM ≤ SQL + 20%

2. **Taxa de Erro**:
   - Logs de exceções
   - Comparar antes/depois de habilitar flag
   - Meta: Taxa de erro não aumentar

3. **Uso de Recursos**:
   - CPU, memória, conexões de DB
   - ORM pode usar mais memória (objetos Python)
   - Meta: Uso aceitável para benefícios ganhos

4. **Throughput**:
   - Requisições por segundo
   - Meta: Manter ou melhorar

### Ferramentas de Monitoramento

```python
import time
from app.core import feature_flags

def monitor_execution_time(func):
    """Decorator para medir tempo de execução."""
    def wrapper(*args, **kwargs):
        start = time.time()
        result = func(*args, **kwargs)
        elapsed = time.time() - start

        mode = "ORM" if feature_flags.use_orm_for_users else "SQL"
        print(f"[{mode}] {func.__name__} executou em {elapsed:.3f}s")

        return result
    return wrapper


@monitor_execution_time
def get_user_data(whatsapp):
    from app.infrastructure.database.adapters import get_user_by_whatsapp
    return get_user_by_whatsapp(whatsapp)
```

### Alertas

Configurar alertas para:
- Taxa de erro > 1% após habilitar flag
- Latência > 500ms em endpoints críticos
- Uso de memória > 80%
- Conexões de DB > 90% do pool

---

## 🔄 Rollback

### Rollback Imediato (Produção)

Se houver problemas críticos após habilitar flag:

1. **Desabilitar flag no .env**:
   ```bash
   USE_ORM_FOR_USERS=false
   ```

2. **Reiniciar aplicação** (se necessário):
   ```bash
   docker-compose restart web
   ```

3. **Verificar logs**:
   ```bash
   docker-compose logs -f web | grep ERROR
   ```

4. **Comunicar equipe** sobre rollback

5. **Investigar causa raiz** antes de reabilitar

### Rollback Gradual

Se problemas forem detectados mas não críticos:

1. **Reduzir percentual** (voltar 1 fase):
   ```bash
   # Era: users + accounts + transactions
   USE_ORM_FOR_TRANSACTIONS=false
   # Volta para: users + accounts
   ```

2. **Monitorar melhora**

3. **Analisar problema** na transação

4. **Corrigir** e retentar

---

## 🧪 Testes

### Teste de Integração

```python
import pytest
from app.infrastructure.database.adapters import get_user_by_whatsapp
from app.core import feature_flags

def test_get_user_with_orm():
    """Testa busca de usuário com ORM habilitado."""
    # Habilitar ORM
    import os
    os.environ['USE_ORM_FOR_USERS'] = 'true'
    feature_flags.reload()

    # Buscar usuário
    user = get_user_by_whatsapp("+5511999999999")

    # Validações
    assert user is not None
    assert user['numero_whatsapp'] == "+5511999999999"
    assert 'id' in user
    assert 'nome' in user


def test_get_user_with_sql():
    """Testa busca de usuário com SQL legado."""
    # Desabilitar ORM
    import os
    os.environ['USE_ORM_FOR_USERS'] = 'false'
    feature_flags.reload()

    # Buscar usuário
    user = get_user_by_whatsapp("+5511999999999")

    # Validações
    assert user is not None
    assert user['numero_whatsapp'] == "+5511999999999"


def test_consistency_sql_vs_orm():
    """Verifica que SQL e ORM retornam mesmos dados."""
    numero = "+5511999999999"

    # Buscar com SQL
    import os
    os.environ['USE_ORM_FOR_USERS'] = 'false'
    feature_flags.reload()
    user_sql = get_user_by_whatsapp(numero)

    # Buscar com ORM
    os.environ['USE_ORM_FOR_USERS'] = 'true'
    feature_flags.reload()
    user_orm = get_user_by_whatsapp(numero)

    # Comparar
    assert user_sql['id'] == user_orm['id']
    assert user_sql['nome'] == user_orm['nome']
    assert user_sql['email'] == user_orm['email']
```

### Teste de Performance

```python
import time
from app.infrastructure.database.adapters import calculate_financial_summary
from datetime import date

def test_performance_sql_vs_orm():
    """Compara performance de cálculo financeiro."""
    user_id = 1
    inicio = date(2025, 12, 1)
    fim = date(2025, 12, 31)

    # Testar SQL
    import os
    os.environ['USE_ORM_FOR_TRANSACTIONS'] = 'false'
    feature_flags.reload()

    start = time.time()
    summary_sql = calculate_financial_summary(user_id, inicio, fim)
    time_sql = time.time() - start

    # Testar ORM
    os.environ['USE_ORM_FOR_TRANSACTIONS'] = 'true'
    feature_flags.reload()

    start = time.time()
    summary_orm = calculate_financial_summary(user_id, inicio, fim)
    time_orm = time.time() - start

    # Comparar
    print(f"SQL: {time_sql:.3f}s | ORM: {time_orm:.3f}s")

    # Validar consistência
    assert summary_sql['receitas'] == summary_orm['receitas']
    assert summary_sql['despesas'] == summary_orm['despesas']

    # Validar performance (ORM pode ser até 50% mais lento inicialmente)
    assert time_orm < time_sql * 1.5
```

---

## ⚠️ Boas Práticas

1. **Nunca habilitar em PROD sem testar em DEV/STG**
2. **Habilitar 1 módulo por vez** (não multiple flags simultaneamente)
3. **Monitorar por 3-5 dias** antes de próximo módulo
4. **Ter plano de rollback** sempre pronto
5. **Documentar problemas** encontrados e soluções
6. **Comunicar equipe** antes de mudanças em PROD
7. **Backup do banco** antes de mudanças críticas
8. **Logs detalhados** durante período de migração

---

## 📝 Checklist de Rollout

### Antes de Habilitar Flag em PROD

- [ ] Testado em DEV por 2+ dias
- [ ] Testado em STG por 2+ dias
- [ ] Testes automatizados passando
- [ ] Performance validada (SQL vs ORM)
- [ ] Logs de erro verificados
- [ ] Plano de rollback documentado
- [ ] Equipe comunicada
- [ ] Monitoramento configurado
- [ ] Backup do banco recente

### Depois de Habilitar Flag em PROD

- [ ] Monitorar logs por 1 hora contínua
- [ ] Verificar taxa de erro (não aumentou)
- [ ] Verificar latência (aceitável)
- [ ] Testar funcionalidades críticas manualmente
- [ ] Verificar uso de recursos (CPU/memória)
- [ ] Documentar observações
- [ ] Manter flag habilitada por 3-5 dias antes de próximo módulo

---

## 🔗 Arquivos Relacionados

- [app/core/feature_flags.py](../app/core/feature_flags.py) - Sistema de feature flags
- [app/infrastructure/database/adapters.py](../app/infrastructure/database/adapters.py) - Adaptadores SQL/ORM
- [.env.example](../.env.example) - Exemplo de configuração
- [PHASE_D_PROGRESS.md](PHASE_D_PROGRESS.md) - Progresso da Fase D

---

**Próximos Passos**: Após validar todos os módulos com flags, iniciar Fase E (eliminação de duplicações) e Fase F (quebra de god objects).
