# Guia de Uso do Alembic - Migrações de Banco de Dados

## Visão Geral

Este projeto usa **Alembic** para controle de versão do schema do banco de dados PostgreSQL.

**IMPORTANTE**: Este banco de dados JÁ EXISTIA antes da implementação do Alembic. Por isso, temos uma **baseline migration** que marca o estado inicial sem executar alterações.

## Estrutura de Arquivos

```
migrations/
├── versions/                    # Arquivos de migração
│   └── 5eb3cc74bfa5_baseline_initial_schema.py
├── env.py                       # Configuração do Alembic
├── script.py.mako               # Template para novas migrações
└── README_USAGE.md              # Este arquivo
alembic.ini                      # Configuração principal
```

## Setup Inicial (Primeira Vez)

### 1. Marcar Baseline como Aplicada

Como o banco de dados já existe com todas as tabelas, precisamos apenas MARCAR a baseline migration como aplicada, SEM executá-la:

```bash
# Dentro do container Docker
docker-compose exec web alembic stamp 5eb3cc74bfa5

# OU executar localmente (se DATABASE_URL apontar para o banco correto)
alembic stamp 5eb3cc74bfa5
```

Isso cria a tabela `alembic_version` e marca que a migração baseline já foi "aplicada".

### 2. Verificar Status

```bash
alembic current
# Deve mostrar: 5eb3cc74bfa5 (head)
```

## Criando Novas Migrações

### Migração Automática (Autogenerate)

O Alembic pode detectar diferenças entre os modelos ORM e o banco de dados:

```bash
# Dentro do Docker (onde Redis/PostgreSQL estão disponíveis)
docker-compose exec web alembic revision --autogenerate -m "add_new_column_to_users"
```

**IMPORTANTE**: Autogenerate só funciona dentro do Docker, pois precisa conectar ao PostgreSQL e os imports dos modelos precisam do Redis.

### Migração Manual

Se não puder usar autogenerate, crie uma migração vazia e edite manualmente:

```bash
alembic revision -m "add_new_column_to_users"
# Editar o arquivo gerado em migrations/versions/
```

Exemplo de conteúdo:

```python
def upgrade() -> None:
    op.add_column('Usuarios', sa.Column('new_field', sa.String(100), nullable=True))

def downgrade() -> None:
    op.drop_column('Usuarios', 'new_field')
```

## Aplicando Migrações

### Aplicar Todas Pendentes

```bash
docker-compose exec web alembic upgrade head
```

### Aplicar Até Uma Revisão Específica

```bash
docker-compose exec web alembic upgrade <revision_id>
```

### Desfazer Última Migração

```bash
docker-compose exec web alembic downgrade -1
```

### Desfazer Até Uma Revisão Específica

```bash
docker-compose exec web alembic downgrade <revision_id>
```

## Comandos Úteis

### Ver Histórico de Migrações

```bash
alembic history --verbose
```

### Ver Migrações Pendentes

```bash
alembic current
alembic heads
```

### Ver SQL Gerado (Sem Executar)

```bash
alembic upgrade head --sql
```

## Workflow Recomendado

### Para Adicionar Uma Nova Coluna

1. **Editar o modelo ORM**:
   ```python
   # app/infrastructure/database/models/user_model.py
   class UserModel(Base):
       # ... campos existentes
       new_field: Mapped[str] = mapped_column(String(100), nullable=True)
   ```

2. **Gerar migração (dentro do Docker)**:
   ```bash
   docker-compose exec web alembic revision --autogenerate -m "add_new_field_to_users"
   ```

3. **Revisar o arquivo gerado**:
   - Abrir `migrations/versions/XXXXX_add_new_field_to_users.py`
   - Verificar se o código está correto
   - Ajustar se necessário

4. **Aplicar migração**:
   ```bash
   docker-compose exec web alembic upgrade head
   ```

5. **Testar downgrade (em DEV)**:
   ```bash
   docker-compose exec web alembic downgrade -1
   docker-compose exec web alembic upgrade head
   ```

6. **Commit do código**:
   ```bash
   git add app/infrastructure/database/models/user_model.py
   git add migrations/versions/XXXXX_add_new_field_to_users.py
   git commit -m "feat: Add new_field to users table"
   ```

## Troubleshooting

### Erro: "Can't locate revision identified by 'XXXX'"

O banco de dados não tem a tabela `alembic_version` ou está dessincronizado.

**Solução**: Marcar a baseline manualmente:
```bash
alembic stamp 5eb3cc74bfa5
```

### Erro: "Target database is not up to date"

Existem migrações pendentes.

**Solução**:
```bash
alembic upgrade head
```

### Erro ao Importar Modelos (Redis Connection)

Se estiver rodando Alembic localmente (fora do Docker), o `migrations/env.py` vai capturar a exceção e usar `target_metadata=None`.

**Solução**: Sempre rodar migrações autogenerate dentro do Docker:
```bash
docker-compose exec web alembic revision --autogenerate -m "description"
```

### Conflitos de Migração (Multiple Heads)

Se houver múltiplos branches de migração:

```bash
alembic heads  # Ver todos os heads
alembic merge <rev1> <rev2> -m "merge branches"  # Mesclar
```

## Integração com CI/CD

### Verificar Migrações Pendentes

```bash
# Em CI, adicionar check de migrações pendentes
alembic check
```

### Aplicar Migrações Automaticamente no Deploy

```bash
# No script de deploy
docker-compose exec web alembic upgrade head
docker-compose restart web
```

## Boas Práticas

1. **SEMPRE revisar migrações autogenerate** - Alembic pode não detectar tudo
2. **Testar downgrade em DEV** - Garantir que rollback funciona
3. **Uma migração por feature** - Facilita rollback
4. **Nomes descritivos** - `add_email_to_users` em vez de `update_users`
5. **Não editar migrações aplicadas** - Criar nova migração corretiva
6. **Backup antes de migrar em PROD** - Sempre!
7. **Commitar migrações junto com código** - Manter sincronizado

## Referências

- [Documentação Oficial do Alembic](https://alembic.sqlalchemy.org/)
- [SQLAlchemy 2.0 Documentation](https://docs.sqlalchemy.org/en/20/)
- [Alembic Tutorial](https://alembic.sqlalchemy.org/en/latest/tutorial.html)
