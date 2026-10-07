# AGENTS.md — HemoDinks API

## 1. Objetivo

Este documento define as regras obrigatórias para agentes de IA que atuem no repositório da API do HemoDinks.

O objetivo é preservar:

- segurança;
- isolamento multi-clínica;
- Clean Architecture;
- regras de negócio existentes;
- consistência arquitetural;
- qualidade de código;
- rastreabilidade;
- testabilidade;
- desempenho;
- estabilidade de produção;
- uso eficiente de contexto, tokens e ferramentas.

O agente deve realizar somente as alterações necessárias para atender à atividade solicitada.

---

# 2. Princípio fundamental

Antes de editar código:

1. entender a solicitação;
2. identificar o domínio afetado;
3. localizar a implementação atual;
4. identificar testes relacionados;
5. avaliar impacto de segurança;
6. avaliar impacto multi-tenant;
7. avaliar risco de regressão;
8. somente então modificar código.

Não iniciar implementações extensas com base apenas na descrição da issue sem inspecionar a implementação existente.

---

# 3. Arquitetura

Preservar a arquitetura existente do projeto.

Priorizar os princípios de:

- Clean Architecture;
- separação de responsabilidades;
- SOLID;
- Dependency Inversion;
- CQRS, quando aplicável;
- baixo acoplamento;
- alta coesão;
- componentes pequenos e testáveis.

Não mover regras de negócio para:

- Controllers;
- endpoints;
- infraestrutura;
- Entity Framework;
- DTOs;
- frontend.

Regras de negócio devem permanecer na camada apropriada.

Evitar introduzir abstrações sem necessidade concreta.

Não criar interfaces, services, factories, handlers ou wrappers apenas para aumentar artificialmente a quantidade de camadas.

---

# 4. Organização das alterações

Antes de criar um novo componente, verificar se já existe implementação reutilizável.

Preferir:

1. reutilizar;
2. estender;
3. compor;
4. somente então criar algo novo.

Evitar duplicação de:

- validators;
- regras de autorização;
- queries;
- regras de tenant;
- serviços;
- helpers;
- políticas de segurança.

Não substituir componentes existentes por implementações completamente diferentes sem justificativa técnica.

---

# 5. Escopo

Modificar somente código relacionado à atividade.

É proibido realizar refatorações oportunistas fora do escopo.

Não:

- renomear arquivos desnecessariamente;
- reorganizar pastas sem necessidade;
- alterar formatação global;
- trocar bibliotecas sem necessidade;
- modernizar código não relacionado;
- alterar contratos públicos não envolvidos;
- alterar regras de negócio funcionais fora do problema.

Se for identificada uma melhoria fora do escopo, mencioná-la separadamente.

Não implementá-la automaticamente.

---

# 6. Multi-clínica e isolamento de dados

O HemoDinks é uma aplicação SaaS multi-clínica.

O isolamento de dados entre clínicas é requisito crítico de segurança.

Toda operação sobre dados pertencentes a uma clínica deve validar corretamente o tenant/ClinicaId aplicável.

Nunca confiar em um ClinicaId fornecido livremente pelo cliente quando a clínica puder ser determinada pelo contexto autenticado.

Verificar cuidadosamente:

- queries;
- commands;
- repositories;
- handlers;
- services;
- filtros;
- exports;
- relatórios;
- jobs;
- endpoints;
- operações administrativas.

Uma clínica nunca pode visualizar, alterar, excluir ou inferir dados pertencentes a outra clínica sem uma regra administrativa explícita e autorizada.

Nunca remover filtros de tenant para "resolver" problemas de consulta.

---

# 7. Fonte correta do ClinicaId

Antes de implementar lógica utilizando ClinicaId, identificar qual entidade representa corretamente a propriedade do dado.

Não assumir que todos os ClinicaId representam a mesma relação de domínio.

Por exemplo, quando existir diferença entre:

- ClinicaId do usuário;
- ClinicaId do paciente;
- ClinicaId da associação;
- ClinicaId selecionada no contexto administrativo;

