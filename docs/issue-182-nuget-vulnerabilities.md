# Issue #182 — vulnerabilidades NuGet

Auditoria e correção em 22/10/2026. Complexidade moderada, risco alto pelo alcance
de uma dependência criptográfica nos projetos de produção. AGENTS.md e
`.codex/config.toml` lidos. Escopo: dependências transitivas e validação dos
artefatos; sem mudança de domínio, autorização, isolamento, schema ou deploy.

DevOps (esforço medium configurado) revisou independentemente a cadeia, fontes e
Dockerfiles, sem editar. O principal verificou advisories e executou a correção
e testes. Modelo e esforço efetivos do principal não foram expostos. Não houve
revisão Security concluída para esta issue; os limites de uso já observados na
sessão impediram contar com essa revisão adicional.
DevOps também aprovou a revisão final dos pins, assets e coerência do relatório,
sem bloqueador novo; o principal concluiu a validação abaixo.

## Antes/depois e advisories

| Dependência | Antes | Depois | Caminho e alcance |
| --- | --- | --- | --- |
| `System.Security.Cryptography.Pkcs` | 9.0.4 | **10.0.13** | EF Core SqlServer 10.0.10 → Microsoft.Data.SqlClient 6.1.1 → Pkcs; Infrastructure, API, Workers, SchemaGuard e testes. |
| `MessagePack` | 2.5.302 | **2.5.303** | Aspire 13.4.6 → StreamJsonRpc 2.25.29 → MessagePack; somente AppHost de desenvolvimento. Annotations resolve automaticamente para 2.5.303. |

Fontes primárias consultadas:

- [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx):
  severidade alta, DoS em `SignedCms.CheckSignature`; corrigido em Pkcs 10.0.13
  ou 9.0.21 nas respectivas linhas.
- [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7):
  moderada, DoS em `EnvelopedCms.Decode` no Windows; mesmas versões corrigidas.
- [GHSA-qhrr-8q5h-9q3h](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h):
  moderada, alocação excessiva ao desserializar dados não confiáveis como
  `object`; correção compatível na linha MessagePack 2 em 2.5.303.

São três advisories distintos, não uma vulnerabilidade por ocorrência/projeto.
Não foi encontrada chamada direta a essas APIs CMS ou ao protocolo MessagePack
nos fontes da API/Application/Infrastructure; isso não comprova ausência de
alcance transitivo nem exploração efetiva.

Somente duas entradas `PackageVersion` foram acrescentadas em
`Directory.Packages.props`, aproveitando o transitive pinning já habilitado.
Não há lockfile versionado para atualizar. Não se alterou NuGetAudit, NoWarn,
fontes, SDK, EF Core, SqlClient, Aspire ou contratos públicos. `nuget.config`
mantém apenas nuget.org com mapping existente. Durante a execução, os pins foram
incorporados à `developer` pelo commit `38ef88a`; o agente não criou esse commit.

## Por que Pkcs 10.0.13

O projeto já usa .NET 10. Um pin 9.0.21 limpa a auditoria NuGet, mas o SDK pode
omitir a DLL do publish em favor do framework compartilhado de versão principal
maior. Isso foi comprovado na imagem intermediária: um probe via reflexão,
executado com os arquivos `.deps.json`/`.runtimeconfig.json` da API, carregou
**10.0.9** de `/usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.9/`.
Por isso o pin final usa 10.0.13, para corrigir também a resolução no contêiner.
Na máquina de testes o framework compartilhado já é 10.0.13.

Verificação final, sem rede nem inicialização da aplicação: o probe carregou
**10.0.13** de `/app/System.Security.Cryptography.Pkcs.dll` na imagem
`hemodinks-issue182-api:verified`; nos Workers carregou **10.0.13** do framework
compartilhado em `/usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.13/`.
Assim, o resultado não depende apenas dos números da auditoria de restore.

API e Workers são publicados separadamente pelos Dockerfiles existentes;
AppHost/MessagePack não integram essas imagens. Os contextos finais de build
foram preparados localmente com os fontes versionados dos projetos necessários,
os Dockerfiles e os mesmos arquivos de configuração, sem `.env`, bin/obj ou
secrets. Imagens locais apenas, sem push/publicação.

## Validação

Evidências locais ignoradas pelo Git em `logs/issue182/`:

- Restore forçado, com cache HTTP dispensado, aprovado nas versões finais.
- Auditoria `--vulnerable --include-transitive --format json` da solução e do
  SchemaGuard sem vulnerabilidades reportadas (`audit-after.json` e
  `audit-schema-guard.json`); baseline em `audit-before.json`.
- Build Release final aprovado, zero erros; 24 avisos IDE0005 preexistentes,
  sem NU1902/NU1903 (`build-final-release.log`).
- `Test-Migrations.ps1 -NoBuild -Configuration Release` aprovado, sem mudança
  de modelo pendente (`migrations-final.log`). É uma validação sem conexão ao
  banco de produção, não uma aplicação de migrations em Azure.
- Dockerfiles da API e Workers compilados com os fontes atuais de `38ef88a`,
  sem modificar as receitas (`docker-api-verified.log`, `docker-workers-final.log`).
  Probes de runtime aprovados; MessagePack ausente nos respectivos `.deps.json`.
- Suíte completa final com SQL Server Full-Text descartável e navegador:
  **1.065 executados, 1.062 aprovados, três falhas, zero ignorados**, em
  23min54s (`issue182-final-full.trx`). O contêiner SQL exclusivo foi removido
  ao terminar. A falha SQL continua no rollback da fixture descrita abaixo.
  Os dois casos de navegador falharam por timeout: `invalid-pin` ao carregar
  a página e `individual` ao marcar o checkbox desabilitado de aceite dos termos.
- Repetição isolada somente desses dois casos de navegador, com a porta 5184
  livre e sem builds concorrentes: **dois aprovados, zero falhas/ignorados**,
  em 45s (`issue182-browser-recheck.trx`). Isso registra a intermitência sem
  transformar retroativamente a suíte completa em aprovada. Não foi demonstrada
  relação causal dessas falhas com os pins NuGet.

CI do commit `38ef88a` consultado sem disparar workflows:
[push 39861226088](https://github.com/Hemodinks/hemodinks-api/actions/runs/39861226088)
e [pull request 39861234195](https://github.com/Hemodinks/hemodinks-api/actions/runs/39861234195)
concluíram com sucesso. O log do job de push registra **1.065 testes: 1.048
aprovados, 17 ignorados, sem NU1901–NU1905**. Os ignorados são 12 de navegador,
dois `SurgeryTypeSqlServerTests` e um caso em cada classe
`PatientUserTenantSqlServerTests`, `PacienteCommandHandlerTests` e
`EventCreatorMigrationTests`. Portanto CI verde não substitui a rodada local
com essas condições habilitadas nem comprova os E2E ignorados.

A rodada inicial, ainda com Pkcs 9.0.21 e builds Docker concorrentes, executou
1.034 testes: 1.021 aprovados, 13 falhas, zero ignorados (`issue182-full.trx`).
Uma falha é da fixture `PatientUserTenantSqlServerTests`, cujo rollback parcial
tenta remover `AK_Users_ClinicaId_Id` enquanto a FK
`FK_Events_Users_ClinicaId_CreatedByUserId` ainda a referencia. Esse limite de
rollback da fixture já é registrado nas auditorias anteriores de isolamento/SQL.
Doze falhas são de navegador: timeout em PIN inválido, cancelamento no cenário
individual e conflitos da porta 5184 nos demais. O Vite órfão daquela execução
foi identificado e encerrado; nenhum servidor não identificado foi encerrado.
Não se modificaram assertions, timeouts, frontend ou a fixture fora do escopo.

## Limites e publicação

A auditoria de pacotes não equivale a um scan de todas as bibliotecas do
framework/OS das imagens nem valida configurações efetivas em produção.
As bases Docker usam tags flutuantes; especialmente nos Workers, que carregam
Pkcs do framework, é necessário repetir a verificação de runtime no próximo
release. O resultado acima comprova as imagens locais verificadas.
Nenhum recurso Azure, secret ou política de deploy foi alterado.
Os pins já estão na branch-alvo `developer`; não foi criado PR artificial sem
diff de implementação contra essa mesma branch. O relatório registra o trabalho
para revisão. Correção e auditoria NuGet concluídas; a suíte completa local não
ficou integralmente verde, pela falha de rollback SQL e pelos dois timeouts de
navegador que passaram na repetição isolada. A correção da fixture SQL permanece
fora deste escopo. Não se declara validação em produção nem encerramento da issue.
