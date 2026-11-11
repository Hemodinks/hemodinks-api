# Issue #227 — CPF protegido em repouso

Migração gradual (expand/contract) do `Users.Cpf`, que hoje fica em texto puro.
Esta entrega é a **fase 1, aditiva**. A remoção do texto puro é a fase 2: destrutiva, num PR
separado e só com pedido explícito.

## O que muda na fase 1

| Item | Antes | Depois |
| --- | --- | --- |
| Gravação | `Cpf` em texto | `Cpf` (legado) + `CpfCifrado` (ASP.NET Data Protection) + `CpfHash` (HMAC-SHA256 com chave) |
| Leitura (DTOs, telas, exportações) | `Cpf` | `Cpf`, sem mudança |
| Unicidade por clínica | `Cpf ==` + índice `(ClinicaId, Cpf)` | `CpfHash == hash \|\| Cpf == cpf` (`CpfQueryExtensions.WhereCpfMatches`, para cobrir linhas ainda sem hash) + novo índice único `(ClinicaId, CpfHash)`; o índice legado continua |
| Busca de usuários e pacientes | parte do CPF (`Contains`) | **somente o CPF completo** (11 dígitos, com ou sem máscara) |

- Migration `20261111154833_Schema_ProtectCpfAtRest`. É só aditiva: as colunas `CpfCifrado` e `CpfHash` são
  anuláveis, e ela cria o índice filtrado e a tabela `DataProtectionKeys`.
- O anel de chaves é dedicado ao CPF (`HemodinksAPI.Cpf`) e não altera o Data Protection padrão da API. Fica em
  `DataProtectionKeys`, compartilhado entre as réplicas, com o XML cifrado pelo certificado. O banco sozinho não
  decifra o CPF.
- `CpfProtectionReconciliationService` roda no startup de cada revisão. Ele recalcula cifra e hash a partir
  da coluna legada nas linhas sem cifra, com cifra ilegível ou com hash divergente. É idempotente e
  condicionado ao valor lido, de modo que uma edição concorrente prevalece. Registra apenas contagens.
- Fail-closed: fora de Development/Testing, a API não sobe sem certificado e chave de hash. Um contexto sem
  protetor recusa gravar CPF.
- Teste de arquitetura (`CpfProtectionTests.Application_never_filters_on_plaintext_cpf`): o Application não pode
  filtrar pela coluna em texto.

A leitura continua na coluna legada de propósito. No blue/green, a revisão antiga grava só a coluna legada
por alguns minutos, e ler a cifra na fase 1 poderia trazer um valor desatualizado. A leitura passa para a
cifra na fase 2, quando todas as revisões em execução já gravam os três campos.

## Custo

Zero. O certificado é autoassinado, os secrets ficam no Container Apps, no Render e no GitHub (sem cobrança) e
não há Key Vault nem pacote novo. A tabela de chaves e as duas colunas ocupam poucos KB.

## Gerar o material de chave (uma vez por ambiente)

Gere numa máquina confiável, fora do repositório, e **use material diferente** em produção e em homologação.

```bash
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
  -subj "/CN=hemodinks-cpf-protection" -keyout cpf.key -out cpf.crt
openssl pkcs12 -export -inkey cpf.key -in cpf.crt -out cpf.pfx -passout pass:'<senha-forte>'
base64 -w0 cpf.pfx > cpf.pfx.b64          # CpfProtection__CertificateBase64
openssl rand -base64 32 > cpf-lookup.key  # CpfProtection__LookupHashKey
```

**Backup obrigatório antes do primeiro deploy:** guarde `cpf.pfx`, a senha e a chave de hash num cofre de
senhas, fora do banco e do Azure. Sem o certificado, as cifras não podem mais ser lidas. Na fase 1 isso é
recuperável, porque a coluna legada existe. Depois da fase 2, a perda é definitiva. Apague os arquivos locais
depois do backup.

## Configurar

- **Produção (Container Apps):** crie três secrets no Container App e referencie-os por variável
  (`CpfProtection__CertificateBase64`, `CpfProtection__CertificatePassword`, `CpfProtection__LookupHashKey`
  como `secretref:`). Prefira o portal: valores passados na linha de comando ficam no histórico do shell.
- **Homologação (Render):** preencha as três chaves `sync: false` de `render.confirmation.yaml` no dashboard.
- **Local:** sem configuração, Development usa chaves efêmeras e o reconciliador regrava tudo a cada
  startup. Para testar o caminho persistente, use `dotnet user-secrets` com material próprio de dev.

## Ordem de deploy

1. Secrets configurados no ambiente. Sem isso a nova revisão não sobe, e o blue/green mantém a anterior.
2. Pipeline normal: a migration é aplicada pelo workflow e só depois a revisão nova recebe tráfego.
3. Conferir no log `Reconciliacao de CPF concluida` com `0 falhas` e `0 cifras ilegiveis`.
4. Conferir no banco (só contagens):

```sql
SELECT COUNT_BIG(*) AS SemCifra FROM dbo.Users WHERE Cpf IS NOT NULL AND (CpfCifrado IS NULL OR CpfHash IS NULL);
SELECT COUNT_BIG(*) AS Chaves FROM dbo.DataProtectionKeys;
```

## Rollback

- **Imagem:** volte para a revisão anterior. Ela lê e grava a coluna `Cpf`, que segue íntegra. Linhas
  alteradas nesse período ficam com cifra ou hash desatualizados, e o reconciliador corrige no próximo
  deploy da fase 1.
- **Schema:** o `Down` remove apenas `CpfCifrado`, `CpfHash`, o índice novo e `DataProtectionKeys`. A coluna
  legada não é tocada. Normalmente não é necessário, porque a imagem anterior ignora as colunas novas.

## Fase 2 (PR separado, destrutivo, só com pedido explícito)

Pré-requisitos: fase 1 estável em produção, contagem `SemCifra = 0` e backup do certificado confirmado.
Escopo: passar a leitura para `CpfCifrado`, zerar e remover a coluna `Cpf` e o índice `(ClinicaId, Cpf)`,
e ajustar `AnonymizeProductionCopy.sql`, que hoje anonimiza `Cpf` e limpa a cifra e o hash.

## Limitações conhecidas

- O reconciliador roda só no startup, para não manter o Azure SQL serverless acordado. Uma linha gravada pela
  imagem anterior durante a janela blue/green fica sem hash até o próximo deploy. Nesse intervalo a busca
  não a encontra, mas a unicidade continua correta, porque também compara a coluna legada.
- A rotação do certificado e o versionamento da chave de hash são **pré-requisitos da fase 2**: hoje só o
  certificado atual é aceito para ler o anel de chaves.

- Não há mais busca por parte do CPF, por decisão de produto. O `hemodinks-front` deve orientar a busca
  pelo CPF completo.
- Não há rotação da chave de hash: trocar `LookupHashKey` exige recalcular todos os hashes (o reconciliador
  faz isso no startup), e enquanto isso a unicidade e a busca ficam degradadas.
- A rotação do certificado exige uma mudança de código, porque o certificado antigo precisa continuar
  aceito para ler as chaves existentes. Ele tem validade de 10 anos.
- O hash não inclui `ClinicaId`. Quem tiver acesso ao banco consegue ver que o mesmo CPF aparece em duas
  clínicas, mas não consegue saber qual é o CPF sem a chave.
- Até a fase 2, o CPF continua existindo em texto puro. O ganho de LGPD só se completa na fase 2.
