# Retenção GHCR: primeira versão somente em dry run

## Escopo e análise do deploy existente

O workflow `cleanup-ghcr.yml` consulta Azure e GHCR e publica propostas de
retenção. **Não existe implementação de exclusão.** `execute_delete=true` falha
antes do login Azure e das consultas. O token recebe `packages: read`, nunca
`packages: write`; nenhum PAT é necessário.

A análise de `publish-container.yml`, `rollback-production.yml`,
`Enforce-ProductionRevisionPolicy.sh` e do runbook Blue/Green identificou:

- A API publica `ghcr.io/hemodinks/hemodinks-api:sha-<commit>`.
- CURRENT é descoberto em `properties.configuration.ingress.traffic`: label
  `blue` **ou** `green` com peso 100. PREVIOUS é a revisão distinta do outro
  label, com peso 0. Ambas devem existir; somente CURRENT precisa estar ativa.
  A imagem de PREVIOUS continua protegida mesmo com `active=false`. As cores alternam;
  nomes, ordem e data de criação das revisões não definem seus papéis.
- Todas as outras revisões ativas da API e a imagem do template do Container
  App também são protegidas, inclusive candidatas ainda com zero tráfego.
- Os workers publicam `hemodinks-api-workers`, mas podem ser implantados por
  atualização direta de Container App ou por `Azure/functions-action`, que
  recebe um pacote produzido por `dotnet publish`. Não há CURRENT/PREVIOUS
  de workers no workflow existente.

Nenhum workflow, script de deploy/rollback, migration ou teste da aplicação
existente foi alterado.

## Política

| Situação | Decisão |
| --- | --- |
| Imagem de CURRENT, PREVIOUS, outra revisão ativa da API ou template | `PROTECTED` |
| Dependência OCI identificada de imagem protegida | `PROTECTED` |
| Dez versões com `sha-*` mais recentes por `created_at` | `KEEP` |
| Versão com qualquer tag fora de `sha-*`, mesmo com outra tag SHA | `KEEP` |
| Dependência identificada de qualquer versão mantida | `KEEP` |
| Versão somente com tags `sha-*`, fora das dez e sem proteção/dependência conhecida | `DELETE_CANDIDATE` |
| Untagged com até 14 dias, inclusive | `KEEP` |
| Untagged com mais de 14 dias sem prova de independência | `SKIPPED_UNSAFE_TO_DELETE` |

São dez **versões**, não dez tags; IDs desempatarão datas iguais. Proteções Azure
são adicionais às dez e nunca expiram por idade. Datas usam UTC. `latest` não é
aceito como referência de produção.

Nos workers, quando nome e Resource Group do Container App estão configurados,
preservam-se **todas as revisões existentes, inclusive inativas**, além do
template. Essa escolha conservadora evita inventar um rollback inexistente.
As demais versões seguem a mesma retenção. O recurso é consultado mesmo se
`AZURE_CONTAINER_APPS_DEPLOY_ENABLED` estiver desligado: isso não prova que um
deploy anterior deixou de executar. Sem nome/RG suficientes, todas as versões
não protegidas dos workers recebem `SKIPPED_UNSAFE_TO_DELETE`. A presença de um
Function App de código não prova que outros consumidores GHCR não existem.

## Manifests, provenance e untagged

Buildx/build-push podem publicar índices OCI e manifests adicionais de
attestation/provenance. A listagem REST de versões não expõe o grafo completo.
O script consulta também a API de manifests do registry, com token de escopo
`pull` derivado do `GITHUB_TOKEN`, e verifica o SHA-256 do conteúdo.

Ele percorre índices OCI/Docker, manifests aninhados, `subject` e a referência
`vnd.docker.reference.digest` do BuildKit. Os pontos de partida incluem imagens
protegidas e **todas** as versões mantidas, inclusive untagged e tags fora de
SHA. Dependências que também tenham uma tag SHA antiga deixam de ser candidatas.
Layers/configs são blobs, não versões a excluir nesta política.

Não se afirma que isso prova a ausência de todo referrer, metadata ou formato
de artifact possível. **Nenhuma untagged é candidata nesta versão**, mesmo
depois de 14 dias. Formato desconhecido, manifest inacessível, digest divergente
ou relação incompleta bloqueiam todas as candidatas daquele pacote com warning.

