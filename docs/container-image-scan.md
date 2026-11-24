# Scan da imagem do contêiner e SBOM (#372)

## O que roda
No `publish-container.yml`, job **publish** (push na `main`), logo depois de publicar a imagem no GHCR e
**antes** de registrar o digest usado pelo deploy:

1. **SBOM SPDX** (`trivy --format spdx-json`) do digest publicado, anexado ao run como artefato
   `sbom-api-<sha>` (retenção de 90 dias).
2. **SARIF** com as CVEs HIGH/CRITICAL que têm correção, enviado ao **Security → Code scanning**
   (categoria `trivy-image-api`).
3. **Gate**: `trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1`. Uma CVE crítica ou alta
   **com correção disponível** na imagem (SO base, runtime .NET/ASP.NET ou dependência) falha o job, e os
   jobs de deploy, que dependem dele, não rodam.

O Trivy roda pela imagem oficial `aquasec/trivy` **fixada por digest** (não pela `trivy-action`, que teve tags
sequestradas em 2026). Para atualizar: escolher a release em `aquasecurity/trivy`, `docker pull
aquasec/trivy:<versão>`, copiar o `RepoDigest` e trocar nos dois passos do workflow.

O gate de pacotes NuGet continua separado (`docs/nuget-vulnerability-gate.md`).

## Falhou o gate
1. Quase sempre a correção está numa imagem base mais nova (`mcr.microsoft.com/dotnet/aspnet:10.0`): o build
   usa a tag flutuante, então basta publicar de novo depois que a Microsoft atualizar a imagem. O Dependabot
   (ecossistema `docker`) também abre PR quando houver tag nova.
2. Se a CVE vier de um pacote NuGet, corrigir pelo `Directory.Packages.props`.
3. Só se não houver correção aplicável e o risco for aceito: adicionar o ID em `.trivyignore` com
   justificativa, issue de acompanhamento e data de revisão, num PR revisado.

## Verificar localmente
```bash
docker build --pull -t hemodinks-api:scan .
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$PWD:/src:ro" \
  aquasec/trivy@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb \
  image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --ignorefile /src/.trivyignore \
  --exit-code 1 hemodinks-api:scan
```
Use `--pull`: uma imagem base antiga em cache local mostra CVEs que o CI (base atual) não tem.
