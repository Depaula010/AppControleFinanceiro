# Desvio 276 — "Paguei … da fatura" paga a fatura; centavos das parcelas; apagar parcela devolve o contador

- **Estado:** implementado
- **Features afetadas:** 004, 012
- **Severidade:** correção (aprova e implementa o desvio 157)

## 1. "paguei 500 da fatura do nubank" (desvio 157)
**Antes:** o passo de pagamento pegava qualquer "paguei/quitei" antes do Gemini e lançava despesa avulsa (dinheiro contado duas vezes, fatura continua aberta). O próprio menu de ajuda e a mensagem de falha do check-in ensinam essa frase.
**Agora:** texto com "fatura" não entra no passo de pagamento de conta fixa e segue para a intenção `Pagamento Fatura`.

## 2. Centavos das compras parceladas pelo WhatsApp
**Antes:** o valor chegava como total/N em `double` (100/3 = 33,3333…) e o `numeric(15,2)` arredondava: 100 em 3x gravava 99,99; 200 em 3x gravava 200,01.
**Agora:** fecha o total em centavos, arredonda a parcela e joga o resíduo na 1ª (100 em 3x = 33,34 + 33,33 + 33,33; 200 em 3x = 66,66 + 66,67 + 66,67). O agendamento guarda o valor das demais parcelas.

## 3. Apagar uma parcela lançada
**Antes:** o contador `parcelas_executadas` não voltava; o populador relançava a parcela seguinte "(4/10)" e a conta terminava uma parcela antes.
**Agora:** apagar a transação ligada a um parcelado devolve 1 ao contador e reativa o agendamento (era a última). Na madrugada o populador relança a parcela com o número certo.

## Impacto nos clientes
Nenhuma rota muda.

## Decisão do Rafael
Pedido na conversa de 16/11/2026 ("pode corrigir todos os problemas encontrados"), achados A1, M3 e M4 da auditoria.
