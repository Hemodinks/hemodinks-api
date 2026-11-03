# Issue #200 — processamento de lembretes vencidos

## Resumo

Complexidade média e risco alto (concorrência entre réplicas, isolamento por
`ClinicaId` e SQL parametrizado escrito à mão). Escopo restrito a
`EventReminderProcessor`; sem migration, sem nova dependência, sem alteração de
contrato público, sem consulta ou mudança em produção.

A #172 registrou que lembretes vencidos cresciam por evento
(**14/50 leituras, 7/25 escritas** para 6/24 vencidos no GET real da agenda).
Esta issue elimina o N+1 de reserva e carga e reduz as escritas a um valor
constante, mantendo a revalidação de autorização por evento.

## O que mudou

| Etapa | Antes | Depois |
| --- | --- | --- |
| Seleção de candidatos | 1 leitura (`Id`) | 1 leitura (`Id`, `ClinicaId`) |
| Reserva (lease 30 min) | 1 `ExecuteUpdate` por evento | 1 `UPDATE ... OUTPUT INSERTED.Id` com junção `VALUES (Id, ClinicaId)` |
| Carga do evento | 1 `SingleAsync` por evento | 1 leitura com `Include` para todos os reservados |
| Destinatários | 1 a 3 leituras por evento | 1 leitura por evento |
| Finalização | 1 `SaveChanges` | 1 `SaveChanges` (inalterado) |

Decisões de segurança preservadas:

- **Reserva idempotente e concorrente.** O `UPDATE` reaplica todas as condições
  de vencimento (`IsCompleted`, `NextReminderAt <= now`, flags de notificação);
  `OUTPUT` devolve exatamente as linhas reservadas por esta réplica, sem depender
  de timestamp compartilhado. A ordem original (`NextReminderAt`, `Id`) é
  restaurada no cliente, pois `OUTPUT` não garante ordem.
- **Isolamento.** Candidatos vêm da consulta com filtro de tenant; a escrita
  confere `Id` **e** `ClinicaId`, de modo que um evento transferido de clínica
  entre a seleção e a reserva não é reservado. Valores sempre parametrizados;
  somente nomes de parâmetro gerados entram no texto SQL (máximo 202
  parâmetros para o lote de 100, abaixo do limite de 2.100).
- **Autorização por evento.** Os destinatários continuam sendo recalculados
  para cada evento, depois dos envios anteriores. Um snapshot por lote
  manteria elegíveis, até o fim do lote, usuários, clínicas, equipes ou
  vínculos revogados no meio do processamento. Por isso as leituras continuam
  `N + 2`, por desenho.
- **Equipe.** `Equipes.UsuarioLoginId` é único (`EquipeConfiguration`), então a
  regra "membro ativo de equipe ativa do dono" é equivalente à anterior
  (`FirstOrDefault` da equipe do dono).
- **Outros provedores relacionais.** Mantida a reserva condicional por evento,
  agora também com `ClinicaId`. O caminho InMemory não mudou.

## Benchmark antes/depois

LocalDB **17.0.4025.3**, SDK **10.0.403**, EF Core **10.0.10**, Debug. Apenas
dados sintéticos, `HEMODINKS_TEST_LOCALDB=1`, sem variável de conexão SQL
externa. Teste `DueBatch_BoundsDatabaseCommandsAndPreservesDelivery`: um
aquecimento e 5 amostras por cenário; "antes" é `HEAD` (4a9805b) em worktree
descartável, com o mesmo teste e sem as asserções de contagem. Três rodadas
alternadas por versão. "Médico" indica `NotifyMedicalProfile` ativo (dois
destinatários por evento).

| Vencidos | Médico | Leituras antes → depois | Escritas antes → depois | Pior amostra ms antes | Pior amostra ms depois |
| --- | --- | --- | --- | --- | --- |
| 6 | não | 13 → 8 | 7 → 2 | 9,1 – 12,0 | 5,9 – 7,4 |
| 6 | sim | 25 → 8 | 7 → 2 | 15,9 – 24,6 | 10,8 – 20,2 |
| 24 | não | 49 → 26 | 25 → 2 | 63,9 – 114,1 | 17,7 – 24,4 |
| 24 | sim | 97 → 26 | 25 → 2 | 87,4 – 132,0 | 43,6 – 60,9 |
| 100 | não | 201 → 102 | 103 → 4 | 219,8 – 318,3 | 393,1 – 557,1 |
| 100 | sim | 401 → 102 | 103 → 4 | 411,8 – 591,8 | 257,6 – 275,0 |

Contagens de comandos são determinísticas e validadas por asserção
(`N + 2` leituras; `1 + ⌈N/42⌉` escritas). Após o lote, uma nova execução sem
vencidos faz 1 leitura e 0 escritas.

### Latência com 100 vencidos

A latência total é dominada pelo `SaveChanges` final, igual nas duas versões.
Instrumentação temporária (não commitada) num lote de 100:

- candidatos + reserva + carga: ~10–33 ms;
- loop de envio (inclui a revalidação por evento): ~45–100 ms;
- `DetectChanges`: ~1–3 ms, 101 entradas rastreadas;
- `SaveChanges` (100 `UPDATE` em lotes de 42): **~105–320 ms**.

A variação de 100 sem médico vem dessa etapa, não da reserva nem das
consultas novas. **Não há alegação de ganho de latência para lotes de 100 no
LocalDB.** O ganho comprovado é de comandos e round-trips, que pesa mais no
Azure SQL, onde cada round-trip tem latência de rede. Tempos absolutos dependem
da máquina local; não são SLA nem estimativa de Azure.

## Testes

Novo `HemodinksAPI.Tests/EventReminderBatchSqlServerTests.cs` (SQL Server real,
banco descartável por teste):

- lotes de 6/24/100, com e sem médico, contagens limitadas e entrega preservada;
- limite de 100 com ordem por vencimento; eventos futuros, concluídos, sem
  notificação ou sem `NextReminderAt` não são tocados;
- revogação durante o lote (dono, destinatário, identidade global, clínica,
  equipe, membro) bloqueia o evento seguinte; deduplicação do dono-médico;
- worker de plataforma e worker de tenant concorrentes com candidatos
  sobrepostos: 120 eventos, 120 entregas, nenhuma duplicada;
- reserva reavalia o estado após a seleção (concluído, futuro, desativado,
  troca de clínica);
- médico designado e usuário inativo; equipe inativa, membro revogado e
  médico de outra clínica;
- falha de transporte: retry em 5 minutos sem bloquear os demais;
- tenant não resolvido não reserva nem envia;
- **Clínica A × Clínica B:** médico da Clínica B nunca recebe lembrete da
  Clínica A, e evento da Clínica B permanece pendente para worker da Clínica A.

`CriticalQuerySqlServerTests` teve as contagens esperadas atualizadas para
`size + 3` comandos e 2 escritas.

Resultado local (LocalDB, filtros `EventReminder` e
`CriticalQuerySqlServerTests`): **31 aprovados, 1 falha de ambiente**.
`EventReminderProcessorConcurrencyTests` usa `MigrateAsync`, que exige
Full-Text Search, ausente no LocalDB. O CI (`ci.yml`) roda SQL Server 2022 com
FTS e deve validá-lo.

## Limitações

- Leituras continuam lineares (`N + 2`) por exigência de segurança.
- Finalização permanece um `UPDATE` por evento via `SaveChanges`; uma
  finalização set-based é possível, mas fica fora deste escopo.
- Benchmark em LocalDB local, com 5 amostras; não substitui medição no Azure
  SQL (ver #201).
