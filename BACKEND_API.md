# API Reference - Backend Flask

Documentacao completa da API REST para integracao com Frontend Angular.

**Base URL:** `http://localhost:5000`
**Autenticacao:** JWT Bearer Token

---

## Autenticacao

### POST /auth/login
Autentica usuario e retorna token JWT.

**Request:**
```json
{
  "whatsapp": "5511999999999",
  "password": "SenhaSegura123"
}
```

**Response (200):**
```json
{
  "status": "success",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 123,
    "nome": "Joao Silva",
    "whatsapp": "5511999999999"
  }
}
```

**Erros:**
- `400` - Campos obrigatorios faltando
- `401` - Senha incorreta
- `404` - WhatsApp nao cadastrado

---

### POST /auth/register
Registra novo usuario.

**Request:**
```json
{
  "nome": "Joao Silva",
  "whatsapp": "5511999999999",
  "password": "SenhaSegura123",
  "dia_vencimento": 10,
  "dia_fechamento": 5
}
```

**Response (201):**
```json
{
  "status": "success",
  "message": "Usuario cadastrado com sucesso",
  "user_id": 123
}
```

**Erros:**
- `400` - Campos obrigatorios faltando ou senha < 6 caracteres
- `409` - WhatsApp ja cadastrado

---

### POST /auth/verify
Verifica se token JWT e valido.

**Request:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response (200):**
```json
{
  "status": "success",
  "valid": true,
  "user_id": 123
}
```

---

## Dashboard

> **Nota:** Todos os endpoints abaixo requerem header `Authorization: Bearer <token>`

### GET /api/dashboard/summary
Retorna resumo financeiro do mes atual.

**Response (200):**
```json
{
  "status": "success",
  "data": {
    "saldo_total": 5430.50,
    "receitas_mes": 8000.00,
    "despesas_mes": 3245.30,
    "saldo_mes": 4754.70,
    "mes_referencia": "Janeiro/2026"
  }
}
```

---

### GET /api/dashboard/charts
Retorna dados para graficos.

**Query Parameters:**
| Param | Tipo | Default | Descricao |
|-------|------|---------|-----------|
| meses | int | 3 | Quantidade de meses para analise (1-12) |

**Response (200):**
```json
{
  "status": "success",
  "data": {
    "gastos_mensais": [
      {"mes": "2025-11", "total": 2890.30},
      {"mes": "2025-12", "total": 3245.30}
    ],
    "gastos_categoria": [
      {
        "macro_categoria": "Alimentacao",
        "subcategoria": "Supermercado",
        "total": 1200.00,
        "quantidade": 45
      }
    ],
    "gastos_dia_semana": [
      {"dia": "Segunda", "total": 450.00, "quantidade": 12}
    ]
  }
}
```

---

### GET /api/dashboard/recent
Lista ultimas 10 transacoes para o dashboard.

**Response (200):**
```json
{
  "status": "success",
  "data": [
    {
      "id": 1234,
      "descricao": "Supermercado",
      "valor": -150.50,
      "tipo": "Despesa",
      "data": "2025-12-11",
      "categoria": "Alimentacao",
      "conta": "Nubank"
    }
  ]
}
```

---

## Transacoes (CRUD)

### GET /api/transactions
Lista transacoes com paginacao e filtros.

**Query Parameters:**
| Param | Tipo | Default | Descricao |
|-------|------|---------|-----------|
| limit | int | 20 | Registros por pagina (1-100) |
| offset | int | 0 | Deslocamento para paginacao |
| tipo | string | - | Filtrar: 'Receita' ou 'Despesa' |
| data_inicio | string | - | Data inicial (YYYY-MM-DD) |
| data_fim | string | - | Data final (YYYY-MM-DD) |

**Response (200):**
```json
{
  "status": "success",
  "data": {
    "total": 245,
    "limit": 20,
    "offset": 0,
    "transactions": [
      {
        "id": 1234,
        "descricao": "Supermercado",
        "valor": -150.50,
        "tipo": "Despesa",
        "data_transacao": "2025-12-11",
        "categoria": "Alimentacao",
        "subcategoria": "Mercado",
        "conta": "Nubank",
        "consolidada": true
      }
    ]
  }
}
```

---

### POST /api/transactions
Cria nova transacao.

**Request:**
```json
{
  "descricao": "Supermercado",
  "valor": 150.50,
  "tipo": "Despesa",
  "data": "2025-12-11",
  "subcategoria_id": 15,
  "conta_id": 1,
  "observacoes": "Compras da semana",
  "consolidada": true
}
```

| Campo | Tipo | Obrigatorio | Descricao |
|-------|------|-------------|-----------|
| descricao | string | Sim | Descricao da transacao |
| valor | number | Sim | Valor (sempre positivo) |
| tipo | string | Sim | 'Receita' ou 'Despesa' |
| data | string | Sim | Data (YYYY-MM-DD) |
| subcategoria_id | int | Sim | ID da subcategoria |
| conta_id | int | Sim | ID da conta |
| observacoes | string | Nao | Observacoes adicionais |
| consolidada | bool | Nao | Default: true |

