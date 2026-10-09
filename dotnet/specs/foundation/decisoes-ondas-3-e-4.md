# Decisões das ondas 3 e 4 (desvios propostos 089 a 233)

Reúne os 48 desvios que os agentes das features 006, 011, 012, 013 e 014 encontraram e **não implementaram** (estado `proposto`). Sem aprovação, o .NET reproduz o comportamento atual do Flask nesses pontos. Cada título é o do arquivo em `parity-deviations/`; abra o arquivo para ver o comportamento atual, o proposto e o impacto.

**Como responder:** "aprovo as recomendações", ou "aprovo as recomendações, exceto X". Os aprovados viram uma rodada de implementação (agrupada por feature, no máximo 2 agentes) antes do homol, para o homol já validar o comportamento final.

Contexto que pesa na decisão: só o Rafael usa o sistema; os dados reais já mostram bugs (fatura Aberta com vencimento errado, `alerta_potes_threshold = 1`).

Legenda: **A** aprovar · **R** rejeitar (manter o Flask) · **D** adiar para depois da virada · **?** decisão sua.

## Dashboard (006)

| # | O que muda | Rec. |
|---|---|---|
| 089 | Recentes devolvem `Renda` ou `Receita`? O Angular compara com `'Renda'` | **R** (manter `Renda` até o front mudar) |
| 090 | Primeiro mês dos gastos mensais sai parcial (janela de 30 dias × meses) | **A** |
| 091 | Gastos mensais em ordem decrescente de mês, e o gráfico mostra o mais novo à esquerda | **A** |
| 092 | Top 10 de categorias devolve as de menor gasto | **A** |
| 093 | Alertas MENSAL e ANUAL ignoram `data_inicio` | **A** |
| 094 | Alerta continua para conta já paga no mês | **A** |
| 095 | Receita aparece como conta a vencer | **A** |

## Automação (011)

| # | O que muda | Rec. |
|---|---|---|
| 096 | SMS: remover a chamada inútil ao Gemini | **A** |
| 097 | Entradas inválidas viram 400 com mensagem | **A** |
| 098 | `valor_pago` como texto numérico passa a funcionar | **A** |
| 099 | Quitação por SMS casa descrições sem diferenciar maiúsculas (hoje `luz` x `Luz` quase não casa) | **A** |
| 100 | Restaurar o emoji do cartão na confirmação (hoje sai caractere quebrado) | **A** |
| 101 | Reprocessar fatura isola o erro de cada agendamento | **A** |
| 102 | Chave do atalho: índice cego e cabeçalho no GET | **D** (mexe no atalho do iPhone; fazer depois da virada) |

## Alertas de calendário (014)

| # | O que muda | Rec. |
|---|---|---|
| 120 | Validar tipos do corpo de `/config` e `/ativar` | **A** |
| 121 | Janela de ±30 s perde o aviso se o ciclo atrasa | **A** |
| 122 | Usuário inativo deixa de receber alertas | **A** |

## Jobs (013)

| # | O que muda | Rec. |
|---|---|---|
| 130 | Fatura vencida deixa de derrubar a mensagem do check-in | **A** |
| 131 | Faturas `Fechada` entram nos alertas de vencimento | **A** |
| 132 | Relatório mensal: sinal das despesas (hoje o saldo soma em vez de subtrair e o top 5 sai invertido) | **A, obrigatório antes de ligar `JOBS_ENABLED` em produção** |
| 133 | Relatório mensal em formato numérico pt-BR | **A** |
| 134 | Agendamento de dia 29 a 31 dispara em mês curto | **A** |
| 135 | Populador de fatura lança `LEMBRETE_VARIAVEL` como despesa | **?** decisão de produto: o lembrete variável tem valor desconhecido, então recomendo **A** (não lançar como despesa) |
| 136 | Horário com minutos (07:30) passa a disparar | **A** |
| 137 | Resumo matinal sem Google conectado ainda envia os alertas financeiros | **A** |
| 138 | Resumo matinal usa a chave do usuário | **A** |
| 139 | Check-in confere o envio e limpa a sessão em caso de falha | **A** |

