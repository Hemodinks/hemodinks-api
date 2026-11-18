# Infraestrutura como código (Bicep) — issue #288

`infra/` descreve em Bicep a infraestrutura Azure de produção e homologação que já existe.
Fase atual: **somente leitura**. O template é comparado com o Azure (`what-if`) em PR e toda
semana; nada é aplicado por workflow. A aplicação controlada é a fase 3 do issue e ainda não existe.

## O que está coberto

| Módulo | Resource group | Recursos |
| --- | --- | --- |
| `modules/sql.bicep` | `rg-hemodinks-prod` | Servidor Azure SQL, admin Entra, regras de firewall de serviço (`AllowAllWindowsAzureIps`, Render), bancos `HemodinksDB` e `HemodinksDBConfirmation` (serverless, oferta gratuita), PITR de 7 dias |
| `modules/container-environment.bicep` | `hemodinks-containers-prod` | Ambiente do Container Apps (Consumption) |
| `modules/storage.bicep` | `hemodinks-workers-production_group` | Storage account de produção, retenção de exclusão de blobs/contêineres (30 dias) e os seis contêineres |
| `modules/function-plan.bicep` + `modules/function-app.bicep` | workers prod/homologação (plano de prod fica em `rg-hemodinks-prod`) | Planos Flex Consumption, Function Apps e Application Insights |
| `modules/static-web-app.bicep` | `rg-hemodinks-prod` | Static Web App do `hemodinks-front` |

`main.bicep` roda no escopo da assinatura e aponta cada módulo para o resource group existente.
Os deploys são sempre **incrementais**: recurso não declarado num resource group é ignorado, não apagado.
Nunca usar modo `Complete`: o servidor SQL é compartilhado com um banco de outro projeto.

## O que fica fora (e por quê)

