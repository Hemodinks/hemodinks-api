# Matriz de perfis e permissões

Atualizado em 07/11/2026. Gerado por `scripts/generate_permissions_matrix.py` (não edite à mão).

Esta documentação descreve o comportamento efetivamente aplicado na API e a visibilidade atual da interface. As permissões sempre acumulam estas restrições:

1. O usuário somente acessa dados da clínica selecionada na sessão (`ClinicaId` do JWT); consultas sem clínica resolvida falham fechadas.
2. No plano `Parcial`, rotas de usuários, pacientes (inclui CBHPM, hospitais, convênios e OPME), faturamento médico, grupos médicos e agenda exigem o módulo contratado e retornam `403` quando ausente. Administrador e SuperAdministrador não são bloqueados por essa regra na API.
3. Assinatura suspensa, cancelada ou vencida e licenças individuais de médicos podem bloquear funcionalidades licenciadas (`Dashboard.Visualizar`, `Pacientes.Visualizar`, `Pacientes.Gerenciar`, `Cbhpm.Consultar`).
4. Médicos enxergam pacientes, faturamentos, atendimentos, arquivos, observações e eventos conforme seu vínculo e seus grupos médicos.
5. O Administrador permanece restrito à própria clínica, inclusive nas rotas de plataforma; somente o SuperAdministrador cria, desativa e alterna entre clínicas.
6. O perfil `Equipe` usa identidade coletiva; operações sensíveis exigem equipe ativa e operador identificado no token e são auditadas.

