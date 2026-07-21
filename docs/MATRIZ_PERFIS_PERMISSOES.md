# Matriz de perfis e permissões

Atualizado em 21/07/2026.

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
| Usuários | CRUD | R/U próprio | R próprio | R próprio | CRUD |
| Resetar senha de outro usuário | Sim | — | — | — | Sim |
| Senha própria | Alterar | Alterar | Alterar | Alterar | Alterar |
| Arquivos do usuário | Qualquer usuário | Próprios | — | — | Qualquer usuário |
| Foto de perfil | Qualquer usuário | Própria e pacientes do escopo | Própria | Própria | Qualquer usuário |
| Pacientes | CRUD | CRU no escopo | — | CRU | CRUD |
| Arquivos de pacientes | CRUD | CRU no escopo | — | CRU | CRUD |
| Observações de pacientes | CRU | CRU no escopo | — | CRU | CRU |
| Faturamento médico | R total da clínica | R no escopo | — | R total da clínica | R total da clínica |
| Grupos médicos | CRUD | — | — | Apenas C | CRUD |
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
| Grupos médicos | Sim | — | — | — | Sim |
| Agenda e notificações | Sim | Sim | Sim | — | Sim |
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

## Divergências identificadas

Os testes registram separadamente o contrato da API e o comportamento da interface. Atualmente existem estas diferenças:

| Situação | Interface | API | Decisão necessária |
|---|---|---|---|
| Paciente em Pacientes | Exibe o módulo como leitura | Retorna `403` para listar pacientes | Definir se o paciente verá apenas o próprio prontuário ou se o menu deve ser removido. |
| Paciente em Meu cadastro | Exibe opção de edição | Permite consultar, mas bloqueia `PUT` do próprio usuário | Definir quais campos pessoais o paciente pode editar. |
| Controller em Agenda | Menu oculto | Permite CRUD de eventos próprios | Liberar o menu ou bloquear a API. |
| Controller em Grupos médicos | Menu oculto | Pode cadastrar, mas não listar/alterar/excluir | Remover a permissão de cadastro ou criar uma tela/fluxo específico. |
| Exportações | Todos os perfis autenticados podem solicitar | Aceita pacientes, faturamentos e CBHPM | Confirmar que o worker aplica o mesmo escopo do perfil ao produzir o arquivo. |

Essas divergências não foram alteradas automaticamente porque representam decisões de produto, não apenas falhas técnicas.

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

### Frontend

Arquivo: `src/app/appAccess.test.ts` no projeto `hemodinks-front`.

- Valida as permissões calculadas para os cinco perfis.
- Valida menu, dashboard, criação/edição/exclusão de pacientes e administração da plataforma.
- Valida a sobreposição das restrições do plano Parcial.

### Comandos

```powershell
dotnet test HemodinksAPI.Tests\HemodinksAPI.Tests.csproj --filter "FullyQualifiedName~ApiCrudAuthorizationMatrix"
```

```powershell
npx vitest run src/app/appAccess.test.ts
```

