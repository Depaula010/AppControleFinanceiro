# 🧪 REGISTRO DE TESTES - CHATBOT WHATSAPP

**Data de Execução**: ___/___/2025
**Testador**: _________________
**Ambiente**: Produção
**Usuário de Teste**: _________________

---

## ⚠️ CHECKLIST PRÉ-TESTE

- [ ] Backup do banco de dados criado em: ___________
- [ ] Usuário de teste configurado: ___________
- [ ] WhatsApp conectado e funcionando
- [ ] Saldos iniciais anotados:
  - Conta 1: _________ - Saldo: R$ _______
  - Conta 2: _________ - Saldo: R$ _______
  - Conta 3: _________ - Saldo: R$ _______
- [ ] Agendamento com vencimento HOJE criado (ID: _____)
- [ ] Agendamento com vencimento AMANHÃ criado (ID: _____)
- [ ] Agendamento ATRASADO criado (ID: _____)

---

## 🏦 CATEGORIA 1: TRANSAÇÕES FINANCEIRAS

### TC001: Renda (Receita)

**Status Geral**: ⏳ Pendente | ✅ Passou | ❌ Falhou

#### TC001.1 - Renda Básica
- **Mensagem**: "Recebi 2000 de salário"
- **Esperado**: Confirmação com valor R$ 2.000,00, descrição "salário"
- **Resultado**: ⏳ Pendente | ✅ Passou | ❌ Falhou
- **Observações**: _______________________________
- **ID Transação**: _______

#### TC001.2 - Renda com Data
- **Mensagem**: "Ganhei 500 reais de freelance dia 15/12"
- **Esperado**: Confirmação com data 15/12
- **Resultado**: ⏳ | ✅ | ❌
- **Observações**: _______________________________

#### TC001.3 - Renda com Conta Específica
- **Mensagem**: "Renda do Inter: 1500"
- **Esperado**: Conta "Inter" selecionada
- **Resultado**: ⏳ | ✅ | ❌
- **Observações**: _______________________________

#### TC001.4 - Confirmar Transação
- **Ação**: Responder "ok" após TC001.1
- **Esperado**: "✅ Transação salva!" + saldo atualizado
- **Resultado**: ⏳ | ✅ | ❌
- **Saldo Antes**: R$ _______ | **Saldo Depois**: R$ _______
- **ID Transação**: _______

#### TC001.5 - Cancelar Transação
- **Ação**: Responder "cancelar" após confirmação
- **Esperado**: "❌ Transação cancelada"
- **Resultado**: ⏳ | ✅ | ❌
- **Verificado no Banco**: ⏳ | ✅ Não criou | ❌ Criou indevidamente

#### TC001.6 - Validação de Valor Negativo
- **Mensagem**: "Recebi -100"
- **Esperado**: Erro: "Valor deve ser positivo"
- **Resultado**: ⏳ | ✅ | ❌
- **Observações**: _______________________________

---

### TC002: Despesa

**Status Geral**: ⏳ | ✅ | ❌

#### TC002.1 - Despesa Básica
- **Mensagem**: "Gastei 150 com mercado"
- **Esperado**: Confirmação com valor R$ 150,00 + categoria sugerida
- **Resultado**: ⏳ | ✅ | ❌
- **Categoria Sugerida**: _______________________
- **ID Transação**: _______

#### TC002.2 - Despesa Parcelada
- **Mensagem**: "Compra de 3000 no computador em 12 parcelas"
- **Esperado**: Confirmação com 12x de R$ 250,00
- **Resultado**: ⏳ | ✅ | ❌
- **Parcelamento Correto**: ⏳ | ✅ | ❌
- **IDs Transações (12)**: _______________________

#### TC002.3 - Despesa com Conta Específica
- **Mensagem**: "Gasolina 80 reais no Nubank"
- **Esperado**: Conta "Nubank" reconhecida
- **Resultado**: ⏳ | ✅ | ❌

