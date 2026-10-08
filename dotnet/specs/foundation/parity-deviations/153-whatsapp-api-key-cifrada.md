# Desvio 153 — Solicitar API Key devolve o valor cru da coluna

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`SolicitarApiKeyIntent` (`admin_intents.py:41-79`) devolve `usuarios.api_key_automate` como está. Para quem se cadastrou depois da correção do desvio 017 o valor é um token Fernet, que não serve como `user_api_key` (a autenticação compara a chave decifrada). A chave em texto puro legada é devolvida inteira por WhatsApp.

## Comportamento proposto (.NET)
Decifrar quando o valor parecer cifrado (`IEncryptionService.LooksEncrypted`) antes de responder.

## Impacto nos clientes
A chave mostrada passa a funcionar no atalho do iPhone.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa. Ou retirar a intenção e entregar a chave só pelo site (decisão de produto, Q-012-2).

## Recomendação
Aprovar a decifração; decidir separadamente se a chave deve trafegar por WhatsApp.

## Decisão do Rafael
(pendente)