| Fora do Bicep | Motivo | Onde é gerido |
| --- | --- | --- |
| Container App `hemodinks-api-prod` | Imagem, revisões, tráfego blue/green e variáveis mudam a cada deploy; segredos via `secretRef` impostos pelo deploy ([deployment.md](deployment.md#azure-container-apps)) | `publish-container.yml`, [runbook blue/green](production-blue-green-runbook.md) |
| App settings das Functions | Contêm connection strings e chaves | Portal / CLI (ver [deployment.md](deployment.md#workers-function-app)) |
| `config/web` das Functions (regras de IP) | Dono é o workflow da #224; dois donos gerariam drift | `functions-network-restriction.yml` |
| Regras de firewall de IP pessoal (`ClientIPAddress_*`, `QueryEditor*`) | Temporárias e dado pessoal em repo público | Portal; remover quando não forem mais usadas |
| Banco de outro projeto no mesmo servidor SQL | Não é da HemoDinks | — |
| Automation (start/stop), alertas de cota, workbook, Speech, portfólio | Fora do escopo do issue; podem entrar depois | Portal |

Para trazer o Container App para o Bicep, todas as variáveis com segredo precisam estar em `secretRef`
(o deploy impõe isso desde a correção da storage e da function key); depois imagem e tráfego entram como
parâmetros lidos do estado atual.

## Dados que não ficam no repositório

O repositório é público. Nomes de recursos expostos à internet (servidor SQL, storage account),
o login SQL e os dados do admin Entra entram por variável de ambiente lida em `infra/main.bicepparam`:

| Variável | Conteúdo |
| --- | --- |
| `AZURE_SUBSCRIPTION_ID` | Assinatura |
| `HEMODINKS_SQL_SERVER_NAME` | Nome do servidor Azure SQL |
| `HEMODINKS_SQL_ADMIN_LOGIN` | Login SQL do administrador do servidor |
| `HEMODINKS_STORAGE_ACCOUNT_NAME` | Storage account de produção |
| `HEMODINKS_SQL_ENTRA_ADMIN_LOGIN` | Login (e-mail) do admin Entra do SQL |
| `HEMODINKS_SQL_ENTRA_ADMIN_OBJECT_ID` | Object ID do admin Entra do SQL |
| `HEMODINKS_SQL_ADMIN_PASSWORD` | Opcional. Só na recriação do servidor; nunca em what-if |

Nenhum segredo é parâmetro do template, exceto a senha opcional acima (`@secure()`).

## What-if local

Requer `az` logado na assinatura com leitura. Não altera nada.

```bash
export AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)
export HEMODINKS_SQL_SERVER_NAME=<servidor-sql-producao>
export HEMODINKS_STORAGE_ACCOUNT_NAME=<storage-producao>
export HEMODINKS_SQL_ADMIN_LOGIN=$(az sql server show -g rg-hemodinks-prod -n "$HEMODINKS_SQL_SERVER_NAME" --query administratorLogin -o tsv)
export HEMODINKS_SQL_ENTRA_ADMIN_LOGIN=<login-entra>
export HEMODINKS_SQL_ENTRA_ADMIN_OBJECT_ID=<object-id-entra>

az deployment sub what-if --location brazilsouth --name infra-what-if-local \
  --validation-level ProviderNoRbac \
  --template-file infra/main.bicep --parameters infra/main.bicepparam \
  --no-pretty-print --output json > whatif.json
python3 -B scripts/infra_what_if.py whatif.json   # 0 = sem drift, 1 = drift, 2 = erro
rm whatif.json                                     # contém valores de propriedades
```

Sem `--no-pretty-print` o `az` mostra o diff legível; use só localmente.

### Ruído conhecido

O what-if acusa mudanças que um deploy não faria. `scripts/infra_what_if.py` ignora só estas,
por tipo de recurso, caminho exato e tipo de mudança (`KNOWN_NOISE`):

- Function Apps: `+ properties.siteConfig.localMySqlEnabled` e `+ properties.siteConfig.netFrameworkVersion`
  (defaults injetados pelo provedor; o GET de `sites` não os devolve).
- Static Web App: `- properties.stableInboundIP` e `- properties.trafficSplitting` (somente leitura).

Qualquer outra diferença, criação ou exclusão é drift. Nova entrada na allowlist só com a
evidência no PR (o what-if mostrando a mesma propriedade com valor idêntico ao estado real).

Validação de 2026-11-18 contra produção: 22 recursos `NoChange`, 20 `Ignore` (não declarados),
3 `Modify` só com o ruído acima; o avaliador retornou 0. Teste negativo: mudar o `autoPauseDelay`
do `HemodinksDB` de 30 para 60 numa cópia fez o avaliador retornar 1 apontando a propriedade.

## Workflow `infra-what-if.yml`

- Dispara em PR para `main`/`developer` que toque `infra/**` ou o avaliador, toda segunda (drift) e manualmente.
- Job `build`: testes do avaliador, `az bicep build` e `az bicep lint`. Não precisa de Azure.
- Job `what-if`: OIDC no Environment `infra-what-if`, `permissions` mínimas (`contents: read`, `id-token: write`).
  Imprime só tipo, nome e caminho das diferenças, nunca valores; o JSON não vira artifact.
  Sem configuração o job termina com aviso, sem falhar.
- PR de fork não roda o what-if (não recebe OIDC).
- `--validation-level ProviderNoRbac`: no modo padrão a ARM exige permissão de **escrita** em cada recurso
  do template (falha com `Microsoft.Resources/deployments/write`, `Microsoft.Sql/servers/write` etc.).
  Com esse nível a checagem de RBAC de escrita é pulada e basta leitura. Não remover.

### Setup único (feito em 2026-11-18)

Estado atual: role `HemoDinks Infra What-If`, app registration `github-actions-hemodinks-infra-what-if`
(credencial federada só para o Environment `infra-what-if`) atribuída na assinatura, e os 6 Environment
secrets abaixo. Os passos ficam como referência para recriar.

A identidade de deploy (`github-actions-hemodinks-api-prod`) tem escrita e só aceita `main` e o
Environment `production`. O what-if roda em PR, então usa uma identidade **separada e somente leitura**.

1. Role customizada na assinatura:

   ```bash
   SUB=$(az account show --query id -o tsv)
   cat > role.json <<EOF
   {
     "Name": "HemoDinks Infra What-If",
     "Description": "Leitura e what-if de deployments para o workflow infra-what-if (#288). Sem escrita e sem leitura de Container Apps.",
     "Actions": [
       "*/read",
       "Microsoft.Resources/deployments/whatIf/action",
       "Microsoft.Resources/deployments/validate/action"
     ],
     "NotActions": [
       "Microsoft.App/containerApps/read",
       "Microsoft.App/containerApps/revisions/read",
       "Microsoft.App/jobs/read"
     ],
     "AssignableScopes": ["/subscriptions/$SUB"]
   }
   EOF
   az role definition create --role-definition @role.json
   ```

   `*/read` não lista chaves de storage, app settings nem secrets (exigem ações `list*`/`POST`), mas leria
   variáveis de ambiente de Container App gravadas como `value`. Como o Container App não está no Bicep,
   a leitura de Container Apps fica em `NotActions`. Se o what-if falhar com `AuthorizationFailed`, a
   mensagem nomeia a ação que falta; acrescente só ela, e nunca uma ação `write`.

2. App registration + service principal, credencial federada e atribuição:

   ```bash
   APP_ID=$(az ad app create --display-name github-actions-hemodinks-infra-what-if --query appId -o tsv)
   az ad sp create --id "$APP_ID"
   az ad app federated-credential create --id "$APP_ID" --parameters '{
     "name": "infra-what-if",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:Hemodinks/hemodinks-api:environment:infra-what-if",
     "audiences": ["api://AzureADTokenExchange"]
   }'
   # Role recém-criada demora a propagar pelo nome; se falhar, use o ID dela em --role.
   az role assignment create --assignee "$APP_ID" --role "HemoDinks Infra What-If" --scope "/subscriptions/$SUB"
   ```

3. GitHub → Settings → Environments → `infra-what-if`: sem revisores obrigatórios (bloqueariam todo PR)
   e com política de branch "No restriction" (PRs rodam em `refs/pull/*`). Environment secrets:
   `INFRA_WHATIF_AZURE_CLIENT_ID` (o `APP_ID`), `HEMODINKS_SQL_SERVER_NAME`, `HEMODINKS_SQL_ADMIN_LOGIN`,
   `HEMODINKS_STORAGE_ACCOUNT_NAME`, `HEMODINKS_SQL_ENTRA_ADMIN_LOGIN`, `HEMODINKS_SQL_ENTRA_ADMIN_OBJECT_ID`.
   `AZURE_TENANT_ID` e `AZURE_SUBSCRIPTION_ID` já existem como secrets do repositório.

4. Rodar `Infra what-if` por `workflow_dispatch` e confirmar "Sem diferencas".

## Quando o what-if acusar drift

1. Descobrir a origem: mudança no portal/CLI ou PR que alterou `infra/`.
2. Mudança legítima feita fora do PR: atualizar `infra/` no PR seguinte até o what-if zerar.
3. Mudança indevida: reverter no Azure pelo caminho de sempre (portal/CLI/workflow do dono) e registrar.
4. Mudança que afeta SKU, capacidade, retenção ou rede muda custo ou exposição: what-if revisado no PR é obrigatório.

## Recriação de ambiente (DR)

Ordem para reconstruir a produção numa assinatura limpa. A recriação real cria recursos e tem custo:
só com autorização explícita (ver a restrição de orçamento em [issue-184](issue-184-backup-recovery.md)).
Aplicar o Bicep exige identidade com escrita; até a fase 3 isso é feito manualmente por quem tem `Owner`.

1. Criar os resource groups com os nomes de `main.bicep` (`az group create`), regiões `brazilsouth`
   e `centralus` para `rg-hemodinks-prod`.
2. Exportar as variáveis da seção "Dados que não ficam no repositório", incluindo `HEMODINKS_SQL_ADMIN_PASSWORD`
   (gerar uma senha nova; guardar no cofre de senhas, não no repositório).
3. Conferir o plano: `az deployment sub what-if ...` (como acima). Esperado: só `Create`.
4. Aplicar: `az deployment sub create --location brazilsouth --name infra-recreate --template-file infra/main.bicep --parameters infra/main.bicepparam`.
5. Banco: restaurar dados conforme [issue-184](issue-184-backup-recovery.md); papéis SQL com
   `scripts/ConfigureSqlRuntimeRole.sql`; validar com `scripts/ValidateRestoredDatabase.sql`.
6. Static Web App: vincular o GitHub (`az staticwebapp update --token`) ou republicar pelo workflow do `hemodinks-front`;
   recriar o domínio customizado.
7. Functions: recriar app settings (incluindo `DEPLOYMENT_STORAGE_CONNECTION_STRING`) e publicar o pacote
   ([deployment.md](deployment.md#workers-function-app)); restrição de origem pelo `functions-network-restriction.yml`.
8. Container App da API: `scripts/Bootstrap-ProductionBlueGreen.ps1` e o [runbook blue/green](production-blue-green-runbook.md);
   secrets do Container App conforme [deployment.md](deployment.md#azure-container-apps).
9. Rodar o what-if de novo: esperado zero diferenças.