## WhatsApp (012)

| # | O que muda | Rec. |
|---|---|---|
| 150 | Confirmar despesa/renda sem agendamento deixa de falhar (gravava `descricao` nula) | **A** (fluxo central do bot; confirme antes `transacoes.descricao` é `NOT NULL`?) |
| 151 | Transferência por WhatsApp deixa de falhar sempre | **A** |
| 152 | Configurar Notificações pelo ramo do Gemini deixa de gravar e responder 500 | **A** |
| 153 | Solicitar API Key mostra a chave em claro, não o valor cifrado | **A** |
| 154 | Extração de despesa e renda passa a pedir data, conta, categoria e parcelamento | **D** (funcionalidade nova, não correção) |
| 155 | Evento pendente "mais recente" deixa de depender da ordem do `KEYS` | **A** |
| 156 | Tratar a resposta casa/trabalho/outro da pergunta de origem da rota | **A** |
| 157 | "Pagamento Fatura" passa a ser alcançável com "paguei..." | **D** (muda o roteamento do prompt; testar com uso real) |
| 158 | Análise e previsão: sem valores negativos, desempate fixo, sem `KeyError` sem histórico | **A** |
| 159 | Upload ao Drive mostra o motivo da falha | **A** |
| 160 | "Quitar Conta Fixa" está no prompt mas não tem ramo | **D** |
| 161 | Vencimentos filtram a descrição pelo usuário | **A** (isolamento entre usuários) |
| 170 | Feedback do pote soma todas as subcategorias do pote | **A** |
| 190 | "Desativar"/"desligar" resumo matinal ou check-in deixa de ativar | **A** |
| 191 | Configurar Notificações (Gemini, `contas_vencer`) deixa de chamar método inexistente | **A** |
| 192 | Deletar Evento: `quando` diferente de `hoje` deixa de virar "amanhã" | **A** |
| 210 | Resposta ao check-in: "assim"/"joaquim" deixam de confirmar tudo | **A** |
| 230 | Gráfico de Gastos: validar tipo e faixa do período | **A** |
| 231 | Gráficos: saldo da linha considera histórico anterior; filtros de `consolidada` consistentes | **A** |
| 232 | Comparação Mensal: totais positivos, variação correta, números em pt-BR | **A** |
| 233 | Análise Inteligente: "Maiores gastos" lista os maiores, dias da semana em português | **A** |

## Resumo

- **A (aprovar): 40** — 090–101, 120–122, 130–134, 136–139, 150–153, 155–156, 158–159, 161, 170, 190–192, 210, 230–233, mais o 135 se você concordar.
- **R:** 089.
- **D (adiar):** 102, 154, 157, 160.
- **Fora da lista, ainda em aberto:** P-007-1 (`daysBeforeDue` gravado em `alerta_potes_threshold`). Seu `alerta_potes_threshold` é **1**, o que indica o bug em ação. Recomendo **reabrir e aprovar**: guardar `daysBeforeDue` em coluna própria. Diga se quer.

## Perguntas que só você responde

1. `transacoes.descricao` é `NOT NULL`? (`SELECT is_nullable FROM information_schema.columns WHERE table_name='transacoes' AND column_name='descricao';`) Define a urgência do 150.
2. Qual a versão do Python em produção? (A 011 e a 012 assumem 3.11.)
3. Quem chama `POST /api/fatura/{id}/reprocessar`? O atalho do iPhone envia `valor_pago` como número ou como texto?
4. O motivo "usuário sem Google conectado" pode aparecer na mensagem de criar evento (Q-012-7)?
5. Nenhum atalho do iPhone/Android chama as rotas `/calendar-alerts` sem JWT? (Passam a receber 401.)
