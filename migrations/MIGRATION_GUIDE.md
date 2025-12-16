# Guia de Migração - Adicionar Campos Faltantes ao Schema

**Data**: 2025-12-16
**Script**: `migration_add_missing_fields.sql`
**Objetivo**: Adequar banco de dados aos modelos ORM SQLAlchemy

---

## 📋 Resumo da Migração

Esta migração adiciona campos que estão nos modelos ORM mas não existem no banco de dados atual.

### Tabelas Afetadas

| Tabela | Campos Adicionados | Impacto |
|--------|-------------------|---------|
| **Usuarios** | 6 campos (email, conta_padrao_id, fuso_horario, ativo, ultimo_acesso, updated_at) | ⚠️ Médio |
| **Contas** | 7 campos (limite_credito, inclui_saldo_total, cor_hex, icone, ativa, ordem, updated_at) | ⚠️ Médio |
| **Transacoes** | 1 campo (updated_at) | ✅ Baixo |
| **Faturas** | 2 campos (created_at, updated_at) | ✅ Baixo |
| **Agendamentos** | 1 campo (updated_at) | ✅ Baixo |
| **PotesDeGastos** | 2 campos (created_at, updated_at) | ✅ Baixo |
| **GrupoCategoria** | 2 campos (created_at, updated_at) | ✅ Baixo |
| **MacroCategoria** | 2 campos (created_at, updated_at) | ✅ Baixo |
| **SubCategoria** | 2 campos (created_at, updated_at) | ✅ Baixo |

**Total**: 25 campos novos + 9 triggers automáticos

---

## ⚠️ IMPORTANTE - Leia Antes de Executar

### Pré-requisitos

1. ✅ **Backup completo do banco de dados**
   ```bash
   pg_dump -U postgres -d meu_secretario > backup_antes_migracao_$(date +%Y%m%d_%H%M%S).sql
   ```

2. ✅ **Ambiente de DEV para testar primeiro**
   - Nunca execute em PROD sem testar em DEV

3. ✅ **Verificar se há transações ativas**
   - Executar em horário de baixo tráfego

4. ✅ **Permissões adequadas**
   - Usuário com permissão ALTER TABLE

### Riscos

| Risco | Mitigação |
|-------|-----------|
| 🔴 Downtime durante execução | Executar em janela de manutenção |
| 🟡 Incompatibilidade com código legado | Novos campos são nullable/têm defaults |
| 🟡 Aumento no tamanho do banco | ~5-10% (estimativa) |
| 🟢 Perda de dados | Backup completo antes |

---

## 🚀 Passo a Passo - Ambiente DEV

### 1. Backup do Banco DEV

```bash
# Via HeidiSQL:
# Tools > Export database as SQL > Salvar

# Ou via CLI:
pg_dump -U postgres -d meu_secretario_dev > backup_dev_$(date +%Y%m%d_%H%M%S).sql
```

### 2. Executar Script no HeidiSQL (DEV)

1. Abrir HeidiSQL
2. Conectar ao banco **DEV** (`meu_secretario_dev` ou similar)
3. Clicar em **File > Load SQL file...**
4. Selecionar: `migrations/migration_add_missing_fields.sql`
5. Revisar o script (CTRL+F para procurar sua tabela)
6. Clicar em **▶ Execute** (F9)
7. Aguardar conclusão (deve levar 10-30 segundos)

### 3. Verificar Resultado

Execute estas queries de validação:

```sql
-- 1. Verificar novos campos em Usuarios
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'Usuarios'
ORDER BY ordinal_position;

-- 2. Verificar novos campos em Contas
SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'Contas'
ORDER BY ordinal_position;

-- 3. Verificar triggers criados
SELECT trigger_name, event_manipulation, event_object_table
FROM information_schema.triggers
WHERE trigger_name LIKE '%updated_at%';

-- 4. Testar trigger de updated_at
UPDATE "Usuarios" SET nome = nome WHERE id = 1;
SELECT id, nome, created_at, updated_at FROM "Usuarios" WHERE id = 1;
-- updated_at deve ser maior que created_at
```

### 4. Testar Aplicação em DEV

```bash
# Subir aplicação em DEV
docker-compose up -d

# Verificar logs
docker-compose logs -f web

# Testar endpoints principais
curl http://localhost:5000/api/health
curl http://localhost:5000/api/usuarios

# Testar ORM (opcional)
docker-compose exec web python -c "
from app.infrastructure.database.models import UserModel
from sqlalchemy import create_engine
from sqlalchemy.orm import Session
import os

engine = create_engine(os.getenv('DATABASE_URL'))
with Session(engine) as session:
    users = session.query(UserModel).limit(5).all()
    for user in users:
        print(f'{user.id}: {user.nome} - {user.email or \"sem email\"}')
    print('✅ ORM funcionando!')
"
```

---

## 🏭 Passo a Passo - Ambiente PROD

⚠️ **ATENÇÃO**: Só execute em PROD após testar em DEV!

### 1. Agendar Janela de Manutenção

- **Horário recomendado**: Madrugada (02:00 - 04:00)
- **Duração estimada**: 5 minutos
- **Notificar usuários**: Via WhatsApp/Email

### 2. Backup Completo PROD

```bash
# Backup via pg_dump
pg_dump -U postgres -h <host> -d meu_secretario > backup_prod_antes_migracao_$(date +%Y%m%d_%H%M%S).sql

# Verificar tamanho do backup
ls -lh backup_prod_*.sql

# Testar restore do backup (opcional mas recomendado)
# createdb meu_secretario_test
# psql -U postgres -d meu_secretario_test < backup_prod_*.sql
```

### 3. Colocar Aplicação em Modo Manutenção (Opcional)

