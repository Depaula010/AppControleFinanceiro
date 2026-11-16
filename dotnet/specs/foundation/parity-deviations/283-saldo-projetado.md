# Desvio 283 — Saldo projetado (campo novo e aditivo)

- **Estado:** implementado
- **Features afetadas:** 002, 006, 012
- **Severidade:** acréscimo (continuação do desvio 278)

## Comportamento atual
Desde o desvio 278 o saldo (`saldo_total`, `saldo` da conta, "qual meu saldo" do bot) soma só até hoje. Quem quer ver o efeito das faturas e contas do cartão que já foram lançadas para datas futuras (o populador cria as do mês seguinte quando a fatura abre) deixou de ter esse número.

## Comportamento proposto (.NET)
Campo **novo**, sem alterar nenhum existente:
- `GET /api/dashboard/summary` (e aliases): `saldo_projetado` = soma dos saldos das contas ativas sem o corte de hoje (a regra do `saldo_total` antes do 278), no fim do objeto.
- `GET /api/accounts` (e `/api/contas`), por item: `saldo_projetado` = `saldo_inicial` + soma de todas as transações consolidadas da conta, no fim do objeto.
- Bot, "qual meu saldo": quando o projetado difere do saldo de hoje (diferença de 1 centavo ou mais), a resposta ganha `_(projetado com faturas futuras: R$ Y)_` (na conta única, depois do saldo; na lista, depois do "Total Geral" com o projetado do total). Sem diferença, a mensagem é a de antes.
- Front: linha secundária menor "Projetado após faturas: R$ X" no card de saldo do dashboard e no card de saldo total da tela de Contas.

## Impacto nos clientes
Só acréscimo de campo no fim do JSON (o Angular ignora o que não conhece). Nenhuma chave existente muda de posição ou valor.

## Decisão do Rafael
Pedido em "faça todos", 16/11/2026 (era a pendência "saldo projetado" do desvio 278).
