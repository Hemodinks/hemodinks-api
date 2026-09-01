# Controles de segurança por ambiente

Os controles abaixo fazem parte do contrato de implantação da API e devem ser
validados antes da promoção para homologação ou produção.

## Origem e transporte

- Configure `Cors:AllowedOrigins` explicitamente em cada ambiente.
- Produção aceita somente origens HTTPS e rejeita endereços loopback ou
  domínios `localhost`.
- O proxy de borda deve redirecionar HTTP para HTTPS e preservar os cabeçalhos
  encaminhados apenas de proxies confiáveis.

## Limitação de requisições

A limitação local da aplicação é uma proteção complementar. Em produção com
mais de uma instância, o gateway/WAF deve aplicar uma política compartilhada
por IP e políticas mais restritivas para login, seleção de clínica, desafios de
PIN e solicitação/confirmação de redefinição de senha.

## Logs e auditoria

- Os arquivos locais de log são mantidos por no máximo 30 arquivos diários.
- O acesso ao endpoint de monitoramento permanece restrito ao perfil de
  administrador da plataforma.
- A retenção da auditoria persistida deve ser definida pela política legal da
  organização e executada por rotina administrativa, sem apagar registros sob
  retenção legal.
- Não enviar CPF, e-mail completo, SQL bruto, conteúdo clínico ou corpo de
  requisição para logs, APM ou alertas.

## Arquivos clínicos

Documentos clínicos devem permanecer em armazenamento privado e ser entregues
somente pelos endpoints autenticados. Respostas de download usam
`Cache-Control: no-store, private`, `Pragma: no-cache` e
`X-Content-Type-Options: nosniff`.
