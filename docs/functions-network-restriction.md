# Restricao de origem das Azure Functions (issue #224)

## Situacao em 09/11/2026

- Function App de producao: `hemodinks-workers-production` (Flex Consumption, grupo `hemodinks-workers-production_group`).
- Functions HTTP: `UploadProfilePhoto`, `UploadPatientFile`, `SendEventEmail`, `SendPasswordResetEmailSync`.
  Todas usam `AuthorizationLevel.Function`. A API de producao chama por HTTP so os uploads (`StorageFunctions__BaseUrl`);
  reset e exportacao usam fila, que nao passa pelas regras de rede.
- Antes desta mudanca, a Function App aceitava qualquer origem ("Allow all"); um teste sem chave recebia `401`.
- A API roda no Container Apps em ambiente so Consumption, sem VNet. Nao ha IP de saida fixo: a Azure publica
  `outboundIpAddresses` com todos os IPs possiveis (181 em 09/11/2026), compartilhados com outros clientes da regiao.

## O que a restricao faz

Workflow `Functions network restriction` (`.github/workflows/functions-network-restriction.yml`), so na `main`:

| Modo | Aprovacao | Efeito |
| --- | --- | --- |
| `check` (e agendado toda segunda) | nao | Compara os IPs de saida da API com as regras aplicadas. Falha se divergirem; so avisa se a restricao ainda nao foi aplicada. |
| `apply` + `RESTRICT-FUNCTIONS` | ambiente `production` | Cria regras `api-egress-NNN` (8 IPs por regra) e muda a acao padrao para Deny. Confirma a configuracao e testa do runner do GitHub, que deve receber `403`. |
| `rollback` + `UNRESTRICT-FUNCTIONS` | ambiente `production` | Remove as regras e volta a aceitar qualquer origem. O teste do runner volta a receber `401`. |

O site SCM (deploy) nao muda: continua com regras proprias (`scmIpSecurityRestrictionsUseMain=false`), para nao
bloquear `Azure/functions-action`. A logica fica em `scripts/functions_network_restrictions.py`, testada sem Azure em
`scripts/tests/test_functions_network_restrictions.py` (rodada no CI).

## Validacao depois do apply

1. O job ja confirma a configuracao e o bloqueio de origem externa (`403` mesmo sem chave).
2. Em producao, enviar uma foto de perfil e um anexo de paciente. Se falhar com erro de upload, rodar `rollback`.
3. Reset de senha e exportacao usam fila; conferir um envio de reset mesmo assim.

## Limitacoes

- Protecao parcial: os IPs de saida do Container Apps sao compartilhados com outros clientes da Azure na regiao.
  A restricao bloqueia a internet em geral, nao esses vizinhos; a function key continua obrigatoria.
- Se a Azure mudar os IPs de saida da API, uploads passam a falhar ate o proximo `apply`. O `check` semanal falha
  quando isso acontece; reaplique com `apply`.
- Homologacao (`hemodinks-workers-confirmation`) fica de fora: a API de homologacao roda no Render, com IPs
  compartilhados, e a identidade de deploy nao tem permissao nesse grupo de recursos.
- Restricao forte e sem custo recorrente exige recriar o ambiente do Container Apps em uma VNet (service endpoint
  `Microsoft.Web` na sub-rede). Com IP de saida fixo via NAT Gateway, ha custo mensal. Nenhuma das duas foi feita aqui.
