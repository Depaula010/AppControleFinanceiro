# Fase B.2 - Plano de Refatoração do finance_service.py

**Arquivo:** `app/services/finance_service.py`
**Tamanho atual:** 1.701 linhas, 34 funções
**Objetivo:** Quebrar em módulos de serviço especializados por domínio

---

## 📊 Análise Atual

### Estatísticas
- **Total de linhas:** 1.701
- **Total de funções:** 34
- **Média de linhas por função:** ~50 linhas
- **Problema:** God object com múltiplas responsabilidades misturadas

### Funções Agrupadas por Domínio

#### 1. **Faturas (Invoices)** - 3 funções, ~180 linhas
```python
12:  get_or_create_fatura(conn, conta_id, data_transacao, usuario_id)          # 68 linhas
447: ensure_current_invoice_exists(conn, usuario_id, conta_id_cartao=None)     # 119 linhas
566: get_fatura_valor(conn, usuario_id, conta_id_cartao=None)                  # 70 linhas
```

#### 2. **Setup/Database** - 7 funções, ~520 linhas
```python
80:   clear_bot_session()                                                      # 21 linhas
101:  setup_database_schema()                                                  # 34 linhas
135:  populate_global_categories()                                             # 48 linhas
183:  setup_user_data(numero_whatsapp, dia_venc_cartao, dia_fech_cartao)      # 68 linhas
1049: add_google_calendar_tokens_table()                                       # 33 linhas
1082: add_nightly_checkin_config_columns()                                     # 49 linhas
1131: criar_tabelas_chaves_api()                                               # 169 linhas
```

#### 3. **Usuários (Users)** - 2 funções, ~40 linhas
```python
251: get_user_by_api_key(api_key)                                              # 37 linhas
288: get_user_by_whatsapp(numero_whatsapp)                                     # 7 linhas
```

#### 4. **Contas (Accounts)** - 8 funções, ~250 linhas
```python
295:  get_user_accounts(conn, usuario_id)                                      # 5 linhas
327:  get_account_by_name(conn, usuario_id, nome_conta, fallback=False)       # 18 linhas
345:  get_account_details_by_name(conn, usuario_id, nome_conta)               # 17 linhas
636:  get_saldo_contas(conn, usuario_id, conta_id=None)                       # 56 linhas
692:  update_saldo_inicial(conn, usuario_id, conta_id, novo_saldo_inicial)    # 28 linhas
1614: get_user_default_accounts(conn, usuario_id)                              # 19 linhas
1633: set_user_default_account(conn, usuario_id, tipo, conta_id)              # 18 linhas
1651: choose_account_for_transaction(conn, usuario_id, texto_msg, tipo_trans) # 51 linhas
```

#### 5. **Categorias (Categories)** - 4 funções, ~80 linhas
```python
300: get_user_categories(conn, usuario_id, tipo_transacao)                     # 17 linhas
317: get_fallback_category_id(conn, tipo_transacao)                            # 10 linhas
362: get_category_name_by_id(conn, subcategoria_id)                            # 6 linhas
870: get_category_spending(conn, usuario_id, nome_categoria_consulta)         # 26 linhas
```

#### 6. **Transações (Transactions)** - 3 funções, ~80 linhas
```python
368: create_transaction(conn, usuario_id, conta_id, subcategoria_id, ...)     # 14 linhas
382: create_transfer_pair(conn, usuario_id, conta_id_origem, ...)             # 30 linhas
412: create_fatura_payment(conn, usuario_id, conta_id_origem, ...)            # 35 linhas
```

#### 7. **Parcelamentos** - 1 função, ~54 linhas
```python
720: create_parcelamento_agendamento(conn, usuario_id, conta_id, ...)         # 54 linhas
```

#### 8. **Potes** - 1 função, ~21 linhas
```python
774: get_pote_status(conn, usuario_id)                                         # 21 linhas
```

#### 9. **Reserva de Emergência** - 1 função, ~75 linhas
```python
795: get_reserva_status(conn, usuario_id)                                      # 75 linhas
```

#### 10. **Vencimentos (Bills)** - 3 funções, ~315 linhas
```python
896:  get_upcoming_bills_and_invoices(conn, usuario_id, target_date=None)     # 153 linhas
1300: get_vencimentos_periodo(conn, usuario_id, data_inicio, data_fim)        # 164 linhas
1464: format_vencimentos_message(vencimentos, periodo, data_referencia)       # 83 linhas
```

#### 11. **Utilities (Text Processing)** - 1 função, ~67 linhas
```python
1547: extract_mentioned_account(conn, usuario_id, texto_msg)                   # 67 linhas
```

---

## 🎯 Estratégia de Refatoração

### Princípios
1. **Domain-Driven Design:** Agrupar por domínio de negócio
2. **Single Responsibility:** Cada módulo uma responsabilidade
3. **Facade Pattern:** Manter finance_service.py como facade para compatibilidade
4. **Gradual Migration:** Permitir migração gradual via feature flags

### Estrutura Proposta

