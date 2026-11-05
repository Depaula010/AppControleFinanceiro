# Desvio 269 — Contador de parcelas `x/N` no site e no bot; cadastro com parcelas já pagas

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 005, 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
- As listas e os avisos de contas mostram o parcelado só pelo nome ("Robo aspirador"); não dá para saber quantas parcelas faltam.
- O check-in noturno e "contas atrasadas" já mostravam a parcela da vez (`Tênis (2/3)`), mas nenhuma outra mensagem.
- O detalhe da fatura acrescentava o contador do agendamento à descrição da transação, que já termina em `(n/N)`: saía `Tênis (2/3) (2/3)`.
- `POST /api/bills` sempre cria o parcelado com `parcelas_executadas = 0`; não dá para cadastrar uma compra que já começou.

## Comportamento proposto (.NET)
- Regra única (`Domain/Bills/InstallmentLabel`):
  - **Listas** (site e "todas as minhas contas") mostram as **já pagas**: `Robo aspirador (4/18)`.
  - **Avisos de uma conta que vence agora** mostram a **parcela da vez**, o mesmo nome que vai para o extrato: `Robo aspirador (5/18)`. São eles: contas fixas pendentes, vence hoje/amanhã/essa semana, check-in e suas respostas, contas atrasadas, resumo da manhã, lembrete de contas, confirmação do "paguei" e da baixa por SMS, e o texto da previsão de gastos.
  - No cartão, o populador da fatura já lança a parcela antes do dia da cobrança, então o aviso usa o contador gravado.
  - Descrição que já termina em `(n/N)` não ganha outro sufixo, o que corrige o detalhe da fatura.
- `POST` e `PUT /api/bills` aceitam `parcelas_executadas` (opcional, só em `PARCELADO`, inteiro com `0 ≤ x < total_parcelas`). Fora disso a resposta é 400, `"Parcelas já pagas deve ser um número inteiro de 0 até o número de parcelas menos 1"`.
- O site mostra um selo `x/N` ao lado do nome do parcelado e ganha o campo "Parcelas já pagas" no formulário.

## Impacto nos clientes
Nenhuma rota muda. O corpo do POST/PUT aceita um campo novo e opcional. O texto das mensagens do WhatsApp muda só nas contas parceladas.

## Decisão do Rafael
Pedido na conversa de 05/11/2026: "gostaria que exibisse quantas parcelas foram pagas, exemplo 5/18". Ele escolheu as já pagas nas listas e a parcela da vez nos avisos.
