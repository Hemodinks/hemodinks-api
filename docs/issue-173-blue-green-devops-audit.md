# Issue #173 — blue/green, rollback e observabilidade

Auditoria em 21–22/10/2026, base local `3b873c7`. Complexidade alta, risco alto:
workflows com acesso a migrations, secrets e tráfego de produção. Escopo das
correções: aprovação por Environment, regressão offline e documentação operacional.
Nenhuma alteração em aplicação, tenant, banco, Azure ou configurações remotas.

AGENTS.md e `.codex/config.toml` lidos integralmente. DevOps (medium) concluiu
revisão independente dos workflows; Security (high) forneceu achados parciais,
mas encerrou por limite de uso antes de concluir seus testes. O principal
executou a validação final. Modelo/esforço efetivos não foram expostos pelas
ferramentas; os esforços acima correspondem às configurações dos papéis.

## Dependências e limite da execução

Execução solicitada explicitamente. As auditorias #170/#171/#172 têm relatórios
locais; isso não comprova encerramento da sequência global 01–07/08. A antecipação
fica limitada à verificação dos gates necessária para um futuro rollout seguro,
sem liberar deploy nem declarar concluídas as auditorias predecessoras.

Não se repetiu implementação de [#151](https://github.com/Hemodinks/hemodinks-api/issues/151)
(warm-up), [#153](https://github.com/Hemodinks/hemodinks-api/issues/153)
(latência em homologação), [#165](https://github.com/Hemodinks/hemodinks-api/issues/165)
(retenção GHCR) ou [#166](https://github.com/Hemodinks/hemodinks-api/issues/166)
(auditoria semanal). Na consulta de 21/10, #151/#153/#165 estavam fechadas e #166
aberta. Retenção/exclusão de imagens não foi alterada.

## Matriz de gates e evidências

Referências de linha relativas à versão final desta alteração.

| Gate | Evidência | Resultado / limite |
| --- | --- | --- |
| Restore, build, testes e migrations antes da publicação | `.github/workflows/publish-container.yml:36`, `:100` | Falhas bloqueiam dependentes; testes offline novos também executam no CI de PR. |
| SHA e bundle do mesmo commit | `publish-container.yml:32`, `:144`, `:178`, `:247` | Artifact por SHA; tags `sha-*` não são garantia de digest imutável. |
| Aprovação e OIDC | `publish-container.yml:218`, `:299`, `:751`, `:783`; `rollback-production.yml:33` | Corrigidos os três jobs de deploy que não declaravam `production`. Todos passam a usar secrets/regras e contexto OIDC desse Environment. |
| Isolamento de operações concorrentes | `publish-container.yml:23`; `rollback-production.yml:19`; `apply-production-migrations.yml:18` | Mesmo grupo, sem cancelamento de operação em curso. |
| Proxy antes de migration e promoção | `publish-container.yml:236`, `:627`; `scripts/forwarded_headers.py:11`, `:49`, `:72` | Preflight de ingress, um salto, rejeição de confiança universal e validação da candidata. |
| Migration antes da candidata | `publish-container.yml:262`, `:296` | Bundle em um job com retries; erro impede deploy. Não há reversão automática de schema. |
| Candidata sem tráfego, smoke antes de promoção | `publish-container.yml:366`, `:576`, `:617` | Estado da revisão, SHA declarado, `/livez` e `/healthz`; promoção exige confirmação do estado observado. |
| Readiness versus liveness | `HemodinksAPI.Api/ApiApplicationExtensions.cs:11`; `ApiApplicationExtensions.Health.cs:7`; `DatabaseHealthCheck.cs:33` | Resposta pública mínima; readiness consulta banco. Com schema gerido pelo deploy, não consulta migrations pendentes. |
| Warm-up sem sessão/tenant | `HemodinksAPI.Api/WarmupEndpointExtensions.cs`; `HemodinksAPI.Tests/WarmupEndpointTests.cs` | Fluxo existente preservado; sucesso, falha e timeout revalidados. |
| Rollback antes de trocar tráfego | `rollback-production.yml:101`, `:124`, `:156` | Alvo ativo/Healthy, smoke, confirmação de 100% e tentativa de restauração se não confirmado. |
| Cleanup após confirmação | `scripts/Enforce-ProductionRevisionPolicy.sh:15`, `:59`, `:128` | Preserva CURRENT/PREVIOUS; bloqueia desativação com tráfego. Somente sintaxe revisada nesta execução. |

Consulta **somente leitura** pela API GitHub: `production` tem required reviewer,
política de branch permitindo apenas `main` e `prevent_self_review=false`.
Portanto a recomendação do runbook de impedir autoaprovação não está aplicada.
Adicionar `environment` altera o subject OIDC dos três jobs; o subject de
Environment já é usado no job de migration e rollback, mas a configuração real
da credencial federada e o RBAC Azure não foram inspecionados.

## CI e histórico curl

- [Run 39620481622](https://github.com/Hemodinks/hemodinks-api/actions/runs/39620481622)
  (`dc30fef`, 21/10): migration e deploy API concluídos; job API de 15:17:22 a
  15:20:01 UTC (**159 s**). Evidência de uma execução anterior, não desta alteração.
- [Run 38276052552](https://github.com/Hemodinks/hemodinks-api/actions/runs/38276052552)
  (12/10): testes falharam; migration/deploy ficaram skipped.
- [Run 37986376629](https://github.com/Hemodinks/hemodinks-api/actions/runs/37986376629)
  (09/10): preflight bloqueou allowlist de ingress ausente/inválida, antes da
  migration; não foi uma falha curl.
- Logs dos jobs API dos runs `37852175619`, `37859452871`, `37983341611`,
  `37994502330` e `38279386771` foram consultados: quatro skipped, um success;
  nenhum erro executado `curl: (N)` encontrado. Comandos/erros impressos pelo
  runner antes da execução não foram contados como falhas reais. Essa amostra
  não comprova ausência de falhas no histórico completo. A consulta ao workflow
  de rollback não retornou runs: não há ensaio remoto comprovado nesta auditoria.

## Métricas e coleta pendente

| Medida | Evidência disponível | Limitação |
| --- | --- | --- |
| p95/p99 de `/livez`, `/readyz`, `/api/warmup` em produção | Não medidos; **n=0** nesta execução | Sem amostras/consulta ao backend de métricas, não calcular percentis nem inferi-los da duração do CI. |
| Cold start Azure e primeiro banco pronto | Não medidos nesta execução | `StartupDiagnostics.cs:12` registra etapa/duração; não há histograma dedicado de cold start em `Hemodinks.ServiceDefaults/Extensions.cs:70`. |
| Duração de deploy API | 159 s, uma execução CI citada acima | Inclui provisionamento/comandos; não representa latência de requisição ou cold start. |
| Histórico Render | HTTP 67,5 s / 7,8 s; banco 14,9 s em `docs/homologation-login-warmup.md:5` | Amostras históricas distintas, sem comparação controlada; assunto da #153. |

Instrumentação HTTP/runtime e exportação OTLP já existem em
`Hemodinks.ServiceDefaults/Extensions.cs:70`. Para completar a medição em ambiente
isolado: separar amostra aquecida por endpoint/status/revisão (ao menos 100
requisições sequenciais) de retomadas após scale-to-zero confirmado; registrar
n, intervalo e erros. Calcular nearest-rank `p95=x[ceil(.95*n)]` e
`p99=x[ceil(.99*n)]` com índice iniciando em 1. Cold start deve registrar tempo
até HTTP pronto e primeiro SQL pronto, sem payloads, tenant ou credenciais.
Não chamar labels inativos de produção para medir: isso pode acordar réplicas.

## Ensaio de rollback em ambiente seguro

Executado offline: `python -B -m unittest scripts.tests.test_production_deployment_gates -v`.
O teste extrai o bloco real de smoke do workflow e substitui `az`, `curl` e
`sleep` por funções locais. Cobriu sucesso, HTTP 503, timeout curl 28, somente
liveness saudável, somente readiness saudável e falha ao aplicar label.
Falhas não produzem `result=passed`; indisponibilidade termina após dez tentativas.
Não testa a API Azure nem a convergência de tráfego.

Plano operacional pendente: usar app/Environment/SQL sintéticos separados, sem
secrets, tráfego ou dados de produção; adaptar os alvos fixos somente numa cópia
de ensaio. Preparar CURRENT/PREVIOUS compatíveis, promover candidata saudável,
repetir com readiness 503, depois simular falha/estado desconhecido na troca e
na restauração. Exigir CURRENT=100% antes de cleanup e preservação de PREVIOUS;
falha de confirmação deve falhar o job e impedir cleanup. Testar também
revision inválida, PREVIOUS inativa e terceira revisão com tráfego. Rollback
de tráfego nunca desfaz migrations; ensaiar roll-forward/PITR separadamente.

## Riscos priorizados e resultado

1. **Alto, corrigido localmente:** falta de Environment nos três deploys.
   Alteração ainda não executada no GitHub; compatibilidade OIDC deve ser
   confirmada antes de rollout.
2. **Alto, pendente:** Environment permite autoaprovação; configuração remota
   preservada conforme escopo de auditoria.
3. **Alto, pendente:** bundle de publish não exige confirmação PITR nem as
   verificações SchemaGuard pré/pós do workflow manual (`apply-production-migrations.yml:10`,
   `:100`, `:144`). A auditoria de migrations existente sinaliza histórico
   destrutivo sem bloquear por padrão. Revisar compatibilidade expand/contract
   antes de qualquer rollback; não há prova de segurança de uma migration específica aqui.
4. **Moderado, pendente:** reutilização de candidata por SHA abreviado e
   conferência do SHA declarado não verificam o digest real (`publish-container.yml:403`,
   `:503`, `:594`). GHCR/OIDC/RBAC efetivos e elegibilidade das imagens retidas
   não foram validados no Azure; retenção continua sob #165/#166.
5. **Moderado, pendente:** faltam p95/p99/cold start atuais e ensaio completo de
   rollback/convergência. Health anônimo consulta SQL por chamada; proteção
   efetiva contra carga no ingress não foi verificada.
6. Build emitiu avisos NU1902/NU1903 preexistentes para
   `System.Security.Cryptography.Pkcs` 9.0.4; atualização fora do escopo.

Validação final: **6 testes Python novos aprovados**, **11 testes de proxy
Python aprovados**, script PowerShell de bootstrap aprovado e **39 testes .NET
aprovados, zero ignorados/falhas** (readiness, warm-up, cold-start e proxy).
TRX: `logs/issue173/issue173-health-proxy.trx` (local, ignorado pelo Git).
Parsing YAML de quatro workflows e `bash -n` de 45 blocos e da política de
revisões aprovados. `git diff --check` aprovado.

Status: auditoria local e correção pontual concluídas; validação operacional e
métricas acima permanecem pendentes. Nenhuma suíte global, E2E em Azure, deploy,
commit, PR, merge ou alteração remota foi executado.
