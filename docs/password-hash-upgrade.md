# Fortalecimento do hash de senhas

## Política e compatibilidade

Novas senhas usam PBKDF2-HMAC-SHA256, **600.000 iterações**, salt aleatório de 16 bytes e chave de 32 bytes. O custo segue a [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html), consultada durante esta implementação. O formato permanece `PBKDF2-SHA256$iterações$saltBase64$chaveBase64`.

`IPasswordHasher.VerifyPasswordWithRehash` distingue credencial inválida, válida e válida que precisa de atualização. A API booleana existente continua disponível. A comparação usa `CryptographicOperations.FixedTimeEquals`.

O verificador aceita:

- Legado: Base64 de salt de 16 bytes concatenado com chave de 20 bytes, PBKDF2-HMAC-SHA256/10.000. Exige atualização.
- Formato versionado existente: salt de 16 bytes e chave de 32 bytes, entre 10.000 e 2.000.000 iterações. Custos abaixo de 600.000 exigem atualização; custos maiores aceitos são preservados.

Antes da derivação, valida algoritmo, número de campos, inteiro positivo sem sinal ou espaços, tamanhos codificados e decodificados, Base64 e limite total de 128 caracteres. O teto de 2.000.000 limita trabalho comandado pelo valor armazenado e admite custos superiores ao alvo. Valores acima desse teto são rejeitados, nunca convertidos para um custo menor. Aumentar a política futura exige revisar explicitamente esse limite e medir seu custo.

PINs de operadores mantêm 210.000 iterações e não recebem rehash automático. `IPinHasher` separa essa política; Infrastructure compartilha as primitivas e o parser validado. Geração, redefinição e verificação de PINs usam essa abstração.

## Momento da migração e isolamento

A descoberta de clínicas continua verificando cada hash distinto uma única vez por requisição, sem rehash nem gravações de migração. A senha não fica em cache entre requisições.

O handler de autenticação da clínica selecionada recebe o resultado da verificação global e faz o rehash somente depois de validar usuário, clínica, vínculo, identidade, bloqueio e equipe ativa, quando aplicável, e de obter a licença. Erros de senha, bloqueio ou inatividade não geram um novo hash. A consulta inicial reutiliza vínculos existentes sem chamar sua rotina de sincronização: isso evita que a autenticação reative uma identidade global inativa ou altere a marca de confirmação da credencial antes de verificá-la.

Em equipes, a existência da equipe ativa é validada antes da confirmação de qualquer credencial legada; uma equipe ausente/inativa não pode provocar nem a cópia permitida pelo fallback local. A migração ocorre no sucesso da **etapa da senha coletiva**. Para modos PIN/seleção, essa etapa retorna apenas o desafio; o JWT continua dependendo da identificação válida do operador. A etapa do PIN não recebe nem armazena a senha coletiva e não faz rehash. Os testes HTTP cobrem os três modos e a rejeição de PIN incorreto.

A atualização técnica grava apenas `UsuarioGlobal.Senha`, a credencial canônica. Não modifica cópias `User.Senha`, outros vínculos, `SecurityVersion`, datas de atualização já preenchidas ou sessões existentes. Não usa `IgnoreQueryFilters` nem sincronização por email para migrar. O fallback local permanece limitado à identidade com `DataAtualizacao == null`, conforme a regra existente; credenciais globais confirmadas nunca aceitam esse fallback. A autenticação válida preenche essa marca somente se ausente, inclusive quando a senha global já tem custo forte, para fechar a transição legada. Isso substitui a confirmação feita anteriormente pela sincronização anterior à verificação, sem reativar identidades inativas.

Geração de senha, recuperação, primeiro acesso e alteração real de senha recebem o novo custo através de `IPasswordHasher`. Credenciais temporárias continuam com suas regras próprias de uso único; não se tenta migrar uma senha temporária já consumida. Recuperação real mantém a revogação de sessões e a mudança de `SecurityVersion` existentes.

Limite desta migração: ela não elimina hashes fracos ainda presentes nas cópias locais legadas. Se uma dessas cópias representar a mesma senha atual, seu custo antigo continua relevante em um vazamento do banco. A remoção ou migração dessas cópias exige uma política própria de compatibilidade e de vínculos entre clínicas; não foi feita uma sincronização transversal para ocultar esse problema. Também permanecem antigos os hashes de identidades que ainda não realizaram um login válido.

## Concorrência e persistência

`UsuarioGlobal.Senha` passa a ser token de concorrência otimista do EF, junto com `SecurityVersion`. O `UPDATE` exige os valores originais: um rehash não pode sobrescrever uma senha alterada ou recuperada após a leitura. Isso também cobre a alteração comum de senha, que atualmente não muda `SecurityVersion`.

Se outro login já atualizou o hash, a operação perdedora recarrega a identidade e descarta sua alteração pendente. Só continua se a versão de segurança permanece igual, a identidade está ativa, não está bloqueada/em recuperação temporária e a senha fornecida ainda valida a credencial atual. Não repete a gravação. Uma recuperação concorrente é rejeitada mesmo quando redefine a mesma senha, pois altera a versão de segurança.

O formato cabe na coluna existente. A alteração de concorrência é metadado de mapeamento; o snapshot foi ajustado e não há migração SQL nem alteração de esquema. A verificação `dotnet ef migrations has-pending-model-changes` confirmou consistência.

