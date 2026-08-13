# Matriz de perfis e permissões

Atualizado em 13/08/2026.

Esta documentação descreve o comportamento efetivamente validado na API e a visibilidade atual da interface. As permissões sempre acumulam as seguintes restrições:

1. O usuário somente acessa dados da clínica selecionada na sessão.
2. No plano `Parcial`, o módulo precisa estar contratado.
3. Assinatura suspensa, cancelada ou vencida pode bloquear funcionalidades licenciadas.
4. Médicos enxergam pacientes, faturamentos, arquivos, observações e eventos conforme seu escopo e seus grupos médicos.
5. `Administrador` e `SuperAdministrador` possuem acesso operacional completo e não são bloqueados pelos módulos do plano; o Administrador permanece restrito à sua clínica.
6. O perfil `Equipe` usa uma identidade coletiva, mas exige uma equipe ativa e identificação válida do operador para operações sensíveis.

## Perfis

| ID | Perfil | Finalidade |
|---:|---|---|
| 1 | Administrador | Administração operacional de uma clínica. |
| 2 | Médicos | Atendimento e gestão dos dados clínicos dentro do escopo médico. |
| 3 | Pacientes | Acesso pessoal limitado. |
| 4 | Controller | Operação e conferência clínica/financeira sem administração geral. |
| 5 | SuperAdministrador | Administração global da plataforma e navegação entre clínicas. |
| 6 | Equipe | Operação coletiva com identificação e auditoria individual do membro. |

## Legenda

| Código | Permissão |
|---|---|
| C | Cadastrar |
| R | Visualizar/listar |
| U | Alterar |
| D | Excluir ou desativar |
| Próprio | Somente o registro pertencente ao usuário autenticado |
| Escopo | Somente registros permitidos pelo vínculo médico/grupo |
| — | Acesso negado |

## Matriz efetiva da API

Esta é a fonte de verdade para segurança. Ocultar um botão na interface não concede nem remove autorização na API.

| Recurso | Administrador | Médicos | Pacientes | Controller | SuperAdministrador | Equipe |
|---|---|---|---|---|---|---|
| Dashboard | R | R | R | R | R | R |
| Clínicas | — | — | — | — | CRUD e troca de clínica | — |
| Auditoria da plataforma | — | — | — | — | R | — |
| Usuários | CRUD | R/U próprio | R/U próprio limitado | R próprio | CRUD | R membros |
| Resetar senha de outro usuário | Sim | — | — | — | Sim | — |
| Senha própria | Alterar | Alterar | Alterar | Alterar | Alterar | Alterar |
| Arquivos do usuário | Qualquer usuário | Próprios | — | — | Qualquer usuário | — |
| Foto de perfil | Qualquer usuário | Própria e pacientes do escopo | Própria | Própria | Qualquer usuário | Membros |
| Pacientes | CRUD | CRU no escopo | R próprio | CRU | CRUD | CRU no escopo |
| Arquivos de pacientes | CRUD | CRU no escopo | R próprios | CRU | CRUD | CRU no escopo |
| Observações de pacientes | CRU | CRU no escopo | — | CRU | CRU | CRU no escopo |
| Faturamento médico | R total da clínica | R no escopo | — | R total da clínica | R total da clínica | R no escopo |
| Grupos médicos | CRUD | — | — | CRUD | CRUD | — |
| Agenda e notificações | CRUD da clínica | CRUD próprio/escopo | CRUD próprio | CRUD próprio | CRUD da clínica | CRUD no escopo |
| CBHPM | R e importar | R | R | R | R e importar | R |
| Hospitais, convênios e OPME | R | R | R | R | R | R |
| Licença própria | R | R | R, sem licença médica individual | R | R | R |
| Licenças de médicos | R/U/liberar completa | — | — | — | R/U/liberar completa | — |
| Exportações | Solicitar | Solicitar | Solicitar | Solicitar | Solicitar | Solicitar no escopo |
| Identidade visual pública | R | R | R | R | R | R |
| Configuração de tema/senha | Própria/interface | Própria via senha | Própria via senha | Própria via senha | Própria/interface | Própria via senha |

### Perfil Equipe

O perfil `Equipe` possui um fluxo de autenticação próprio e não deve ser criado diretamente pelo CRUD de usuários:

- o Administrador ou SuperAdministrador cria a identidade coletiva em `/api/equipes`;
- cada membro ativo é associado a uma equipe e pode ser identificado por seleção ou PIN;
- o JWT contém a equipe, o operador e as versões de sessão usadas para revogação;
- usuários, pacientes, arquivos, observações, agenda, faturamento médico e exportações respeitam o escopo da equipe;
- administração de clínicas, licenças, grupos médicos e financeiro administrativo permanece bloqueada;
- operações sensíveis exigem uma equipe válida no token e são registradas na auditoria.

### Exclusão de dados

- A exclusão de clínica é lógica: a clínica é desativada e seus dados são preservados.
- Pacientes somente podem ser excluídos por Administrador ou SuperAdministrador.
- Usuários somente podem ser excluídos por Administrador ou SuperAdministrador.
- Eventos podem ser excluídos pelo proprietário; Administrador e SuperAdministrador podem gerenciar todos os eventos da clínica.
- Médico não recebe permissão de exclusão de paciente, mesmo quando pode alterar o paciente do seu escopo.

