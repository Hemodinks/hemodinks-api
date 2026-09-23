# Retenção GHCR: dry run padrão e exclusão limitada da API

## Escopo e execução

O workflow cleanup-ghcr.yml continua em **DRY RUN** por padrão e no agendamento
semanal. Somente um dispatch manual com **execute_delete=true** permite DELETE,
na branch **refs/heads/main**, limitado ao package **hemodinks-api**:
até **20 tagged sha-*** antigas + **50 untagged órfãs** com mais de 14 dias,
total máximo teórico de **70 versões por execução**. Workers nunca são excluídos.
DELETE em developer ou qualquer outra branch é bloqueado antes da auditoria.

Após publicar os arquivos, abrir **Actions → GHCR Cleanup → Run workflow** e
escolher execute_delete=false (auditoria) ou true (exclusão). Seguir a aprovação
existente do Environment production, quando exigida. Via CLI:

    gh workflow run cleanup-ghcr.yml --ref main -f execute_delete=false
    gh workflow run cleanup-ghcr.yml --ref main -f execute_delete=true

O agendamento continua às segundas, 06:23 UTC, sempre em DRY RUN na branch padrão.
Pode aguardar as regras de aprovação de production. DELETE não é suportado como
execução local avulsa fora do workflow.

Nenhum comportamento de publish, deploy, rollback, migrations, tráfego,
ativação/desativação de revisões ou Function App foi alterado.

## Proteção e retenção

CURRENT/PREVIOUS são descobertos em properties.configuration.ingress.traffic de
hemodinks-api-prod, no Resource Group rg-hemodinks-prod:

- CURRENT: label blue **ou** green com peso 100, revisão existente e ativa.
- PREVIOUS: revisão distinta, no outro label, peso 0 e existente. Pode estar
  inativa; sua imagem continua PROTECTED, com aviso no Summary.
- Também são protegidas outras revisões ativas da API, o template e dependências
  OCI/BuildKit identificadas dessas imagens.

Cor, nome, ordem ou data das revisões não definem os papéis. latest não é aceito
como referência de produção. Ausência/ambiguidade, CURRENT inativa, tags de
produção ausentes ou digest divergente bloqueiam o processo.

| Situação na API | Classificação inicial |
| --- | --- |
| Imagem protegida pelo Azure ou sua dependência identificada | PROTECTED |
| Dez versões SHA mais recentes por created_at | KEEP |
| Qualquer tag fora de sha-*, mesmo junto com outra tag SHA | KEEP |
| Dependência de versão mantida | KEEP |
| Somente tags sha-*, fora das dez e sem proteção conhecida | DELETE_CANDIDATE, candidate_type=tagged |
| Untagged >14 dias, órfã comprovada e fora do grafo retido | DELETE_CANDIDATE, candidate_type=untagged |
| Untagged <=14 dias, artifact ou relacionamento não comprovado | SKIPPED_UNSAFE_TO_DELETE |

São dez versões, não dez tags; IDs desempatarão datas iguais. Proteção Azure
nunca expira por idade. Idade e ausência de tags, isoladamente, não autorizam
remoção. DRY RUN mostra candidatas das duas categorias sem executar DELETE.

Workers usam atualização direta do Container App ou deploy de código por
Azure/functions-action, sem CURRENT/PREVIOUS próprio. Quando configurado, todas
as revisões existentes do Container App (inclusive inativas) e o template são
consultados. Seus resultados são exclusivamente KEEP ou SKIPPED_UNSAFE_TO_DELETE;
motivos de proteção Azure continuam no relatório. Sem inventário configurado,
o pacote fica bloqueado. Seu endpoint nunca é aceito pela função de DELETE.

## Manifests e limite

A API REST lista versões; a API de manifests do registry fornece o grafo. O
script usa token pull derivado do GITHUB_TOKEN, verifica SHA-256 e percorre
índices OCI/Docker, manifests aninhados, subject e vnd.docker.reference.digest
do BuildKit, nas annotations de manifests e descriptors. Relações subject e
BuildKit são associadas nos dois sentidos para preservar os artifacts da imagem.
Não remove blobs de layers/configs. Formatos desconhecidos ou falhas na leitura
de manifests bloqueiam todas as candidatas.

Também consulta `/v2/hemodinks/hemodinks-api/referrers/{digest}`, sem filtro de
artifactType, segue paginação Link restrita ao mesmo endpoint e percorre os
manifests descobertos, mesmo fora do inventário REST. Uma resposta 404, falha,
paginação ambígua ou resposta incompleta **não significa lista vazia**: todas as
untagged ficam inelegíveis enquanto não for possível comprovar o grafo completo.
O script não usa ausência de uma tag de fallback como prova de orfandade.
A política tagged continua sujeita às proteções conhecidas e à revalidação.

