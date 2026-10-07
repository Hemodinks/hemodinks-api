# AGENTS.md — HemoDinks API

## Objetivo

Trabalhe no HemoDinks API preservando segurança, isolamento multi-clínica,
arquitetura existente e regras de negócio já funcionais.

Faça a menor alteração correta e segura necessária para atender à tarefa.

---

## Arquitetura

- Preserve a Clean Architecture e a organização atual do projeto.
- Respeite as responsabilidades de Domain, Application, Infrastructure e API.
- Mantenha Controllers/Endpoints finos.
- Regras de negócio devem permanecer na camada apropriada.
- Reutilize services, handlers, validators, abstrações e padrões existentes antes
  de criar novos.
- Não introduza abstrações, interfaces ou camadas sem necessidade concreta.
- Preserve CQRS, FluentValidation, EF Core e demais padrões já utilizados pelo
  projeto quando aplicáveis.

---

## Escopo

- Modifique somente o necessário para a tarefa.
- Não faça refatorações oportunistas.
- Não reorganize pastas ou renomeie arquivos sem necessidade.
- Não troque bibliotecas ou padrões arquiteturais sem requisito explícito.
- Não altere regras de negócio que não estejam relacionadas ao problema.
- Se encontrar outro problema, reporte-o separadamente em vez de corrigi-lo
  automaticamente.

---

## Segurança e multi-tenant

O isolamento entre clínicas é requisito crítico.

- Nenhuma clínica pode acessar, alterar, excluir ou inferir dados de outra clínica
  sem autorização explícita.
- Nunca remova filtros de tenant para facilitar uma implementação.
- Não confie em ClinicaId enviado pelo cliente quando ele puder ser obtido do
  contexto autenticado.
- Ao existir mais de um ClinicaId no fluxo, identifique no domínio qual representa
  corretamente a propriedade do dado.
- Perfis administrativos não devem virar bypass implícito de isolamento.
- Autorização efetiva deve ocorrer no backend.

Mudanças envolvendo os itens abaixo são consideradas de alto risco:

- autenticação;
- autorização;
- ClinicaId;
- sessão;
- JWT;
- refresh token;
- SecurityVersion;
- senha;
- recuperação de senha;
- senha temporária;
- MFA;
- primeiro acesso;
- provisionamento administrativo.

Nesses casos, investigue o fluxo completo diretamente relacionado antes de editar.

---

## Senhas, tokens e dados sensíveis

Nunca:

- registre senhas;
- registre tokens completos;
- registre secrets;
- exponha connection strings;
- armazene senha em texto puro;
- implemente criptografia própria;
- reduza a segurança do hash existente;
- retorne informações sensíveis em erros.

Preserve os mecanismos existentes de autenticação, hashing e configuração segura.

Dados sensíveis devem ser minimizados em logs, métricas e telemetria.

---

## EF Core e banco

Ao alterar acesso a dados:

- preserve filtros de tenant;
- evite N+1;
- evite carregar dados desnecessários;
- use projeção quando apropriado;
- considere tracking somente quando necessário;
- avalie impacto de queries e migrations.

Não crie migration destrutiva sem necessidade explícita.

---

## Eficiência de execução

Antes de editar:

1. identifique o fluxo exato relacionado à tarefa;
2. localize símbolos, arquivos e testes diretamente relacionados;
3. leia apenas o necessário para compreender esse fluxo;
4. faça a menor alteração segura possível.

Não varra o repositório inteiro sem justificativa.

Prefira busca direcionada por:

- classe;
- método;
- endpoint;
- handler;
- validator;
- entidade;
- teste.

Evite:

- reler arquivos já compreendidos;
- buscas repetidas equivalentes;
- análises longas de módulos não relacionados;
- criar vários subagentes para tarefas simples;
- implementar alternativas que não foram solicitadas.

Só amplie a investigação quando houver evidência de dependência ou risco transversal.

---

## Planejamento proporcional ao risco

Para mudanças simples, investigue e implemente diretamente.

Faça um plano curto antes de editar quando houver:

- autenticação;
- autorização;
- isolamento por clínica;
- alteração de banco;
- migration;
- integração externa;
- concorrência;
- segurança;
- mudança transversal.

Não produza planos extensos para tarefas triviais.

---

## Testes

Execute primeiro os testes diretamente relacionados à alteração.

Amplie a suíte proporcionalmente ao risco.

Mudanças em:

- autenticação;
- autorização;
- tenant;
- sessão;
- senha;
- SecurityVersion;
- middleware;
- migrations;
- contratos compartilhados;

devem incluir testes negativos e de regressão quando aplicável.

Para isolamento multi-clínica, teste explicitamente cenários em que:

- Clínica A possui o recurso;
- Clínica B tenta acessá-lo;
- o acesso deve ser negado ou não revelar o recurso.

Não execute repetidamente suítes caras se o código não mudou.

---

## Anti-alucinação

Nunca assuma que existe:

- classe;
- endpoint;
- tabela;
- coluna;
- serviço;
- configuração;
- fluxo;
- teste;

sem localizar evidência no repositório.

Não invente comportamento da aplicação.

Quando houver informação insuficiente, investigue o código relacionado antes de
decidir.

---

## Dependências externas

Não adicione nova dependência se a solução puder ser implementada adequadamente
com a infraestrutura existente.

Antes de adicionar uma dependência, considere:

- segurança;
- manutenção;
- disponibilidade;
- custo;
- privacidade.

Nunca criar serviço pago ou assinatura externa sem requisito explícito.

---

## Finalização

Antes de concluir:

- revise o diff;
- confirme que o escopo foi respeitado;
- confirme que segurança e isolamento continuam válidos;
- execute os testes relevantes;
- verifique que nenhum secret ou dado sensível foi introduzido.

Ao finalizar, informe apenas:

1. o que foi alterado;
2. principais arquivos afetados;
3. testes executados e resultado;
4. riscos ou pendências reais.

Não gere documentação extensa sem necessidade.

---

## Prioridade

Em caso de conflito, priorize:

SEGURANÇA > ISOLAMENTO DE DADOS > CORREÇÃO FUNCIONAL > ARQUITETURA >
TESTABILIDADE > PERFORMANCE > CONVENIÊNCIA.