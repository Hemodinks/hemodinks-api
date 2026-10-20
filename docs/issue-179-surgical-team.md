# API #179 â€” equipe cirÃºrgica

Complexidade e risco altos: cadastro/ediÃ§Ã£o de pacientes, atendimentos, contratos,
permissÃµes mÃ©dicas e schema SQL. DependÃªncias: API, migration e frontend consumidor.
Eletiva/UrgÃªncia (#178) nÃ£o foi alterada.

## Comportamento

- Principal pode ocupar tambÃ©m um dos papÃ©is de auxiliar.
- Principal pode estar ausente, desde que pelo menos um auxiliar esteja selecionado.
- Auxiliares entre si continuam distintos; IDs nÃ£o positivos sÃ£o rejeitados.
- CriaÃ§Ã£o e ediÃ§Ã£o exigem seleÃ§Ã£o explÃ­cita: o mÃ©dico autenticado nÃ£o Ã© inserido
  automaticamente como principal quando o campo nÃ£o Ã© enviado.
- MÃ©dicos selecionados permanecem sujeitos ao escopo de clÃ­nica/grupo existente.
  Em atendimentos, os IDs Ãºnicos precisam ser mÃ©dicos ativos da clÃ­nica e o
  mÃ©dico autenticado precisa participar da equipe. EdiÃ§Ã£o tambÃ©m valida os IDs.
- Faturamento mÃ©dico mantÃ©m exclusividade do principal quando ele existe;
  apenas na ausÃªncia dele o auxiliar ganha acesso pelo seu vÃ­nculo.
- Filtros de mÃ©dico em pacientes/faturamento reconhecem auxiliares quando o
  principal estÃ¡ ausente. ProjeÃ§Ãµes preservam principal nulo, sem mÃ©dico fictÃ­cio.
- As consultas de atendimento, dashboard e resumo financeiro usam predicados OR
  por papel: um mÃ©dico em dois papÃ©is nÃ£o gera duas cirurgias nem dois valores.
  NotificaÃ§Ãµes de observaÃ§Ã£o jÃ¡ deduplicam destinatÃ¡rios com Distinct.
- Registros legados sem mÃ©dicos continuam legÃ­veis pelo escopo autorizado, mas
  precisam receber uma equipe vÃ¡lida para serem gravados novamente.

## Schema e operaÃ§Ã£o

Migration `20261017015429_Schema_AllowOptionalSurgicalPrincipal`:
`AtendimentosCirurgicos.MedicoResponsavelId` torna-se nullable; a constraint que
proibia principal=auxiliar Ã© substituÃ­da pelas regras de auxiliares distintos e
pelo menos um mÃ©dico. NÃ£o remove colunas, dados nem FKs compostas por ClinicaId.
Pacientes jÃ¡ possuem IDs de mÃ©dicos nullable; nÃ£o hÃ¡ backfill de mÃ©dicos.

A expansÃ£o de schema aceita gravaÃ§Ãµes antigas vÃ¡lidas. Ela nÃ£o torna binÃ¡rios
antigos capazes de ler principal nulo ou aceitar sobreposiÃ§Ã£o de papÃ©is. Aplicar
migration uma Ãºnica vez pelo processo de migrations; validar nova API e retirar
trÃ¡fego de todas as instÃ¢ncias antigas antes de permitir novas formas de equipe.
Depois atualizar o frontend. NÃ£o liberar escrita nova durante coexistÃªncia com
revisÃµes antigas sem bloqueio operacional de trÃ¡fego/escrita.

Rollback de schema sÃ³ Ã© possÃ­vel enquanto todos os atendimentos forem
compatÃ­veis com a regra antiga. Down verifica principal nulo ou igual a auxiliar
e lanÃ§a erro 50000 antes de qualquer alteraÃ§Ã£o; nÃ£o escolhe mÃ©dicos nem apaga
papÃ©is automaticamente. ApÃ³s gravar equipes novas, preferir roll-forward. Uma
reversÃ£o precisa de decisÃ£o de negÃ³cio e cÃ³pia/backup, alÃ©m de bloquear novas
escritas; nÃ£o retornar binÃ¡rios antigos antes dessa verificaÃ§Ã£o. Nenhum deploy
ou banco de aplicaÃ§Ã£o foi alterado nesta tarefa.

## VerificaÃ§Ã£o

Security realizou revisÃ£o somente leitura. Database produziu
`SurgicalTeamSqlServerTests.cs`, mas encerrou por limite de uso antes da execuÃ§Ã£o;
o principal revisou e executou os testes. PapÃ©is especialistas configurados com
esforÃ§o high; modelo/esforÃ§o efetivos do principal nÃ£o informados pelo ambiente.

- Matriz inicial HTTP: 16 aprovados.
- ValidaÃ§Ã£o final e regressÃ£o: resultados registrados apÃ³s tÃ©rmino nos TRX em
  `out/issue-validation/issue179-final-targeted.trx` e `issue179-regression.trx`.
- Testes SQL usam bancos sintÃ©ticos GUID e removem no finally; verificam FK
  multiclÃ­nica, matriz de constraints, preservaÃ§Ã£o de dados, Up/Down e replay do
  script idempotente da nova migration duas vezes.
- Testes HTTP cobrem ediÃ§Ã£o invÃ¡lida sem perda da equipe anterior, associaÃ§Ã£o
  cruzada negada no POST/PUT e leitura/ediÃ§Ã£o negadas entre clÃ­nicas; a matriz
  verifica tambÃ©m valor Ãºnico no faturamento e resumo financeiro.
- EF `migrations has-pending-model-changes --no-build`: nenhuma diferenÃ§a.
- Script idempotente incremental gerado em
  `out/issue-validation/issue179-migration.sql`.

LimitaÃ§Ãµes: os testes de navegador de login sÃ£o opt-in. Cadastro responsivo,
PDF/XLSX e telas de relatÃ³rios pertencem ao frontend e nÃ£o foram exercitados
nesta tarefa da API. A alteraÃ§Ã£o nÃ£o certifica esses fluxos E2E. O LocalDB nÃ£o
possui Full-Text; os bloqueios histÃ³ricos de migrations/fixtures registrados na
#172 continuam fora do escopo. Avisos NuGet preexistentes de
System.Security.Cryptography.Pkcs 9.0.4 nÃ£o foram alterados.

Comandos principais executados:

```powershell
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --environment HEMODINKS_TEST_LOCALDB=1 --results-directory out/issue-validation --logger 'trx;LogFileName=issue179-regression.trx'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --no-restore --filter 'FullyQualifiedName~SurgicalTeam|FullyQualifiedName~FaturamentoMedicoQueryHandlerTests|FullyQualifiedName~PacienteCommandHandler' --environment HEMODINKS_TEST_LOCALDB=1 --results-directory out/issue-validation --logger 'trx;LogFileName=issue179-final-targeted.trx'
```

Status: implementação da API validada nos testes direcionados; regressão global
com bloqueios históricos documentados e validação E2E de frontend pendente.

Publicação: commit remoto autorizado pelo usuário, porém bloqueado nesta sessão.
O conector GitHub recusou create_tree porque a ação exige aprovação e a política
ativa é never. O .git local é somente leitura (git fetch falhou ao escrever
FETCH_HEAD). Nenhum commit/ref remoto foi criado ou atualizado; os 24 arquivos
permanecem no workspace para publicação em sessão com permissão de escrita.

