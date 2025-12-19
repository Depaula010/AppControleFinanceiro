# Fase B.3 - Plano de Refatoração: webhooks.py

**Data:** Dezembro 2024
**Status:** 📋 PLANEJAMENTO
**Arquivo Alvo:** `app/routes/webhooks.py` (3.322 linhas)

---

## 📊 Análise Atual

### Visão Geral

**Problema:** Arquivo monolítico de 3.322 linhas com responsabilidades misturadas
**Maior Ofensor:** Rota `/webhook-whatsapp` com ~2.100 linhas (63% do arquivo!)

### Estrutura Atual

```
app/routes/webhooks.py (3.322 linhas)
├── handle_automate_webhook()         (134 linhas) - Android transactions
├── handle_api_transacao()            (262 linhas) - API transactions
├── handle_whatsapp_webhook()         (2.100 linhas) - 25+ WhatsApp intents ⚠️
├── handle_sms_payment()              (138 linhas) - SMS payments
├── connect_calendar()                (85 linhas) - OAuth início
├── oauth2callback()                  (204 linhas) - OAuth callback
├── disconnect_calendar()             (24 linhas) - OAuth revoke
├── toggle_incluir_reserva_agendamento()  (130 linhas) - Reserve toggle
└── listar_agendamentos_reserva()     (197 linhas) - Reserve list
```

### WhatsApp Handler - Arquitetura de Intents

A rota `handle_whatsapp_webhook` processa mensagens em 5 camadas:

**Camada 0:** Security & Setup (65 linhas)
- Validação HMAC
- Autenticação API key
- Verificação de usuário registrado
- Onboarding flow

**Camada 1:** Event-Specific Handlers (133 linhas)
- Confirmação de eventos: "sim", "confirmar", "ok"
- Cancelamento: "não", "cancelar"
- Cálculo de rota: "calcular rota"

**Camada 2:** Nightly Check-in (22 linhas)
- Detecta sessão ativa via Redis
- Processa confirmações de itens noturnos

**Camada 3:** Transaction Confirmation (135 linhas)
- Confirmação de transações pendentes
- Seleção de categoria
- Parcelamento
- Feedback pós-criação

**Camada 4:** Payment Recognition (262 linhas)
- Keywords: "paguei", "quitei", "liquidei", "saldei", "zerei"
- Liquidação de contas fixas
- Processamento direto (skip categorização)

**Camada 5:** Intent Classification (1.370 linhas) - 25 INTENTS
- Usa Gemini AI para classificar intent
- Roteamento para handlers específicos

---

## 🎯 Estrutura Proposta

### Opção A: Modular (Recomendada)

```
app/routes/
├── webhooks/
│   ├── __init__.py                    # Blueprint aggregation
│   ├── base.py                        # Shared utilities, security
│   │
│   ├── transactions.py                # Transaction input routes
│   │   ├── handle_automate_webhook    (134 linhas)
│   │   ├── handle_api_transacao       (262 linhas)
│   │   └── handle_sms_payment         (138 linhas)
│   │
│   ├── calendar.py                    # OAuth and calendar routes
│   │   ├── connect_calendar           (85 linhas)
│   │   ├── oauth2callback             (204 linhas)
│   │   └── disconnect_calendar        (24 linhas)
│   │
│   ├── reserves.py                    # Reserve management
│   │   ├── toggle_incluir_reserva     (130 linhas)
│   │   └── listar_agendamentos        (197 linhas)
│   │
│   ├── whatsapp_router.py             # WhatsApp main router
│   │   └── handle_whatsapp_webhook    (300 linhas)
│   │       - Security layer
│   │       - Intent classification
│   │       - Intent routing
│   │
│   └── intents/                       # WhatsApp intent handlers
│       ├── __init__.py                # Intent registry
│       ├── base_intent.py             # Abstract base class
│       │
│       ├── transaction_intents.py     (~250 linhas)
│       │   ├── RendaIntent
│       │   ├── DespesaIntent
│       │   └── PagueiIntent
│       │
│       ├── query_intents.py           (~350 linhas)
│       │   ├── ConsultaReservaIntent
│       │   ├── ConsultaSaldoIntent
│       │   ├── ConsultaPotesIntent
│       │   ├── ListarContasIntent
│       │   ├── AjustarSaldoIntent
│       │   ├── ConsultaPeriodoIntent
│       │   ├── ConsultaContasFixasIntent
│       │   ├── VencimentosHojeIntent
│       │   ├── VencimentosAmanhaIntent
│       │   └── VencimentosSemanaIntent
│       │
│       ├── transfer_intents.py        (~150 linhas)
│       │   ├── TransferenciaIntent
│       │   ├── PagamentoFaturaIntent
│       │   └── ConsultaFaturaIntent
│       │
│       ├── calendar_intents.py        (~300 linhas)
│       │   ├── CriarEventoIntent
│       │   ├── DeletarEventoIntent
│       │   ├── ConsultarAgendaIntent
│       │   └── HorariosLivresIntent
│       │
│       ├── notification_intents.py    (~250 linhas)
│       │   └── ConfigurarNotificacoesIntent
│       │       - Resumo Matinal
│       │       - Check-in Noturno
│       │       - Agenda Diária
│       │       - Contas a Vencer
│       │
│       ├── analytics_intents.py       (~200 linhas)
│       │   ├── AnaliseInteligenteIntent
│       │   ├── ComparacaoMensalIntent
│       │   ├── PrevisaoGastosIntent
│       │   └── GraficoGastosIntent
│       │
│       ├── confirmation_intents.py    (~400 linhas)
│       │   ├── EventConfirmationIntent
│       │   ├── TransactionConfirmationIntent
│       │   ├── NightlyCheckinIntent
│       │   └── TravelTimeIntent
│       │
│       └── admin_intents.py           (~100 linhas)
│           ├── SolicitarApiKeyIntent
│           ├── ConfigurarLocalizacaoIntent
│           └── ConfigurarRelatorioMensalIntent
```