**Response (201):**
```json
{
  "status": "success",
  "message": "Transacao criada com sucesso",
  "data": {
    "id": 1235,
    "descricao": "Supermercado",
    "valor": -150.50,
    "tipo": "Despesa",
    "data": "2025-12-11"
  }
}
```

**Erros:**
- `400` - Campos obrigatorios faltando ou valores invalidos
- `404` - Conta ou subcategoria nao encontrada

---

### PUT /api/transactions/:id
Atualiza transacao existente.

**Request (todos campos opcionais):**
```json
{
  "descricao": "Supermercado Extra",
  "valor": 180.00,
  "tipo": "Despesa",
  "data": "2025-12-12",
  "subcategoria_id": 16,
  "conta_id": 2,
  "observacoes": "Compras atualizadas",
  "consolidada": true
}
```

**Response (200):**
```json
{
  "status": "success",
  "message": "Transacao atualizada com sucesso",
  "data": {
    "id": 1234,
    "descricao": "Supermercado Extra",
    "valor": -180.00,
    "tipo": "Despesa",
    "data": "2025-12-12"
  }
}
```

**Erros:**
- `400` - Nenhum dado fornecido ou valores invalidos
- `404` - Transacao, conta ou subcategoria nao encontrada

---

### DELETE /api/transactions/:id
Deleta transacao.

**Response (200):**
```json
{
  "status": "success",
  "message": "Transacao deletada com sucesso"
}
```

**Erros:**
- `404` - Transacao nao encontrada

> **Nota:** Se a transacao for uma transferencia, o par tambem e deletado automaticamente.

---

## Contas

### GET /api/accounts
Lista contas do usuario com saldos.

**Response (200):**
```json
{
  "status": "success",
  "data": [
    {
      "nome_conta": "Nubank",
      "tipo_conta": "Conta Corrente",
      "saldo": 2345.50
    },
    {
      "nome_conta": "Cartao Inter",
      "tipo_conta": "Cartao de Credito",
      "saldo": -1200.00
    }
  ]
}
```

---

## Categorias

### GET /api/categories
Lista categorias disponiveis para forms.

**Query Parameters:**
| Param | Tipo | Default | Descricao |
|-------|------|---------|-----------|
| tipo | string | - | Filtrar: 'Receita' ou 'Despesa' |

**Response (200):**
```json
{
  "status": "success",
  "data": [
    {
      "grupo": "Despesa Essencial",
      "macro_id": 5,
      "macro_categoria": "Alimentacao Essencial",
      "subcategorias": [
        {"id": 15, "nome": "Supermercado / Mercearia"},
        {"id": 16, "nome": "Feira / Hortifruti"},
        {"id": 17, "nome": "Suplementos e Itens Basicos"}
      ]
    },
    {
      "grupo": "Renda",
      "macro_id": 1,
      "macro_categoria": "Renda Principal",
      "subcategorias": [
        {"id": 1, "nome": "Salario Fixo / Pro-labore"},
        {"id": 2, "nome": "Remuneracao Variavel / Comissoes"}
      ]
    }
  ]
}
```

---

## Health Check

### GET /api/health
Verifica se API esta online (nao requer autenticacao).

**Response (200):**
```json
{
  "status": "success",
  "message": "API esta funcionando",
  "version": "1.0.0"
}
```

---

## Codigos de Erro

| Codigo | Descricao |
|--------|-----------|
| 400 | Bad Request - Dados invalidos |
| 401 | Unauthorized - Token invalido ou expirado |
| 404 | Not Found - Recurso nao encontrado |
| 409 | Conflict - Recurso ja existe |
| 500 | Internal Server Error |
| 503 | Service Unavailable - Banco de dados nao configurado |

---

## Configuracao CORS

O backend esta configurado para aceitar requisicoes de:
- `http://localhost:4200`
- `http://127.0.0.1:4200`

Para adicionar novas origens em producao, edite o arquivo `.env`:
```env
CORS_ENABLED=true
CORS_ORIGINS=http://localhost:4200,https://app.meusecretario.com
```

---

## Exemplo de Uso (Angular)

```typescript
// auth.service.ts
login(credentials: {whatsapp: string, password: string}) {
  return this.http.post<LoginResponse>(`${API_URL}/auth/login`, credentials);
}

// dashboard.service.ts
getCategories(tipo?: 'Receita' | 'Despesa') {
  const params = tipo ? { tipo } : {};
  return this.http.get<CategoryResponse>(`${API_URL}/api/categories`, { params });
}

createTransaction(data: CreateTransactionRequest) {
  return this.http.post<TransactionResponse>(`${API_URL}/api/transactions`, data);
}
```

---

**Ultima atualizacao:** 2026-01-20
**Versao API:** 1.0.0
