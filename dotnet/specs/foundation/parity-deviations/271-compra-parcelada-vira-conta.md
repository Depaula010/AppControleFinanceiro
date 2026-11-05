# Desvio 271 — Compra parcelada vira conta parcelada até a última parcela

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 004, 005, 011, 013
- **Severidade:** correção

## Comportamento atual (Flask)
- Uma compra parcelada que chega como despesa simples, com `"LOJA (n/N)"` na descrição (SMS, atalho, automate, extrato do banco), fica só como transação. Ela não aparece em Contas Mensais, e as próximas parcelas não são previstas.
  - Exemplo em produção: `ZP ROGGI 62899 (1/6)`, R$ 511,00 no Cartão Inter.
- A compra parcelada do WhatsApp ("comprei em 3x") cria o parcelado, mas deixa a 1ª parcela sem `agendamento_id` e com a descrição sem `(1/N)` (AC-004-130). A idempotência do populador da fatura é "a fatura já tem transação deste agendamento", então naquela madrugada ele lançava a `(2/N)` na mesma fatura.

## Comportamento proposto (.NET)
- **Gancho no lançamento** (`TransactionPosting.PostSimpleAsync`, na mesma transação do banco). Vale para despesa sem agendamento cuja descrição termina em `(n/N)`, com `2 ≤ N ≤ 99` e `1 ≤ n ≤ N`; aceita um `(Conta)` depois, como no SMS (`InstallmentLabel.Parse`). Ele procura um parcelado ativo do usuário com a mesma conta, a mesma descrição-base (sem o sufixo, sem diferença de maiúsculas) e o mesmo total:
  - **Não existe** e `n < N`: cria o parcelado com valor = parcela, conta e subcategoria da transação, dia = dia da parcela, `data_inicio` = um mês depois e `parcelas_executadas = n`, e liga a transação.
  - **Existe** e `n > executadas`: grava `executadas = n` (desativa se `n = N`) e liga a transação.
  - **Existe** e `n ≤ executadas`: nada muda. É uma parcela que o agendamento já contou, provavelmente uma repetição.
  - `n = N` sem parcelado: nada.
- **Compra parcelada do WhatsApp:** a 1ª parcela sai como `X (1/N)` e ligada ao parcelado criado.
- **Carga inicial** (migration `0120_parcelados_em_aberto.sql`): cada compra com `(n/N)` sem agendamento que ainda tem parcela vira parcelado pelas mesmas regras. O contador é calculado assim:
  - no cartão, as parcelas cujas faturas já fecharam;
  - nas outras contas, as parcelas com data até hoje.

  A compra que acabou antes (a mesma origem, como o extrato importado, trouxe lançamentos depois da data da próxima parcela, e ela não veio) fica de fora, e a última parcela lançada é ligada ao parcelado. Em 05/11/2026, em produção, isso cria:
  - `ZP ROGGI 62899` (2/6);
  - `SHOPEE InovarMetaisOn` (2/3).

  Em seguida o populador lança a parcela da fatura aberta.

## Impacto nos clientes
Nenhuma rota muda. Novas linhas em `agendamentos`. O nome da 1ª parcela da compra parcelada do WhatsApp ganha `(1/N)`.

## Riscos
Se um extrato importado de fora do sistema trouxer de novo uma parcela que o populador já lançou, a transação é gravada sem mexer no contador, e a fatura fica com a parcela duas vezes. Quem importa precisa pular as transações já ligadas a agendamento.

## Decisão do Rafael
Pedido na conversa de 05/11/2026: "um pagamento parcelado [...] deve aparecer nas contas mensais até ser quitada" ("ROGGI foi só um exemplo").