#### TC002.4 - Trocar Categoria
- **Ação**: Responder "trocar" após TC002.1
- **Esperado**: Lista de categorias (1️⃣ 2️⃣ 3️⃣...)
- **Resultado**: ⏳ | ✅ | ❌
- **Categorias Exibidas**: _______________________

#### TC002.5 - Selecionar Nova Categoria
- **Ação**: Responder "2"
- **Esperado**: Confirmação com nova categoria
- **Resultado**: ⏳ | ✅ | ❌
- **Nova Categoria**: _______________________

#### TC002.6 - Confirmar Despesa
- **Ação**: Responder "ok"
- **Esperado**: "✅ Transação salva!"
- **Resultado**: ⏳ | ✅ | ❌
- **Saldo Antes**: R$ _______ | **Saldo Depois**: R$ _______

#### TC002.7 - Validação de Parcelas Máximas
- **Mensagem**: "Gastei 5000 parcelado em 50x"
- **Esperado**: Erro: "máximo 48 parcelas"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC003: Transferência

**Status Geral**: ⏳ | ✅ | ❌

#### TC003.1 - Transferência Básica
- **Mensagem**: "Transferir 500 do Inter para Nubank"
- **Esperado**: Confirmação origem→destino R$ 500,00
- **Resultado**: ⏳ | ✅ | ❌
- **ID Transação**: _______

#### TC003.2 - Transferência com Nomes Parciais
- **Mensagem**: "Mover 1000 da poupança para corrente"
- **Esperado**: Sistema encontra contas por nome parcial
- **Resultado**: ⏳ | ✅ | ❌

#### TC003.3 - Confirmar Transferência
- **Ação**: Responder "ok" após TC003.1
- **Esperado**: Saldo origem -500, destino +500
- **Resultado**: ⏳ | ✅ | ❌
- **Saldo Origem Antes**: R$ _______ | **Depois**: R$ _______
- **Saldo Destino Antes**: R$ _______ | **Depois**: R$ _______

#### TC003.4 - Validação Contas Iguais
- **Mensagem**: "Transferir 100 do Inter para Inter"
- **Esperado**: Erro: "Contas devem ser diferentes"
- **Resultado**: ⏳ | ✅ | ❌

#### TC003.5 - Validação Saldo Insuficiente
- **Mensagem**: "Transferir 999999 do Inter para Nubank"
- **Esperado**: Erro: "Saldo insuficiente"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC004: Pagamento de Fatura

**Status Geral**: ⏳ | ✅ | ❌

#### TC004.1 - Pagamento Básico
- **Mensagem**: "Paguei 500 reais da fatura do Nubank"
- **Esperado**: Confirmação cartão "Nubank" R$ 500,00
- **Resultado**: ⏳ | ✅ | ❌
- **ID Transação**: _______

#### TC004.2 - Pagamento Variação
- **Mensagem**: "Pagamento do cartão Inter com 300"
- **Esperado**: Valor R$ 300,00 extraído
- **Resultado**: ⏳ | ✅ | ❌

#### TC004.3 - Confirmar Pagamento
- **Ação**: Responder "ok" após TC004.1
- **Esperado**: Fatura -R$ 500, Conta -R$ 500
- **Resultado**: ⏳ | ✅ | ❌
- **Fatura Antes**: R$ _______ | **Depois**: R$ _______
- **Conta Antes**: R$ _______ | **Depois**: R$ _______

#### TC004.4 - Validação Cartão Inexistente
- **Mensagem**: "Paguei fatura de cartão inexistente"
- **Esperado**: Erro: "Cartão não encontrado"
- **Resultado**: ⏳ | ✅ | ❌

---

## 📊 CATEGORIA 2: CONSULTAS FINANCEIRAS

### TC005: Consulta Saldo

**Status Geral**: ⏳ | ✅ | ❌

#### TC005.1 - Saldo Geral
- **Mensagem**: "Qual meu saldo?"
- **Esperado**: Lista todas as contas + total
- **Resultado**: ⏳ | ✅ | ❌
- **Saldos Exibidos**: _______________________

