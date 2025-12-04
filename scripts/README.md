# 📂 Scripts - Sistema de Chaves de API

Este diretório contém scripts auxiliares para configurar e gerenciar suas chaves de API no sistema SaaS.

---

## 📋 Arquivos Disponíveis

### **1. inserir_minhas_chaves.py** ⭐ (RECOMENDADO)
**Descrição:** Script Python automatizado que criptografa suas chaves e gera SQL pronto para executar.

**Uso:**
```bash
# 1. Edite o arquivo e cole suas chaves
nano scripts/inserir_minhas_chaves.py

# 2. Execute o script
python scripts/inserir_minhas_chaves.py

# 3. O script irá gerar: scripts/output_sql_chaves.sql
```

**Recursos:**
- ✅ Valida formato das chaves
- ✅ Criptografa automaticamente
- ✅ Gera SQL pronto para executar
- ✅ Instruções passo a passo
- ✅ Tratamento de erros completo

---

### **2. descobrir_usuario_id.sql**
**Descrição:** Script SQL para descobrir seu usuario_id no banco.

**Uso:**
```bash
# Opção A: psql
psql -f scripts/descobrir_usuario_id.sql

# Opção B: Copiar e colar no DBeaver/pgAdmin
# Abra o arquivo e copie os comandos que precisa
```

**Queries disponíveis:**
- Buscar por email
- Buscar por WhatsApp
- Listar todos os usuários
- Buscar por nome (parcial)
- Ver informações completas do usuário

---

### **3. template_inserir_chaves.sql**
**Descrição:** Template SQL manual (se preferir não usar o script Python).

**Uso:**
1. Criptografe manualmente suas chaves (Python console):
   ```python
   from app.services.encryption_service import encryption_service
   print(encryption_service.encrypt("SUA_CHAVE"))
   ```

2. Edite o template substituindo os placeholders:
   - `{USUARIO_ID}` → Seu ID
   - `{GEMINI_ENCRYPTED}` → Chave criptografada do Gemini
   - `{WEATHER_ENCRYPTED}` → Chave criptografada do Weather
   - `{OPENROUTE_ENCRYPTED}` → Chave criptografada do OpenRoute

3. Execute o SQL no banco

**⚠️ Não recomendado:** Use o script Python (mais fácil e seguro).

---

### **4. GUIA_INSERIR_CHAVES.md**
**Descrição:** Guia completo com instruções detalhadas.

**Contém:**
- Pré-requisitos
- Método automatizado (Python)
- Método manual (SQL direto)
- Testes e verificações
- Solução de problemas
- FAQs

---

## 🚀 Guia Rápido

### **Passo 1: Descubra seu usuario_id**

```bash
psql -f scripts/descobrir_usuario_id.sql
```

OU execute no banco:
```sql
SELECT id, nome, email FROM Usuarios WHERE email = 'seu-email@exemplo.com';
```

Anote o `id` (ex: 1).

---

### **Passo 2: Prepare suas chaves**

Você precisa ter gerado suas chaves nos provedores:

- **Google Gemini:** [aistudio.google.com/apikey](https://aistudio.google.com/app/apikey)
- **WeatherAPI:** [weatherapi.com/signup](https://www.weatherapi.com/signup.aspx)
- **OpenRouteService:** [openrouteservice.org/dev](https://openrouteservice.org/dev/#/signup)

📚 Consulte os manuais em `docs/` para instruções detalhadas.

---

### **Passo 3: Execute o script Python**

```bash
# 1. Edite o script
nano scripts/inserir_minhas_chaves.py

# Configure:
USUARIO_ID = 1  # Seu ID do passo 1
GEMINI_KEY = "AIzaSy..."  # Suas chaves
WEATHER_KEY = "..."
OPENROUTE_KEY = "..."

# 2. Execute
python scripts/inserir_minhas_chaves.py

# 3. O script gera: scripts/output_sql_chaves.sql
```

---

### **Passo 4: Execute o SQL no banco**

```bash
# Opção A: psql
psql -f scripts/output_sql_chaves.sql

# Opção B: DBeaver/pgAdmin
# Abra scripts/output_sql_chaves.sql e execute
```

---

### **Passo 5: Teste**

Envie mensagem no WhatsApp:
```
Bom dia
```

Verifique os logs do servidor:
```
[GEMINI] ✅ Usando chave de gemini propria para usuário 1
[WEATHER] ✅ Usando chave de weather propria para usuário 1
```

✅ **Funcionando!** Você está usando suas próprias chaves (grátis).

---

## 📊 Estrutura de Arquivos

```
scripts/
├── README.md                        # Este arquivo
├── inserir_minhas_chaves.py         # ⭐ Script automatizado (USAR ESTE)
├── descobrir_usuario_id.sql         # SQL para descobrir seu ID
├── template_inserir_chaves.sql      # Template SQL manual
├── GUIA_INSERIR_CHAVES.md          # Guia completo
└── output_sql_chaves.sql           # ⚠️ Gerado automaticamente (NÃO EDITE)
```

---

## ⚠️ Importante

### **Segurança:**
- ✅ As chaves são criptografadas antes de serem armazenadas
- ✅ Usa Fernet (criptografia simétrica segura)
- ✅ Requer `ENCRYPTION_KEY` configurada no `.env`
- ❌ NÃO compartilhe suas chaves de API
- ❌ NÃO commite `output_sql_chaves.sql` no Git (está no .gitignore)

### **Arquivo .gitignore:**
```
scripts/output_sql_chaves.sql
```

---

## 🔧 Solução de Problemas

### **Erro: "ENCRYPTION_KEY não configurada"**

**Solução:**
```bash
# Verifique se existe no .env
cat .env | grep ENCRYPTION_KEY

# Se não existir, gere uma nova:
python -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())"

# Adicione ao .env:
echo "ENCRYPTION_KEY=sua-chave-gerada-aqui" >> .env
```

---

### **Erro: "Chave Gemini deve começar com 'AIzaSy'"**

**Causa:** Você copiou a chave errada ou incompleta.

**Solução:**
1. Acesse [Google AI Studio](https://aistudio.google.com/app/apikey)
2. Copie a chave completa
3. Deve começar com `AIzaSy`
4. Tem ~39 caracteres

---

### **Erro: "ModuleNotFoundError: No module named 'app'"**

**Causa:** Executando de diretório errado.

**Solução:**
```bash
# Execute sempre da RAIZ do projeto:
cd /caminho/para/AppControleFinanceiro
python scripts/inserir_minhas_chaves.py
```

---

## 📚 Recursos Adicionais

### **Documentação:**
- [docs/API_CHAVES_DOCUMENTACAO.md](../docs/API_CHAVES_DOCUMENTACAO.md) - API completa
- [docs/MANUAL_GEMINI.md](../docs/MANUAL_GEMINI.md) - Como gerar chave do Gemini
- [docs/MANUAL_WEATHER.md](../docs/MANUAL_WEATHER.md) - Como gerar chave do Weather
- [docs/MANUAL_OPENROUTE.md](../docs/MANUAL_OPENROUTE.md) - Como gerar chave do OpenRoute

### **Scripts SQL úteis:**
```sql
-- Ver suas chaves (sem descriptografar)
SELECT id, provedor, ativo FROM ChavesApiUsuario WHERE usuario_id = 1;

-- Ver suas preferências
SELECT provedor, usar_chave_propria FROM PreferenciasChaveApi WHERE usuario_id = 1;

-- Ver uso mensal
SELECT provedor, tipo_chave, quantidade_chamadas
FROM RastreamentoUsoApi
WHERE usuario_id = 1 AND mes_ano = TO_CHAR(CURRENT_DATE, 'YYYY-MM');

-- Deletar tudo (recomeçar)
-- ⚠️ CUIDADO!
-- DELETE FROM PreferenciasChaveApi WHERE usuario_id = 1;
-- DELETE FROM ChavesApiUsuario WHERE usuario_id = 1;
```

---

## 🤝 Suporte

Precisa de ajuda?

- 💬 **WhatsApp:** (31) 9400-1072
- 📧 **Email:** suporte@meusecretario.com
- 📖 **Guia completo:** scripts/GUIA_INSERIR_CHAVES.md

---

**Versão:** 1.0
**Última Atualização:** 04/12/2025
