# Forwarded headers: production preflight

O workflow suporta o ingress HTTP gerenciado do Azure Container Apps sem exigir
IPs internos fixos. O bootstrap manual preserva a imagem existente e continua
exigindo uma allowlist explicita de proxies/redes verificados.
`TrustAnyImmediateProxy` permanece desabilitado e `ForwardLimit=1`. Configuracao
invalida interrompe o workflow antes de aplicar o bundle de migrations.
A candidata recebe exatamente o JSON validado nessa etapa, sem herdar indices
antigos, aliases ou confianca irrestrita da revisao anterior.
Se a candidata ja existir, o reuso exige que sua politica de proxy corresponda
ao preflight aprovado; divergencias bloqueiam o reuso e a promocao.

No GitHub Environment **production**, estas variaveis sao opcionais:

- `API_FORWARDED_HEADERS_KNOWN_PROXIES`: IPs verificados do proxy imediato.
- `API_FORWARDED_HEADERS_KNOWN_NETWORKS`: CIDRs canonicos verificados desse proxy.

Com ambas ausentes, vazias ou `[]`, o workflow valida o recurso Azure antes das
migrations, antes de criar a candidata e antes de promover: nome/resource group
exatos, HTTP/Auto, targetPort 8080, HTTPS obrigatorio e nenhuma porta TCP adicional.
Falha de consulta ou topologia divergente bloqueia a operacao. O preflight gera
`ForwardedHeaders__AzureContainerAppsIngress=true`, nome e sufixo DNS esperados.
A API verifica `CONTAINER_APP_NAME`, `CONTAINER_APP_REVISION` e
`CONTAINER_APP_ENV_DNS_SUFFIX` antes de iniciar. Esses marcadores identificam a
execucao; a fronteira de confianca e o ingress HTTP verificado.

A [documentacao Microsoft](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview#http-headers)
garante que ACA acrescenta o IP da direita de `X-Forwarded-For` e sobrescreve
`X-Forwarded-Proto`. O middleware especifico usa somente esse IP e um protocolo
unico http/https antes de autenticacao/rate limiting. Prefixos do cliente nunca
determinam identidade. Metadados invalidos ou maiores que 8192 caracteres
retornam 400 sem expor seu conteudo. Probes sem forwarding preservam o IP da
conexao. As listas de confianca do middleware padrao nao sao removidas.

O modo gerenciado exige que todas as conexoes HTTP de aplicacao atravessem o
ingress ACA. Exposicao direta por TCP, sidecars que reescrevem esses headers ou
mudanca dessa fronteira exigem revisao. Marcadores de ambiente isoladamente nao
autenticam uma conexao direta. Nenhuma descoberta de IP ou aprendizagem por
requisicoes ocorre; a politica nao cria recursos pagos adicionais.

Se uma lista tiver entradas, o workflow usa a politica convencional de allowlist
e desativa o modo gerenciado. A outra pode ficar vazia ou `[]`. Exemplo apenas:
`["10.20.0.4"]` e `["10.21.0.0/24"]`; esses enderecos sao ficticios e nao devem
ser copiados para producao sem verificacao. Entradas vazias, invalidas, universais,
multicast ou nao especificadas sao recusadas. Sintaxe valida nao comprova que o
IP/CIDR corresponde ao ingress. Nao inferir confianca do IP publico da aplicacao,
`staticIp` do ambiente ou de uma rede generica da VNet. Se existir mais de um proxy,
revisar a topologia antes de usar esta politica de um salto.

O helper `scripts/forwarded_headers.py` usa apenas a biblioteca padrao do Python.
Ele tambem desabilita `ASPNETCORE_FORWARDEDHEADERS_ENABLED` herdado para manter
somente o middleware explicito da API. Env vars e secretrefs alheios ao proxy
sao preservados; erros nao exibem seus valores.

Para bootstrap manual, com Python disponivel:

```powershell
./scripts/Bootstrap-ProductionBlueGreen.ps1 `
  -SubscriptionId "<subscription-id>" `
  -KnownProxies @("<IP verificado>") `
  -WhatIf
```

Pode usar `-KnownNetworks` no lugar de `-KnownProxies`. A validacao ocorre antes
de chamar Azure CLI, inclusive com `-WhatIf`. A execucao real continua exigindo
a autorizacao operacional existente.

O aquecimento da candidata tem janela de aproximadamente 10 minutos, limite nos
comandos Azure/curl e timeout de etapa de 12 minutos. Estado de inicializacao
`Failed` interrompe a espera; falha de readiness bloqueia a promocao e mostra
estado/status HTTP para diagnostico. Consultar logs console/system da revisao
candidata no Azure sem registrar credenciais. Aumentar o timeout nao corrige uma
configuracao invalida.

Verificacao local: 90 testes Python, bootstrap com Azure simulado, 48 testes de
forwarding/spoofing/rate limiting/warm-up e 20 de senha/sessao/cookies/isolamento,
todos aprovados. YAML e 25 blocos Bash validados. Evidencias .NET em
`logs/aca-ingress/aca-ingress-regression.trx` e `aca-security-regression.trx`.
Nenhum deploy ou migration foi executado; E2E no ingress real ainda e pendente.
O deploy real e a verificacao do ingress em producao permanecem etapas
operacionais posteriores a revisao desta alteracao. Nao e necessario repetir o
bootstrap em uma app ja configurada como multiple e com labels blue/green.
