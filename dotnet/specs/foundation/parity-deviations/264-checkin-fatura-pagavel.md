# Desvio 264 — No check-in noturno a fatura de cartão é um item pagável

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
O check-in lista "FATURAS VENCIDAS" e "FATURAS QUE VENCEM HOJE" só como informação: sem número, fora da sessão do Redis e sem "COMO RESPONDER" quando só há faturas. Responder "ok" cai em "Não há itens recentes para confirmar" (25/10/2026: "Cartão Inter - R$ 405,73 - Venceu em 20/10/2026"). A fatura segue "vencida" toda noite, por até 30 dias, até o pagamento ser registrado por outro caminho.

## Comportamento proposto (.NET)
- No check-in (com ID) cada fatura vencida e cada fatura que vence hoje vira **item numerado**, depois das contas (`1. Cartão Inter - R$ 405,73 - Venceu em 20/10/2026`), e a mensagem traz o "COMO RESPONDER". No modo leitura ("pendências") o texto do Flask não muda (marcador `•`).
- A sessão ganha o campo `invoices` (índices depois dos de `items`; `total_items` conta os dois). O Flask ignora o campo extra, então o rollback continua valendo.
- "ok" (ou o número da fatura) leva o bot a perguntar **com qual conta o pagamento foi feito** (contas ativas que não são cartão, numeradas; com uma só conta basta "ok"; "pular" deixa a fatura em aberto). Depois das contas e dos valores dos lembretes variáveis, uma fatura por vez, com contador.
- Pagar usa a mesma porta do "paguei a fatura do ..." (`PostInvoicePayment`): par `Pagamento Fatura`, fatura `Paga` (R5, desvio 010: paga a `Fechada` mais antiga do cartão). O valor é o total da fatura mostrado na mensagem. Se a porta recusar, o bot diz como registrar à mão.

## Impacto nos clientes
Nenhuma rota muda. Texto do check-in: faturas numeradas e instruções de resposta; o "ok" passa a significar "tudo pago" também para as faturas listadas, então ele pergunta a conta antes de registrar. Quem responde só os números das contas não toca nas faturas.

## Alternativas
Só corrigir o texto e manter a fatura informativa (menor, mas o usuário continua registrando à mão). Marcar `Paga` sem transação (desvia a R5 e deixa o saldo errado).

## Recomendação
Aprovar.

## Decisão do Rafael
Escolhida em 25/10/2026 ("1" na conversa: a fatura vira item numerado do check-in, o "ok" pergunta a conta e registra o pagamento).