O grafo começa em todas as versões. Suas raízes retidas incluem PROTECTED, KEEP
e todas as versões não elegíveis. Uma untagged só recebe candidate_type=untagged
quando tem mais de 14 dias, não é artifact/attestation/provenance/SBOM, não possui
nenhuma referência de entrada conhecida (inclusive de outra candidata) e está
fora da closure retida. Isso preserva manifests filhos, plataformas e relações
subject/referrer. Uma dependência de candidata tagged removida nesta execução
só poderá ser reavaliada como órfã numa próxima execução.

Para DELETE, o grafo de **todas as versões da API** é validado antes da primeira
mutação. Cada candidata é comparada à closure de todas as outras versões ainda
armazenadas, inclusive candidatas adiadas pelo limite e versões puladas. Suas
dependências não podem ser removidas.

O plano seleciona primeiro as **20 tagged mais antigas** e depois as
**50 untagged seguras mais antigas**, por created_at ascendente e ID como
desempate. São limites independentes, não um limite indiscriminado de 70.
Cada versão ainda passa pelas verificações ao vivo. Versões puladas não são
substituídas nesta execução. Excedentes aparecem em deferred_tagged_by_limit,
deferred_untagged_by_limit e deferred_by_limit e exigem nova auditoria.

## Revalidação e fail-closed

1. Coletar snapshot inicial: Azure, ingress, revisões/imagens e inventários
   paginados de ambos os packages. Classificar e resolver proteções.
2. Reconsultar o snapshot inteiro ao concluir a auditoria e antes de iniciar
   DELETE. Qualquer diferença aborta sem excluir imagens.
3. Validar todos os manifests da API. Antes de cada versão, reconsultar o
   snapshot, resolver novamente tags de produção, recalcular retenção e conferir
   dependências. Somente manifests identificados por digest usam cache; tags
   nunca reutilizam resolução anterior. Referrers são consultados novamente em
   cada iteração. Mudança no grafo aborta mesmo sem mudança nos metadados REST.
   Para untagged, confirmar tags vazias e ausência na closure de todas as outras
   versões ainda presentes, incluindo candidatas adiadas e versões puladas.
4. Reconsultar novamente o snapshot após a análise do grafo e fazer GET da
   versão por ID imediatamente antes do DELETE. No primeiro DELETE, o snapshot
   precisa ser igual ao inicial. Tags, digest, ID e criação precisam concordar.
5. Gravar checkpoint antes da requisição e após cada resultado. Revalidar os
   inventários depois da operação, antes de prosseguir.

Após iniciar as requisições de DELETE, o snapshot esperado só desconta IDs com
remoção confirmada ou ausência 404 confirmada no inventário. Nenhuma outra
mudança é aceita. Metadata alterada recebe SKIP e aborta o restante da execução.
Uma ausência por GET antes da primeira requisição impede iniciar DELETE com
snapshot divergente.

Não há transação distribuída entre Azure e GHCR: falhas depois de exclusões
confirmadas interrompem o restante, mas não desfazem exclusões anteriores.
O relatório preserva esses resultados, sem substituí-los por Deleted: 0.

| Resposta do DELETE por ID | Tratamento |
| --- | --- |
| 204 | Exclusão confirmada |
| 404 | Registrar ausência/SKIP; continuar somente com inventários legíveis e consistentes |
| 401/403 | Falhar imediatamente; nunca ignorar autorização |
| 409, 429 e demais códigos inesperados | Falhar e interromper novos DELETEs |
| Erro de rede/timeout sem resposta | Resultado desconhecido; interromper sem retry automático |

Endpoint exclusivo:

    DELETE /orgs/hemodinks/packages/container/hemodinks-api/versions/{version_id}

Não há operação para excluir o package inteiro nem exclusão por tag.

## Concorrência e autenticação

Com execute_delete=true, o workflow usa production-container-publish e
cancel-in-progress=false, como publish/deploy, rollback, migrations e tarefas
operacionais. Essas execuções não se sobrepõem. O script exige dispatch manual,
o repositório correto e o marcador desse grupo. Esses marcadores verificam o
contexto; quem fornece o lock é o GitHub Actions.

DRY RUN usa ghcr-cleanup-dry-run. Os demais workflows não foram alterados.
O modelo existente permite uma execução ativa e uma pendente; novas solicitações
podem substituir uma execução pendente. Evite enfileirar vários deploys/cleanups.

