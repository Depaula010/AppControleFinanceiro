# Desvio 159 — Upload ao Drive: o motivo específico da falha nunca é mostrado

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`GoogleDriveService.upload_file` devolve `{"success": False, "error": "<motivo>"}` (cota, permissão, credenciais), mas `UploadDriveIntent.format_response` lê a chave `message` (`drive_intents.py:331-333`): a falha sempre vira `❌ Erro ao enviar arquivo para o Drive.`. O usuário nunca vê "Limite de armazenamento do Google Drive atingido." ou o pedido de reconexão.

## Comportamento proposto (.NET)
Mostrar `❌ {erro}` com os motivos fixos (sem `str(e)`).

## Impacto nos clientes
O usuário sabe se precisa reconectar ou liberar espaço.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
