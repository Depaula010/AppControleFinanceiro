# Desvio 284 — Chave própria para o `x-api-key` do webhook (`WEBHOOK_API_KEY`)

- **Estado:** implementado (código); a troca real da chave em produção é um passo manual do Rafael
- **Features afetadas:** 009, 012
- **Severidade:** segurança (achado M11 da auditoria 2026-11-16)

## Comportamento atual
`API_SECRET_KEY` faz quatro papéis: assina o JWT, é o `x-api-key` de `/admin` e do webhook, assina o `state` do OAuth do Google e é gravada no banco do bot (`sessions.metadata.webhook_auth_token`). Quem ler o banco do bot consegue forjar JWT e chamar `/admin`.

## Comportamento proposto (.NET)
Variável **opcional** `WEBHOOK_API_KEY` (mínimo 32 caracteres, validada no boot sem ecoar o valor):
- Definida: `POST /webhook-whatsapp` aceita **só** ela no `x-api-key` (a `API_SECRET_KEY` deixa de abrir o webhook).
- Ausente ou vazia: o webhook segue aceitando a `API_SECRET_KEY`, como hoje (nada quebra em produção).
- `/admin`, JWT, OAuth e as demais rotas com `x-api-key` continuam usando só a `API_SECRET_KEY`. O HMAC (`WEBHOOK_SIGNATURE_KEY`) não muda.
- Implementação: `WebhookApiKeyValidator` (Infrastructure) injetado só no webhook; `AppSettings.WebhookApiKey`.

## Passo manual do Rafael (o código não faz isto e nenhum valor de segredo foi escrito em arquivo)
1. Gerar um valor novo e longo (>= 32 caracteres) e guardar só no cofre de senhas.
2. Criar o secret `WEBHOOK_API_KEY` no GitHub (Actions) e incluí-lo no `.env` que o `deploy.yml` escreve (hoje o workflow não conhece essa variável: acrescentar a linha `WEBHOOK_API_KEY` junto das demais).
3. Atualizar o banco do bot: `sessions.metadata.webhook_auth_token` da sessão do Meu Secretário (e do `telegram-gateway`, se ele chamar o webhook) para o mesmo valor, reiniciando a sessão para recarregar o metadata (ver `CLAUDE.md` §5).
4. Fazer o deploy; conferir `docker logs meu-secretario-api` sem "Webhook do WhatsApp sem x-api-key válida".
5. Só depois, rotacionar a `API_SECRET_KEY` (pendência §9.2), que invalida os JWT em uso.
Ordem importante: o secret novo e o banco do bot precisam trocar juntos; entre uma troca e outra o webhook responde 401.

## Impacto nos clientes
Nenhum enquanto a variável não for definida.

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026; a parte de rotação ficou explicitamente fora do código.
