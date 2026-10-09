# Desvio 210 — Resposta ao check-in procura "ok"/"sim"/"não" como trecho de qualquer palavra

- **Estado:** proposto
- **Features afetadas:** 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`nightly_checkin_service.py:430-480` (`parse_checkin_response`): `any(p in msg_lower for p in ['sim', 'ok', 'confirmar', 'tudo pago', 'tudo certo', 'tudo ok'])` é busca de **trecho**, não de palavra, e a confirmação vem antes do adiamento. "assim", "joaquim", "okay", "tokyo" ou "não, ok" confirmam **todas** as contas do check-in e geram as transações; "não sei" ou "ainda não" só adiam porque "não" é testado depois. O mesmo vale para os adiamentos ("nao" dentro de "ainda nao" etc.) e para o escape do check-in (`ESCAPE_KEYWORDS`, também por trecho: "valor" casa com "valorizar").

## Comportamento proposto (.NET)
Casar palavras inteiras (borda de palavra) e dar prioridade ao adiamento quando a mensagem tem "não" junto de uma confirmação ("ok não" adia). Os números continuam valendo como confirmação parcial.

## Impacto nos clientes
Nenhum campo muda; uma resposta sem relação com o check-in deixa de pagar tudo.

## Alternativas
Manter como está (paridade estrita), que é o que a 012d implementa e o harness confere ("assim" confirma tudo).

## Recomendação
Aprovar: é o único caminho em que texto ambíguo grava dinheiro sem o usuário dizer.

## Decisão do Rafael
(pendente)
