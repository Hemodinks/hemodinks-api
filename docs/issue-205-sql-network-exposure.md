# API #205 - Exposicao de rede do Azure SQL: inventario e alternativas

Parte sem custo da #205: inventario somente leitura, alternativas e plano. **Nada foi
alterado no Azure**: nenhuma regra criada ou removida, nenhum recurso provisionado.
A #205 continua aberta; o aceite exige o controle de rede aplicado e testado.

Repositorio publico: servidor, IPs e regras pessoais aparecem aqui so como marcadores
ou contagens. O inventario completo fica no portal/CLI, nunca no Git.

## Inventario em 25/11/2026 (somente leitura)

Comandos: `az sql server list/show`, `az sql server firewall-rule list` (processado em
memoria, so nome e tamanho da faixa), `vnet-rule list`, `outbound-firewall-rule list`,
ARM GET `privateEndpointConnections`, `az sql db list`, `az containerapp env list`,
ARM GET do Container App, `az functionapp list/show`, `az network vnet/nat gateway list`,
`az role assignment list` no grupo `rg-hemodinks-prod`. Nenhuma conexao SQL foi aberta.

### Servidor `<servidor-sql-producao>` (`rg-hemodinks-prod`, Brazil South)

| Item | Valor |
| --- | --- |
| `publicNetworkAccess` | `Enabled` |
| TLS minimo | `1.2` |
| Somente Entra (`azureAdOnlyAuthentication`) | `false` (admin Entra configurado; login SQL tambem aceito) |
| Regras de firewall | **16** (detalhe abaixo) |
| VNet rules / Private Endpoints / regras de saida | 0 / 0 / 0 |
| VNets e NAT Gateways na assinatura | 0 / 0 |

Bancos no mesmo servidor (todos serverless, `Paused` na leitura):

| Banco | Uso | Auto-pause |
| --- | --- | --- |
| `HemodinksDB` | producao | 30 min |
| `HemodinksDBConfirmation` | homologacao (Render) | `-1` (oferta gratuita) |
| `ESCOLA_API` | outro sistema, inativo | 60 min |

Firewall e por **servidor**: toda regra vale para os tres bancos. Restringir a rede
de producao tambem afeta homologacao e `ESCOLA_API`, e vice-versa.

### Regras de firewall

| Grupo | Qtde | Tipo | Quem usa |
| --- | --- | --- | --- |
| `AllowAllWindowsAzureIps` | 1 | `0.0.0.0` (Azure Services) | Container Apps de producao e runners do GitHub Actions (ver abaixo) |
| `Render`, `RenderVirginia` | 2 | IP unico cada | API de homologacao no Render (tambem em `infra/modules/sql.bicep`) |
| `ClientIPAddress_*` | 10 | IP unico | acessos administrativos de 05/2026 a 08/2026, criados pelo portal |
| `QueryEditorClientIPAddress_*` | 3 | IP unico | Query Editor do portal |
| `ExportLocalTemporario-20260728` | 1 | IP unico | exportacao local pontual de 28/07/2026 |