```bash
# Parar containers
docker-compose down

# Ou criar página de manutenção
# nginx: return 503 "Sistema em manutenção. Voltamos em 5 minutos."
```

### 4. Executar Migração em PROD

1. Conectar HeidiSQL ao banco PROD
2. **CONFERIR 3 VEZES** se está no banco correto!
3. Executar script: `migration_add_missing_fields.sql`
4. Aguardar conclusão
5. Verificar mensagem: `✅ VALIDAÇÃO CONCLUÍDA: Todos os campos foram adicionados com sucesso!`

### 5. Validar em PROD

```sql
-- Query rápida de validação
SELECT
    'Usuarios' as tabela,
    COUNT(*) FILTER (WHERE column_name IN ('email', 'conta_padrao_id', 'fuso_horario', 'ativo', 'ultimo_acesso', 'updated_at')) as campos_novos
FROM information_schema.columns
WHERE table_name = 'Usuarios'
UNION ALL
SELECT
    'Contas',
    COUNT(*) FILTER (WHERE column_name IN ('limite_credito', 'inclui_saldo_total', 'cor_hex', 'icone', 'ativa', 'ordem', 'updated_at'))
FROM information_schema.columns
WHERE table_name = 'Contas';

-- Resultado esperado:
-- Usuarios: 6
-- Contas: 7
```

### 6. Subir Aplicação

```bash
# Subir containers
docker-compose up -d

# Monitorar logs
docker-compose logs -f web

# Verificar saúde
curl http://localhost:5000/api/health
```

### 7. Monitoramento Pós-Deploy

- Monitorar logs por 30 minutos
- Verificar uso de CPU/memória
- Testar funcionalidades críticas:
  - Login de usuário
  - Criar transação
  - Visualizar dashboard
  - Criar agendamento

---

## 🔄 Rollback (Se Necessário)

Se algo der errado, execute o rollback:

### Opção 1: Restaurar Backup (RECOMENDADO)

```bash
# Parar aplicação
docker-compose down

# Dropar banco atual
psql -U postgres -c "DROP DATABASE meu_secretario;"

# Recriar banco
psql -U postgres -c "CREATE DATABASE meu_secretario;"

# Restaurar backup
psql -U postgres -d meu_secretario < backup_prod_antes_migracao_YYYYMMDD_HHMMSS.sql

# Subir aplicação
docker-compose up -d
```

### Opção 2: Script de Rollback Manual

Descomentar e executar a seção de rollback no final do arquivo `migration_add_missing_fields.sql`.

⚠️ **ATENÇÃO**: Rollback manual causa perda de dados inseridos nos novos campos!

---

## ✅ Checklist de Execução

### Pré-Execução
- [ ] Backup completo realizado
- [ ] Script testado em DEV
- [ ] Aplicação testada em DEV após migração
- [ ] Janela de manutenção agendada (PROD)
- [ ] Time de plantão alertado

### Execução
- [ ] Conectado ao banco correto
- [ ] Script executado sem erros
- [ ] Validação SQL retornou sucesso
- [ ] Triggers criados corretamente

### Pós-Execução
- [ ] Aplicação subiu sem erros
- [ ] Endpoints respondendo
- [ ] Logs sem erros críticos
- [ ] Funcionalidades críticas testadas
- [ ] Monitoramento ativo por 1 hora

---

## 📊 Métricas de Sucesso

| Métrica | Esperado |
|---------|----------|
| Downtime | < 5 minutos |
| Tempo de execução | 10-30 segundos |
| Erros durante migração | 0 |
| Erros pós-migração | 0 |
| Testes funcionais passando | 100% |

---

## 🆘 Troubleshooting

### Erro: "column already exists"

**Causa**: Campo já existe no banco (script foi executado parcialmente antes)

**Solução**: Normal! O script usa `ADD COLUMN IF NOT EXISTS`, então ignora campos existentes.

---

### Erro: "permission denied for table Usuarios"

**Causa**: Usuário sem permissão ALTER TABLE

**Solução**:
```sql
GRANT ALL ON TABLE "Usuarios" TO seu_usuario;
-- Repetir para todas as tabelas
```

---

### Erro: "trigger already exists"

**Causa**: Trigger já foi criado antes

**Solução**: Script já contém `DROP TRIGGER IF EXISTS`, deve funcionar. Se persistir:
```sql
DROP TRIGGER trigger_update_usuarios_updated_at ON "Usuarios" CASCADE;
```

---

### Aplicação não sobe após migração

**Possíveis causas**:
1. Código legado esperando schema antigo
2. ORM com configuração incorreta
3. Alembic baseline desatualizada

**Solução**:
```bash
# Verificar logs
docker-compose logs web

# Rodar dentro do container
docker-compose exec web python -c "from app import create_app; create_app()"
```

---

## 📞 Contatos de Emergência

- **DBA**: [seu_email@exemplo.com]
- **DevOps**: [devops@exemplo.com]
- **Backup/Restore**: [Ver documentação em /docs/backup.md]

---

## 📝 Próximos Passos

Após executar esta migração com sucesso:

1. ✅ Marcar baseline do Alembic como aplicada:
   ```bash
   docker-compose exec web alembic stamp 5eb3cc74bfa5
   ```

2. ✅ Atualizar documentação de schema

3. ✅ Criar issues no backlog para:
   - Implementar validação de email
   - UI para configurar conta padrão
   - UI para definir fuso horário
   - UI para cor/ícone de contas

4. ✅ Iniciar Fase D.4 (Repository Pattern)

---

**Dúvidas?** Consulte [PHASE_D_PROGRESS.md](../docs/PHASE_D_PROGRESS.md)