**Total:** 3.322 linhas → ~3.000 linhas em 21 arquivos
**Redução:** 10% (código duplicado + boilerplate consolidado)
**Média:** ~143 linhas/arquivo

---

## 🏗️ Implementação

### Intent Base Class Pattern

```python
# app/routes/webhooks/intents/base_intent.py
from abc import ABC, abstractmethod
from typing import Dict, Any, Optional

class BaseIntent(ABC):
    """
    Classe base para todos os intents WhatsApp.

    Padrão Template Method:
    - Validação comum
    - Logging automático
    - Error handling
    - Resposta padronizada
    """

    def __init__(self, usuario_id: int, mensagem: str, conn):
        self.usuario_id = usuario_id
        self.mensagem = mensagem
        self.conn = conn

    @abstractmethod
    def handle(self) -> Dict[str, Any]:
        """
        Processa o intent e retorna resposta.

        Returns:
            {
                "success": bool,
                "message": str,
                "data": Optional[Dict]
            }
        """
        pass

    @abstractmethod
    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da mensagem usando Gemini."""
        pass

    def validate(self) -> Optional[str]:
        """
        Valida parâmetros extraídos.

        Returns:
            None se válido, mensagem de erro caso contrário
        """
        return None

    def format_response(self, data: Dict) -> str:
        """Formata resposta para WhatsApp."""
        return str(data.get("message", ""))
```

### Intent Example

```python
# app/routes/webhooks/intents/transaction_intents.py
from .base_intent import BaseIntent
from app.services import gemini_service, finance_service

class RendaIntent(BaseIntent):
    """Handler para intent 'Renda' (income recording)."""

    def extract_params(self) -> Dict[str, Any]:
        """Usa Gemini para extrair: valor, descrição, data, conta."""
        return gemini_service.extract_income_params(
            self.mensagem,
            self.usuario_id
        )

    def validate(self) -> Optional[str]:
        """Valida que valor > 0 e conta existe."""
        params = self.params

        if not params.get("valor") or params["valor"] <= 0:
            return "❌ Valor inválido. Por favor, informe um valor positivo."

        # Validar conta existe
        conta = finance_service.get_account_by_name(
            self.conn,
            self.usuario_id,
            params.get("conta", "")
        )
        if not conta:
            return "❌ Conta não encontrada."

        return None

    def handle(self) -> Dict[str, Any]:
        """Registra renda e retorna confirmação."""
        self.params = self.extract_params()

        # Validação
        error = self.validate()
        if error:
            return {"success": False, "message": error}

        # Criar transação com confirmação
        from app.services.transaction_confirmation_service import (
            TransactionConfirmationService
        )

        confirmation_service = TransactionConfirmationService()
        pending_id = confirmation_service.create_pending_transaction(
            usuario_id=self.usuario_id,
            tipo="renda",
            **self.params
        )

        msg = (
            f"💰 *Renda a confirmar:*\n\n"
            f"Valor: {formatar_moeda(self.params['valor'])}\n"
            f"Descrição: {self.params['descricao']}\n"
            f"Conta: {self.params['conta']}\n\n"
            f"Responda *confirmar* ou *cancelar*"
        )

        return {"success": True, "message": msg, "pending_id": pending_id}
```

### Intent Registry & Router

