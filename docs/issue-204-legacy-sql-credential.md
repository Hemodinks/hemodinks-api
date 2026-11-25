# API #204 - Neutralizar a credencial SQL legada: passos 2 a 4

O passo 1 foi executado em 23/11/2026 e registrado na issue: o secret
`connectionstrings-defaultconnection` foi removido do Container App de producao. Este
documento cobre os passos 2 a 4: o que ja esta comprovado, o diagnostico novo e o
procedimento para a rotacao. **A rotacao nao foi executada**: ela exige aprovacao
explicita e os pre-requisitos abaixo.

Repositorio publico: logins, servidor e valores de secret aparecem so como marcadores.

## Passo 2 - Quem ainda usa o login administrativo

Login administrativo SQL do servidor: `<admin-sql>`, que e dbo em todos os bancos do
servidor, inclusive `<banco-legado>` (outro sistema no mesmo servidor). Admin Entra
configurado: a conta do responsavel. Nao ha registro historico de logins que responda a
pergunta, entao a comprovacao e feita por configuracao e por diagnostico.

| Consumidor | Usa o admin? | Evidencia |
| --- | --- | --- |
| API de producao (Container Apps) | **nao** | `ConnectionStrings__DefaultConnection` vem de `connectionstrings-runtime-prod-v2` em todas as revisoes retidas (inventario de 23/11); o SchemaGuard bloqueia deploy se o runtime tiver privilegio administrativo (`AuditSqlRuntimePermissions.sql`); o `rollback-production` recusa alvo sem `connectionstrings-runtime-*` (#380). |
| API de homologacao (Render) | **nao** | O preflight (`scripts/check_homologation.py`) exige que a conexao do Render seja igual a `HOMOLOGATION_SQL_RUNTIME_CONNECTION_STRING`, auditada pelo SchemaGuard a cada publicacao. |
| Workers (Functions) | **nao** | O projeto nao acessa SQL; a Function App de producao so tem settings de storage. |
| Migrations (`*_SQL_MIGRATION_CONNECTION_STRING`) | **a comprovar** | `--validate-connections` so garante identidade diferente do runtime. Diagnostico novo, abaixo. |
| Secrets antigos `AZURE_SQL_CONNECTION_STRING` (env `production`) e `HOMOLOGATION_SQL_CONNECTION_STRING` (env `homologation`) | provavelmente sim | Nenhum workflow da `main` nem da `developer` os referencia (busca em 25/11). Sao credenciais paradas: devem ser apagadas no passo 3. |
| `<banco-legado>` | desconhecido | Nenhum consumidor identificado na assinatura. Confirmar com o responsavel. |
| Acesso humano (portal, SSMS, cofre de senhas) | possivel | Confirmar com o responsavel. Com o admin Entra, o acesso humano nao precisa da senha SQL. |

### Diagnostico `--report-migration-identity`

O SchemaGuard ganhou um modo somente leitura que conecta com
`HEMODINKS_SQL_MIGRATION_CONNECTION_STRING` e imprime so dois booleanos:

```
MigrationIdentityMapsToDbo=true|false
MigrationIdentityInDbOwnerRole=true|false
```

- `MapsToDbo=true`: a migration entra como dbo (admin SQL do servidor, admin Entra ou
  dono do banco). O job emite um `::warning::`. **Enquanto isso for verdade, a rotacao
  quebra as migrations.**
- `InDbOwnerRole=true` com `MapsToDbo=false`: usuario dedicado, mas com `db_owner`.
  Nao bloqueia a rotacao, mas e mais privilegio do que o necessario (ver #197).

Onde roda:

- homologacao: `publish-homologation.yml` em modo `diagnose-sql`, somente leitura e sem
  deploy;
- producao: `publish-container.yml` e `apply-production-migrations.yml`, logo depois de
  `--validate-connections`, com `continue-on-error` (nunca bloqueia o deploy). Disponivel
  so depois da promocao `developer` -> `main`.

Nenhum login, servidor, banco ou valor de secret e impresso, inclusive nos erros.

### Consulta manual complementar

Com o admin Entra no Query Editor do portal, somente leitura (ja publicada na issue):
logins SQL em `master` e usuarios/roles em cada banco. Ela mostra se existe um login de
migration dedicado e se ha logins esquecidos.

## Passo 3 - Rotacao da credencial administrativa

### Pre-requisitos (todos)

1. `diagnose-sql` da homologacao e o proximo deploy de producao com
   `MigrationIdentityMapsToDbo=false`. Se for `true`: criar um usuario de migration
   dedicado (procedimento da #197, passos 2 e 3), trocar o secret e repetir o
   diagnostico.
2. Responsavel confirma que `<banco-legado>` nao e usado ou que pode ficar sem a senha
   antiga.
3. **Caminho de recuperacao comprovado antes**, nao so declarado:
   - login real com o admin Entra no banco de producao (Query Editor do portal);
   - a conta que vai redefinir a senha tem papel RBAC no servidor (`Contributor` ou
     `SQL Server Contributor`). O `az sql server update --admin-password` passa pelo
     Azure Resource Manager: quem redefine a senha e o RBAC, nao o papel de admin Entra
     do SQL.
   Risco registrado: o admin Entra e uma conta pessoal unica, entao ela e o unico
   caminho de recuperacao sem senha SQL. Recomendado trocar por um grupo Entra com pelo
   menos duas contas antes ou logo depois da rotacao.
4. Janela combinada: fora do horario de uso das clinicas.

### Procedimento

1. **Apagar os secrets parados** do GitHub: `AZURE_SQL_CONNECTION_STRING` (environment
   `production`) e `HOMOLOGATION_SQL_CONNECTION_STRING` (environment `homologation`).
   Nao afeta nenhum workflow (passo 2).
2. **Trocar a senha de `<admin-sql>` por um valor aleatorio que ninguem guarda.**
   Com o admin Entra configurado, a senha SQL do admin nao e necessaria para
   administrar os bancos, e quem tem RBAC no servidor pode redefini-la a qualquer
   momento (`az sql server update --admin-password`). Gerar o valor em memoria e passa-lo direto
   ao `az`, sem eco, historico ou arquivo:
   ```powershell
   $bytes = [byte[]]::new(48); [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
   $adminSecret = [Convert]::ToBase64String($bytes) + 'aA1!'
   az sql server update -n <servidor> -g rg-hemodinks-prod --admin-password $adminSecret --only-show-errors -o none
   Remove-Variable adminSecret, bytes
   ```
   O sufixo garante as quatro classes de caractere exigidas pela politica do Azure SQL.
   A senha passa pela linha de comando do `az` por alguns segundos; rodar numa estacao
   confiavel, nunca num runner compartilhado.
3. **Verificar** em seguida: `/healthz` de producao 200, login na producao e na
   homologacao, `publish-homologation` em `diagnose-sql` verde.

### Rollback

A senha antiga nao volta. Se algum consumidor nao mapeado falhar com
`AuthenticationFailed` (18456), quem tem RBAC no servidor define uma nova senha para o admin, a
entrega so ao consumidor afetado e registra o caso. Depois, o consumidor deve ganhar
usuario proprio. Nao recriar os secrets apagados.

O `infra/modules/sql.bicep` recebe `administratorLoginPassword` como parametro: o
proximo deploy de infraestrutura so troca a senha se esse parametro for informado. Nao
reutilizar a senha antiga nesse parametro.

## Passo 4 - Autenticacao somente Entra

Avaliado e **nao aplicavel agora**. Com `azureAdOnlyAuthentication=true`, o servidor
recusa toda autenticacao SQL, inclusive dos usuarios de runtime e de migration atuais,
o que derruba producao e homologacao. Para chegar la, sem custo de recurso novo:

- API de producao: identidade gerenciada do Container App como usuario Entra contido
  no banco (`Authentication=Active Directory Managed Identity`);
- migrations: login OIDC do GitHub Actions (o `azure/login` ja existe em
  `publish-container.yml`) com token Entra;
- homologacao no Render (fora da Azure): service principal com segredo ou
  certificado. Isso traz outro segredo para gerenciar, entao a homologacao e o ponto
  que mais pesa na decisao;
- `<banco-legado>`: precisa ser migrado ou desativado antes.

E uma mudanca de autenticacao em producao, com plano e janela proprios (alto risco
pelo `AGENTS.md`). Fica registrada aqui e na #197; nao faz parte do aceite da #204.

## Aceite da #204

Fecha quando: o diagnostico mostrar migrations sem dbo nos dois ambientes, os secrets
parados forem apagados, a senha do admin for rotacionada sem quebra e os positivos do
passo 3 forem registrados (horario UTC e resultado, sem valores).