## Arquivos e responsabilidades

| Área | Arquivos |
| --- | --- |
| Contratos | `Application/Utils/IPasswordHasher.cs`, `IPinHasher.cs` |
| Criptografia e política de PIN | `Infrastructure/PasswordHasher.cs`, `PinHasher.cs` |
| Orquestração | `Application/Authentication/GlobalIdentityService.cs`, `PasswordHashUpgrade.cs`, `Features/Users/Commands/AuthenticateUserCommandHandler.cs` |
| Concorrência | `Infrastructure/Data/Configurations/UsuarioGlobalConfiguration.cs`, `Data/Migrations/AppDbContextModelSnapshot.cs` |
| Consumidores de PIN | Parciais `TeamUseCases`, `TeamAuthenticationUseCases`, `TeamMembershipUseCases`, `ClinicaPlatformTeamRequestHandler` |
| Composição | `Api/ApiServiceCollectionExtensions.Application.cs` |
| Testes | `PasswordHasherTests`, `LoginContextWorkTests`, `UserCommandHandlerPasswordUpgradeTests`, `PasswordHashUpgradeConcurrencyTests`, `PasswordHashUpgradeEndpointTests`, helper `PasswordHashTestData` |

Os prefixos dos projetos são `HemodinksAPI.`. Nenhuma dependência concreta de Infrastructure foi adicionada à Application; endpoints e contratos HTTP continuam iguais.

## Medição local

Windows 10.0.26300, .NET 10.0.12, 12 processadores lógicos; build Debug. Para cada cenário foram descartadas duas execuções de aquecimento e medidas sete execuções sequenciais, sem a suíte completa em paralelo. Valores em milissegundos:

| Cenário | Mediana | Mínimo–máximo |
| --- | ---: | ---: |
| Gerar hash, 210.000 | 24,85 | 22,66–27,79 |
| Gerar hash, 600.000 | 66,75 | 65,16–69,38 |
| Verificar hash, 210.000 | 22,46 | 21,88–24,77 |
| Verificar hash, 600.000 | 64,13 | 63,16–67,03 |
| Descoberta, identidade compartilhada por 3 clínicas, 210.000 | 31,48 | 29,66–36,28 |
| Descoberta, identidade compartilhada por 3 clínicas, 600.000 | 89,15 | 85,13–93,19 |
| Login selecionado, verificar 210.000 e migrar para 600.000 | 123,30 | 116,66–134,08 |
| Login selecionado, credencial já em 600.000 | 93,05 | 86,64–96,96 |

A derivação isolada aumentou cerca de 2,69 vezes. Os cenários de login executam os handlers reais, com EF InMemory, usuário controller, serviço de licença real, proteção de conta sem operações e JWT simulado. Não incluem HTTP, TLS, SQL Server, persistência de sessão, latência de rede nem contenção em produção. A diferença entre os dois últimos cenários não é uma comparação antes/depois do sistema: o primeiro faz duas derivações e o segundo apenas verifica o hash forte. O custo de migração ocorre uma vez por identidade; a descoberta posterior passa a verificar o hash forte.

O programa diagnóstico e sua saída local estão em `logs/password-hash-benchmark/` (ignorado pelo Git). Para repetir no mesmo workspace: `dotnet run --no-restore --project logs/password-hash-benchmark/Bench.csproj`. A senha usada é sintética e não é impressa. Esses números não representam capacidade sob carga nem p95/p99 de produção.

## Validação

- Teste inicial falhou com 210.000 em vez de 600.000, antes da implementação.
- 63 testes focados aprovados na versão final, incluindo testes HTTP de equipes/isolamento. Os testes de confirmação da credencial e de fallback com equipe inválida também foram executados sem as respectivas correções (falharam) e com elas (passaram).
- Testes SQLite usam os mapeamentos de produção e contextos independentes com leituras intercaladas: troca de senha, recuperação, dois logins com a mesma senha e estados de segurança alterados. Verificam também que salvar novamente o contexto perdedor não restaura seu hash descartado.
- Compilação final da solução completa aprovada: zero erros e 16 avisos `IDE0005` sobre diretivas desnecessárias. Verificação de modelo EF aprovada; `git diff --check` aprovado.
- Suíte completa final com SQL Server descartável, LocalDB e navegador: **600 aprovados, zero falhas, zero ignorados**, em 5 min 10 s. Inclui 11 cenários de navegador usando a API real. O contêiner criado para a execução foi removido ao terminar.

Evidências locais: `logs/password-hash/final-build.log`, `logs/password-hash/final-focused.log`, `logs/password-hash/final/password-hash-final.trx`, `logs/password-hash-model.log` e `logs/password-hash-benchmark/results.log`. Comandos principais: `dotnet build HemodinksAPI.slnx --no-restore`, `dotnet test HemodinksAPI.slnx --no-build --no-restore` (com as variáveis dos ambientes de teste configuradas pelo script local `logs/password-hash/run-final-validation.ps1`) e `dotnet ef migrations has-pending-model-changes --context AppDbContext --project HemodinksAPI.Infrastructure --startup-project HemodinksAPI.Api --no-build`.

Não ficaram verificações funcionais pendentes. Não foi realizado teste de carga nem medição em produção; o benchmark acima tem apenas o alcance local descrito.

Nenhum deploy foi executado e nenhum dado de produção foi modificado.