O inventário completo de rotas com o acesso por perfil está no [README](../README.md#endpoints-principais).

## Perfis

| ID | Perfil | Finalidade |
|---:|---|---|
| 1 | Administrador | Administração operacional de uma clínica. |
| 2 | Médicos | Atendimento e dados clínicos no escopo médico. |
| 3 | Pacientes | Acesso pessoal limitado. |
| 4 | Controller | Operação clínica e financeira, sem administração geral. |
| 5 | SuperAdministrador | Administração da plataforma e troca de clínica. |
| 6 | Equipe | Identidade coletiva com operador identificado e auditado. |

## Legenda

| Código | Permissão |
|---|---|
| C | Cadastrar |
| R | Visualizar/listar |
| U | Alterar |
| D | Excluir ou desativar |
| Próprio | Somente o registro pertencente ao usuário autenticado |
| Escopo | Somente registros permitidos pelo vínculo médico/grupo ou pela equipe |
| — | Acesso negado |

## Matriz efetiva da API

Esta é a fonte de verdade para segurança. Ocultar um botão na interface não concede nem remove autorização na API.

| Recurso | Administrador | Médicos | Pacientes | Controller | SuperAdministrador | Equipe |
|---|---|---|---|---|---|---|
| Dashboard | R | R | R | R | R | R |
| Clínicas (`/api/platform/clinicas`) | R/U da própria + equipes | — | — | — | CRUD + troca | — |
| Auditoria da plataforma | — | — | — | — | R | — |
| Usuários | CRUD | R/U próprio | R/U próprio limitado | R próprio | CRUD | R membros |
| Resetar senha de terceiro | Sim | — | — | — | Sim | — |
| Equipes (`/api/equipes`) | CRUD | — | — | — | CRUD | Trocar PIN próprio |
| Pacientes e cirurgias | CRUD | CRUD · escopo | R próprio | CRU | CRUD | CRU · escopo |
| Arquivos de pacientes | CRD | CRD · escopo | R próprios | CRD | CRD | CRD · escopo |
| Observações de pacientes | CRU | CRU · escopo | — | CRU | CRU | CRU · escopo |
| Faturamento médico | R clínica | R · escopo | — | R clínica | R clínica | R · escopo |
| Histórico de arquivos do faturamento | CRD | R · escopo | — | CRD | CRD | R · escopo |
| Atendimentos cirúrgicos | CRUD | CRUD · escopo | — | CRUD | CRUD | — |
| Faturamentos, glosas e recursos | CRUD | R · escopo | — | CRUD | CRUD | — |
| Contas a receber e recebimentos | CRUD | — | — | CRUD | CRUD | — |
| Relatórios e auditoria financeira | R | — | — | R | R | — |
| Preços por convênio | CRUD | R | — | CRUD | CRUD | — |
| Grupos médicos | CRUD | — | — | CRUD | CRUD | — |
| Agenda e notificações | CRUD clínica | CRUD próprio/escopo | CRUD próprio | CRUD próprio | CRUD clínica | CRUD · escopo |
| CBHPM | R + importar | R | R | R | R + importar | R |
| Hospitais, convênios e OPME | R | R | R | R | R | R |
| Licença própria | R | R | R | R | R | R |
| Licenças de médicos | R/U/liberar | — | — | — | R/U/liberar | — |
| Exportações (`POST /api/exports`) | Solicitar | Solicitar · escopo | Solicitar · próprio | Solicitar | Solicitar | Solicitar · escopo |
| Monitoramento (erros e segurança) | R/limpar da clínica | — | — | — | R/limpar todas | — |
| Aceites legais e privacidade | Próprios | Próprios | Próprios | Próprios | Próprios | Próprios |
| Identidade visual pública | R | R | R | R | R | R |

### Perfil Equipe

O perfil `Equipe` possui um fluxo de autenticação próprio e não deve ser criado diretamente pelo CRUD de usuários:

- o Administrador ou SuperAdministrador cria a identidade coletiva em `/api/equipes` (ou pela clínica em `/api/platform/clinicas/{id}/equipes`);
- cada membro ativo é associado a uma equipe e identificado por seleção ou PIN em `/api/equipe-auth/identificar`;
- o JWT contém a equipe, o operador e as versões de sessão usadas para revogação (`/api/session/renovar-equipe`);
- usuários, pacientes, arquivos, observações, agenda, faturamento médico e exportações respeitam o escopo da equipe;
- administração de clínicas, licenças, grupos médicos e o módulo financeiro permanecem bloqueados;
- operações sensíveis exigem uma equipe válida no token e são registradas na auditoria.

### Exclusão de dados

- A exclusão de clínica é lógica: a clínica é desativada e seus dados são preservados (somente SuperAdministrador).
- Usuários somente podem ser excluídos por Administrador ou SuperAdministrador.
- Pacientes podem ser excluídos por Administrador, SuperAdministrador e Médico (no seu escopo). Controller e Equipe não excluem pacientes.
- Eventos podem ser excluídos pelo proprietário; Administrador e SuperAdministrador gerenciam todos os eventos da clínica.

## Menu atual da interface

`Licença` significa que o item depende da feature de licença correspondente; todos os itens operacionais também dependem do módulo contratado no plano `Parcial` (o SuperAdministrador ignora essa restrição na interface).

| Item | Administrador | Médicos | Pacientes | Controller | SuperAdministrador | Equipe |
|---|---|---|---|---|---|---|
| Painel | Sim | Sim | Licença | Licença | Sim | Licença |
| Pacientes | Sim | Sim | Leitura | Licença | Sim | Licença |
| Faturamento médico, histórico e relatórios | Sim | Sim | — | Sim | Sim | Sim |
| Grupos médicos | Sim | — | — | Sim | Sim | — |
| Agenda e notificações | Sim | Sim | Sim | Sim | Sim | Sim |
| Usuários | Sim | — | — | — | Sim | Sim |
| Meu cadastro | — | Sim | Sim | — | — | — |
| Clínicas | Própria | — | — | — | Sim | — |
| Configuração | Sim | — | — | — | Sim | — |
| Tutoriais interativos | Sim | Sim | Sim | Sim | Sim | Sim |

## Divergências conhecidas entre interface e API

| Situação | Interface | API | Estado |
|---|---|---|---|
| Médico exclui paciente | Botão de exclusão só para Administrador/SuperAdministrador | Permite excluir paciente do escopo (`PacienteExcluir`) | Interface mais restritiva que a API. |
| Administrador em módulo não contratado | Oculta o módulo (só o SuperAdministrador ignora o plano) | Administrador e SuperAdministrador não são bloqueados pelo plano | Interface mais restritiva que a API. |
| Equipe sem operador identificado | Modo somente leitura | Operações sensíveis exigem operador (`EquipeOperacaoSensivel`) | Alinhado. |
| Paciente em Meu cadastro | Edita nome, telefone, nascimento e foto | `PUT` próprio preserva e-mail, CPF, perfil, status e dados médicos | Alinhado. |

## Exportações

Todos os perfis autenticados podem solicitar `POST /api/exports` para `pacientes`, `faturamentos-medicos` e `cbhpm`, mas a solicitação não amplia o acesso:

| Recurso exportado | Escopo obrigatório |
|---|---|
| Pacientes | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico e Equipe: escopo do vínculo/grupo ou equipe; Paciente: somente o próprio prontuário. |
| Faturamentos médicos | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico e Equipe: seu escopo; Paciente: nenhum dado. |
| CBHPM | Somente dados que o perfil já pode consultar. Filtros `tipoCirurgia` e `statusCirurgia` são recusados. |

A API monta o conteúdo no servidor (`FileExportSnapshotBuilder`) reutilizando as queries e o escopo do perfil, e envia o snapshot ao worker `GenerateFileExport`. As telas do front geram suas planilhas e PDFs no navegador, a partir dos dados que a API já filtrou.

## Cobertura automatizada

- Backend: `HemodinksAPI.Tests/ApiAuthorizationMatrixTests.cs` — 34 operações representativas × 6 perfis (204 verificações), com equipe, membro, operador e JWT reais. Permissão negada precisa retornar `403`; concedida precisa alcançar o endpoint sem `401`, `403` ou `500`. Os testes funcionais de cada módulo validam payload, persistência, isolamento e regras de negócio.
- Frontend: `src/app/appAccess.test.ts` no `hemodinks-front` — permissões calculadas, menu, ações de pacientes, plataforma e plano `Parcial`.

```powershell
dotnet test HemodinksAPI.slnx --filter "FullyQualifiedName~ApiAuthorizationMatrixTests"
```

```powershell
npx vitest run src/app/appAccess.test.ts
```