#### TC005.2 - Saldo Conta Específica
- **Mensagem**: "Saldo do Nubank"
- **Esperado**: Apenas saldo do Nubank
- **Resultado**: ⏳ | ✅ | ❌

#### TC005.3 - Saldo Variação de Pergunta
- **Mensagem**: "Quanto tenho no Inter?"
- **Esperado**: Saldo do Inter
- **Resultado**: ⏳ | ✅ | ❌

---

### TC006: Consulta Reserva de Emergência

**Status Geral**: ⏳ | ✅ | ❌

#### TC006.1 - Reserva Básica
- **Mensagem**: "Quanto já tenho de reserva?"
- **Esperado**: Gasto mensal + Reserva ideal + Atual + % + barra
- **Resultado**: ⏳ | ✅ | ❌
- **Dados Exibidos**: _______________________

#### TC006.2 - Reserva Variação
- **Mensagem**: "Status da minha reserva de emergência"
- **Esperado**: Mesma resposta
- **Resultado**: ⏳ | ✅ | ❌

---

### TC007: Consulta Potes (Distribuição de Gastos)

**Status Geral**: ⏳ | ✅ | ❌

#### TC007.1 - Potes Básico
- **Mensagem**: "Como estão meus potes?"
- **Esperado**: Lista categorias + valores + %
- **Resultado**: ⏳ | ✅ | ❌
- **Categorias Exibidas**: _______________________

#### TC007.2 - Potes Variação
- **Mensagem**: "Distribuição de gastos"
- **Esperado**: Mesma resposta
- **Resultado**: ⏳ | ✅ | ❌

---

### TC008: Consulta Período

**Status Geral**: ⏳ | ✅ | ❌ | 🔴 Não Implementado

#### TC008.1 - Gastos do Mês
- **Mensagem**: "Gastos de janeiro"
- **Esperado**: Lista ou placeholder
- **Resultado**: ⏳ | ✅ | ❌ | 🔴
- **Observações**: _______________________

#### TC008.2 - Receitas Mês Anterior
- **Mensagem**: "Receitas do mês passado"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

#### TC008.3 - Última Semana
- **Mensagem**: "Transações da última semana"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

---

### TC009: Consulta Categoria Específica

**Status Geral**: ⏳ | ✅ | ❌ | 🔴 Não Implementado

#### TC009.1 - Categoria Alimentação
- **Mensagem**: "Quanto gastei com alimentação?"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

#### TC009.2 - Categoria Transporte
- **Mensagem**: "Gastos de transporte esse mês"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

---

### TC010: Consulta Contas Fixas

**Status Geral**: ⏳ | ✅ | ❌ | 🔴 Não Implementado

#### TC010.1 - Contas Fixas
- **Mensagem**: "Minhas contas fixas"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

#### TC010.2 - Agendamentos
- **Mensagem**: "Quais agendamentos tenho?"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

---

### TC011: Consulta Valor Fatura

**Status Geral**: ⏳ | ✅ | ❌ | 🔴 Não Implementado

#### TC011.1 - Fatura Específica
- **Mensagem**: "Quanto tá a fatura do Nubank?"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

#### TC011.2 - Fatura Geral
- **Mensagem**: "Valor da fatura esse mês"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

---

## 🔔 CATEGORIA 3: NOTIFICAÇÕES E ALERTAS

### TC012: Vencimentos Hoje

**Status Geral**: ⏳ | ✅ | ❌

#### TC012.1 - Com Vencimentos
- **Mensagem**: "O que vence hoje?"
- **Esperado**: Lista de contas + valores
- **Resultado**: ⏳ | ✅ | ❌
- **Itens Exibidos**: _______________________

#### TC012.2 - Variação
- **Mensagem**: "Contas de hoje"
- **Resultado**: ⏳ | ✅ | ❌

#### TC012.3 - Sem Vencimentos
- **Mensagem**: "O que vence hoje?" (se nenhum)
- **Esperado**: "Nenhuma conta vence hoje! 🎉"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC013: Vencimentos Amanhã

