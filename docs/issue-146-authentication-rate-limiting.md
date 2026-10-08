# API #146 — limites de autenticação e contrato para WEB #154

Alteração de risco alto: autenticação, PIN e concorrência. Branch `developer`.

## Contrato HTTP

Os limites HTTP retornam **429**, `Content-Type: application/json`,
`Cache-Control: no-store` e `Retry-After: N` (segundos inteiros positivos,
arredondados para cima). O CORS `Frontend` expõe `Retry-After` às origens permitidas.

```json
{"code":"rate_limited","message":"Muitas tentativas. Aguarde antes de tentar novamente.","retryAfterSeconds":300,"requestId":"identificador-da-requisicao"}
```

`retryAfterSeconds` coincide com o cabeçalho. O exemplo não fixa a duração:
a janela restante depende da cota que recusou a requisição. Ao terminar a espera,
outra cota ainda pode recusar uma nova tentativa. A resposta não revela existência
de conta, clínica, vínculo, PIN ou token; não cria sessão nem remove cookies.

Aplicável a `POST /api/users/login-context`, `/api/users/authenticate`,
`/api/equipe-auth/identificar`, `/api/users/password/reset`,
`/api/users/password/reset/confirm` e aos limitadores nomeados existentes,
incluindo renovação de sessão. Validação, autorização, CSRF e resolução de clínica
continuam podendo recusar a requisição antes do limitador por identidade.

Para WEB #154: mostrar a mensagem genérica, impedir submissões simultâneas e
aguardar o tempo informado antes de habilitar nova tentativa voluntária. Não
repetir automaticamente senha, PIN ou escritas; 429 não significa logout ou
expiração da sessão. O bloqueio persistido por credencial mantém a resposta
401 genérica existente, sem expor duração ou existência da conta.

## Cotas e configuração

Seção `AuthenticationRateLimiting` (também disponível como variáveis com `__`):

| Propriedade | Padrão | Unidade / partição |
| --- | ---: | --- |
| LoginPermitLimit | 30 | requisições por email normalizado; descoberta e autenticação compartilham cota |
| OperatorPermitLimit | 5 | requisições por desafio + operador |
| RecoveryPermitLimit | 5 | por email na solicitação; por token na confirmação, cotas separadas |
| SubjectWindowSeconds | 300 | janela das cotas acima |
| IpPermitLimit | 300 | requisições admitidas pela cota de identidade, por IP, nos cinco endpoints acima |
| IpWindowSeconds | 60 | janela de origem |
| SessionPermitLimit | 300 | requisições por IP na política SessionRefresh |
| SessionWindowSeconds | 60 | janela de renovação |

Limites positivos; cotas de identidade até 10.000, origem/sessão até 100.000,
janelas até 86.400 segundos. Configuração inválida impede inicialização.
Identificadores têm tamanho limitado e são particionados por HMAC com chave
aleatória local, sem armazenar emails/tokens em claro nas chaves. IPv4 e sua
representação IPv6 mapeada compartilham a cota de origem. Slug/ClinicaId enviados
pelo cliente não selecionam a cota de identidade. A cota do desafio não concede
acesso ao operador: continuam obrigatórias as verificações de desafio, equipe,
clínica, vínculo, PIN e SecurityVersion existentes.

Uma identidade já limitada não consome a cota compartilhada de origem. Outra
conta no mesmo IP continua disponível enquanto a cota ampla de origem permitir.
Um ataque distribuído por muitas identidades pode atingir essa cota; dimensionar
por ambiente conforme o uso real, sem remover a proteção.

As cotas HTTP são **por processo**, reiniciam no restart e não são coordenadas
entre réplicas. Os bloqueios por senha/PIN continuam persistidos no banco e
compartilhados entre réplicas. `LoginProtection` já configura falhas por identidade
global (padrão 5 em janela de 15 minutos, bloqueio de 15 minutos). PIN conserva a
regra existente de 5 falhas até sucesso e bloqueio de 15 minutos. Incrementos SQL
são atômicos; sucesso atrasado não limpa bloqueio ativo. Bloqueio global expirado
reinicia a contagem, evitando novo bloqueio imediato. Não há migration de schema;
os tokens de concorrência do operador são metadados EF sobre colunas existentes.
Descoberta já registra uma falha por identidade global, independentemente do
número de vínculos; isso foi preservado. Bloqueios temporários por conta ainda
podem ser provocados por quem conhece o identificador — risco inerente à regra
existente, limitado por janela e pelas cotas HTTP.

A observabilidade existente registra recusas HTTP como `Throttled/rate_limited`,
sem clínica presumida no pré-login e com identificadores minimizados. Nenhum
registro de senha, PIN ou token completo foi adicionado.

## Proxy e publicação pendente

`ForwardedHeaders` só deve aceitar proxies/redes explicitamente confiáveis,
com `ForwardLimit` adequado ao caminho real. `TrustAnyImmediateProxy=true` agora
é recusado quando forwarding está habilitado. Configurar `KnownProxies` e/ou
`KnownNetworks` com a fronteira real; sem configuração explícita, permanecem
somente os padrões loopback do framework. Não usar uma rede universal.

**Antes de publicar:** o workflow atual ainda define `TrustAnyImmediateProxy`.
Ele precisa ser revisto com a topologia confirmada em homologação; não foi
alterado nem executado nesta tarefa. Não publicar esta versão com aquela opção
habilitada. Cabeçalhos de origem de clientes não confiáveis são ignorados;
o teste do proxy confiável confirma que o prefixo fornecido pelo cliente não
substitui o último salto confiável.

## Evidências

Testes e resultados finais são registrados em `logs/issue-146/*.trx` e no
relatório de revisão. Os bancos dos testes SQL são exclusivos e descartáveis;
nenhum banco de aplicação é migrado. Nenhum commit, push, merge ou deploy foi feito.
