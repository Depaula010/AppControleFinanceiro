# 📖 Documentação - API de Gerenciamento de Chaves (SaaS)

## 📋 Índice

- [Visão Geral](#visão-geral)
- [Autenticação](#autenticação)
- [Endpoints - Chaves de API](#endpoints---chaves-de-api)
- [Endpoints - Preferências](#endpoints---preferências)
- [Endpoints - Consultas e Auditoria](#endpoints---consultas-e-auditoria)
- [Endpoints - LGPD](#endpoints---lgpd)
- [Códigos de Erro](#códigos-de-erro)
- [Exemplos de Uso](#exemplos-de-uso)

---

## 🎯 Visão Geral

Esta API permite que usuários do sistema SaaS gerenciem suas chaves de API de terceiros (Google Gemini, WeatherAPI, OpenRouteService) e configurem se desejam usar suas próprias chaves (gratuito) ou chaves do sistema (pago).

**Base URL:** `https://seu-dominio.com/api-keys`

**Formato de Resposta:** JSON

**Provedores Suportados:**
- `gemini` - Google Gemini AI
- `weather` - WeatherAPI
- `openroute` - OpenRouteService

---

## 🔐 Autenticação

Todos os endpoints (exceto `/health`) requerem autenticação via header:

```http
X-API-KEY: sua-chave-secreta-aqui
```

A chave de API do sistema é configurada via variável de ambiente `API_SECRET_KEY`.

**Exemplo:**
```bash
curl -H "X-API-KEY: minha-chave-secreta" \
  https://seu-dominio.com/api-keys/health
```

**Resposta de Erro (401):**
```json
{
  "erro": "Não autorizado",
  "mensagem": "API key inválida ou ausente"
}
```

---

## 🔑 Endpoints - Chaves de API

### 1. Cadastrar Chave de API

Cadastra uma nova chave de API para um usuário. A chave é criptografada antes de ser armazenada.

**Endpoint:** `POST /usuario/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
Content-Type: application/json
```

**Body:**
```json
{
  "provedor": "gemini",
  "chave_api": "AIzaSyABCD1234567890XYZ"
}
```

**Campos:**
- `provedor` (string, obrigatório): `"gemini"` | `"weather"` | `"openroute"`
- `chave_api` (string, obrigatório): Chave de API do provedor

**Resposta de Sucesso (201):**
```json
{
  "mensagem": "Chave cadastrada com sucesso",
  "id": 42,
  "provedor": "gemini",
  "usuario_id": 123
}
```

**Erros Possíveis:**
- `400` - Dados incompletos ou provedor inválido
- `401` - Não autorizado
- `500` - Erro interno do servidor

---

### 2. Listar Chaves de Usuário

Lista todas as chaves de API cadastradas por um usuário (sem descriptografar).

**Endpoint:** `GET /usuario/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "total": 2,
  "chaves": [
    {
      "id": 42,
      "provedor": "gemini",
      "ativo": true,
      "ultimo_uso_em": "2025-12-04T10:30:00",
      "criado_em": "2025-12-01T08:00:00",
      "atualizado_em": "2025-12-04T10:30:00"
    },
    {
      "id": 43,
      "provedor": "weather",
      "ativo": true,
      "ultimo_uso_em": null,
      "criado_em": "2025-12-02T09:00:00",
      "atualizado_em": "2025-12-02T09:00:00"
    }
  ]
}
```

---

### 3. Atualizar Chave de API

Atualiza uma chave de API existente (valor da chave e/ou status ativo).

**Endpoint:** `PUT /chave/<chave_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
Content-Type: application/json
```

**Body:**
```json
{
  "chave_api": "NOVA_CHAVE_AQUI",
  "ativo": true
}
```

**Campos (todos opcionais, mas pelo menos um obrigatório):**
- `chave_api` (string, opcional): Nova chave de API
- `ativo` (boolean, opcional): Status ativo/inativo

**Resposta de Sucesso (200):**
```json
{
  "mensagem": "Chave atualizada com sucesso",
  "id": 42,
  "provedor": "gemini",
  "usuario_id": 123
}
```

**Erros Possíveis:**
- `400` - Nenhum campo fornecido
- `404` - Chave não encontrada
- `500` - Erro interno

---

### 4. Desativar Chave de API

Desativa uma chave de API (soft delete). A chave não é removida do banco.

**Endpoint:** `DELETE /chave/<chave_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "mensagem": "Chave desativada com sucesso",
  "id": 42,
  "provedor": "gemini"
}
```

**Erros Possíveis:**
- `404` - Chave não encontrada
- `500` - Erro interno

---

## ⚙️ Endpoints - Preferências

### 5. Configurar Preferência

Configura se o usuário vai usar sua própria chave (grátis) ou chave do sistema (pago).

**⚠️ IMPORTANTE:** Esta escolha é **OBRIGATÓRIA** e não possui fallback automático.

**Endpoint:** `POST /preferencias/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
Content-Type: application/json
```

**Body:**
```json
{
  "provedor": "gemini",
  "usar_chave_propria": true
}
```

**Campos:**
- `provedor` (string, obrigatório): `"gemini"` | `"weather"` | `"openroute"`
- `usar_chave_propria` (boolean, obrigatório):
  - `true` = Usar chave própria (grátis, ilimitado)
  - `false` = Usar chave do sistema (cobrado conforme plano)

**Resposta de Sucesso (201):**
```json
{
  "mensagem": "Preferência configurada com sucesso",
  "id": 15,
  "usuario_id": 123,
  "provedor": "gemini",
  "usar_chave_propria": true,
  "tipo": "própria (grátis)"
}
```

**Comportamento:**
- Se já existir preferência para o provedor, será atualizada (UPSERT)
- Esta configuração é necessária para usar qualquer serviço

---

### 6. Listar Preferências

Lista todas as preferências de chaves de API de um usuário.

**Endpoint:** `GET /preferencias/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "total": 3,
  "preferencias": [
    {
      "id": 15,
      "provedor": "gemini",
      "usar_chave_propria": true,
      "tipo": "própria (grátis)",
      "atualizado_em": "2025-12-01T10:00:00"
    },
    {
      "id": 16,
      "provedor": "weather",
      "usar_chave_propria": false,
      "tipo": "sistema (cobrado)",
      "atualizado_em": "2025-12-02T11:00:00"
    }
  ]
}
```

---

### 7. Remover Preferência

Remove preferência de um provedor específico. Útil para resetar configuração.

**Endpoint:** `DELETE /preferencias/<usuario_id>/<provedor>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Exemplo:** `DELETE /preferencias/123/gemini`

**Resposta de Sucesso (200):**
```json
{
  "mensagem": "Preferência removida com sucesso",
  "usuario_id": 123,
  "provedor": "gemini"
}
```

---

## 📊 Endpoints - Consultas e Auditoria

### 8. Consultar Uso Mensal

Consulta uso mensal de APIs por usuário.

**Endpoint:** `GET /uso/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Query Params (opcionais):**
- `mes_ano` (string): Formato "YYYY-MM" (padrão: mês atual)
- `provedor` (string): Filtrar por provedor específico

**Exemplo:** `GET /uso/123?mes_ano=2025-12&provedor=gemini`

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "mes_ano": "2025-12",
  "resumo": {
    "total_chamadas": 350,
    "chamadas_chave_propria": 250,
    "chamadas_chave_sistema": 100
  },
  "detalhes": [
    {
      "provedor": "gemini",
      "tipo_chave": "propria",
      "total_chamadas": 200,
      "mes_ano": "2025-12",
      "ultima_atualizacao": "2025-12-04T10:30:00"
    },
    {
      "provedor": "gemini",
      "tipo_chave": "sistema",
      "total_chamadas": 50,
      "mes_ano": "2025-12",
      "ultima_atualizacao": "2025-12-04T10:30:00"
    }
  ]
}
```

---

### 9. Consultar Logs de Acesso

Consulta logs de acesso às chaves de API (auditoria de segurança).

**Endpoint:** `GET /logs/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Query Params (opcionais):**
- `limit` (int): Número de registros (padrão: 50, máx: 500)
- `provedor` (string): Filtrar por provedor
- `sucesso` (boolean): Filtrar por sucesso/erro (`true` ou `false`)

**Exemplo:** `GET /logs/123?limit=100&provedor=gemini&sucesso=false`

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "total": 15,
  "logs": [
    {
      "id": 450,
      "provedor": "gemini",
      "tipo_chave": "propria",
      "operacao": "generate_content",
      "sucesso": true,
      "mensagem_erro": null,
      "criado_em": "2025-12-04T10:30:00"
    },
    {
      "id": 449,
      "provedor": "weather",
      "tipo_chave": "sistema",
      "operacao": "get_weather",
      "sucesso": false,
      "mensagem_erro": "API key invalid",
      "criado_em": "2025-12-04T10:15:00"
    }
  ]
}
```

---

## 🛡️ Endpoints - LGPD

### 10. Registrar Consentimento

Registra ou atualiza consentimento LGPD do usuário.

**Endpoint:** `POST /lgpd/consentimento`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
Content-Type: application/json
```

**Body:**
```json
{
  "usuario_id": 123,
  "tipo_consentimento": "uso_dados_pessoais",
  "consentimento_dado": true,
  "versao_termos": "1.0"
}
```

**Campos:**
- `usuario_id` (int, obrigatório): ID do usuário
- `tipo_consentimento` (string, obrigatório): Tipo do consentimento
  - `uso_dados_pessoais`
  - `armazenamento_chaves_api`
  - `rastreamento_uso`
  - `comunicacao_whatsapp`
  - `compartilhamento_terceiros`
- `consentimento_dado` (boolean, obrigatório): `true` (consentiu) ou `false` (revogou)
- `versao_termos` (string, opcional): Versão dos termos (padrão: "1.0")

**Resposta de Sucesso (201):**
```json
{
  "status": "sucesso",
  "consentimento_id": 78,
  "mensagem": "Consentimento registrado com sucesso",
  "tipo": "uso_dados_pessoais",
  "consentimento_dado": true
}
```

---

### 11. Listar Consentimentos

Lista histórico completo de consentimentos do usuário.

**Endpoint:** `GET /lgpd/consentimentos/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "total": 5,
  "consentimentos": [
    {
      "id": 78,
      "tipo_consentimento": "uso_dados_pessoais",
      "descricao": "Uso de dados pessoais para funcionamento do sistema",
      "consentimento_dado": true,
      "versao_termos": "1.0",
      "data_consentimento": "2025-12-01T08:00:00"
    }
  ]
}
```

---

### 12. Solicitar Consentimentos Iniciais

Retorna lista de consentimentos necessários para novo usuário (útil para onboarding).

**Endpoint:** `GET /lgpd/consentimentos-iniciais/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "consentimentos_necessarios": [
    {
      "tipo": "uso_dados_pessoais",
      "descricao": "Uso de dados pessoais para funcionamento do sistema",
      "obrigatorio": true,
      "versao_termos": "1.0"
    },
    {
      "tipo": "armazenamento_chaves_api",
      "descricao": "Armazenamento de chaves de API criptografadas",
      "obrigatorio": true,
      "versao_termos": "1.0"
    },
    {
      "tipo": "comunicacao_whatsapp",
      "descricao": "Envio de notificações via WhatsApp",
      "obrigatorio": false,
      "versao_termos": "1.0"
    }
  ],
  "mensagem": "Para continuar, você precisa consentir com os termos abaixo"
}
```

---

### 13. Exportar Dados do Usuário

Exporta TODOS os dados do usuário (Direito de Portabilidade - LGPD Art. 18, V).

**⚠️ ATENÇÃO:** Este endpoint retorna chaves de API **DESCRIPTOGRAFADAS**. Use apenas com autenticação forte.

**Endpoint:** `GET /lgpd/exportar/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "data_exportacao": "2025-12-04T11:00:00",
  "dados": {
    "chaves_api": [
      {
        "id": 42,
        "provedor": "gemini",
        "chave_api": "AIzaSyABCD1234567890XYZ",
        "ativo": true,
        "criado_em": "2025-12-01T08:00:00"
      }
    ],
    "preferencias": [
      {
        "id": 15,
        "provedor": "gemini",
        "usar_chave_propria": true,
        "atualizado_em": "2025-12-01T10:00:00"
      }
    ],
    "uso_mensal": [
      {
        "provedor": "gemini",
        "tipo_chave": "propria",
        "quantidade_chamadas": 200,
        "mes_ano": "2025-12"
      }
    ],
    "logs_acesso": [],
    "consentimentos": [],
    "assinatura": {
      "id": 5,
      "plano": "Bronze",
      "preco_mensal": 0.0,
      "limites": {
        "gemini": 1000,
        "weather": 500,
        "openroute": 500
      },
      "ativo": true
    }
  }
}
```

---

### 14. Verificar Consentimento

Verifica se usuário possui consentimento ativo para determinado tipo.

**Endpoint:** `GET /lgpd/verificar-consentimento/<usuario_id>/<tipo_consentimento>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
```

**Exemplo:** `GET /lgpd/verificar-consentimento/123/comunicacao_whatsapp`

**Resposta de Sucesso (200):**
```json
{
  "usuario_id": 123,
  "tipo_consentimento": "comunicacao_whatsapp",
  "consentimento_ativo": true
}
```

---

### 15. Deletar Conta do Usuário

Deleta TODOS os dados do usuário (Direito ao Esquecimento - LGPD Art. 18, VI).

**⚠️ ATENÇÃO:** Esta operação é **IRREVERSÍVEL**!

**Endpoint:** `DELETE /lgpd/deletar-conta/<usuario_id>`

**Headers:**
```http
X-API-KEY: sua-chave-secreta
Content-Type: application/json
```

**Body:**
```json
{
  "confirmacao": "CONFIRMO_DELECAO"
}
```

**⚠️ IMPORTANTE:** A string de confirmação deve ser **EXATAMENTE** `"CONFIRMO_DELECAO"`.

**Resposta de Sucesso (200):**
```json
{
  "status": "sucesso",
  "usuario_id": 123,
  "registros_deletados": {
    "chaves_api": 3,
    "preferencias": 3,
    "logs": 450,
    "uso": 36,
    "consentimentos": 5,
    "assinaturas": 1
  },
  "total_registros": 498,
  "mensagem": "Conta e todos os dados do usuário 123 foram deletados com sucesso"
}
```

**Erros Possíveis:**
- `400` - Confirmação inválida
- `500` - Erro interno

---

## 🏥 Health Check

### 16. Health Check

Verifica se a API está funcionando (não requer autenticação).

**Endpoint:** `GET /health`

**Headers:** Nenhum

**Resposta de Sucesso (200):**
```json
{
  "status": "ok",
  "servico": "API Keys Management",
  "versao": "1.0.0"
}
```

---

## ❌ Códigos de Erro

### HTTP Status Codes

| Código | Significado | Quando Ocorre |
|--------|-------------|---------------|
| `200` | OK | Requisição bem-sucedida |
| `201` | Created | Recurso criado com sucesso |
| `400` | Bad Request | Dados inválidos ou incompletos |
| `401` | Unauthorized | API key inválida ou ausente |
| `404` | Not Found | Recurso não encontrado |
| `500` | Internal Server Error | Erro interno do servidor |

### Formato de Erro Padrão

```json
{
  "erro": "Tipo do erro",
  "mensagem": "Descrição detalhada do que aconteceu"
}
```

**Exemplos:**

```json
{
  "erro": "Dados incompletos",
  "mensagem": "Provedor e chave_api são obrigatórios"
}
```

```json
{
  "erro": "Provedor inválido",
  "mensagem": "Provedor deve ser um de: gemini, weather, openroute"
}
```

```json
{
  "erro": "Chave não encontrada",
  "mensagem": "Nenhuma chave com ID 999"
}
```

---

## 💡 Exemplos de Uso

### Exemplo 1: Fluxo Completo de Cadastro

```bash
# 1. Cadastrar chave do Google Gemini
curl -X POST https://seu-dominio.com/api-keys/usuario/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "gemini",
    "chave_api": "AIzaSyABCD1234567890XYZ"
  }'

# 2. Configurar preferência (usar chave própria)
curl -X POST https://seu-dominio.com/api-keys/preferencias/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "gemini",
    "usar_chave_propria": true
  }'

# 3. Verificar configuração
curl -X GET https://seu-dominio.com/api-keys/preferencias/123 \
  -H "X-API-KEY: sua-chave-secreta"
```

---

### Exemplo 2: Consultar Uso Mensal

```bash
# Consultar uso de dezembro/2025
curl -X GET "https://seu-dominio.com/api-keys/uso/123?mes_ano=2025-12" \
  -H "X-API-KEY: sua-chave-secreta"

# Consultar apenas uso do Gemini
curl -X GET "https://seu-dominio.com/api-keys/uso/123?provedor=gemini" \
  -H "X-API-KEY: sua-chave-secreta"
```

---

### Exemplo 3: Registrar Consentimentos (Onboarding)

```bash
# 1. Obter lista de consentimentos necessários
curl -X GET https://seu-dominio.com/api-keys/lgpd/consentimentos-iniciais/123 \
  -H "X-API-KEY: sua-chave-secreta"

# 2. Registrar consentimento obrigatório
curl -X POST https://seu-dominio.com/api-keys/lgpd/consentimento \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "usuario_id": 123,
    "tipo_consentimento": "uso_dados_pessoais",
    "consentimento_dado": true
  }'

# 3. Registrar consentimento de WhatsApp
curl -X POST https://seu-dominio.com/api-keys/lgpd/consentimento \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "usuario_id": 123,
    "tipo_consentimento": "comunicacao_whatsapp",
    "consentimento_dado": true
  }'
```

---

### Exemplo 4: Exportar Todos os Dados (LGPD)

```bash
# Exportar dados do usuário
curl -X GET https://seu-dominio.com/api-keys/lgpd/exportar/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  > dados_usuario_123.json

# O arquivo dados_usuario_123.json conterá TODOS os dados, incluindo chaves descriptografadas
```

---

### Exemplo 5: Python com requests

```python
import requests

BASE_URL = "https://seu-dominio.com/api-keys"
API_KEY = "sua-chave-secreta"

headers = {
    "X-API-KEY": API_KEY,
    "Content-Type": "application/json"
}

# Cadastrar chave
response = requests.post(
    f"{BASE_URL}/usuario/123",
    headers=headers,
    json={
        "provedor": "gemini",
        "chave_api": "AIzaSyABCD1234567890XYZ"
    }
)

if response.status_code == 201:
    data = response.json()
    print(f"Chave cadastrada! ID: {data['id']}")
else:
    print(f"Erro: {response.json()}")

# Configurar preferência
response = requests.post(
    f"{BASE_URL}/preferencias/123",
    headers=headers,
    json={
        "provedor": "gemini",
        "usar_chave_propria": True
    }
)

print(response.json())
```

---

## 📚 Recursos Adicionais

- [Manual: Como Gerar Chave do Google Gemini](./MANUAL_GEMINI.md)
- [Manual: Como Gerar Chave do WeatherAPI](./MANUAL_WEATHER.md)
- [Manual: Como Gerar Chave do OpenRouteService](./MANUAL_OPENROUTE.md)
- [Política de Privacidade e LGPD](./POLITICA_LGPD.md)

---

## 🤝 Suporte

Para dúvidas ou problemas, entre em contato através de:
- WhatsApp: (31) 9400-1072
- Email: suporte@meusecretario.com

---

**Versão:** 1.0.0
**Última Atualização:** 04/12/2025