**Status Geral**: ⏳ | ✅ | ❌

#### TC013.1 - Vencimentos Amanhã
- **Mensagem**: "O que vence amanhã?"
- **Resultado**: ⏳ | ✅ | ❌

#### TC013.2 - Sem Vencimentos
- **Mensagem**: "Vencimentos amanhã" (se nenhum)
- **Esperado**: "Nenhuma conta vence amanhã!"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC014: Vencimentos Essa Semana

**Status Geral**: ⏳ | ✅ | ❌

#### TC014.1 - Vencimentos Semana
- **Mensagem**: "O que vence essa semana?"
- **Esperado**: Lista agrupada por dia
- **Resultado**: ⏳ | ✅ | ❌
- **Dias Exibidos**: _______________________

#### TC014.2 - Variação
- **Mensagem**: "Vencimentos da semana"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC015: Contas Atrasadas

**Status Geral**: ⏳ | ✅ | ❌

#### TC015.1 - Com Atrasadas
- **Mensagem**: "Tenho alguma conta atrasada?"
- **Esperado**: Lista + dias de atraso + total
- **Resultado**: ⏳ | ✅ | ❌
- **Itens Atrasados**: _______________________

#### TC015.2 - Variação
- **Mensagem**: "Contas vencidas"
- **Resultado**: ⏳ | ✅ | ❌

#### TC015.3 - Sem Atrasadas
- **Mensagem**: "Contas atrasadas" (se nenhuma)
- **Esperado**: "Nenhuma conta atrasada! 🎉"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC016: Configurar Notificações

**Status Geral**: 🔴 Não Implementado

#### TC016.1 - Ativar Notificações
- **Mensagem**: "Quero receber lembretes de vencimentos"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

#### TC016.2 - Notificações Diárias
- **Mensagem**: "Ativar notificações diárias"
- **Resultado**: ⏳ | ✅ | ❌ | 🔴

---

## 📅 CATEGORIA 4: CALENDÁRIO E AGENDA

### TC017-020: Intents de Calendário

**Status**: 🔴 TODOS PLACEHOLDERS - Não testar

---

## 🤖 CATEGORIA 5: ANÁLISES E INTELIGÊNCIA

### TC021-026: Intents de Análise

**Status**: 🔴 TODOS PLACEHOLDERS - Não testar

---

## ⚙️ CATEGORIA 6: ADMINISTRAÇÃO

### TC025: Listar Contas

**Status Geral**: ⏳ | ✅ | ❌

#### TC025.1 - Listar Todas
- **Mensagem**: "Quais minhas contas?"
- **Esperado**: Lista com tipos e saldos
- **Resultado**: ⏳ | ✅ | ❌
- **Contas Exibidas**: _______________________

#### TC025.2 - Variação 1
- **Mensagem**: "Listar contas cadastradas"
- **Resultado**: ⏳ | ✅ | ❌

#### TC025.3 - Variação 2
- **Mensagem**: "Mostrar minhas contas bancárias"
- **Resultado**: ⏳ | ✅ | ❌

---

### TC026: Ajustar Saldo Inicial

**Status Geral**: ⏳ | ✅ | ❌ | ⚠️ CUIDADO!

#### TC026.1 - Ajustar Saldo
- **⚠️ ANOTAR SALDO ORIGINAL**: R$ _______
- **Mensagem**: "Ajustar saldo do Nubank para 1500"
- **Resultado**: ⏳ | ✅ | ❌
- **Saldo Alterado**: ⏳ | ✅ | ❌
- **⚠️ RESTAURADO**: ⏳ | ✅

---

### TC027-029: Outros Intents Admin

**Status**: 🔴 NÃO IMPLEMENTADOS - Não testar

---

## 🔄 TESTES ESPECIAIS: FLUXOS COMPLEXOS

### TC030: Fluxo Completo com Troca de Categoria

**Status Geral**: ⏳ | ✅ | ❌

