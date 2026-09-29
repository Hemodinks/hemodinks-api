# Agenda: dia inteiro e preparação para recorrência

## Implementado

`Event` ganhou `IsAllDay`, `AllDayStartDate`, `AllDayEndDate` e `TimeZoneId`. As datas civis usam `DateOnly`/SQL `date`; a data final é inclusiva. Eventos de um dia têm datas iguais. A migration `20260929133639_AddAllDayEvents` mantém os registros existentes com `IsAllDay=false` e não converte seus horários. Uma constraint verifica a coerência básica dos metadados. `Start` e `End` continuam UTC para compatibilidade com consultas, dashboard e workers.

Para dia inteiro, a API deriva `Start` do primeiro instante válido da data inicial no fuso registrado; `End` é o primeiro instante válido do dia seguinte à data final, exclusivo. Não há ajuste artificial para 23:59:59. Dias de 23/25 horas e mudanças de horário de verão na meia-noite são tratados. Em horário ambíguo usa-se o primeiro instante. Datas/zonas inválidas e o último dia de 9999 são rejeitados. A conversão verifica a volta ao horário civil para não aceitar normalização silenciosa.

A aplicação não possui fuso por clínica. Neste incremento o navegador informa seu identificador IANA na criação e o evento o persiste; editar em outro fuso preserva o identificador existente. A UI informa esse fuso junto às datas. As datas de um evento de dia inteiro são exibidas como datas civis, independentemente do fuso do observador. Não foi alterada a interpretação dos eventos com horário.

## Contrato e consultas

POST/PUT de dia inteiro recebem, além dos campos comuns:

```json
{
  "title": "Encontro da clínica",
  "isAllDay": true,
  "allDayStartDate": "2026-09-26",
  "allDayEndDate": "2026-09-28",
  "timeZoneId": "America/Sao_Paulo"
}
```

O frontend omite `start`/`end` neste modo. Se um cliente enviar projeções UTC, elas não substituem as datas civis: a API as recalcula. Para eventos com horário, `start`/`end` continuam obrigatórios por FluentValidation. Desmarcar dia inteiro em uma edição remove os metadados civis ao persistir. Alternar o checkbox no formulário preserva datas e horários digitados; ao editar um evento que já era de dia inteiro, os horários para eventual conversão são inicialmente 09:00/10:00, pois horários não fazem parte desse evento.

GET mantém `from`/`to` e aceita o par opcional `fromDate`/`toDate` para o intervalo civil. O frontend envia ambos os pares: instantes para eventos com horário e datas para eventos de dia inteiro. Isso evita perder eventos nas bordas do período quando os fusos do evento e observador diferem muito. Chamadas legadas apenas com instantes continuam funcionando por sobreposição UTC; o fim de dia inteiro é exclusivo. O cache e a organização no calendário usam o mesmo critério civil. Nenhuma chamada por dia foi adicionada.

Os DTOs de eventos retornam os quatro metadados novos. As notificações de próximos eventos no dashboard retornam também as datas civis, mantendo `Data` UTC para a ordenação existente; a UI usa as datas civis na apresentação. Notificações diretas preservam a mensagem e a data de envio existentes.

## Lembretes e segurança

A janela existente de 48 horas antes do início e a repetição até conclusão permanecem. Para dia inteiro, a referência é o início civil convertido no fuso registrado; não foi criada uma preferência de horário comercial. A mensagem informa Dia inteiro e a faixa inclusiva, sem horário fictício. Destinatários continuam sendo validados pelos mecanismos existentes e revalidados pelo worker na clínica do evento.

`ClinicaId`, autorização, dono, médicos, grupos e destinatários não são inferidos das datas/fuso nem aceitos de um contexto externo. Testes mantêm bloqueio de acesso e edição entre clínicas.

Aplicar a migration antes de publicar a API/worker e publicar o frontend após a API. Ela foi validada em banco isolado; não foi executada em produção/homologação. Um rollback após criar eventos de dia inteiro removeria seus metadados; planejar a reversão com preservação desses dados. Bancos existentes, regras de autenticação e faturamento não foram modificados neste incremento.

## Proposta de recorrência para o próximo incremento

Não foram criadas colunas/tabelas de recorrência sem uso. O ponto reutilizável entregue é a resolução central de datas civis/fuso e sua separação dos instantes UTC.

1. Introduzir `EventSeries`, com `ClinicaId`, responsável, fuso, modo de duração (instantes/horário local ou quantidade de dias civis), configuração de lembrete e regra validada. Começar com não repetir, diariamente, semanalmente e mensalmente; personalizado deve ser um subconjunto explícito e limitado de regras. Definir política para dia 31, último dia do mês e transições de horário de verão antes da implementação.
2. Projetar ocorrências apenas para o intervalo consultado, com limites de expansão. Não pré-gravar dezenas de cópias. A chave estável deve combinar série e início local originalmente agendado (data para dia inteiro), independente de alterações pontuais.
3. Persistir exceções para cancelamento, reagendamento e conclusão individual, sempre com `ClinicaId` e referência à ocorrência original. Definir edição de uma ocorrência, desta em diante (divisão da série) e da série toda. Preservar histórico e usar controle de concorrência.
4. Persistir a política de destinatários da série de forma explícita. O fluxo atual de notificações diretas não equivale a uma política recorrente persistida. Reutilizar a autorização de quem modifica a série e revalidar usuários ativos, vínculos e grupos no envio de cada ocorrência, usando a clínica da série.
5. Expandir uma janela limitada no worker e usar registro de entrega/outbox com chave única por clínica, série, ocorrência, destinatário, disparo e canal. Validar a versão da série ao enviar para impedir mensagens de ocorrências canceladas. Separar repetição do lembrete de recorrência do evento.
6. Evoluir o DTO de consulta com identidade explícita de ocorrência, sem simular IDs de eventos persistidos. Testar fusos/DST, limites de expansão, exceções, idempotência, concorrência e isolamento entre clínicas antes de habilitar o recurso.

## Verificação

Resultados finais serão registrados após concluir a validação.
