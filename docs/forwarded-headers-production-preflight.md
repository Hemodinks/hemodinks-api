# Forwarded headers: production preflight

O workflow e o bootstrap usam uma allowlist explicita de proxies/redes do ingress.
`TrustAnyImmediateProxy` permanece desabilitado e `ForwardLimit=1`. Configuracao
invalida ou ausente interrompe o workflow antes de aplicar o bundle de migrations.
A candidata recebe exatamente o JSON validado nessa etapa, sem herdar indices
antigos, aliases ou confianca irrestrita da revisao anterior.
Se a candidata ja existir, o reuso exige que sua politica de proxy corresponda
ao preflight aprovado; divergencias bloqueiam o reuso e a promocao.

No GitHub Environment **production**, configurar ao menos uma destas variaveis
como array JSON de strings:

- `API_FORWARDED_HEADERS_KNOWN_PROXIES`: IPs verificados do proxy imediato.
- `API_FORWARDED_HEADERS_KNOWN_NETWORKS`: CIDRs canonicos verificados desse proxy.

A variavel nao utilizada pode ficar vazia ou `[]`. Exemplo de formato apenas:
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

Verificacao local: testes de validacao/merge, sintaxe YAML/Bash/PowerShell e
regressoes de forwarding/spoofing. Nenhum deploy ou migration foi executado.
A sessao Azure local estava expirada; os IPs/CIDRs reais ainda precisam ser
confirmados e configurados no Environment antes da proxima publicacao.