**Passos**:
1. Enviar: "Gastei 50 no supermercado"
   - **Resultado**: ⏳ | ✅ | ❌
2. Responder: "trocar"
   - **Resultado**: ⏳ | ✅ | ❌
3. Responder: "2" (escolher categoria)
   - **Resultado**: ⏳ | ✅ | ❌
4. Responder: "ok"
   - **Resultado**: ⏳ | ✅ | ❌
5. Verificar categoria no banco
   - **Categoria Final**: _______________________
   - **Correto**: ⏳ | ✅ | ❌

---

### TC031: Fluxo de Cancelamento

**Status Geral**: ⏳ | ✅ | ❌

**Passos**:
1. Enviar: "Recebi 100 de presente"
   - **Resultado**: ⏳ | ✅ | ❌
2. Responder: "cancelar"
   - **Resultado**: ⏳ | ✅ | ❌
3. Verificar não criou no banco
   - **Verificado**: ⏳ | ✅ | ❌
4. Verificar saldo não mudou
   - **Verificado**: ⏳ | ✅ | ❌

---

## 📊 RESUMO DE RESULTADOS

### Estatísticas Finais:
- **Total de Testes Executados**: _____ / 80
- **Testes Passaram**: _____ (____%)
- **Testes Falharam**: _____ (____%)
- **Testes Não Implementados**: _____ (____%)

### Por Categoria:
| Categoria | Total | Passou | Falhou | Não Impl. |
|-----------|-------|--------|--------|-----------|
| Transações | 24 | ___ | ___ | ___ |
| Consultas | 14 | ___ | ___ | ___ |
| Notificações | 11 | ___ | ___ | ___ |
| Calendário | 0 | 0 | 0 | 4 |
| Análises | 0 | 0 | 0 | 6 |
| Administração | 9 | ___ | ___ | ___ |
| Fluxos Especiais | 2 | ___ | ___ | ___ |

---

## 🐛 BUGS ENCONTRADOS

### Bug #1
- **Severidade**: ⚠️ Crítico | 🔴 Alto | 🟡 Médio | 🟢 Baixo
- **Teste Relacionado**: TC___
- **Descrição**: _______________________________
- **Passos para Reproduzir**: _______________________________
- **Resultado Esperado**: _______________________________
- **Resultado Atual**: _______________________________
- **Screenshot**: _______________________________

### Bug #2
- **Severidade**: ⚠️ | 🔴 | 🟡 | 🟢
- **Teste Relacionado**: TC___
- **Descrição**: _______________________________

### Bug #3
- **Severidade**: ⚠️ | 🔴 | 🟡 | 🟢
- **Teste Relacionado**: TC___
- **Descrição**: _______________________________

---

## 💡 SUGESTÕES DE MELHORIA

1. _______________________________
2. _______________________________
3. _______________________________

---

## 🧹 CHECKLIST PÓS-TESTE (LIMPEZA)

- [ ] Deletar transações de teste criadas
  - IDs: _______________________________
- [ ] Restaurar saldos originais
  - Conta 1: Saldo restaurado para R$ _______
  - Conta 2: Saldo restaurado para R$ _______
  - Conta 3: Saldo restaurado para R$ _______
- [ ] Deletar agendamentos de teste
  - IDs: _______________________________
- [ ] Verificar faturas de cartões
  - Valores corretos: ⏳ | ✅ | ❌
- [ ] Verificar nenhuma transação de teste permanece
  - Verificado: ⏳ | ✅ | ❌
- [ ] Logs revisados
  - Erros críticos encontrados: _____ | Nenhum: ✅
- [ ] Planilha preenchida completamente
  - Completa: ⏳ | ✅

---

## ✅ APROVAÇÃO

**Testes concluídos em**: ___/___/2025
**Aprovado por**: _________________
**Taxa de Sucesso**: _____%
**Ambiente Limpo**: ⏳ | ✅
**Pronto para Produção**: ⏳ | ✅ | ❌

---

**Observações Finais**:
_______________________________
_______________________________
_______________________________
