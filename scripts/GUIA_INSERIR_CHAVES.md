# 🔑 Guia: Como Inserir Suas Chaves de API no Banco de Dados

## 📋 Pré-requisitos

Antes de começar, você precisa ter:

- ✅ Suas chaves de API já geradas:
  - Google Gemini (formato: `AIzaSy...`)
  - WeatherAPI (formato: 32 caracteres)
  - OpenRouteService (formato: 58 caracteres)
- ✅ Acesso ao banco de dados PostgreSQL
- ✅ Python instalado (para criptografar as chaves)
- ✅ Variável `ENCRYPTION_KEY` configurada no `.env`

---

## 🚀 Método 1: Script Automatizado (Recomendado)

### **Passo 1: Descubra seu Usuario ID**

Execute no banco de dados:

```sql
SELECT id, nome, email, whatsapp_numero
FROM Usuarios
ORDER BY id;
```

Anote o `id` do seu usuário. Exemplo:
```
 id | nome          | email              | whatsapp_numero
----+---------------+--------------------+------------------
  1 | Rafael Silva  | rafael@email.com   | 5531940012345
```

Seu `usuario_id` é **1**.

---

### **Passo 2: Configure o Script Python**

Abra o arquivo `scripts/inserir_minhas_chaves.py` e edite:

```python
# 1. Seu ID de usuário
USUARIO_ID = 1  # ALTERE AQUI com o ID do passo 1!

# 2. Suas chaves de API
GEMINI_KEY = "AIzaSyABCD1234567890XYZ"  # Cole sua chave do Gemini
WEATHER_KEY = "a1b2c3d4e5f6g7h8i9j0"    # Cole sua chave do Weather
OPENROUTE_KEY = "5b3ce3597851110001cf6248"  # Cole sua chave do OpenRoute
```

⚠️ **IMPORTANTE:** Mantenha as aspas e substitua apenas o conteúdo!

---

### **Passo 3: Execute o Script**

No terminal, na raiz do projeto:

```bash
python scripts/inserir_minhas_chaves.py
```

**Saída esperada:**

```
================================================================================
🔐 GERADOR DE SCRIPT SQL PARA CHAVES DE API
================================================================================

📋 Configuração:
   • Usuario ID: 1

🔐 Criptografando chaves...
   ✅ Chaves criptografadas com sucesso!

================================================================================
📝 SCRIPT SQL GERADO
================================================================================

-- Cole este script no seu cliente PostgreSQL...
[SQL aparecerá aqui]

================================================================================
✅ Script SQL salvo em: e:\Projetos\...\scripts\output_sql_chaves.sql
================================================================================
```

---

### **Passo 4: Execute o SQL no Banco**

O script gerou um arquivo `scripts/output_sql_chaves.sql`.

**Opção A: Via psql (linha de comando)**

```bash
# Windows (PowerShell)
$env:PGPASSWORD="sua-senha"
psql -h seu-host -U seu-usuario -d seu-banco -f scripts/output_sql_chaves.sql

# Linux/Mac
PGPASSWORD="sua-senha" psql -h seu-host -U seu-usuario -d seu-banco -f scripts/output_sql_chaves.sql
```

**Opção B: Via DBeaver/pgAdmin**

1. Abra DBeaver ou pgAdmin
2. Conecte ao seu banco
3. Abra uma nova SQL Console
4. Cole o conteúdo de `scripts/output_sql_chaves.sql`
5. Execute (F5 ou botão Run)

---

### **Passo 5: Verifique se Funcionou**

Após executar o SQL, você verá algo como:

```
✅ INSERT 0 1
✅ INSERT 0 1
✅ INSERT 0 1
✅ INSERT 0 1
✅ INSERT 0 1
✅ INSERT 0 1

 id | provedor  | ativo | criado_em           | atualizado_em
----+-----------+-------+---------------------+--------------------
 42 | gemini    | t     | 2025-12-04 11:00:00 | 2025-12-04 11:00:00
 43 | openroute | t     | 2025-12-04 11:00:00 | 2025-12-04 11:00:00
 44 | weather   | t     | 2025-12-04 11:00:00 | 2025-12-04 11:00:00

 id | provedor  | usar_chave_propria | tipo                  | atualizado_em
----+-----------+--------------------+-----------------------+--------------------
 15 | gemini    | t                  | Chave própria (grátis)| 2025-12-04 11:00:00
 16 | openroute | t                  | Chave própria (grátis)| 2025-12-04 11:00:00
 17 | weather   | t                  | Chave própria (grátis)| 2025-12-04 11:00:00
```