O job usa production, azure/login@v2 e os secrets OIDC existentes AZURE_CLIENT_ID,
AZURE_TENANT_ID e AZURE_SUBSCRIPTION_ID. Faz somente leituras no Azure.
Permissões GitHub: contents: read, packages: write, id-token: write. O token do
job recebe packages: write nos dois modos, mas o padrão não executa DELETE.

Não há PAT. O GITHUB_TOKEN precisa ler ambos os packages; para DELETE, o
repositório precisa de acesso administrativo ao package da API. O workflow não
concede esse acesso: erros de autorização bloqueiam a execução.

Variáveis de workers: AZURE_CONTAINER_APP_FUNCTIONS_NAME,
AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP (fallback AZURE_RESOURCE_GROUP) e
AZURE_FUNCTION_APP_WORKERS_NAME. Desligar seu deploy não prova que um recurso
anterior deixou de consumir imagens; quando configurado, ele é consultado.

## Summary e artifact

O Summary separa TAGGED e UNTAGGED: limites 20/50, candidatas, exclusões
confirmadas e remanescentes. Exibe também SKIPPED_UNSAFE_UNTAGGED, total untagged,
untagged preservadas, TOTAL DELETED, PROTECTED, KEEP e workers deletion disabled.
Removidas/puladas incluem ID, tags, digest, criação e motivo. REMAINING
DELETE_CANDIDATE conta propostas iniciais ainda não confirmadas como removidas
ou ausentes; elas precisam de nova auditoria para outra execução.

O artifact ghcr-cleanup-<run_id> contém ghcr-cleanup-report.json, incluindo:

- snapshot inicial, revalidado e imediatamente pré-delete;
- snapshot divergente, se houver, e hashes/timestamps de cada revalidação;
- todas as versões, decisões, candidatas, removidas e puladas;
- tagged_candidates, untagged_candidates, deleted_tagged_versions,
  deleted_untagged_versions e skipped_untagged_versions;
- grafo inicial de digests; cada untagged removida inclui version_id, digest,
  created_at, reason, timestamp da exclusão e evidence: ausência na closure retida,
  hash/tamanho dessa closure, hash do grafo inspecionado, referrers completos,
  ausência de referências de entrada e horário da verificação;
- tentativas com HTTP status e resultado DELETED, ALREADY_ABSENT, FAILED ou
  PENDING_OR_UNKNOWN;
- timestamps, run_id e SHA do commit do workflow.

Snapshots contêm metadata usada pela retenção, nunca tokens, env vars ou secrets
dos containers. O Summary exibe até 100 linhas por classificação/pacote; logs e
artifact preservam o inventário completo. A paginação não limita o total a 100.
O checkpoint é escrito atomicamente antes de cada DELETE. Em interrupções, o
fallback do Summary conserva sucessos confirmados e sinaliza resultados
desconhecidos, que exigem conferência no GHCR.

## Validação e riscos remanescentes

Testes sem rede/credenciais, com todas as exclusões simuladas:

    python -B -m unittest discover -s scripts/tests -p 'test_ghcr_cleanup*.py' -v

Cobrem modos, bloqueios por status/package/tags, CURRENT/PREVIOUS, PREVIOUS
inativa, alternância das cores, paginação acima de 100, dependências, limite e
ordem, snapshots alterados, reexecuções, 403/404, falhas parciais e interrupções.
Incluem orfandade >14 dias, limite exato de idade, closure de KEEP/CURRENT/PREVIOUS,
subject reverso, BuildKit, provenance/SBOM, referrers paginados ou indisponíveis,
mudanças de relacionamentos entre DELETEs e execução de 20 tagged + 50 untagged.

O lock não cobre alterações manuais nem workflows externos sem o mesmo grupo.
Existe uma janela entre a última leitura e a requisição HTTP: não há DELETE
condicional atômico entre Azure e GHCR. Tags SHA podem ser republicadas;
consumidores externos/homologação não são inventariados; revisões inativas da
API fora de PREVIOUS não têm retenção garantida. Inventários grandes podem
atingir timeout/rate limit, sobretudo com referrers reconsultados antes de cada
DELETE. Nesses casos a execução para sem relaxar a verificação. Consistência eventual após DELETE pode interromper
a execução conservadoramente; sucessos confirmados permanecem no artifact.
Cancelamentos podem impedir o upload final, embora o checkpoint local e logs
já produzidos sejam preservados no runner.

Referências oficiais: [GitHub Packages API](https://docs.github.com/en/rest/packages/packages),
[permissões GHCR](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry#authenticating-in-a-github-actions-workflow),
[concorrência de Actions](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency),
[attestations Docker](https://docs.docker.com/build/metadata/attestations/),
[OCI Distribution: referrers](https://github.com/opencontainers/distribution-spec/blob/main/spec.md#listing-referrers).
