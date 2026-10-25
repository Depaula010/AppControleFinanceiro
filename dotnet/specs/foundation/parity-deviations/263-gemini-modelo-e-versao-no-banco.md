# Desvio 263 — Modelo e versão da API do Gemini ficam no banco

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 010, 012
- **Severidade:** operação

## Comportamento atual (Flask)
O modelo vinha da variável `GEMINI_MODEL` (padrão `gemini-2.5-flash-lite`) e a versão da API (`v1beta`) era uma constante no código. Trocar qualquer um exigia mexer no segredo/código e fazer deploy.

## Comportamento proposto (.NET)
Nova tabela `configuracoes (chave, valor, descricao, atualizado_em)` (migration 0100), semeada com `gemini.modelo = gemini-2.5-flash-lite` e `gemini.versao_api = v1beta`. O transporte do Gemini lê os dois valores a cada chamada por um cache de **60 s**, então uma troca por SQL vale em até 1 minuto, sem deploy:

```sql
UPDATE configuracoes SET valor = 'gemini-2.5-flash', atualizado_em = NOW() WHERE chave = 'gemini.modelo';
```

Rede de segurança: linha ausente ou valor fora do formato (letras, dígitos, `.`, `_`, `-` para o modelo; `v1`, `v1beta`… para a versão; o valor vai para a URL) cai no padrão (`GEMINI_MODEL`, depois `gemini-2.5-flash-lite`; versão `v1beta`) com um WARNING sem o valor. Se o banco falhar, mantém o último valor bom. `GEMINI_MODEL` continua valendo como padrão quando a linha não existe.

## Impacto nos clientes
Nenhuma rota muda. A chave de cache do Gemini não inclui o modelo (já era assim): após trocar o modelo, respostas em cache de até o TTL continuam saindo do modelo antigo; para valer já, `POST /admin/gemini-cache-clear`.

## Alternativas
Rota de admin para trocar (mais código e superfície; o Rafael já usa o HeidiSQL). Ler a cada chamada sem cache (uma consulta extra por mensagem).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 25/10/2026 ("quero guardar isso no banco de dados pois, quando preciso trocar essa informação tenho que ficar revirando o código").