✅ **Sucesso!** Suas 3 chaves foram inseridas e configuradas para uso próprio (grátis).

---

## 🛠️ Método 2: Manual (Avançado)

Se você preferir fazer tudo manualmente:

### **Passo 1: Criptografar Manualmente**

Execute no Python:

```python
from app.services.encryption_service import encryption_service

# Suas chaves
gemini = "AIzaSyABCD1234567890XYZ"
weather = "a1b2c3d4e5f6g7h8i9j0"
openroute = "5b3ce3597851110001cf6248"

# Criptografar
print("Gemini:", encryption_service.encrypt(gemini))
print("Weather:", encryption_service.encrypt(weather))
print("OpenRoute:", encryption_service.encrypt(openroute))
```

**Saída:**
```
Gemini: gAAAAABl...encrypted_string_here...
Weather: gAAAAABl...encrypted_string_here...
OpenRoute: gAAAAABl...encrypted_string_here...
```

Copie essas strings criptografadas.

---

### **Passo 2: Montar SQL Manualmente**

```sql
-- Substituir:
-- {USUARIO_ID} pelo seu ID
-- {GEMINI_ENCRYPTED} pela string criptografada do Gemini
-- {WEATHER_ENCRYPTED} pela string criptografada do Weather
-- {OPENROUTE_ENCRYPTED} pela string criptografada do OpenRoute

-- 1. Inserir chaves
INSERT INTO ChavesApiUsuario
    (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES
    ({USUARIO_ID}, 'gemini', '{GEMINI_ENCRYPTED}', TRUE, NOW(), NOW()),
    ({USUARIO_ID}, 'weather', '{WEATHER_ENCRYPTED}', TRUE, NOW(), NOW()),
    ({USUARIO_ID}, 'openroute', '{OPENROUTE_ENCRYPTED}', TRUE, NOW(), NOW());

-- 2. Configurar preferências
INSERT INTO PreferenciasChaveApi
    (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES
    ({USUARIO_ID}, 'gemini', TRUE, NOW()),
    ({USUARIO_ID}, 'weather', TRUE, NOW()),
    ({USUARIO_ID}, 'openroute', TRUE, NOW());
```

---

## 🧪 Testando

### **Teste 1: Verificar no Banco**

```sql
-- Ver suas chaves cadastradas (NÃO mostra as chaves, apenas metadata)
SELECT
    id,
    provedor,
    ativo,
    LENGTH(chave_api_criptografada) as tamanho_criptografado,
    ultimo_uso_em,
    criado_em
FROM ChavesApiUsuario
WHERE usuario_id = 1  -- Seu ID aqui
ORDER BY provedor;

-- Ver suas preferências
SELECT
    provedor,
    CASE
        WHEN usar_chave_propria THEN '✅ Chave própria (GRÁTIS)'
        ELSE '❌ Chave do sistema (PAGO)'
    END as configuracao
FROM PreferenciasChaveApi
WHERE usuario_id = 1  -- Seu ID aqui
ORDER BY provedor;
```

**Resultado esperado:**

```
 provedor  | configuracao
-----------+---------------------------
 gemini    | ✅ Chave própria (GRÁTIS)
 openroute | ✅ Chave própria (GRÁTIS)
 weather   | ✅ Chave própria (GRÁTIS)
```

---

### **Teste 2: Enviar Mensagem no WhatsApp**

Envie qualquer mensagem para o bot do WhatsApp:

```
Bom dia
```

O sistema deve:
1. Usar SUA chave do Gemini para processar
2. Usar SUA chave do Weather para buscar clima
3. Funcionar normalmente!

Verifique os logs do servidor:

```
[GEMINI] ✅ Usando chave de gemini propria para usuário 1
[WEATHER] ✅ Usando chave de weather propria para usuário 1
[TRAVEL-TIME] ✅ Usando chave de openroute propria para usuário 1
```

✅ **Perfeito!** Você está usando suas próprias chaves (grátis).

---

### **Teste 3: Verificar Rastreamento de Uso**

Após usar o sistema por alguns dias:

```sql
-- Ver quanto você usou no mês atual
SELECT
    provedor,
    tipo_chave,
    quantidade_chamadas,
    mes_ano
FROM RastreamentoUsoApi
WHERE usuario_id = 1  -- Seu ID
  AND mes_ano = TO_CHAR(CURRENT_DATE, 'YYYY-MM')
ORDER BY provedor;
```

**Resultado:**

```
 provedor  | tipo_chave | quantidade_chamadas | mes_ano
-----------+------------+---------------------+---------
 gemini    | propria    | 150                 | 2025-12
 weather   | propria    | 45                  | 2025-12
 openroute | propria    | 20                  | 2025-12
```

---

## 🔄 Trocando Entre Chave Própria e Chave do Sistema

Se quiser **testar** usar a chave do sistema (paga):

```sql
-- Trocar Gemini para chave do sistema
UPDATE PreferenciasChaveApi
SET usar_chave_propria = FALSE,
    atualizado_em = NOW()
WHERE usuario_id = 1
  AND provedor = 'gemini';

-- Voltar para chave própria
UPDATE PreferenciasChaveApi
SET usar_chave_propria = TRUE,
    atualizado_em = NOW()
WHERE usuario_id = 1
  AND provedor = 'gemini';
```

---

## 🗑️ Removendo Tudo (Recomeçar do Zero)

Se algo deu errado e quer recomeçar:

```sql
-- CUIDADO: Isso deleta TUDO relacionado às suas chaves!

DELETE FROM LogAcessoChaveApi WHERE usuario_id = 1;
DELETE FROM RastreamentoUsoApi WHERE usuario_id = 1;
DELETE FROM PreferenciasChaveApi WHERE usuario_id = 1;
DELETE FROM ChavesApiUsuario WHERE usuario_id = 1;

-- Agora você pode recomeçar do Passo 1
```

---

## ❓ Perguntas Frequentes

### **P: As chaves ficam visíveis no banco?**
**R:** NÃO! Elas são criptografadas com Fernet antes de serem armazenadas. Mesmo com acesso ao banco, ninguém consegue descriptografar sem a `ENCRYPTION_KEY`.

### **P: O que é a ENCRYPTION_KEY?**
**R:** É uma chave secreta usada para criptografar/descriptografar as chaves de API. Está no seu arquivo `.env`:
```
ENCRYPTION_KEY=sua-chave-fernet-aqui
```

### **P: Posso ter chave do Gemini própria e Weather do sistema?**
**R:** SIM! Você configura individualmente para cada provedor. Exemplo:
```sql
-- Gemini: chave própria (grátis)
UPDATE PreferenciasChaveApi SET usar_chave_propria = TRUE WHERE provedor = 'gemini';

-- Weather: chave do sistema (pago)
UPDATE PreferenciasChaveApi SET usar_chave_propria = FALSE WHERE provedor = 'weather';
```

### **P: Como saber qual chave está sendo usada?**
**R:** Verifique os logs do servidor ou consulte:
```sql
SELECT provedor, usar_chave_propria FROM PreferenciasChaveApi WHERE usuario_id = 1;
```

### **P: Posso cadastrar chave para outro usuário?**
**R:** SIM! Basta trocar o `USUARIO_ID` no script. Útil se você é administrador do sistema.

---

## 🛡️ Segurança

### **✅ O Que o Sistema Faz:**
- Criptografa chaves antes de armazenar
- Usa Fernet (criptografia simétrica segura)
- Registra logs de acesso
- Rastreia uso para billing

### **⚠️ O Que VOCÊ Deve Fazer:**
- Manter `ENCRYPTION_KEY` segura e em backup
- Não compartilhar suas chaves de API
- Monitorar uso no dashboard de cada provedor
- Rotacionar chaves a cada 6 meses

---

## 🎉 Pronto!

Agora você está usando suas próprias chaves de API e economizando dinheiro no seu plano do Meu Secretário!

**Próximos passos:**
1. ✅ Testar enviando mensagens no WhatsApp
2. ✅ Monitorar uso nos dashboards dos provedores
3. ✅ Acompanhar rastreamento no banco de dados
4. ✅ Aproveitar o uso ilimitado (grátis)!

---

**Precisa de ajuda?**
- 💬 WhatsApp: (31) 9400-1072
- 📧 Email: suporte@meusecretario.com
