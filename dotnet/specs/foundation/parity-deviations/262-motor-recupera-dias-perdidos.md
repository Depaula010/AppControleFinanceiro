# Desvio 262 — Motor de agendamentos recupera o que ficou para trás

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
O motor (`schedule_processor.py`) só lança no dia exato do vencimento. Se o app estiver fora do ar nesse dia (deploy, queda, job que falhou), a conta **nunca** é lançada sozinha; só o check-in pode oferecê-la depois.

## Comportamento proposto (.NET)
Cada execução do motor reconfere os **3 últimos dias**: lista os FIXO/PARCELADO que venceram naquele dia e já existiam (`data_inicio`), e chama o mesmo executor, que é idempotente (competência, regra de "paga", índice único do desvio 258). O que já foi lançado dá `Skipped`; o que ficou para trás é lançado **na data do próprio vencimento**. Lembrete variável não entra. Se recuperar algo, registra um WARNING e **avisa o dono** (desvio 261): significa que um dia foi perdido.

## Impacto nos clientes
Nenhuma rota muda. Uma conta que o motor perdeu passa a ser lançada sozinha em até 3 dias, em vez de esperar a confirmação do check-in. Risco já existente e mantido: se o usuário lançou à mão a mesma despesa com outra descrição, o lançamento automático duplica (o aviso traz os ids para conferir).

## Alternativas
Manter como está (a conta fica sem lançar até alguém lembrar). Janela maior que 3 dias (mais chance de lançar o que o usuário já resolveu por outro caminho).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 23/10/2026 ("O motor não recupera um dia perdido … um recuperar o que ficou para trás idempotente fecha isso"). Implementado nesta branch.
