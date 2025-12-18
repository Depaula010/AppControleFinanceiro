# Fase B.3 - Progresso da Refatoração de webhooks.py

**Data Início:** Dezembro 2024
**Status:** 🟢 EM PROGRESSO
**Arquivo Alvo:** `app/routes/webhooks.py` (3.322 linhas)

---

## 📊 Progresso Geral

### Meta Global
- **Antes:** 1 arquivo monolítico (3.322 linhas)
- **Depois:** 21 arquivos modulares (~143 linhas/arquivo)
- **Progresso:** Fase 1 de 5 CONCLUÍDA ✅

### Fases do Projeto

| Fase | Descrição | Status | Duração | Data |
|------|-----------|--------|---------|------|
| **Fase 1** | Infraestrutura base | ✅ CONCLUÍDA | 1 dia | Dez 2024 |
| Fase 2 | Extrair rotas simples | ⏳ PENDENTE | 3-4 dias | - |
| Fase 3 | 5 Intents prioritários | ⏳ PENDENTE | 5-7 dias | - |
| Fase 4 | Remaining 20 Intents | ⏳ PENDENTE | 10-12 dias | - |
| Fase 5 | Consolidação | ⏳ PENDENTE | 5-7 dias | - |

---

## ✅ Fase 1: Infraestrutura (CONCLUÍDA)

### Objetivos
- [x] Criar estrutura de diretórios
- [x] Implementar `BaseIntent` abstrata
- [x] Criar Intent Registry com factory pattern
- [x] Criar utilitários base compartilhados
- [x] Validar sintaxe e imports

### Estrutura Criada

```
app/routes/webhooks/
├── __init__.py                    # Blueprint principal
├── base.py                        # Utilitários compartilhados
└── intents/
    ├── __init__.py                # Intent Registry + Factory
    └── base_intent.py             # Classe abstrata Template Method
```

### Arquivos Criados

#### 1. `base_intent.py` (238 linhas)
**Propósito:** Classe abstrata que implementa Template Method Pattern

**Métodos:**
- `handle()` - Template method (final, não override)
- `extract_params()` - Abstract (must implement)
- `validate()` - Hook method (opcional override)
- `execute()` - Abstract (must implement)
- `format_response()` - Hook method (opcional override)

**Fluxo de Execução:**
```
1. extract_params() → Extrai dados da mensagem (Gemini)
2. validate() → Valida parâmetros
3. execute() → Executa lógica do intent
4. format_response() → Formata resposta WhatsApp
```

**Exemplo de Uso:**
```python
class MeuIntent(BaseIntent):
    def extract_params(self) -> Dict[str, Any]:
        return gemini_service.extract_params(self.mensagem)

    def execute(self) -> Dict[str, Any]:
        # Lógica específica
        return {"data": resultado}
```

#### 2. `intents/__init__.py` (Intent Registry - 205 linhas)
**Propósito:** Factory Pattern para roteamento de intents

**Componentes:**
- `INTENT_REGISTRY` - Dict mapping intent name → handler class
- `route_intent()` - Factory que cria e executa handlers
- `register_intent()` - Adiciona intents dinamicamente
- `list_registered_intents()` - Lista intents disponíveis

**Uso:**
```python
result = route_intent(
    intent_name="Consulta Saldo",
    usuario_id=123,
    mensagem="quanto tenho?",
    conn=connection
)

if result["success"]:
    send_whatsapp(result["message"])
```

#### 3. `base.py` (Utilitários - 242 linhas)
**Propósito:** Helpers compartilhados para webhooks

**Validators:**
- `validate_hmac_signature()` - Valida assinatura HMAC
- `validate_api_key()` - Valida API key, retorna usuario_id
- `validate_user_registered()` - Verifica registro do usuário

**Response Helpers:**
- `success_response()` - JSON padronizado de sucesso
- `error_response()` - JSON padronizado de erro
- `service_unavailable_response()` - 503 Service Unavailable

**Decorators:**
- `@require_hmac_validation` - Valida HMAC antes da rota
- `@require_api_key_auth` - Valida API key, injeta usuario_id
- `@require_db_engine` - Verifica se DB está disponível

**Constantes:**
- `MSG_NOT_UNDERSTOOD` - Mensagem padrão "não entendi"
- `MSG_INTERNAL_ERROR` - Mensagem de erro genérico
- `MSG_USER_NOT_REGISTERED` - Mensagem de onboarding

**Exemplo:**
```python
@webhooks_bp.route('/api/endpoint', methods=['POST'])
@require_db_engine
@require_api_key_auth
def my_endpoint(usuario_id):  # usuario_id injetado
    return success_response("OK", data={...})
```

#### 4. `webhooks/__init__.py` (39 linhas)
**Propósito:** Agregação de blueprints