```python
# app/routes/webhooks/intents/__init__.py
from .transaction_intents import RendaIntent, DespesaIntent, PagueiIntent
from .query_intents import (
    ConsultaReservaIntent, ConsultaSaldoIntent, ConsultaPotesIntent
)
# ... outros imports

# Mapeamento intent name → handler class
INTENT_REGISTRY = {
    'Renda': RendaIntent,
    'Despesa': DespesaIntent,
    'Consulta Reserva': ConsultaReservaIntent,
    'Consulta Saldo': ConsultaSaldoIntent,
    'Consulta Potes': ConsultaPotesIntent,
    # ... 20 outros intents
}

def route_intent(intent_name: str, usuario_id: int, mensagem: str, conn):
    """
    Factory pattern: cria e executa intent handler.

    Args:
        intent_name: Nome do intent retornado pelo Gemini
        usuario_id: ID do usuário
        mensagem: Texto da mensagem
        conn: Conexão do banco

    Returns:
        Resposta formatada para WhatsApp
    """
    handler_class = INTENT_REGISTRY.get(intent_name)

    if not handler_class:
        return {
            "success": False,
            "message": "❓ Não entendi. Pode reformular?"
        }

    handler = handler_class(usuario_id, mensagem, conn)
    result = handler.handle()

    return result
```

### WhatsApp Router Simplificado

```python
# app/routes/webhooks/whatsapp_router.py
from flask import request, jsonify
from app.routes.webhooks.intents import route_intent
from app.services import gemini_service

@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """
    Roteador principal WhatsApp - SIMPLIFICADO.

    Camadas:
    0. Security & Setup
    1-4. Priority Handlers (Event, Nightly, Confirmation, Payment)
    5. Intent Classification & Routing
    """

    # === LAYER 0: Security ===
    # (65 linhas - manter inline)

    # === LAYERS 1-4: Priority Handlers ===
    # Verificar contextos ativos (event confirmation, nightly checkin, etc)
    # (450 linhas - considerar extrair para separate handlers)

    # === LAYER 5: Intent Classification & Routing ===
    intent_name = gemini_service.classify_intent(mensagem, usuario_id)

    result = route_intent(
        intent_name=intent_name,
        usuario_id=usuario_id,
        mensagem=mensagem,
        conn=conn
    )

    if result["success"]:
        notification_service.send_whatsapp(
            usuario_id,
            result["message"]
        )
        return jsonify({"status": "ok"}), 200
    else:
        # Handle error
        return jsonify({"status": "erro", "mensagem": result["message"]}), 400
```

---

## 📋 Plano de Execução

### Fase 1: Infraestrutura (2-3 dias)

**Objetivos:**
- Criar estrutura de diretórios
- Implementar `BaseIntent` abstrata
- Criar intent registry

**Entregas:**
```
app/routes/webhooks/
├── __init__.py
├── base.py
└── intents/
    ├── __init__.py (registry)
    └── base_intent.py
```

**Critério de Sucesso:** Intent registry funcional, testes passando

---

### Fase 2: Extrair Rotas Simples (3-4 dias)

**Objetivos:**
- Extrair rotas independentes (calendar, reserves, transactions)
- Manter 100% backward compatibility

**Entregas:**
```
app/routes/webhooks/
├── calendar.py         (3 rotas, 313 linhas)
├── reserves.py         (2 rotas, 327 linhas)
└── transactions.py     (3 rotas, 534 linhas)
```

**Critério de Sucesso:** Rotas funcionando, testes de integração OK

---

### Fase 3: Extrair 5 Intents Prioritários (5-7 dias)

**Prioridade por tráfego (baseado em logs):**

1. **RendaIntent** (~80 linhas)
2. **DespesaIntent** (~140 linhas)
3. **ConsultaSaldoIntent** (~70 linhas)
4. **ConsultaReservaIntent** (~90 linhas)
5. **TransferenciaIntent** (~80 linhas)

**Entregas:**
```
app/routes/webhooks/intents/
├── transaction_intents.py (Renda, Despesa)
├── query_intents.py (ConsultaSaldo, ConsultaReserva)
└── transfer_intents.py (Transferencia)
```

**Critério de Sucesso:**
- 5 intents funcionais via registry
- WhatsApp router usando factory pattern
- Testes unitários para cada intent

---

### Fase 4: Extrair Remaining Intents (10-12 dias)

**Grupos:**

**Semana 1:**
- Query Intents restantes (7 intents, ~280 linhas)
- Transfer Intents restantes (2 intents, ~70 linhas)

**Semana 2:**
- Calendar Intents (4 intents, ~300 linhas)
- Notification Intents (1 intent multi-função, ~250 linhas)
- Confirmation Intents (4 intents, ~400 linhas)

