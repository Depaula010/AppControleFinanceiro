# Desvio 031 — Validações do cadastro: WhatsApp sem dígitos, dias não inteiros e cadastro concorrente

- **Estado:** proposto
- **Features afetadas:** 001
- **Severidade:** correção

## Comportamento atual (Flask)
(a) `whatsapp: "abc"` vira `""` depois de filtrar dígitos e o `INSERT` grava um usuário com número vazio (só o primeiro passa, por causa da UNIQUE) (`auth.py:130`). (b) Dia que não é inteiro (`"abc"`, `true`, `null` dentro de objeto) lança `ValueError` e a mensagem sai com o texto interno do Python, por exemplo `Dias inválidos: invalid literal for int() with base 10: 'abc'` (`auth.py:116-127`). (c) Dois cadastros simultâneos do mesmo número: o segundo passa pela checagem prévia, falha na UNIQUE e responde 500.

## Comportamento proposto (.NET)
(a) Número sem nenhum dígito ⇒ 400 `"Campos obrigatórios: nome, whatsapp, password"` e nada é gravado. (b) Dia não inteiro ⇒ 400 `"Dias inválidos: dia_vencimento e dia_fechamento devem ser números inteiros"`; as mensagens de faixa continuam idênticas. (c) Violação de unicidade em `numero_whatsapp` ⇒ 409 `"WhatsApp já cadastrado"`.

## Impacto nos clientes
Só quem enviava dados inválidos percebe: recebe 400/409 com texto em português, em vez de gravar lixo ou de ver uma mensagem técnica do Python. O Angular valida antes de enviar.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (pequeno e seguro).

## Decisão do Rafael
(pendente)