## Menu e dashboard atuais

`Sim` significa que o item aparece quando o módulo também está contratado no plano da clínica.

| Item | Administrador | Médicos | Pacientes | Controller | SuperAdministrador |
|---|---:|---:|---:|---:|---:|
| Painel | Sim | Sim | Sim | Sim | Sim |
| Usuários | Sim | — | — | — | Sim |
| Meu cadastro | — | Sim | Sim | — | — |
| Pacientes | Sim | Sim | Sim | Sim | Sim |
| Faturamento médico | Sim | Sim | — | Sim | Sim |
| Grupos médicos | Sim | — | — | Sim | Sim |
| Agenda e notificações | Sim | Sim | Sim | Sim | Sim |
| Clínicas | — | — | — | — | Sim |
| Configuração | Sim | — | — | — | Sim |

## Plano Parcial

| Módulo contratado | Rotas protegidas e itens controlados |
|---|---|
| Usuários | `/api/users` e menu/card Usuários |
| Pacientes | `/api/pacientes`, CBHPM, hospitais, convênios, OPME e menu/card Pacientes |
| Faturamento médico | `/api/faturamentos-medicos` e menu/card Faturamento médico |
| Grupos médicos | `/api/grupos-medicos` e menu/card Grupos médicos |
| Agenda e notificações | `/api/events` e menu/card Agenda |

Quando um módulo não está contratado:

- ele não aparece no menu nem no dashboard;
- o acesso direto por URL é redirecionado/bloqueado pela interface;
- a API responde `403 Forbidden`;
- Administrador e SuperAdministrador ignoram essa restrição operacional, conforme a regra de acesso administrativo completo;
- `Clínicas` e `Configuração` continuam disponíveis ao SuperAdministrador por serem funções de plataforma.

## Alinhamentos realizados

Os testes registram separadamente o contrato da API e o comportamento da interface. Atualmente existem estas diferenças:

| Situação | Interface | API | Estado |
|---|---|---|---|
| Paciente em Pacientes | Exibe o módulo como leitura | Lista apenas o próprio prontuário | Alinhado. |
| Paciente em Meu cadastro | Exibe edição de nome, telefone, nascimento e foto; e-mail é somente leitura | Permite `PUT` próprio e preserva e-mail, CPF, perfil, status e dados médicos | Alinhado. |
| Controller em Agenda | Menu visível | Permite CRUD de eventos próprios | Alinhado. |
| Controller em Grupos médicos | Menu visível | Permite listar, cadastrar, alterar e excluir | Alinhado. |
| Exportações | Todos os perfis autenticados podem solicitar | Aceita pacientes, faturamentos e CBHPM | Contrato confirmado: o conteúdo deve respeitar módulo, clínica e escopo de leitura do perfil. |

As divergências de navegação e autorização foram eliminadas e o contrato funcional das exportações foi definido.

### Escopo obrigatório das exportações

Todos os perfis autenticados podem solicitar os três recursos, mas solicitar uma exportação não amplia o acesso do usuário:

| Recurso exportado | Escopo obrigatório |
|---|---|
| Pacientes | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico: pacientes do seu vínculo/grupo; Paciente: somente o próprio prontuário. |
| Faturamentos médicos | Administrador, Controller e SuperAdministrador: clínica selecionada; Médico: seu escopo médico; Paciente: nenhum dado de faturamento. |
| CBHPM | Somente dados que o perfil já pode consultar no módulo CBHPM. |

A mensagem assíncrona já contém `ClinicaId`, `RequestedByUserId` e `RequestedByPerfilId`. O worker atual gera um manifesto técnico do job; quando a geração dos dados reais for implementada, deverá reutilizar as mesmas regras de escopo aplicadas pelas queries da API, sem confiar em filtros enviados pelo cliente.

## Cobertura automatizada

### Backend

Arquivo: `HemodinksAPI.Tests/ApiAuthorizationMatrixTests.cs`

- 35 operações representativas.
- 6 perfis, incluindo uma equipe, membro, operador e JWT reais.
- 210 verificações de autorização por execução.
- Valida leitura, cadastro, alteração, exclusão/desativação e operações especiais.
- Uma permissão negada precisa retornar `403`.
- Uma permissão concedida precisa alcançar o endpoint sem `401`, `403` ou `500`.
- Os testes funcionais existentes continuam responsáveis por validar payloads, persistência, isolamento e regras de negócio de cada CRUD.
- Suíte completa atual: 228 testes aprovados.

### Frontend

Arquivo: `src/app/appAccess.test.ts` no projeto `hemodinks-front`.

- Valida as permissões calculadas para os cinco perfis.
- Valida menu, dashboard, criação/edição/exclusão de pacientes e administração da plataforma.
- Valida a sobreposição das restrições do plano Parcial.
- Suíte completa atual: 108 testes aprovados.

### Comandos

```powershell
dotnet test HemodinksAPI.Tests\HemodinksAPI.Tests.csproj --filter "FullyQualifiedName~ApiCrudAuthorizationMatrix"
```

```powershell
npx vitest run src/app/appAccess.test.ts
```
