# Implementacao - Hemodinks API

Este documento resume a implementacao atual do backend.

## Arquitetura

O projeto usa Minimal APIs com extensoes por modulo:

- `UserEndpointExtensions`
- `PacienteEndpointExtensions`
- `CbhpmEndpointExtensions`
- `DashboardEndpointExtensions`

Cada endpoint delega a operacao para MediatR:

- Commands: criacao, atualizacao, exclusao, upload, importacao.
- Queries: listagens, detalhes, dashboard, CBHPM.

O `AppDbContext` concentra o mapeamento EF Core e aplica indices, chaves, relacionamentos e seed de perfis.

## Modulos

### Users

Responsavel por autenticacao, cadastro de usuarios, senha, foto de perfil e documentos de cadastro medico.

Classes principais:

- `User`
- `Perfil`
- `UserArquivo`
- `JwtTokenService`
- `PasswordHasher`
- `UserPatientSyncService`
- `AzureBlobProfilePhotoStorage`
- `AzureBlobPatientFileStorage`

### Pacientes

Responsavel pelo cadastro clinico/administrativo do paciente, vinculo com usuario do perfil Pacientes, anexos e selecao de procedimento CBHPM.

Campos CBHPM persistidos no paciente:

- `CbhpmCodigo`
- `CbhpmPorte`
- `Procedimento`

Quando o payload informa `CbhpmCodigo`, o backend consulta o cache CBHPM e normaliza `Procedimento` e `CbhpmPorte` com o item cadastrado na tabela `CBHPMGeral`.

### CBHPM

Responsavel pela consulta paginada e importacao administrativa da tabela CBHPM geral.

Fluxo de leitura:

1. Endpoint `GET /api/cbhpm` recebe filtros `page`, `pageSize`, `search`, `codigo`, `procedimento`, `porte`.
2. Handler `GetCbhpmGeralQueryHandler` consulta `ICbhpmCache`.
3. `CbhpmCache` carrega todos os itens da tabela `CBHPMGeral` na primeira chamada.
4. Filtro e paginacao rodam em memoria.
5. Resposta retorna `PagedResult<CbhpmGeralDto>`.

Fluxo de invalidacao:

- `ImportCbhpmGeralCommandHandler` invalida o cache depois de salvar alteracoes.
- `CbhpmSeeder` invalida o cache depois do seed inicial.

### Dashboard

Responsavel por resumo e notificacoes. As consultas respeitam o perfil do usuario autenticado.

## Documentacao da API

A API expoe:

- Swagger UI: `/swagger`
- Scalar UI: `/scalar`
- OpenAPI JSON: `/openapi/v1.json`
- Swagger JSON: `/swagger/v1/swagger.json`

O documento OpenAPI inclui:

- titulo `Hemodinks API`
- versao `v1`
- descricao dos modulos
- esquema de seguranca `Bearer`

## Persistencia

Banco relacional: SQL Server local, SQL Server em container ou Azure SQL Database.

Tabelas:

- `Perfis`
- `Users`
- `Pacientes`
- `PacienteArquivos`
- `UserArquivos`
- `CBHPMGeral`

Migrations relevantes:

- `InitialCreate`
- `AddUserProfiles`
- `AddUserProfilePhoto`
- `AddPatientsAndCpf`
- `FixPatientFileUrls`
- `AddUserArquivos`
- `AddUserUpdateDate`
- `AddCbhpmGeral`
- `AddPacienteCbhpmSelection`

## Arquivos e Azure Storage

Fotos de perfil:

- service: `IProfilePhotoStorage`
- implementacao: `AzureBlobProfilePhotoStorage`
- container padrao: `profile-photos`

Anexos:

- service: `IPatientFileStorage`
- implementacao: `AzureBlobPatientFileStorage`
- container padrao: `patient-files`

Se a connection string do Azure Storage nao estiver configurada, operacoes sem upload continuam funcionando; uploads retornam erro de configuracao.

## Cache CBHPM

Implementacao:

- interface: `ICbhpmCache`
- classe: `CbhpmCache`
- provider: `IMemoryCache`

Configuracao:

- expiracao absoluta: 12 horas
- expiracao deslizante: 2 horas
- prioridade alta

Esse cache e local ao processo da API. Em deploy com mais de uma instancia, cada instancia tera seu proprio cache.

## Azure Queue

Nao existe uso de Azure Queue Storage ou Azure Service Bus no codigo atual. O ponto natural para introduzir fila futuramente seria:

- processamento de uploads grandes
- geracao de relatatorios
- notificacoes assincronas
- auditoria fora do fluxo principal da requisicao

## Testes

Suite atual:

```powershell
dotnet test .\HemodinksAPI.Tests\HemodinksAPI.Tests.csproj
```

Cobertura existente valida:

- comandos e queries de usuarios
- regras de pacientes
- consulta CBHPM com cache
- importacao/seed CBHPM
- permissoes e filtros principais

## Observacoes de custo

O uso de `IMemoryCache` nao cria recurso pago separado na Azure; ele consome memoria da propria instancia onde a API roda. Azure SQL Database e Azure Blob Storage continuam sendo recursos cobrados conforme uso/plano. Azure Queue so gera custo se um recurso desse tipo for criado e usado.
