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
- Os endpoints de monitoramento (`/api/monitoramento/erros` e `/seguranca`) exigem a
  política `Administrador`: o Administrador vê somente a própria clínica e o
  SuperAdministrador vê todas.
- A retenção da auditoria persistida deve ser definida pela política legal da
  organização e executada por rotina administrativa, sem apagar registros sob
  retenção legal.
- Trilha de auditoria de pacientes (`AuditoriasClinica`, #368): retenção de
  **5 anos** (`AuditoriaClinica:RetencaoDias = 1825`, em `appsettings.json`,
  decidida em 26/11/2026, #400). O prazo cobre a prescrição de 3 anos da
  reparação civil e de 5 anos do CDC, e fica acima do mínimo de 6 meses do
  Marco Civil (art. 15) para registros de acesso. O
  `ClinicAuditRetentionHostedService` roda ao subir a API e a cada 24 h
  (`AuditoriaClinica:IntervaloHoras`) e apaga, em lotes, só eventos da trilha
  com `DataUtc` anterior ao prazo. O prontuário, os arquivos e os demais dados
  do paciente não são afetados (guarda própria de 20 anos). Para mudar o prazo
  num ambiente, sobrescrever `AuditoriaClinica__RetencaoDias`; `0` desliga a
  limpeza.
- Não enviar CPF, e-mail completo, SQL bruto, conteúdo clínico ou corpo de
  requisição para logs, APM ou alertas.

## Arquivos clínicos

Documentos clínicos devem permanecer em armazenamento privado e ser entregues
somente pelos endpoints autenticados. Respostas de download usam
`Cache-Control: no-store, private`, `Pragma: no-cache` e
`X-Content-Type-Options: nosniff`.
