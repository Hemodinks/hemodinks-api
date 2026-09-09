# Datas futuras e isolamento de pacientes e usuários

## Achados e correções

- O frontend validava `dataAtendimento` (Cirurgias Consolidadas) como data de nascimento. Datas futuras eram enviadas como `null`. A validação agora verifica o calendário, permite datas futuras e preserva o valor no cadastro e na edição. Datas inexistentes produzem erro no formulário. O calendário do campo e os filtros correspondentes permitem datas futuras.
- Na troca de clínica, o frontend limpava o React Query, mas mantinha estados locais de listas e formulários. O conteúdo da sessão agora é recriado pela chave `ClinicaId + UserId + token`, descartando estados antigos. O fluxo de login permanece fora dessa chave para preservar os avisos de sessão expirada e o redirecionamento após autenticação.
- A API já resolve a clínica autenticada pelo token e aplica filtros globais por `ClinicaId` nos registros pertencentes à clínica. A validação de associação ativa agora também confere a clínica do usuário vinculado.
- As referências a pacientes e usuários tinham chaves estrangeiras apenas pelo ID. A migração `20260909170626_Schema_EnforcePatientUserClinicRelationships` cria chaves alternativas `(ClinicaId, Id)` em `Users` e `Pacientes` e inclui `ClinicaId` em 31 relacionamentos: paciente/usuário, cirurgião, auxiliares, arquivos, observações, vínculos de clínica e demais dependências desses registros. Os IDs existentes e as chaves primárias são preservados.
- A validação de gravação do contexto foi adaptada às chaves compostas e passa a cobrir exclusões e a sobrecarga `SaveChangesAsync(bool, CancellationToken)`.

A inconsistência de estado encontrada no frontend é compatível com exibição de dados da sessão anterior. A causa exata do incidente relatado não foi confirmada por logs nem pela inspeção do banco de produção. As chaves estrangeiras reforçam a integridade dos vínculos; a autorização das leituras continua dependendo dos filtros e da validação da sessão na API.

## Aplicação da migração

1. Executar `scripts/audit-patient-user-clinic-relationships.sql` no banco de destino. O script é somente leitura e apresenta IDs dos vínculos que cruzam clínicas.
2. Caso existam resultados, investigar cada vínculo antes da migração. Não transferir registros entre clínicas nem excluir dados automaticamente. A nova restrição rejeita dados históricos incompatíveis.
3. Publicar a API e aplicar a migração pelo fluxo de implantação do projeto. Publicar também o frontend atualizado. Os testes locais não aplicam a migração no banco real.

## Validação

Resultado local: 262 testes do frontend aprovados, build e auditoria de arquitetura aprovados; 349 testes da API aprovados com `Category!=SqlServer` e `HEMODINKS_TEST_LOCALDB=1`, incluindo o novo teste relacional. O modelo EF corresponde à migração gerada, sem alterações pendentes.

- Testes do frontend cobrem o envio de `01/12/2030`, edição, ano bissexto, datas inválidas e a troca de clínica enquanto a nova lista de pacientes/usuários está pendente.
- Testes de criação e atualização da API verificam a persistência de `01/12/2030`.
- Testes do contexto verificam isolamento de leitura entre clínicas e rejeição de alterações/exclusões fora da clínica atual.
- `PatientUserTenantSqlServerTests` cria e remove um banco exclusivo no SQL Server LocalDB. Verifica a migração sobre dados válidos, a rejeição de vínculos históricos incompatíveis, comandos SQL diretos que tentam cruzar clínicas e a persistência de data futura.

Para executar o teste relacional em Windows com LocalDB instalado:

```powershell
$env:HEMODINKS_TEST_LOCALDB = '1'
dotnet test HemodinksAPI.Tests/HemodinksAPI.Tests.csproj --filter FullyQualifiedName~PatientUserTenantSqlServerTests
```

Testes antigos com a categoria `SqlServer` que executam todo o histórico de migrações exigem uma instância com Full-Text Search. O teste relacional acima não depende desse recurso.