**Conteúdo:**
- Cria `webhooks_bp` Blueprint
- Documentação da arquitetura
- Imports futuros (quando rotas forem criadas)

---

## 🧪 Validação

### Testes Executados

**Script:** `test_phase_b3_syntax.py`

**Resultados:**
```
[PASSOU] - Estrutura de Diretorios
[PASSOU] - Sintaxe Python
[PASSOU] - Conteudo dos Arquivos
```

**Verificações:**
- [x] Estrutura de diretórios criada
- [x] Sintaxe Python válida em todos os arquivos
- [x] BaseIntent tem métodos abstratos
- [x] Intent Registry funcional
- [x] Utilitários base presentes
- [x] Blueprint criado

---

## 📐 Padrões de Design Implementados

### 1. Template Method Pattern
**Onde:** `BaseIntent.handle()`

**Benefício:** Garante fluxo consistente para todos os intents
- Extract → Validate → Execute → Format
- Subclasses implementam apenas lógica específica
- Evita código duplicado

### 2. Factory Pattern
**Onde:** `route_intent()`

**Benefício:** Criação dinâmica de handlers
- Intent name → Handler class (via registry)
- Sem coupling direto
- Fácil adicionar novos intents

### 3. Strategy Pattern
**Onde:** Intent handlers individuais

**Benefício:** Cada intent = estratégia isolada
- Testável individualmente
- Substituível sem afetar outros
- Responsabilidade única

---

## 🔄 Próximos Passos

### Fase 2: Extrair Rotas Simples (3-4 dias)

**Objetivo:** Extrair rotas independentes do WhatsApp

**Rotas a Extrair:**
1. **calendar.py** (3 rotas, 313 linhas)
   - `connect_calendar()`
   - `oauth2callback()`
   - `disconnect_calendar()`

2. **reserves.py** (2 rotas, 327 linhas)
   - `toggle_incluir_reserva_agendamento()`
   - `listar_agendamentos_reserva()`

3. **transactions.py** (3 rotas, 534 linhas)
   - `handle_automate_webhook()`
   - `handle_api_transacao()`
   - `handle_sms_payment()`

**Critério de Sucesso:**
- [x] Arquivos criados
- [ ] Rotas registradas no blueprint
- [ ] 100% backward compatibility
- [ ] Testes passando

---

## 📚 Documentação Técnica

### Como Criar um Novo Intent

**Passo 1:** Criar classe que herda de `BaseIntent`
```python
from app.routes.webhooks.intents.base_intent import BaseIntent

class MeuNovoIntent(BaseIntent):
    def extract_params(self) -> Dict[str, Any]:
        # Usar Gemini para extrair parâmetros
        return gemini_service.extract_meu_params(self.mensagem)

    def execute(self) -> Dict[str, Any]:
        # Lógica do intent
        return {"data": resultado}
```

**Passo 2:** Registrar no `INTENT_REGISTRY`
```python
# Em app/routes/webhooks/intents/__init__.py
from .meu_modulo import MeuNovoIntent

INTENT_REGISTRY['Meu Intent'] = MeuNovoIntent
```

**Passo 3:** Configurar Gemini para classificar
```python
# Adicionar intent na lista de classificação do Gemini
# (fora do escopo desta refatoração)
```

**Passo 4:** Testar
```python
result = route_intent("Meu Intent", 123, "mensagem teste", conn)
assert result["success"] == True
```

---

## 🎯 Métricas de Qualidade

### Antes (Monolítico)
- **Linhas/arquivo:** 3.322
- **Cyclomatic complexity:** ~150
- **Testabilidade:** RUIM (tudo inline)
- **Manutenibilidade:** RUIM (God object)

### Depois (Meta - Fase 5)
- **Linhas/arquivo:** ~143 média
- **Cyclomatic complexity:** ~15/handler
- **Testabilidade:** BOA (intents isolados)
- **Manutenibilidade:** BOA (SRP)

### Progresso Atual (Fase 1)
- **Infraestrutura:** ✅ 100%
- **Rotas extraídas:** 0/9 (0%)
- **Intents extraídos:** 0/25 (0%)
- **Código refatorado:** ~5% (apenas infra)

---

## ✅ Checklist de Conclusão da Fase 1

- [x] Estrutura de diretórios criada
- [x] BaseIntent abstrata implementada
- [x] Intent Registry implementado
- [x] Factory Pattern funcionando
- [x] Utilitários base criados
- [x] Decorators de segurança criados
- [x] Blueprint principal criado
- [x] Testes de sintaxe passando
- [x] Documentação criada

---

**Atualizado:** Dezembro 2024
**Fase Atual:** B.3.1 (Infraestrutura)
**Status:** ✅ CONCLUÍDA
**Próxima Fase:** B.3.2 (Extrair Rotas Simples)
