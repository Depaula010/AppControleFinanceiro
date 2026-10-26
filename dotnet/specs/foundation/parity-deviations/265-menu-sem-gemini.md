# Desvio 265 — "menu" e "ajuda" respondem sem chamar o Gemini

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 012
- **Severidade:** operação

## Comportamento atual (Flask)
Toda mensagem que não cai num passo determinístico (check-in, "paguei…", confirmação de transação, rota, relatórios) vai para o Gemini classificar a intenção. "menu" e "ajuda" também: o Gemini devolve `Menu de Ajuda` e o bot responde o texto fixo. Se o Gemini estiver sem cota ou sem saldo (429), o menu não responde (26/10/2026: o bot ficou mudo, com "Estamos com instabilidade").

## Comportamento proposto (.NET)
Um passo determinístico (`HelpCommandStep`, depois do check-in e dos relatórios, antes da confirmação de transação) responde o **mesmo texto fixo** da intenção `Menu de Ajuda` quando a mensagem INTEIRA é "menu", "menu de ajuda", "ajuda", "help", "comandos", "lista de comandos" ou "o que você faz" (sem acento, sem diferença de maiúsculas, com `/`, `?`, `!` ou `.` nas pontas). Frases que só contêm a palavra ("ajuda com a fatura", "menu da fatura do inter") continuam indo ao Gemini. Sem chamada ao Gemini, sem consumo de cota e sem custo.

## Impacto nos clientes
Nenhuma rota muda. O texto do menu é o mesmo. Muda só que ele responde na hora e mesmo com o Gemini fora do ar. Quem pedia "me ajuda" ou "socorro" continua passando pelo Gemini.

## Alternativas
Manter tudo no Gemini (o menu cai junto com ele). Casar a palavra em qualquer parte da frase (captura pedidos de ajuda específicos, como "ajuda com a fatura", que precisam do Gemini).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 26/10/2026 ("Sim coloque tudo lá"), depois de o bot ficar mudo por falta de saldo no Gemini.
