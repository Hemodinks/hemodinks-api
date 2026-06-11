# Hemodinks API - Production Readiness

Este checklist registra o que ja esta coberto no repositorio e o que ainda depende de configuracao externa antes de vender ou operar em producao.

## Estado atual no repositorio

- [x] CI com restore, build e testes em `.github/workflows/ci.yml`.
- [x] API com `/healthz` publico.
- [x] `/healthz` valida conectividade com o banco.
- [x] Logs HTTP com Serilog, `TraceIdentifier` e header `X-Request-ID`.
- [x] Reset de senha alternavel por ambiente: `PasswordReset__UseEmail=true|false`.
- [x] Trial/licenca com politicas por feature.
- [x] Endpoints administrativos de licenca protegidos por perfil administrador.
- [x] Testes de reset, autenticacao, endpoints principais e licenca.

## GitGuardian e secrets

- [ ] Confirmar que `Hemodinks/hemodinks-api` aparece como monitored no GitGuardian.
- [ ] Confirmar que nao existem incidentes `Open` para a API.
- [ ] Manter qualquer senha vazada revogada, mesmo se o historico ja foi limpo.
- [ ] Rodar uma varredura local antes de releases importantes:

```powershell
ggshield secret scan repo .
```

Regras praticas:

- Nunca commitar `.env`.
- Exemplos devem usar placeholders, como `CHANGE_ME`.
- Secrets reais ficam apenas no Render, GitHub Secrets, Azure Key Vault ou provedor equivalente.

## Backup e restore do banco

O projeto usa SQL Server. Para producao, o banco precisa ter backup automatico fora do container da API.

Checklist minimo:

- [ ] Identificar onde o SQL Server de producao esta hospedado.
- [ ] Ativar backup automatico/PITR quando o provedor suportar.
- [ ] Configurar retencao minima operacional, por exemplo 7 a 35 dias.
- [ ] Configurar retencao longa se houver obrigacao comercial ou fiscal.
- [ ] Testar restore para um banco separado antes do primeiro cliente real.
- [ ] Documentar RPO e RTO esperados.

Recomendacao:

- Azure SQL e uma boa opcao por ter backup automatico, point-in-time restore e long-term retention.
- Banco em container ou plano sem backup nao deve ser usado para producao comercial.

## CI, branch protection e deploy

O workflow `CI / Build and test` ja esta no repositorio. Para ele bloquear deploy/merge de verdade, configure no GitHub:

- [ ] `main` protegida contra push direto.
- [ ] Pull request obrigatorio para `main`.
- [ ] Status check obrigatorio: `CI / Build and test`.
- [ ] Branch precisa estar atualizada antes do merge.
- [ ] Force push bloqueado em `main`.
- [ ] Render apontando para a branch correta.
- [ ] Render usando deploy apenas apos checks passarem.

Fluxo recomendado:

1. Desenvolvimento em `developer`.
2. Pull request de `developer` para `main`.
3. CI passa.
4. Merge.
5. Render faz deploy da `main`.

## Logs, monitoramento e alertas

No codigo:

- `/healthz` retorna `Healthy` ou `Unhealthy`.
- O check `database` valida conectividade com o banco.
- Cada resposta inclui `X-Request-ID`.
- Serilog registra metodo, path, status code, tempo e trace id.

Configuracoes externas recomendadas:

- [ ] Monitor externo chamando `/healthz` a cada 1 a 5 minutos.
- [ ] Alerta por email/WhatsApp/Slack quando `/healthz` falhar.
- [ ] Log stream do Render para Better Stack, Datadog, Grafana Cloud ou similar.
- [ ] Alerta para aumento de respostas 5xx.
- [ ] Alerta para falhas de login/reset acima do normal.

## Permissoes, roles e licenca

Perfis atuais:

- `1` - Administrador.
- `2` - Medico.
- `3` - Paciente.

Features atuais:

- `Dashboard.Visualizar`
- `Pacientes.Visualizar`
- `Pacientes.Gerenciar`
- `Cbhpm.Consultar`

Trial atual:

- Dashboard visualizar.
- Pacientes visualizar.
- CBHPM consultar.
- Nao inclui gerenciamento de pacientes.

Completa:

- Todas as features atuais.

Checklist antes de vender:

- [ ] Confirmar quem sera administrador em producao.
- [ ] Remover ou trocar senhas de usuarios seed/teste.
- [ ] Confirmar `Licensing__TrialDays`.
- [ ] Validar fluxo manual: criar medico, trial ativo, liberar completa, suspender licenca.
- [ ] Validar que medico trial nao consegue acessar acoes de gerenciamento bloqueadas.
- [ ] Validar que medico completo consegue acessar as features contratadas.
- [ ] Definir politica comercial para licenca expirada ou suspensa.

## Variaveis criticas de producao

Obrigatorias:

- `ConnectionStrings__DefaultConnection`
- `JwtSettings__SecretKey`
- `JwtSettings__Issuer`
- `JwtSettings__Audience`
- `JwtSettings__ExpirationMinutes`
- `PasswordReset__UseEmail`

Quando usar storage:

- `AzureStorage__ConnectionString`
- `AzureStorage__ContainerName`
- `AzureStorage__PublicBaseUrl`
- `AzureStorage__PatientFilesContainerName`
- `AzureStorage__PatientFilesPublicBaseUrl`

Quando usar email real:

- `Email__Provider`
- `Email__FromEmail`
- `Email__FromName`
- Credenciais/API key do provedor escolhido
- `Frontend__ResetPasswordUrl`

## Go-live minimo

Antes do primeiro cliente pagante:

- [ ] CI obrigatoria em PR.
- [ ] GitGuardian sem incidente aberto.
- [ ] Backup automatico confirmado.
- [ ] Restore testado em ambiente separado.
- [ ] Monitor externo ativo.
- [ ] Administrador real criado.
- [ ] Usuarios de seed/teste removidos ou com senha trocada.
- [ ] Politica de trial/licenca validada.
- [ ] Reset de senha definido: temporario por senha padrao ou definitivo por email.
- [ ] Variaveis de producao revisadas sem secrets no Git.
