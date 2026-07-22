# Matriz de perfis e permissões

Atualizado em 22/07/2026.

Esta documentação descreve o comportamento efetivamente validado na API e a visibilidade atual da interface. As permissões sempre acumulam as seguintes restrições:

1. O usuário somente acessa dados da clínica selecionada na sessão.
2. No plano `Parcial`, o módulo precisa estar contratado.
3. Assinatura suspensa, cancelada ou vencida pode bloquear funcionalidades licenciadas.
4. Médicos enxergam pacientes, faturamentos, arquivos, observações e eventos conforme seu escopo e seus grupos médicos.
5. O `SuperAdministrador` administra a plataforma e todas as clínicas, mas os módulos operacionais continuam respeitando o plano da clínica selecionada.

## Perfis

| ID | Perfil | Finalidade |
|---:|---|---|
| 1 | Administrador | Administração operacional de uma clínica. |
| 2 | Médicos | Atendimento e gestão dos dados clínicos dentro do escopo médico. |
| 3 | Pacientes | Acesso pessoal limitado. |
| 4 | Controller | Operação e conferência clínica/financeira sem administração geral. |
| 5 | SuperAdministrador | Administração global da plataforma e navegação entre clínicas. |

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

| Recurso | Administrador | Médicos | Pacientes | Controller | SuperAdministrador |
|---|---|---|---|---|---|
| Dashboard | R | R | R | R | R |
| Clínicas | — | — | — | — | CRUD e troca de clínica |
| Auditoria da plataforma | — | — | — | — | R |
| Usuários | CRUD | R/U próprio | R/U próprio limitado | R próprio | CRUD |
| Resetar senha de outro usuário | Sim | — | — | — | Sim |
| Senha própria | Alterar | Alterar | Alterar | Alterar | Alterar |
| Arquivos do usuário | Qualquer usuário | Próprios | — | — | Qualquer usuário |
| Foto de perfil | Qualquer usuário | Própria e pacientes do escopo | Própria | Própria | Qualquer usuário |
| Pacientes | CRUD | CRU no escopo | R próprio | CRU | CRUD |
| Arquivos de pacientes | CRUD | CRU no escopo | R próprios | CRU | CRUD |
| Observações de pacientes | CRU | CRU no escopo | — | CRU | CRU |
| Faturamento médico | R total da clínica | R no escopo | — | R total da clínica | R total da clínica |
| Grupos médicos | CRUD | — | — | CRUD | CRUD |
| Agenda e notificações | CRUD da clínica | CRUD próprio/escopo | CRUD próprio | CRUD próprio | CRUD da clínica |
| CBHPM | R e importar | R | R | R | R e importar |
| Hospitais, convênios e OPME | R | R | R | R | R |
| Licença própria | R | R | R, sem licença médica individual | R | R |
| Licenças de médicos | R/U/liberar completa | — | — | — | R/U/liberar completa |
| Exportações | Solicitar | Solicitar | Solicitar | Solicitar | Solicitar |
| Identidade visual pública | R | R | R | R | R |
| Configuração de tema/senha | Própria/interface | Própria via senha | Própria via senha | Própria via senha | Própria/interface |

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
- a restrição também vale para o SuperAdministrador dentro da clínica selecionada;
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
- 5 perfis.
- 175 verificações de autorização por execução.
- Valida leitura, cadastro, alteração, exclusão/desativação e operações especiais.
- Uma permissão negada precisa retornar `403`.
- Uma permissão concedida precisa alcançar o endpoint sem `401`, `403` ou `500`.
- Os testes funcionais existentes continuam responsáveis por validar payloads, persistência, isolamento e regras de negócio de cada CRUD.
- Suíte completa atual: 152 testes aprovados.

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