Referências: [attestations do Docker](https://docs.docker.com/build/metadata/attestations/),
[índice OCI](https://github.com/opencontainers/image-spec/blob/main/image-index.md),
[API de versões do GitHub Packages](https://docs.github.com/en/rest/packages/packages),
[autenticação GHCR em Actions](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry#authenticating-in-a-github-actions-workflow).

## Autenticação e execução

O job usa `environment: production` e `azure/login@v2`, com os mesmos secrets
OIDC `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` e `AZURE_SUBSCRIPTION_ID` do publish.
`id-token: write` permite esse login; `contents: read` permite checkout.
O script executa somente consultas Azure. A identidade existente precisa ler
os Container Apps e suas revisões; ela pode já ter privilégios maiores por
ser compartilhada com o deploy.

Variáveis de workers reutilizadas:

- `AZURE_CONTAINER_APP_FUNCTIONS_NAME`;
- `AZURE_CONTAINER_APP_FUNCTIONS_RESOURCE_GROUP`, com fallback para `AZURE_RESOURCE_GROUP`;
- `AZURE_FUNCTION_APP_WORKERS_NAME`, apenas para contextualizar o relatório.

O repositório precisa de acesso de leitura a ambos os packages via GitHub
Actions. Se houver erro 403/404, revisar o vínculo/permissões dos packages;
não substituir por PAT nem interpretar erro como inventário vazio.

Após disponibilizar os arquivos na branch padrão:

1. Abrir **Actions → GHCR Cleanup Dry Run → Run workflow**.
2. Manter `execute_delete` desmarcado (`false`).
3. Aprovar o Environment `production` caso as regras existentes exijam.
4. Consultar Summary, logs e o artifact `ghcr-cleanup-dry-run-<run_id>`.

Também há agendamento às segundas-feiras, **06:23 UTC / 03:23 America/Sao_Paulo**.
O agendamento executa a branch padrão e pode aguardar a aprovação do Environment;
este trabalho não altera essas regras. Via CLI:

```bash
gh workflow run cleanup-ghcr.yml --ref main -f execute_delete=false
```

## Relatório e falhas

Cada versão registra status, ID, digest, tags, `created_at` e motivo. O Summary
limita a exibição a 100 linhas por categoria/pacote para evitar o limite de
tamanho; **logs e artifact JSON contêm todas as versões**. A paginação REST usa
100 versões por página e continua até a última página, sem limitar o inventário
a 100. IDs/digests/tags duplicados ou metadata incompleta invalidam a leitura.

Exemplo ilustrativo, sem consulta real à produção:

```text
GHCR Cleanup Dry Run
Package: hemodinks-api
PROTECTED                 id=501 digest=sha256:… tags=[sha-…] reason=CURRENT (green, 100%)
PROTECTED                 id=487 digest=sha256:… tags=[sha-…] reason=PREVIOUS (blue, 0%)
KEEP                      id=520 digest=sha256:… tags=[sha-…] reason=10 newest SHA versions
DELETE_CANDIDATE          id=430 digest=sha256:… tags=[sha-…] reason=outside newest 10
SKIPPED_UNSAFE_TO_DELETE   id=390 digest=sha256:… tags=[]      reason=old untagged, unproven relationships
Deleted: 0
```

Falha de autenticação, consulta Azure/GHCR, CURRENT/PREVIOUS ausente/ambíguo, CURRENT inativa,
tag de produção ausente ou digest divergente: job termina com erro, sem publicar
candidatas parciais, e o Summary registra o bloqueio com `Deleted: 0`. Uma etapa
final `always()` também relata falhas anteriores ao script, como login Azure.
Indisponibilidade de relações de manifests resulta em warning e descarte das
propostas daquele pacote. Workers sem inventário configurado também geram warning.
PREVIOUS existente mas inativa não bloqueia a auditoria: sua imagem permanece
`PROTECTED`, e o Summary registra `PREVIOUS revision exists but is inactive;
GHCR image remains protected.` Nenhuma revisão é ativada e nenhum tráfego é alterado.

O inventário Azure e as versões GHCR são relidos antes de publicar o relatório.
Mudanças durante a auditoria invalidam as propostas e pedem nova execução. Um
lock próprio evita sobrepor auditorias sem deslocar deploys pendentes no grupo
de concorrência da produção. Isso **não é um lock de exclusão real** nem uma
transação entre Azure e GHCR. O relatório é uma observação, não autorização
duradoura para executar suas candidatas.

## Validação local

Sem rede, secrets, Azure ou alterações na suíte .NET:

```bash
python -B -m unittest discover -s scripts/tests -p test_ghcr_cleanup.py -v
```

Os testes cobrem alternância blue/green, revisões extras, PREVIOUS ativa ou inativa
com ambas as imagens protegidas, CURRENT inativa, ausência/ambiguidade dos papéis,
múltiplas revisões com peso 100, indisponibilidade Azure, retenção, versões com múltiplas tags,
limiar de 14 dias, 101/200/205 versões, metadata inválida, tags divergentes,
manifests aninhados/attestations/subjects, concorrência, relatórios de falha e
bloqueio de `execute_delete=true` antes de qualquer consulta. Com os mesmos
inventários e instante de referência, as decisões são determinísticas;
reexecuções não alteram nenhum recurso remoto.

## Antes de uma futura implementação de exclusão real

Alterar apenas o input **não habilita exclusão**. Uma nova implementação deverá:

1. Confirmar os consumidores reais de ambos os packages, inclusive ambientes
   fora da produção e revisões antigas usadas em recuperação manual.
2. Definir a retenção de rollback dos workers e provar a independência de
   manifests/referrers antes de considerar excluir untagged; manter os casos
   desconhecidos bloqueados.
3. Coordenar exclusões com publish/deploy/rollback e operações manuais; reler
   Azure, tags, digests e dependências imediatamente antes de cada operação.
4. Implementar e testar explicitamente exclusão por version ID, limites,
   confirmações, falhas parciais e auditoria; nunca executar um relatório antigo.
5. Só então revisar `packages: write` e o acesso administrativo do repositório
   aos packages, preservando `GITHUB_TOKEN` e uma identidade Azure de leitura.

Riscos ainda presentes: tags `sha-*` podem ser republicadas por reruns (o workflow
não impõe imutabilidade no registry); dois snapshots não detectam toda alteração
transitória; consumidores externos não são descobertos; metadados OCI podem
exigir retenção adicional; inventários grandes podem exceder timeout/rate limit.
Esses casos não causam exclusão nesta versão e devem ser resolvidos antes de
qualquer modo destrutivo.