Nenhuma regra cobre faixa larga alem da `AllowAllWindowsAzureIps`. As 14 regras de IP
unico de acesso pessoal/temporario sao IPs residenciais ou de rede movel: depois que
o provedor reatribui o IP, quem o recebe passa a ter caminho de rede ate o servidor
(ainda precisando de credencial). A auditoria anterior (#197, 28/10) contou 14 regras;
a contagem subiu para 16.

### Consumidores do SQL e origem de rede

| Consumidor | Banco | Origem de rede | Regra que libera hoje |
| --- | --- | --- | --- |
| API de producao (`hemodinks-api-prod`, Container Apps) | `HemodinksDB` | Ambiente Consumption **sem VNet**; 181 IPs de saida possiveis (163 faixas contiguas, maior com 4 IPs), compartilhados com outros clientes da regiao | `AllowAllWindowsAzureIps` |
| `publish-container.yml` e `apply-production-migrations.yml` (SchemaGuard + bundle EF) | `HemodinksDB` | runner `ubuntu-latest` hospedado pelo GitHub, IP dinamico na Azure | `AllowAllWindowsAzureIps` |
| `publish-homologation.yml` (SchemaGuard, bundle, `diagnose-sql`) | `HemodinksDBConfirmation` | runner `ubuntu-latest`, IP dinamico na Azure | `AllowAllWindowsAzureIps` |
| API de homologacao (Render) | `HemodinksDBConfirmation` | IPs de saida do Render, compartilhados entre clientes do Render | `Render`, `RenderVirginia` |
| Workers (`HemodinksAPI.Workers`, Functions) | nenhum | - | nenhuma: o projeto nao usa EF nem `SqlConnection` (busca no codigo) |
| Acesso administrativo (portal, SSMS, exportacao) | qualquer | IP do administrador | regras `ClientIPAddress_*`, `QueryEditor*`, `ExportLocal*` |
| `ESCOLA_API` | `ESCOLA_API` | **nao comprovado** | desconhecida; banco pausado, sem consumidor identificado na assinatura |

Identidade de deploy: um service principal tem `Contributor` em `rg-hemodinks-prod`,
o que ja permite criar e apagar regras de firewall do servidor. Nao foi necessario
nem feito nenhum ajuste de RBAC.

## O que a regra `AllowAllWindowsAzureIps` realmente libera

A regra libera conexoes vindas de **qualquer IP da Azure, de qualquer cliente e de
qualquer regiao**, nao so dos recursos desta assinatura. Quem tem uma VM ou um runner
na Azure alcanca a porta 1433 e so para na autenticacao. Usuario SQL de menor
privilegio (#197) reduz o dano de uma credencial vazada, mas nao e controle de rede.

## Alternativas

Ordem pedida pela issue: primeiro recursos existentes, sem custo.

### A. Limpeza das regras pessoais (custo zero, baixo risco)

Remover as 14 regras `ClientIPAddress_*`, `QueryEditorClientIPAddress_*` e
`ExportLocalTemporario-*`. Acesso administrativo futuro: criar regra com o IP do
momento, com nome datado, e apagar ao fim (o portal oferece isso no Query Editor).
Nao afeta API, workers nem workflows. Antes: confirmar que nenhum processo
agendado fora da Azure (ex.: exportacao local recorrente) depende desses IPs.

### B. Trocar `AllowAllWindowsAzureIps` por lista de IPs (custo zero, complexidade media)

1. **Container Apps:** uma regra por faixa contigua dos `outboundIpAddresses`
   (163 hoje; o limite e 256 regras por servidor). Reaproveita o padrao do workflow
   `functions-network-restriction.yml` (#224): `apply`, `check` semanal e `rollback`.
2. **Runners do GitHub:** regra temporaria por execucao. Depois do `azure/login`, o
   job descobre o IP publico do proprio runner, cria `gha-<run_id>-<attempt>`, executa
   SchemaGuard/bundle e apaga a regra num passo `if: always()`. Hoje so
   `publish-container.yml` tem `azure/login`; `apply-production-migrations.yml` e
   `publish-homologation.yml` precisariam de login OIDC.
3. **Render:** manter as duas regras de IP unico, depois de conferir no painel do
   Render que sao os IPs de saida atuais da regiao do servico.
4. Remover `AllowAllWindowsAzureIps` so depois de 1 a 3 validados.

Limites:

- Os IPs de saida do Container Apps sao compartilhados com outros clientes de
  Container Apps em Brazil South. O ganho e reduzir "toda a Azure no mundo" para esse
  conjunto, nao isolar a API.
- Se a Azure mudar os IPs de saida, a API de producao perde o banco ate o proximo
  `apply`. Por isso o `check` agendado e obrigatorio, e o primeiro `apply` precisa de
  janela com rollback pronto.
- **Servidor compartilhado:** a identidade usada pela homologacao para criar regra
  temporaria poderia abrir o firewall tambem para producao. Para nao dar a
  homologacao poder sobre producao, as migrations de homologacao teriam de rodar
  com identidade e environment separados, com aprovacao, ou o banco de homologacao
  teria de sair deste servidor.
- O runner precisa descobrir o proprio IP publico. Isso exige consultar um servico
  externo de "qual e meu IP" ou a resposta de erro do SQL. Escolher e revisar antes
  de implementar.

### C. Container Apps em VNet + service endpoint `Microsoft.Sql` (sem custo recorrente previsto, complexidade alta)

Recriar o ambiente do Container Apps (perfil Consumption) numa VNet com sub-rede
dedicada (`/27` ou maior), habilitar o service endpoint `Microsoft.Sql` na sub-rede e
criar uma VNet rule no servidor. A API passa a chegar ao SQL pela VNet, sem depender
de IP de saida, e a mesma VNet resolve a restricao das Functions (#224).

- Pela documentacao da Microsoft, VNet, sub-rede, service endpoint e perfil
  Consumption nao tem cobranca propria. A API de precos lista um medidor
  "Standard Service Endpoint Virtual Network" (US$ 0,10/h) cuja aplicacao nao ficou
  clara: **confirmar na calculadora antes de aprovar**.
- O ambiente atual nao pode ganhar VNet depois de criado: e outro ambiente, com
  migracao do app, do dominio e do certificado customizados, do blue/green e do Bicep.
  Exige janela.
- Ao ligar o service endpoint, a origem vista pelo SQL vira IP privado da sub-rede.
  A VNet rule precisa existir antes, senao a producao cai.
- Runners do GitHub e Render continuam vindo pela internet: B.2 e B.3 continuam
  necessarios, e `publicNetworkAccess` continua `Enabled`.

### D. Private Endpoint + DNS privado (custo recorrente)

Endpoint privado do servidor na VNet de C, zona `privatelink.database.windows.net` e,
no fim, `publicNetworkAccess=Disabled`. Bloqueia tudo que nao vem da VNet, entao
runner do GitHub e Render ficam sem acesso. Isso exige runner dentro da VNet
(self-hosted em VM ou runners com rede privada, que sao pagos) e tirar a homologacao
deste servidor ou dar ao Render conectividade privada. Na pratica, homologacao e
`ESCOLA_API` saem do servidor compartilhado.

### E. IP de saida fixo via NAT Gateway (custo recorrente)

Sub-rede de C com NAT Gateway e IP publico estatico: o SQL passa a liberar so 1 IP da
API. Nao resolve runner nem Render. O servidor continua publico. Com C pronto, C ja
resolve o caso da API sem custo, entao E so faria sentido se outra necessidade pedir
IP fixo (ex.: allowlist em parceiro externo).

## Comparacao de custo e complexidade

Precos de tabela da Azure Retail Prices API (25/11/2026), em USD, sem impostos,
convertidos para mes de 730 h. Sao referencia: **confirmar na calculadora** antes de
qualquer proposta.

| Opcao | Recursos novos | Custo mensal estimado | Complexidade | Risco de indisponibilidade | O que fecha |
| --- | --- | --- | --- | --- | --- |
| A. Limpeza de regras pessoais | nenhum | US$ 0 | baixa | muito baixo | 14 caminhos residenciais/temporarios |
| B. Lista de IPs + regra temporaria do runner | nenhum | US$ 0 | media | medio (troca de IPs do Container Apps) | acesso de toda a Azure global |
| C. Container Apps em VNet + service endpoint | VNet, sub-rede, novo ambiente | US$ 0 previsto (confirmar medidor do service endpoint) | alta | medio/alto (migracao do ambiente) | dependencia de IP da API; tambem habilita #224 forte |
| D. Private Endpoint | C + Private Endpoint + zona DNS + runner na VNet | Private Endpoint US$ 0,01/h (~US$ 7,30) + US$ 0,01/GB; zona DNS ~US$ 0,50; runner VM B1s Linux Brazil South US$ 0,0168/h (~US$ 12,26) + disco | muito alta | alto | exposicao publica do servidor (com mudanca de homologacao/`ESCOLA_API`) |
| E. NAT Gateway | C + NAT + IP estatico | IP Standard US$ 0,005/h (~US$ 3,65) + NAT (preco nao obtido na API; confirmar) + trafego | alta | medio | so a origem da API |

## Recomendacao

1. **Agora, se aprovado (custo zero):** A, a limpeza das regras pessoais.
2. **Depois, com janela (custo zero):** B em homologacao primeiro, depois em
   producao. Remover `AllowAllWindowsAzureIps` so com todos os positivos e negativos
   abaixo comprovados.
3. **Quando houver janela maior:** C, que tambem resolve a #224 sem custo recorrente.
4. **D e E ficam no backlog** ate haver receita, conforme a politica financeira da
   issue. Antes de D, separar homologacao e `ESCOLA_API` deste servidor.

Antes de qualquer passo que remova a regra ampla: comprovar que `ESCOLA_API` esta
mesmo sem consumidor (ou decidir com o responsavel) e conferir os IPs do Render.

## Testes exigidos em cada mudanca

Positivos, antes e depois:

- API de producao: readiness, login e um CRUD sintetico;
- API de homologacao: readiness e login;
- `publish-homologation` em modo `diagnose-sql`;
- SchemaGuard de producao (`publish-container`, etapa de migrations).

Negativo: conexao de uma origem nao autorizada (ex.: VM/Cloud Shell fora da lista, ou
runner sem a regra temporaria) deve falhar **na rede** (erro 40615, IP nao permitido),
nao na autenticacao.

Registrar so contagens, horario UTC e resultado, sem IPs nem credenciais.

## Rollback

- Guardar o inventario completo das regras (nome e faixa) fora do Git, no cofre,
  antes de qualquer remocao.
- Falha de conexao da API depois de B: recriar `AllowAllWindowsAzureIps`
  (`az sql server firewall-rule create ... --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0`)
  e investigar com a producao de pe. Esse e o unico caso em que religar a regra ampla
  e aceitavel, e deve ser registrado com prazo para nova tentativa.
- Falha em C: o ambiente antigo so e removido depois de o novo passar todos os
  positivos; o rollback e voltar o trafego para ele.
- `infra/modules/sql.bicep` declara `AllowAllWindowsAzureIps` e as regras do Render:
  qualquer mudanca aplicada no Azure precisa ir junto no Bicep, senao o proximo deploy
  de infraestrutura recria a regra removida.

## Observacoes fora do escopo

- `HemodinksDBConfirmation` aparece com `autoPauseDelay=-1` e `Paused`. Coerente com
  a oferta gratuita, mas vale conferir se o limite mensal gratuito pausou o banco.
- `azureAdOnlyAuthentication=false`: logins SQL continuam aceitos. Migrar runtime e
  migrations para identidade Entra e assunto da #197.

## Referencias

- [Controles de acesso de rede do Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/network-access-controls-overview?view=azuresql)
- [Regras de firewall e limite de 256 regras](https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure?view=azuresql)
- [VNet service endpoints para Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/vnet-service-endpoint-rule-overview?view=azuresql)
- [Private Endpoint para Azure SQL](https://learn.microsoft.com/en-us/azure/azure-sql/database/private-endpoint-overview?view=azuresql)
- [Rede do Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/networking)
- Documentos relacionados: `docs/issue-197-sql-runtime-network.md`, `docs/functions-network-restriction.md`,
  `docs/issue-198-pitr-rehearsal.md`, `docs/infra-as-code.md`.