usar a relação definida pela regra de negócio correspondente.

Nunca corrigir divergências simplesmente escolhendo o primeiro ClinicaId disponível.

---

# 8. SuperAdmin e usuários multi-clínica

Perfis administrativos globais não eliminam automaticamente o contexto da clínica.

Quando o usuário estiver operando dentro de uma clínica selecionada:

- respeitar o contexto selecionado;
- aplicar autorização correspondente;
- preservar isolamento.

A capacidade de visualizar múltiplas clínicas deve depender explicitamente de permissões e regras existentes.

Não transformar permissões administrativas em bypass global de segurança.

---

# 9. Autenticação e autorização

Alterações envolvendo autenticação ou autorização são consideradas de alto risco.

Examinar cuidadosamente:

- JWT;
- claims;
- refresh tokens;
- sessão;
- SecurityVersion;
- recuperação de senha;
- senha temporária;
- MFA;
- autorização por perfil;
- autorização por clínica;
- contexto de login;
- login unificado.

Nunca confiar somente no frontend para autorização.

Toda autorização efetiva deve ser aplicada no backend.

---

# 10. Senhas

Nunca:

- armazenar senha em texto puro;
- registrar senha em logs;
- retornar senha em respostas;
- reduzir o custo do algoritmo de hash existente;
- implementar criptografia própria;
- criar mecanismos de recuperação reversível.

Preservar o mecanismo seguro de hashing já utilizado pelo projeto.

Políticas de senha devem ser centralizadas e reutilizadas.

---

# 11. Recuperação e alteração de senha

Ao modificar fluxos de senha, analisar:

- recuperação por token;
- alteração autenticada;
- primeiro acesso;
- provisionamento administrativo;
- senha temporária;
- redefinição forçada.

Verificar se a alteração exige:

- incremento de SecurityVersion;
- invalidação de sessões;
- revogação de tokens;
- invalidação de credenciais temporárias.

Não criar caminhos alternativos que contornem políticas de senha.

---

# 12. Dados sensíveis

Não registrar em logs:

- senhas;
- tokens completos;
- refresh tokens;
- secrets;
- connection strings;
- documentos sensíveis;
- dados médicos desnecessários;
- payloads completos contendo dados pessoais.

Quando for necessário identificar uma operação, utilizar identificadores mínimos adequados.

Aplicar minimização de dados.

---

# 13. LGPD

Evitar exposição ou processamento desnecessário de dados pessoais.

Sempre avaliar:

- necessidade;
- finalidade;
- minimização;
- autorização;
- isolamento;
- retenção;
- auditoria.

Não introduzir novos dados pessoais em logs ou telemetria sem necessidade.

---

# 14. EF Core

Ao alterar acesso a dados:

- evitar N+1;
- utilizar projeções quando apropriado;
- evitar materialização precoce;
- avaliar tracking;
- preservar filtros multi-tenant;
- verificar índices relevantes;
- evitar consultas excessivamente amplas.

Não carregar entidades completas quando somente poucos campos forem necessários.

---

# 15. Migrations

Migrations exigem cuidado especial.

Antes de criar migration:

1. verificar modelo atual;
2. verificar migrations existentes;
3. avaliar compatibilidade com dados existentes;
4. avaliar produção;
5. evitar perda de dados.

Nunca criar migration destrutiva sem necessidade explícita.

Mudanças que eliminem ou transformem dados devem ser claramente sinalizadas.

---

# 16. Azure SQL

Considerar impacto de:

- índices;
- Full-Text Search;
- migrations;
- volume de dados;
- locks;
- concorrência;
- custo de queries.

Não introduzir consultas desnecessariamente pesadas em caminhos frequentes.

---

# 17. Azure e infraestrutura

Mudanças envolvendo Azure devem preservar os padrões existentes.

Isso inclui, quando aplicável:

- Azure Container Apps;
- Azure SQL;
- Blob Storage;
- Service Bus;
- Queues;
- Functions;
- observabilidade;
- health checks;
- warm-up.

Nunca inserir secrets diretamente no código.

