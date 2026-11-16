# Desvio 281 — "Gastei" lê data, parcelas e valor da parcela do texto (implementa o 154)

- **Estado:** implementado
- **Features afetadas:** 012
- **Severidade:** correção (achado M12 da auditoria 2026-11-16; implementa o desvio 154, que fica como implementado por este)

## Comportamento atual (Flask)
O prompt de extração só pede `valor_decimal` e `descricao_bruta`; `data`, `conta` e `parcelamento` só existiam se o modelo os inventasse. "Gastei 80 ontem" entrava hoje, "comprei TV 3000 em 10x" entrava à vista e, com data numa compra de cartão, o Flask passava o **texto** da data para `get_or_create_fatura` (AttributeError) e o usuário via "Ops! Algo deu errado".

## Comportamento proposto (.NET)
A leitura é feita em código (`TransactionTextHints`, Domain), sobre o texto da mensagem, e não pelo Gemini: o cache da extração é global por mensagem (uma data relativa ficaria errada no dia seguinte) e a regra fica testável. O prompt do Gemini **não muda** (os cenários de paridade continuam valendo).
- **Data** (fuso America/Sao_Paulo, pelo `IClock`): `hoje`, `ontem`, `anteontem`; `dia 10` (a ocorrência mais recente até hoje); `10/11`, `10/11/2026`, `10/11/26` (sem ano, a mais recente até hoje); `10 de novembro`; dia da semana com `na/no/da/do` ou `passada` (`na sexta`, `segunda passada`: a última antes de hoje). Data inválida (`31/02`) é ignorada. Data que o modelo invente só vale se for ISO válida.
- **Parcelas** (só despesa): `em 10x`, `10x`, `em 12 vezes`, `6 parcelas`, `parcelado em 5`. `1x` é à vista. Fora de 2 a 48 a validação existente responde "Número de parcelas inválido".
- **Valor da parcela:** `10x de 300` (ou `10 vezes de 300`) significa 10 parcelas de 300, total 3.000. Sem o "de", o valor extraído é o total (`TV 3000 em 10x` = 10 de 300). **Muda** o cenário antigo "comprei em 3x de 90" (antes 90 no total).
- **Conta citada:** a menção já era lida do texto (`AccountMention`); agora ela é procurada no texto **sem** a data e as parcelas, para "dia 10" ou "em 3 vezes" não virarem nome de conta.
- **Cartão com data:** deixa de lançar exceção; a fatura é a do ciclo da data (`InvoiceCycle`, via `InvoiceForDate`).
- **Confirmação:** quando a data não é hoje, a mensagem ganha a linha `📅 dd/MM/yyyy` (antes do cartão/conta).

## Cenários de paridade que passam a divergir
`despesa_parcelada` e `despesa_parcelada_cartao` ("em 3x de 90": 3 parcelas de 90) e `despesa_data_inventada_pelo_modelo` (linha da data), registrados em `KnownDivergences` com o campo `deviation` = 281 no JSON.

## Impacto nos clientes
Nenhuma rota muda. A pendência no Redis já tinha `data_transacao` e `num_parcelas`.

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026.