```
app/services/finance/
├── __init__.py                  # Facade + exports
├── _database.py                 # Utilitários de DB compartilhados
├── invoice_service.py           # Serviço de faturas (3 funções)
├── account_service.py           # Serviço de contas (8 funções)
├── category_service.py          # Serviço de categorias (4 funções)
├── transaction_service.py       # Serviço de transações (3 funções)
├── installment_service.py       # Serviço de parcelamentos (1 função)
├── pot_service.py               # Serviço de potes (1 função)
├── emergency_reserve_service.py # Serviço de reserva (1 função)
├── bills_service.py             # Serviço de vencimentos (3 funções)
├── user_service.py              # Serviço de usuários (2 funções)
├── setup_service.py             # Serviço de setup/migrations (7 funções)
└── text_utils.py                # Utilitários de texto (1 função)
```

### Compatibilidade 100%

O arquivo original `app/services/finance_service.py` se tornará um **facade**:

```python
# app/services/finance_service.py
"""
Facade para serviços financeiros (REFATORADO - Fase B.2).

Este arquivo mantém compatibilidade 100% re-exportando funções
dos módulos especializados em app/services/finance/
"""

# Re-exportar tudo dos módulos
from .finance.invoice_service import (
    get_or_create_fatura,
    ensure_current_invoice_exists,
    get_fatura_valor
)

from .finance.account_service import (
    get_user_accounts,
    get_account_by_name,
    # ... etc
)

# ... todos os imports

# Manter __all__ para compatibilidade
__all__ = [
    'get_or_create_fatura',
    'ensure_current_invoice_exists',
    # ... todas as 34 funções
]
```

**Resultado:** Qualquer código que faz `from app.services.finance_service import get_saldo_contas` continua funcionando!

---

## 📋 Plano de Implementação

### Fase 1: Infraestrutura (1 dia)
- [ ] Criar diretório `app/services/finance/`
- [ ] Criar `__init__.py` (facade vazio)
- [ ] Criar `_database.py` com utilitários compartilhados

### Fase 2: Módulos Simples (2 dias)
- [ ] `user_service.py` (2 funções, ~40 linhas)
- [ ] `pot_service.py` (1 função, ~21 linhas)
- [ ] `emergency_reserve_service.py` (1 função, ~75 linhas)
- [ ] `installment_service.py` (1 função, ~54 linhas)
- [ ] `text_utils.py` (1 função, ~67 linhas)

### Fase 3: Módulos de Negócio Core (3 dias)
- [ ] `category_service.py` (4 funções, ~80 linhas)
- [ ] `account_service.py` (8 funções, ~250 linhas)
- [ ] `transaction_service.py` (3 funções, ~80 linhas)
- [ ] `invoice_service.py` (3 funções, ~180 linhas)

### Fase 4: Módulos Complexos (3 dias)
- [ ] `bills_service.py` (3 funções, ~315 linhas)
- [ ] `setup_service.py` (7 funções, ~520 linhas)

### Fase 5: Facade e Testes (1 dia)
- [ ] Atualizar `finance_service.py` para ser facade
- [ ] Verificar todos os imports no projeto
- [ ] Documentar mudanças

**Total estimado:** ~10 dias de trabalho

---

## ⚠️ Riscos e Mitigações

### Risco 1: Quebrar código existente
**Mitigação:**
- Manter finance_service.py como facade
- Re-exportar todas as funções
- Testar imports em todos os arquivos

### Risco 2: Dependências circulares
**Mitigação:**
- Usar `_database.py` para utilidades compartilhadas
- Evitar imports entre serviços (usar facade)
- Se necessário, usar TYPE_CHECKING

### Risco 3: Funções grandes e complexas
**Mitigação:**
- Extrair primeiro sem refatorar internamente
- Fase futura para quebrar funções grandes
- Documentar TODOs para melhorias

---

## 📝 Checklist de Validação

Para cada módulo criado:
- [ ] Todas as funções extraídas
- [ ] Imports corretos
- [ ] Docstrings mantidas/melhoradas
- [ ] Re-exportado no facade
- [ ] Tipagem adicionada (se possível)

Para o projeto completo:
- [ ] `finance_service.py` limpo (apenas re-exports)
- [ ] Todos os imports verificados
- [ ] 100% compatibilidade mantida
- [ ] Documentação atualizada
- [ ] Testes passando (se existirem)

---

## 🎉 Resultado Esperado

**Antes:**
```
app/services/finance_service.py - 1.701 linhas, 34 funções
```

**Depois:**
```
app/services/finance_service.py - ~100 linhas (facade)
app/services/finance/
├── 12 módulos especializados
└── ~1.600 linhas totais (distribuídas)
```

**Benefícios:**
- ✅ Separação de responsabilidades clara
- ✅ Módulos pequenos e focados
- ✅ Fácil de testar isoladamente
- ✅ 100% compatibilidade mantida
- ✅ Preparado para testes unitários
- ✅ Base para futuras otimizações

---

**Status:** 📝 Planejamento completo
**Próximo passo:** Iniciar Fase 1 (Infraestrutura)
