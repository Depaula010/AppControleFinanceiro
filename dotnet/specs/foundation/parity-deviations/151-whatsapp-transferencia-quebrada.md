# Desvio 151 — Transferência por WhatsApp falha sempre

- **Estado:** implementado (feature 012, 2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`extract_transfer_params` (`gemini_service.py:464-498`) lê `c["id"]`, `c["nome_conta"]` de linhas do SQLAlchemy 2.0.30 (`Row` não aceita chave de texto): `TypeError` para todo usuário com ao menos uma conta, capturado por `BaseIntent.handle` ⇒ `❌ Ops! Algo deu errado. Por favor, tente novamente.`, sem chamar o Gemini. Mesmo corrigido, `TransferenciaIntent.execute` chama `create_pending_transaction(usuario_id=..., tipo=...)` (assinatura inexistente: o serviço recebe `(numero_whatsapp, transacao_data)`) e `format_response` indexa linhas por texto. A mensagem de saldo insuficiente e a de confirmação nunca são alcançadas.

## Comportamento proposto (.NET)
Implementar a transferência: contas por nome (ativas), saldo suficiente, pendência de confirmação no Redis e, no `OK`, `ITransactionPosting.PostTransferAsync`.

## Impacto nos clientes
"Transferir 100 da carteira para o banco" passa a funcionar (hoje responde erro).

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar se a transferência por WhatsApp for desejada (Q-012-5); exige spec adicional da confirmação.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
