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

Gere numa máquina confiável e **use material diferente** em produção e em homologação:

```powershell
pwsh ./scripts/New-CpfProtectionKeys.ps1 -Environment Production
pwsh ./scripts/New-CpfProtectionKeys.ps1 -Environment Confirmation
```

O script cria uma pasta em `~/hemodinks-cpf-keys/` (e recusa qualquer pasta dentro do repositório) com
`cpf.pfx` (RSA 3072, 10 anos), `CertificateBase64.txt`, `CertificatePassword.txt` (senha aleatória),
`LookupHashKey.txt` (32 bytes aleatórios) e um `LEIA-ME.txt` dizendo para qual variável vai cada arquivo.
Ele relê o PFX como a API faz no startup e não mostra nenhum valor na tela.

**Backup obrigatório antes do primeiro deploy:** guarde a pasta inteira num cofre de senhas, fora do banco
e do Azure. Sem o certificado, as cifras não podem mais ser lidas. Na fase 1 isso é recuperável, porque a
coluna legada existe. Depois da fase 2, a perda é definitiva. Apague a pasta local depois do backup.

## Configurar

- **Produção (Container Apps):** crie três secrets no Container App e referencie-os por variável
  (`CpfProtection__CertificateBase64`, `CpfProtection__CertificatePassword`, `CpfProtection__LookupHashKey`
  como `secretref:`). Prefira o portal: valores passados na linha de comando ficam no histórico do shell.
  > **Atualizado em 12/11/2026:** crie só os secrets `cpf-protection-certificate`,
  > `cpf-protection-certificate-password` e `cpf-protection-lookup-hash-key`. Não edite variáveis: o deploy
  > copia as da revisão ativa e agora referencia esses três secrets sozinho
  > (ver [deployment.md](deployment.md#azure-container-apps)). Variáveis de rotação (`Previous*`) ainda precisam
  > estar na revisão ativa para serem copiadas; tratar quando houver a primeira rotação.
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

## Rotação (certificado e chave de hash)

As duas rotações usam o mesmo procedimento e podem ser feitas juntas. Nada é apagado do banco.

1. Gere material novo com o script (pasta nova).
2. No ambiente, mova os valores **atuais** para as variáveis de anterior e coloque os novos como atuais:

   | Valor | Vai para |
   | --- | --- |
   | certificado e senha atuais | `CpfProtection__PreviousCertificates__0__CertificateBase64` e `__CertificatePassword` |
   | chave de hash atual | `CpfProtection__PreviousLookupHashKeys__0` |
   | arquivos da pasta nova | `CpfProtection__CertificateBase64`, `__CertificatePassword`, `__LookupHashKey` |

3. Publique uma revisão. No startup:
   - o certificado anterior serve só para **ler** as chaves antigas; chaves novas são cifradas pelo atual;
   - se a chave padrão do Data Protection ainda for do certificado anterior, uma chave nova é criada
     (log `chaves de certificado anterior no anel; chave nova criada: True`);
   - o reconciliador recifra os CPFs que usam chaves do certificado anterior e regrava os hashes com a
     chave atual;
   - busca e unicidade aceitam o hash da chave atual e o das anteriores enquanto a rotação não termina.
4. Espere a revisão anterior ser desativada. Até lá ela ainda grava com a chave antiga e, se reiniciar,
   regrava com a chave antiga as linhas que não consegue ler. Depois disso, reinicie a revisão nova (ou
   publique outra) para rodar **mais uma** reconciliação. Só o log desse segundo startup vale como
   critério: `Reconciliacao de CPF concluida` com `0 falhas`, `0 cifras ilegiveis` e
   `0 cifras pendentes de recifragem`. Confira também no banco:

   ```sql
   SELECT COUNT_BIG(*) AS SemCifra FROM dbo.Users WHERE Cpf IS NOT NULL AND (CpfCifrado IS NULL OR CpfHash IS NULL);
   ```

5. Só então remova as variáveis `PreviousCertificates__*` e `PreviousLookupHashKeys__*` e publique de novo.
   As chaves antigas continuam na tabela, ilegíveis sem o certificado anterior, mas nenhum CPF depende delas.
6. Mantenha o material anterior no cofre até a revisão sem as variáveis de anterior estar estável.

No blue/green, a revisão antiga não conhece o certificado novo durante a janela de troca. Na fase 1 isso não
afeta a leitura, que é da coluna legada. Na fase 2, a primeira publicação da rotação deve incluir o
certificado novo só como anterior, e a troca para atual vem numa segunda publicação.

## Rollback

- **Imagem:** volte para a revisão anterior. Ela lê e grava a coluna `Cpf`, que segue íntegra. Linhas
  alteradas nesse período ficam com cifra ou hash desatualizados, e o reconciliador corrige no próximo
  deploy da fase 1.
- **Schema:** o `Down` remove apenas `CpfCifrado`, `CpfHash`, o índice novo e `DataProtectionKeys`. A coluna
  legada não é tocada. Normalmente não é necessário, porque a imagem anterior ignora as colunas novas.

## Erros de gravação de usuário sem CPF no log

O SQL Server repete na mensagem de erro o valor que violou um índice (por exemplo, o CPF no índice
`(ClinicaId, Cpf)`) ou que foi truncado. Numa gravação que envolve `User`, o `AppDbContext` troca a
`DbUpdateException` por outra só com o número do erro SQL e o nome do índice, sem `InnerException`. Os
eventos do EF que registravam a exceção bruta (`SaveChangesFailed` e `CommandError`) passaram para Debug.
A exceção que se propaga continua registrada pelo middleware de erros. A resposta HTTP não muda.

## Fase 2 (PR separado, destrutivo, só com pedido explícito)

Pré-requisitos: fase 1 estável em produção, contagem `SemCifra = 0` e backup do certificado confirmado.
Escopo: passar a leitura para `CpfCifrado`, zerar e remover a coluna `Cpf` e o índice `(ClinicaId, Cpf)`,
e ajustar `AnonymizeProductionCopy.sql`, que hoje anonimiza `Cpf` e limpa a cifra e o hash.

## Limitações conhecidas

- O reconciliador roda só no startup, para não manter o Azure SQL serverless acordado. Uma linha gravada pela
  imagem anterior durante a janela blue/green fica sem hash até o próximo deploy. Nesse intervalo a busca
  não a encontra, mas a unicidade continua correta, porque também compara a coluna legada.
- Não há mais busca por parte do CPF, por decisão de produto. O `hemodinks-front` deve orientar a busca
  pelo CPF completo.
- A rotação só termina no startup de uma revisão (o reconciliador não roda periodicamente). Até lá o
  certificado e a chave de hash anteriores precisam continuar configurados.
- A rotação exige `CpfProtection__ReconcileOnStartup` ligado (é o padrão). Desligado, nem a chave nova é
  criada nem as cifras são refeitas.
- O número de "chaves de certificado anterior no anel" nunca chega a zero, porque as chaves antigas ficam na
  tabela. O critério de fim é `0 cifras pendentes de recifragem`.
- Para identificar a chave de cada CPF cifrado, o código lê o cabeçalho documentado do payload do Data
  Protection (magic header + id da chave). Não há criptografia própria.
- O hash não inclui `ClinicaId`. Quem tiver acesso ao banco consegue ver que o mesmo CPF aparece em duas
  clínicas, mas não consegue saber qual é o CPF sem a chave.
- Até a fase 2, o CPF continua existindo em texto puro. O ganho de LGPD só se completa na fase 2.