Utilizar configuração e mecanismos existentes.

---

# 18. Warm-up e health checks

Endpoints utilizados para:

- health check;
- readiness;
- liveness;
- warm-up;

devem ser:

- leves;
- idempotentes;
- seguros;
- rápidos.

Não utilizar operações de negócio apenas para aquecer a aplicação.

Não executar escrita no banco em warm-up.

Não gerar efeitos colaterais.

---

# 19. Observabilidade

Preservar a integração existente com ferramentas de observabilidade.

Evitar:

- logs excessivos;
- duplicação de telemetria;
- cardinalidade desnecessária;
- captura de informações sensíveis.

Logs devem auxiliar diagnóstico sem comprometer segurança ou custo.

---

# 20. Performance

Não realizar otimizações prematuras.

Entretanto, ao modificar caminhos críticos, considerar:

- round-trips ao banco;
- chamadas externas;
- serialização;
- queries;
- N+1;
- processamento repetido;
- operações síncronas desnecessárias.

---

# 21. Validação

Utilizar o mecanismo de validação existente.

Preferir FluentValidation quando esse for o padrão da camada.

Não duplicar a mesma validação em múltiplas camadas sem necessidade.

Diferenciar:

- validação estrutural;
- regra de negócio;
- autorização.

---

# 22. Controllers e endpoints

Controllers devem permanecer pequenos.

Evitar lógica de negócio significativa diretamente em endpoints.

Responsabilidades típicas:

- receber request;
- delegar;
- traduzir resultado;
- retornar response.

---

# 23. Contratos da API

Não alterar contratos públicos desnecessariamente.

Antes de modificar:

- DTOs;
- nomes de campos;
- códigos HTTP;
- formatos de erro;
- rotas;

avaliar impacto no frontend e integrações existentes.

---

# 24. Tratamento de erros

Não utilizar exceptions como fluxo normal de negócio.

Preservar o padrão existente de tratamento de erros.

Não retornar detalhes internos de exceptions ao cliente.

Nunca expor:

- stack traces;
- SQL;
- secrets;
- caminhos internos;
- dados sensíveis.

---

# 25. Testes — estratégia

Aplicar pirâmide de testes.

Prioridade:

1. testes unitários;
2. testes de Application/Domain;
3. testes de integração;
4. E2E quando necessário.

Não executar a suíte mais cara imediatamente sem motivo.

---

# 26. Testes após alterações

Após uma alteração:

1. executar testes diretamente relacionados;
2. corrigir falhas;
3. executar testes do módulo afetado;
4. ampliar testes proporcionalmente ao risco.

Executar suíte mais ampla quando houver impacto transversal.

---

# 27. Quando ampliar os testes

Executar testes mais abrangentes quando houver alteração em:

- autenticação;
- autorização;
- tenant;
- ClinicaId;
- senha;
- SecurityVersion;
- MFA;
- middleware;
- filtros globais;
- banco;
- migrations;
- contratos amplamente utilizados;
- infraestrutura compartilhada.

---

# 28. Testes negativos

Para segurança, não testar apenas o caminho permitido.

Sempre que aplicável, testar também:

- usuário sem permissão;
- clínica incorreta;
- token inválido;
- token expirado;
- usuário inativo;
- sessão revogada;
- tentativa cross-tenant;
- payload inválido;
- recurso inexistente.

---

# 29. Segurança de tenant em testes

Alterações envolvendo dados clínicos devem possuir, quando relevante, teste demonstrando que:

Clinica A ≠ Clinica B.

O teste deve verificar que uma clínica não consegue acessar dados da outra.

---

# 30. Política de investigação eficiente

Não ler o repositório inteiro para cada tarefa.

Começar por:

1. issue/prompt;
2. símbolos relevantes;
3. endpoints relacionados;
4. handlers;
5. validators;
6. repositories;
7. testes existentes.

Expandir a investigação apenas quando necessário.

---

# 31. Uso eficiente de contexto

Evitar:

