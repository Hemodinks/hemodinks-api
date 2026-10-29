# Issue #199 - replay integral com Full-Text

## Causa e correção

O replay via `IMigrator.MigrateAsync` passa, mas o script EF idempotente reúne
o `ALTER TABLE Users ADD PerfilId` e o backfill estático na mesma batch.
O SQL Server compila a referência à coluna antes de executar o `ALTER`, gerando
`Invalid column name 'PerfilId'`. O `IF NOT EXISTS` da tabela de histórico não
cria uma fronteira de compilação. Full-Text é requisito para alcançar o fim
da cadeia, mas não é a origem desse erro.

Nove comandos em oito migrations apresentam esse padrão: perfis, CPF/pacientes,
médico, convênio, `ClinicaId`, competência, endurecimento multiclínica e branding.
Os comandos agora usam `EXEC(N'...')` com escape de aspas para compilação tardia,
conforme a [orientação oficial do EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing#arbitrary-changes-via-raw-sql).

Uma migration nova não conseguiria corrigir o replay: a execução falha antes
de chegar a ela. Por isso os arquivos históricos foram ajustados apenas no
invólucro dos comandos. O SQL interno, predicados, IDs, DDL, métodos `Down`,
designers e snapshot foram preservados. Não há migration nova nem mudança de
modelo, autorização, filtros, contratos ou dependências da aplicação.
O histórico EF mantém os mesmos IDs e ignora essas migrations em bancos que
já as receberam. Não se deve remover ou editar `__EFMigrationsHistory`.

## Validação local

Foi construída a imagem a partir de `sqlserver/Dockerfile.fts`, usando um
container exclusivo do issue, porta de loopback `14399`, sem volume de dados
compartilhado. `FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` retornou `1`.
Os testes usam `HEMODINKS_TEST_SQLSERVER_CONNECTION_STRING` e criam/excluem
somente bancos descartáveis de nomes únicos.

- Antes: `out/issue-199/before.trx`, 1 aprovado (direto), 1 falha (idempotente,
  `PerfilId`), zero ignorados.
- Depois: replay direto e script integral executados duas vezes; oito índices
  Full-Text, histórico completo, schema e dados preservados no segundo passe.
- Upgrade de três prefixos previamente aplicados, incluindo o estado com
  Full-Text já instalado; repetição integral preserva os dados.
- Fixture legada anterior aos backfills: perfil, criação de paciente, vínculo
  de médico/convênio, propriedade por `ClinicaId`, competência, idempotência e
  branding, com dados exclusivamente sintéticos.
- Rollback SQL dirigido de Full-Text e reaplicação: schema e dados preservados.
- Leitura da clínica A positiva, leitura pela B negativa e FK rejeitando vínculo
  de médico de outra clínica. Writer SQL legado e `SchemaHistoryGuard` exercitados.
- Banco migrado pela DLL original de `developer` (`62cf83c`) via CLI EF recebeu
  o script corrigido duas vezes. Schema, dados sintéticos e histórico permaneceram
  idênticos nas duas comparações (SHA256 do snapshot:
  `2EEF86ED64B4E7BD9078C20231C09C52E96C4F11D03775BAE492F0BED0A40BD9`).

Resultados registrados em `out/issue-199/`:

- Replay final: **7 aprovados, zero falhas/ignorados**, incluindo a fixture
  legada e comparação de faturamento, idempotência e configurações.
- Regressão API: **1.082 aprovados, zero falhas/ignorados**, com gate TRX aprovado.
- Auditoria: **73 migrations**, sem alterações pendentes no modelo.
- Gates offline: **17** de Blue/Green, **3** de identidade SQL e **12** do gate TRX
  aprovados. A primeira tentativa dos gates Blue/Green exigiu disponibilizar `jq`.
- Navegador: primeira execução **8 aprovados e 4 timeouts**, zero ignorados;
  repetição somente dos quatro cenários: **4 aprovados**, sem alteração de código.
  Três timeouts ocorreram em `page.goto`; `bootstrap-lock` excedeu o tempo em
  `page.evaluate` ao adquirir um Web Lock. A execução inicial não foi verde.

Comandos de verificação (conexão local descartável previamente configurada):

```powershell
dotnet test ./HemodinksAPI.slnx -c Release --filter 'FullyQualifiedName!~LoginBrowserTests'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj -c Release --no-build --filter FullyQualifiedName~LoginBrowserTests
./scripts/Test-Migrations.ps1 -NoBuild -Configuration Release
python -B -m unittest discover -s scripts/tests -p test_production_deployment_gates.py -v
python -B -m unittest discover -s scripts/tests -p test_sql_identity_separation.py -v
python -B -m unittest discover -s scripts/tests -p test_test_results_gate.py -v
```

O navegador requer `HEMODINKS_E2E_FRONT_PATH`, frontend e Playwright existentes.
Os gates offline requerem Bash e `jq`. Não se utiliza configuração de banco
da aplicação para os testes SQL. Credenciais locais e TRX permanecem em `out/`,
fora do versionamento.

## Limites

Blue/Green é validado localmente por compatibilidade de schema/contrato,
no-op em banco atualizado, writer antigo e gates offline. Não houve deploy,
troca de tráfego ou execução de dois binários em produção. O rollback testado
é dirigido ao Full-Text; não se recomenda desfazer integralmente migrations
históricas destrutivas em um banco com dados. Produção, homologação e Azure
não foram acessados ou alterados. Custo Azure: R$ 0,00.

Uma verificação exploratória adicional do **script integral sem `--idempotent`**
reproduziu a falha original em `PerfilId` e, após esta correção, falhou em `Cpf`
no índice filtrado gerado pelo EF. Nesse formato o provider também gera
`UpdateData` estático referindo colunas recém-adicionadas (branding, `ClinicaId`
e plano). Essa limitação preexistente permanece separada: corrigi-la ampliaria
a intervenção para DDL/seed gerados e outra migration histórica. Os workflows
`generate-migration-script`, `apply-production-migrations`, `publish-container`
e `publish-homologation`, assim como o export padrão local, usam `--idempotent`.
Esta entrega valida o replay direto pelo EF e esse formato efetivo de rollout.
