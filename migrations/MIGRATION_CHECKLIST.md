# ✅ Checklist de Migração - Adicionar Campos ao Schema

**Script**: `migration_add_missing_fields.sql`
**Data de Execução**: ___/___/2025

---

## 🔧 AMBIENTE: DEV

### Pré-Execução
- [ ] Backup do banco DEV realizado
  - [ ] Arquivo salvo em: `_______________________________`
  - [ ] Tamanho do backup: `_______ MB`
  - [ ] Data/hora: `___/___/___ __:__`

- [ ] HeidiSQL aberto e conectado ao banco **DEV**
  - [ ] Nome do banco: `_______________________________`
  - [ ] Host: `_______________________________`

- [ ] Script revisado
  - [ ] Li as seções principais
  - [ ] Entendi o que será alterado

### Execução
- [ ] Script carregado no HeidiSQL
  - [ ] File > Load SQL file > `migration_add_missing_fields.sql`

- [ ] Script executado (F9)
  - [ ] Hora de início: `__:__`
  - [ ] Hora de término: `__:__`
  - [ ] Duração: `____ segundos`

- [ ] Validação automática passou
  - [ ] Mensagem: "✅ VALIDAÇÃO CONCLUÍDA: Todos os campos foram adicionados com sucesso!"
  - [ ] ✅ SIM / ❌ NÃO

### Validação Manual

- [ ] Verificar novos campos em Usuarios
```sql
SELECT column_name FROM information_schema.columns
WHERE table_name = 'Usuarios' AND column_name IN
('email', 'conta_padrao_id', 'fuso_horario', 'ativo', 'ultimo_acesso', 'updated_at');
```
- [ ] Resultado: `___` linhas (esperado: 6)

- [ ] Verificar novos campos em Contas
```sql
SELECT column_name FROM information_schema.columns
WHERE table_name = 'Contas' AND column_name IN
('limite_credito', 'inclui_saldo_total', 'cor_hex', 'icone', 'ativa', 'ordem', 'updated_at');
```
- [ ] Resultado: `___` linhas (esperado: 7)

- [ ] Verificar triggers criados
```sql
SELECT COUNT(*) FROM information_schema.triggers
WHERE trigger_name LIKE '%updated_at%';
```
- [ ] Resultado: `___` triggers (esperado: 9)

- [ ] Testar trigger de updated_at
```sql
UPDATE "Usuarios" SET nome = nome WHERE id = 1;
SELECT created_at, updated_at FROM "Usuarios" WHERE id = 1;
```
- [ ] updated_at > created_at? ✅ SIM / ❌ NÃO

### Teste da Aplicação

- [ ] Docker Compose iniciado
```bash
docker-compose up -d
```
- [ ] Hora de início: `__:__`

- [ ] Logs verificados
```bash
docker-compose logs -f web
```
- [ ] Erros encontrados? ✅ NÃO / ❌ SIM
- [ ] Se SIM, descrever: `_______________________________`

- [ ] Endpoint de saúde testado
```bash
curl http://localhost:5000/api/health
```
- [ ] Resposta: `_______________________________`

- [ ] ORM testado (opcional)
```bash
docker-compose exec web python -c "
from app.infrastructure.database.models import UserModel
from sqlalchemy import create_engine
from sqlalchemy.orm import Session
import os
engine = create_engine(os.getenv('DATABASE_URL'))
with Session(engine) as session:
    user = session.query(UserModel).first()
    print(f'ID: {user.id}, Nome: {user.nome}')
"
```
- [ ] Funcionou? ✅ SIM / ❌ NÃO

### Conclusão DEV
- [ ] Todos os testes passaram
- [ ] Aplicação está funcionando normalmente
- [ ] Pronto para executar em PROD

**Observações DEV**:
```
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
```

---

## 🏭 AMBIENTE: PROD

⚠️ **ATENÇÃO**: Só preencher após sucesso em DEV!

### Pré-Execução
- [ ] Todos os testes em DEV passaram
- [ ] Janela de manutenção agendada
  - [ ] Data: `___/___/2025`
  - [ ] Horário: `__:__ - __:__`
  - [ ] Duração prevista: `___ minutos`

- [ ] Backup COMPLETO do banco PROD realizado
  - [ ] Arquivo: `_______________________________`
  - [ ] Tamanho: `_______ MB`
  - [ ] Data/hora: `___/___/___ __:__`
  - [ ] Testado? (restore em banco teste) ✅ SIM / ❌ NÃO

- [ ] Aplicação em modo manutenção (opcional)
  - [ ] `docker-compose down` executado
  - [ ] Hora: `__:__`

- [ ] Time de plantão alertado
  - [ ] DBA: ✅ Alertado
  - [ ] DevOps: ✅ Alertado
  - [ ] Desenvolvedor: ✅ Alertado