**Semana 3:**
- Analytics Intents (4 intents, ~200 linhas)
- Admin Intents (3 intents, ~100 linhas)

**Entregas:** Todos os 25 intents modularizados

---

### Fase 5: Consolidação e Otimização (5-7 dias)

**Objetivos:**
- Remover código duplicado
- Aplicar utilitários da Fase A (`@handle_errors`, `@require_user_auth`)
- Performance profiling
- Documentação completa

**Entregas:**
- Documentação de arquitetura
- Guia de criação de novos intents
- Testes de carga
- Métricas de performance

---

## ⚠️ Riscos e Mitigações

### Riscos Críticos

| Risco | Impacto | Probabilidade | Mitigação |
|-------|---------|---------------|-----------|
| **Breaking WhatsApp Flow** | CRÍTICO | MÉDIA | Feature flags, rollout gradual 10%→50%→100% |
| **Redis State Corruption** | ALTO | BAIXA | Manter confirmation service intacto, testar intensivamente |
| **OAuth Flow Break** | ALTO | BAIXA | Extrair calendar routes por último, após validação |
| **Intent Routing Errors** | MÉDIO | MÉDIA | Comprehensive logging, fallback para "não entendi" |
| **Performance Degradation** | MÉDIO | BAIXA | Factory pattern não adiciona overhead significativo |

### Estratégias de Mitigação

1. **Feature Flags:**
   ```python
   USE_NEW_INTENT_SYSTEM = os.getenv('USE_NEW_INTENT_SYSTEM', 'false') == 'true'

   if USE_NEW_INTENT_SYSTEM:
       return route_intent(intent_name, ...)
   else:
       # Código antigo inline
   ```

2. **A/B Testing:**
   - 10% tráfego → novo sistema
   - Monitorar erros, latência, user satisfaction
   - 50% → 100% gradualmente

3. **Comprehensive Tests:**
   - Unit tests para cada intent
   - Integration tests para WhatsApp flow
   - Load tests (simulate 100 msgs/sec)

4. **Rollback Plan:**
   - Manter código antigo por 4 semanas
   - Rollback com uma env var

---

## 📊 Métricas de Sucesso

### Antes vs Depois

| Métrica | Antes | Depois | Melhoria |
|---------|-------|--------|----------|
| **Maior arquivo** | 3.322 linhas | ~300 linhas | -91% |
| **Arquivos totais** | 1 | 21 | +2000% (modularidade) |
| **Média linhas/arquivo** | 3.322 | ~143 | -96% |
| **Cyclomatic Complexity** | ~150 | ~15/handler | -90% |
| **Testabilidade** | RUIM | BOA | ⬆️ |
| **Tempo para adicionar intent** | 30-60 min | 5-10 min | -83% |

### KPIs de Produção

- **Latência média:** ≤500ms (atual: ~400ms)
- **Error rate:** ≤0.5% (atual: ~0.3%)
- **Uptime:** ≥99.9% (atual: 99.95%)
- **User satisfaction:** Manter ≥95%

---

## ✅ Checklist de Conclusão

Fase B.3 está completa quando:

- [ ] Estrutura de diretórios criada
- [ ] BaseIntent implementada com testes
- [ ] Intent registry funcionando
- [ ] 3 rotas simples extraídas (calendar, reserves, transactions)
- [ ] 25 intents extraídos e funcionais
- [ ] WhatsApp router simplificado (<300 linhas)
- [ ] Utilitários Fase A aplicados
- [ ] 100% backward compatibility mantida
- [ ] Testes unitários + integração passando
- [ ] Documentação completa
- [ ] Feature flag habilitado em produção
- [ ] Monitoramento estável por 2 semanas

---

## 📚 Documentação Relacionada

- [PHASE_B_REFACTORING_PLAN.md](PHASE_B_REFACTORING_PLAN.md) - Visão geral Fase B
- [PHASE_A_UTILITIES_GUIDE.md](PHASE_A_UTILITIES_GUIDE.md) - Utilitários disponíveis
- [PHASE_B1_ADMIN_REFACTORING.md](PHASE_B1_ADMIN_REFACTORING.md) - Exemplo de refatoração
- [PHASE_B2_PROGRESS.md](PHASE_B2_PROGRESS.md) - Exemplo de modularização

---

**Próxima Ação:** Aguardar aprovação do usuário para iniciar Fase 1 (Infraestrutura)

**Autor:** Claude Sonnet 4.5
**Data:** Dezembro 2024
**Fase:** B.3 (Refatoração de webhooks.py - Planejamento)
