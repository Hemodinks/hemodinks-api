# Validação da concorrência de sessões — 06/10/2026

## Código, commit e implantação

- Correção original: commit 53449402793981dbb56d2346652e2b8bede8e18d.
- HEAD inspecionado: ed74b4f165204040e104c0f9afbced7d693bb64a, branch developer, checkout inicialmente limpo.
- A ancestralidade da correção em HEAD foi confirmada com git merge-base; o conteúdo do middleware nesse SHA contém a resposta 503.
- Serviço confirmado no workspace autorizado GMARCONE TECH SOLUTIONS: hemodinks-api-1, srv-d8hmtje47okc738ldllg, URL https://hemodinks-api-1-90nb.onrender.com.
- Render informou deploy dep-db2jqhmi0phs738r41gg, commit ed74b4f, status live, concluído em 06/10/2026 às 18:28:05 UTC (15:28:05 Brasília).
- Publicação inicial da correção: dep-db2h740m7kps73err3sg, SHA 5344940, concluída às 15:29:59 UTC (12:29:59 Brasília), posteriormente substituída pelo deploy acima.
- A captura anterior do usuário, às 13:58:43 UTC, antecede essa publicação. Isso não constitui, sozinho, comprovação da causa de cada 401 histórico.

Fonte de implantação: https://dashboard.render.com/web/srv-d8hmtje47okc738ldllg

Os blueprints locais indicam Confirmation/developer para homologação e produção separada. O serviço consultado confirma developer e deploy automático desligado. Não foram lidos valores secretos nem alterados parâmetros do Render. Blueprint não comprova os valores efetivos de todas as variáveis do ambiente.

## Evidência operacional

Consulta filtrada aos logs após a primeira publicação retornou 18 linhas da mensagem de contenção, sem paginação pendente. Existem linhas duplicadas; o número não representa 18 incidentes distintos. A última ocorrência retornada foi às 18:38:09 UTC, depois do deploy atualmente live. No intervalo 18:38:00–18:38:20 UTC, foram encontradas duas linhas de resposta HTTP 503. A coincidência temporal é evidência operacional compatível; não estabelece correlação individual sem request ID no log antigo.

Não foram copiados logs brutos, identificadores de sessão, tokens ou dados clínicos para este documento. Nenhum conflito foi provocado no ambiente compartilhado.

## Melhorias desta revisão, apenas locais

- Respostas de rejeição da validação incluem requestId e Cache-Control: no-store. O header X-Request-ID já existe no pipeline.
- Evento estruturado SessionValidationRejected (4101) registra FailureCode, StatusCode e RequestId, sem identidade, cookie, token ou corpo da requisição.
- Conflitos continuam como session_validation_busy/503, Retry-After: 1; contexto incompatível recebe session_context_mismatch/401; demais recusas sem motivo público específico recebem session_invalid/401. Códigos existentes de expiração são preservados.
- O log sem correlação da Application foi removido desse caminho; o resultado continua sendo definido pela Application e mapeado para HTTP pela API.
- Três tentativas de persistência continuam limitadas; cada conflito exige nova leitura de segurança. Não há autorização com snapshot antigo nem cache entre requisições.

## Retry

Consultas do QueryClient já admitem no máximo uma repetição de falha transitória; 4xx não são repetidos e mutations usam retry 0. O transporte não repete 503 automaticamente.

Foi encontrada repetição adicional no transporte após 401 de token expirado, inclusive para escritas. A revisão mantém a renovação, mas só repete GET/HEAD/OPTIONS. Para escritas, retorna erro local 409/session_write_retry_required, solicitando conferir o resultado antes de tentar novamente. Um header Idempotency-Key isolado não é considerado prova de suporte do endpoint. Não foram criados retries de escrita.

## Validação e limites

Reprodução determinística usa SQL Server LocalDB com rowversion nativa, bancos descartáveis e dados sintéticos. O teste exige exatamente três conflitos, 503, ausência de Set-Cookie, endpoint protegido não executado, nenhuma revogação e posterior validação da mesma sessão. Também verifica correlação e ausência de tokens/ID de sessão no novo log. Outros cenários cobrem revogação, troca de vínculo, refresh concorrente, expiração e indisponibilidade de persistência.

Resultados desta execução ficam nos arquivos logs/session-validation-audit-*.log. Não houve commit, push ou deploy desta revisão. As novas melhorias de diagnóstico e retry não estão incluídas na evidência de deploy acima. A conferência não substitui teste de carga, auditoria completa da infraestrutura ou prova de ausência de todos os erros em produção.

## Resultados finais

- Backend: 77 testes aprovados, incluindo os seis cenários SQL Server/rowversion; nenhum caso ignorado nessa execução.
- Frontend: 59 testes aprovados (cliente HTTP, ciclo de sessão e política de retry).
- Navegador: dois E2Es finais aprovados, cobrindo falha transitória de grupos médicos sem logout e login normal. Executados com API simulada; concorrência real de banco foi validada separadamente no backend.
- Compilação do backend e build TypeScript/Vite concluídos. A compilação .NET emitiu avisos existentes de diretivas desnecessárias em arquivos fora do escopo.
- git diff --check sem erros nos dois repositórios.