### Execução PROD
- [ ] HeidiSQL conectado ao banco **PROD**
  - [ ] ⚠️ VERIFICADO 3 VEZES que é PROD!
  - [ ] Nome do banco: `_______________________________`
  - [ ] Host: `_______________________________`

- [ ] Script carregado
  - [ ] File > Load SQL file > `migration_add_missing_fields.sql`

- [ ] Script executado (F9)
  - [ ] Hora de início: `__:__`
  - [ ] Hora de término: `__:__`
  - [ ] Duração: `____ segundos`

- [ ] Validação automática passou
  - [ ] Mensagem: "✅ VALIDAÇÃO CONCLUÍDA" apareceu
  - [ ] ✅ SIM / ❌ NÃO

### Validação PROD

- [ ] Query de validação rápida
```sql
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
```
- [ ] Usuarios: `___` (esperado: 6)
- [ ] Contas: `___` (esperado: 7)

### Subir Aplicação

- [ ] Docker Compose iniciado
```bash
docker-compose up -d
```
- [ ] Hora: `__:__`

- [ ] Logs verificados
- [ ] Erros? ✅ NÃO / ❌ SIM
- [ ] Se SIM: `_______________________________`

- [ ] Endpoint de saúde
```bash
curl http://localhost:5000/api/health
```
- [ ] Status: `_______________________________`

### Testes Funcionais

- [ ] Login de usuário
  - [ ] Funcionou? ✅ SIM / ❌ NÃO

- [ ] Criar transação
  - [ ] Funcionou? ✅ SIM / ❌ NÃO

- [ ] Visualizar dashboard
  - [ ] Funcionou? ✅ SIM / ❌ NÃO

- [ ] Criar agendamento
  - [ ] Funcionou? ✅ SIM / ❌ NÃO

### Monitoramento Pós-Deploy

**30 minutos após deploy**:
- [ ] CPU: `_____%` (normal: < 50%)
- [ ] Memória: `_____%` (normal: < 70%)
- [ ] Erros nos logs: `___` (esperado: 0)
- [ ] Requisições respondendo: ✅ SIM / ❌ NÃO

**1 hora após deploy**:
- [ ] CPU: `_____%`
- [ ] Memória: `_____%`
- [ ] Erros nos logs: `___`
- [ ] Sistema estável: ✅ SIM / ❌ NÃO

### Pós-Execução

- [ ] Marcar baseline do Alembic
```bash
docker-compose exec web alembic stamp 5eb3cc74bfa5
```
- [ ] Executado? ✅ SIM / ❌ NÃO

- [ ] Aplicação saiu do modo manutenção
- [ ] Usuários notificados que sistema voltou
- [ ] Time de plantão liberado

### Conclusão PROD
- [ ] Migração concluída com sucesso
- [ ] Sistema funcionando normalmente
- [ ] Nenhum erro crítico
- [ ] Documentação atualizada

**Observações PROD**:
```
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
```

---

## 🔄 Rollback (Se Necessário)

- [ ] Problema identificado durante execução
  - [ ] Descrição: `_______________________________`

- [ ] Decisão de rollback tomada
  - [ ] Por quem: `_______________________________`
  - [ ] Hora: `__:__`

- [ ] Aplicação parada
  - [ ] `docker-compose down`

- [ ] Backup restaurado
```bash
psql -U postgres -d meu_secretario < backup_prod_antes_migracao.sql
```
- [ ] Executado? ✅ SIM / ❌ NÃO
- [ ] Hora de início: `__:__`
- [ ] Hora de término: `__:__`

- [ ] Aplicação reiniciada
- [ ] Sistema voltou ao normal: ✅ SIM / ❌ NÃO

**Motivo do rollback**:
```
_______________________________________________________________
_______________________________________________________________
```

---

## 📊 Métricas Finais

| Métrica | DEV | PROD |
|---------|-----|------|
| Tempo de execução do script | ___ seg | ___ seg |
| Downtime total | N/A | ___ min |
| Erros durante migração | ___ | ___ |
| Erros pós-migração (1h) | ___ | ___ |
| Rollback necessário? | Não | Sim/Não |

---

## ✅ Assinaturas

**Executor**:
- Nome: `_______________________________`
- Data: `___/___/2025`
- Hora: `__:__`
- Assinatura: `_______________________________`

**Aprovador** (se aplicável):
- Nome: `_______________________________`
- Data: `___/___/2025`
- Assinatura: `_______________________________`

---

## 📝 Observações Gerais

```
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
_______________________________________________________________
```

---

**Status Final**: ✅ SUCESSO / ❌ FALHA / 🔄 ROLLBACK
