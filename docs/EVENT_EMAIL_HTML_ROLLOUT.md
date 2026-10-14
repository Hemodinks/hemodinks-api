# Notificações de evento — template HTML (#175)

A mensagem é produzida com base no evento persistido, após validação de clínica,
remetente e destinatário. O transporte mantém o texto puro e acrescenta `Template`
estruturado; o SMTP gera o HTML localmente com escape de conteúdo do usuário.
A mensagem final usa `multipart/alternative` (text/plain e text/html).
Nenhum HTML arbitrário é aceito do cliente, e não há imagens remotas nem links.

Os horários UTC persistidos são convertidos para `Event.TimeZoneId` somente
quando o fuso informado é reconhecido. Sem fuso válido, o e-mail identifica UTC.
Eventos de dia inteiro exibem datas civis, sem conversão de horas.

## Publicação segura

**Publicar Workers primeiro:** a Azure Function anterior usa desserialização
com campos desconhecidos proibidos e rejeitaria o campo `Template` novo.
Depois de validar que o worker atualizado aceita mensagens antigas sem template,
publicar a API. Nenhuma migration ou mudança de endpoints é necessária.

Validar em homologação o envio real e a renderização em Gmail, Outlook e mobile,
com dados sintéticos e configurações protegidas, sem informações de pacientes.
Testes alvo: `EventEmailTemplateTests`, `EventEmailTransportTests`,
`EventEmailNotificationTests` e regressões de autorização/tenant.

O estado `sent` continua representando aceitação pelo SMTP, não confirmação
de chegada à caixa postal; idempotência e tentativas permanecem inalteradas.