- abrir arquivos gigantes sem necessidade;
- reler arquivos já compreendidos;
- repetir buscas equivalentes;
- investigar módulos sem relação com a tarefa;
- gerar longos resumos internos antes de agir.

Preferir buscas por símbolo, referência ou fluxo.

---

# 32. Política de complexidade

Antes de implementar, classificar mentalmente a tarefa.

## Baixa

Exemplos:

- texto;
- pequena validação;
- ajuste localizado;
- teste isolado.

Estratégia:

- investigação mínima;
- alteração localizada;
- testes relacionados.

## Média

Exemplos:

- endpoint;
- CRUD;
- validator;
- serviço;
- relatório simples.

Estratégia:

- investigar fluxo completo relacionado;
- implementar;
- testes unitários/integrados necessários.

## Alta

Exemplos:

- alterações multi-arquivo;
- migrations;
- autenticação;
- autorização;
- integração Azure;
- concorrência.

Estratégia:

- análise arquitetural;
- análise de segurança;
- plano curto;
- implementação;
- testes ampliados.

## Crítica

Exemplos:

- isolamento multi-tenant;
- exposição de dados;
- autenticação;
- MFA;
- tokens;
- recuperação de senha;
- SecurityVersion;
- incidente de produção.

Estratégia:

- localizar fluxo completo;
- identificar trust boundaries;
- identificar bypasses;
- implementar menor alteração segura;
- criar testes negativos;
- validar regressão.

---

# 33. Escalonamento de raciocínio

Não utilizar análise máxima para tarefas triviais.

Aumentar profundidade somente quando houver:

- ambiguidade arquitetural;
- comportamento inesperado;
- falha de segurança;
- concorrência;
- race condition;
- impacto multi-tenant;
- incidentes de produção;
- testes falhando sem causa evidente;
- múltiplos módulos envolvidos.

Antes de aumentar complexidade de análise, verificar se simplesmente falta contexto.

---

# 34. Subagentes

Não criar múltiplos subagentes para tarefas simples.

Utilizar divisão de trabalho apenas quando houver paralelismo real.

Exemplos razoáveis:

- investigar backend enquanto outro agente analisa testes;
- segurança e implementação em fluxos realmente complexos.

Evitar subagentes para:

- rename;
- CSS;
- DTO pequeno;
- validator simples;
- alteração de uma única função.

---

# 35. Anti-alucinação

Nunca assumir que:

- uma classe existe;
- uma rota existe;
- uma tabela existe;
- um campo existe;
- um serviço existe;
- um padrão arquitetural existe;

sem verificar no código.

Antes de mencionar arquivos, métodos ou classes como existentes, localizá-los.

Quando não houver evidência suficiente, indicar a incerteza.

---

# 36. Não inventar requisitos

Implementar somente:

- requisitos explicitamente solicitados;
- requisitos tecnicamente necessários;
- requisitos decorrentes de segurança evidente.

Não acrescentar funcionalidades "porque seriam boas".

---

# 37. Git

Manter alterações pequenas e focadas.

Não incluir arquivos não relacionados.

Não modificar arquivos gerados desnecessariamente.

Nunca incluir:

- secrets;
- credenciais;
- tokens;
- arquivos locais;
- configurações pessoais.

---

# 38. Finalização da tarefa

Antes de considerar a atividade concluída:

- revisar diff;
- verificar escopo;
- verificar isolamento de dados;
- verificar autorização;
- executar testes relevantes;
- verificar que nenhum secret foi adicionado;
- verificar que nenhuma regra não relacionada foi alterada.

---

# 39. Relatório final

Ao concluir, informar de forma objetiva:

- o que foi alterado;
- arquivos principais;
- decisões relevantes;
- testes executados;
- resultado dos testes;
- riscos ou pendências.

Não gerar documentação extensa desnecessariamente.

---

# 40. Regra máxima

No HemoDinks:

SEGURANÇA > ISOLAMENTO DE DADOS > CORREÇÃO > ARQUITETURA > TESTABILIDADE > PERFORMANCE > CONVENIÊNCIA.

Se uma implementação mais curta comprometer qualquer item acima, não utilizá-la.