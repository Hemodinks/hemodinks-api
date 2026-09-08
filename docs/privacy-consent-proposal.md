# Proposta de persistência de preferências de privacidade

## Situação atual

O backend não possui entidade, repositório ou endpoint para preferências de privacidade. A decisão é mantida somente pelo frontend no navegador. O cookie `hemodinks_refresh` é estritamente necessário para renovação e revogação da sessão: ele é `HttpOnly`, usa `Secure` fora de desenvolvimento/testes, tem `SameSite=None` nesses ambientes, é limitado ao caminho `/api/session` e está marcado como essencial.

A telemetria do servidor (Serilog e, quando configurados, New Relic e exportadores OTLP) atende à operação, disponibilidade e segurança da API. Ela não deve ser ligada à categoria opcional de análise do navegador. O pipeline atual registra método, rota, identificadores técnicos de requisição, usuário e clínica, sem registrar intencionalmente corpos de requisições ou conteúdo clínico.

## Modelo sugerido

Uma implementação futura pode introduzir `UserPrivacyPreference` com:

- `Id`;
- `UsuarioGlobalId` (`UserId` no contrato público);
- `ClinicaId` (`TenantId` no contrato público);
- `DocumentVersion`;
- `PreferencesEnabled`;
- `AnalyticsEnabled`;
- `AcceptedAtUtc`;
- `UpdatedAtUtc`.

A combinação `(UsuarioGlobalId, ClinicaId, DocumentVersion)` deve ser única. O registro deve ficar vinculado à identidade global e ao vínculo de clínica ativo, sem reutilizar preferências de uma clínica em outra implicitamente.

## Contrato e isolamento

Os endpoints sugeridos são `GET /api/privacy-preferences` e `PUT /api/privacy-preferences`. Ambos devem exigir autenticação, obter usuário global e clínica exclusivamente dos claims validados e ignorar identificadores enviados pelo cliente. O acesso deve passar pelos mesmos controles de sessão, resolução de clínica e isolamento já utilizados pela API.

O `PUT` deve aceitar somente a versão vigente e as duas categorias opcionais. `necessary` deve ser sempre verdadeiro e não deve ser gravado como uma escolha desativável. Alterações devem produzir registro de auditoria sem incluir token, endereço IP completo, conteúdo clínico ou outros dados desnecessários.

## Sincronização proposta

1. Antes da autenticação, o frontend usa a decisão local do navegador.
2. Após autenticação e resolução da clínica, o frontend consulta a preferência vinculada ao usuário e tenant.
3. Se apenas um dos lados tiver uma decisão válida para a versão vigente, ela passa a ser a fonte e é sincronizada para o outro lado.
4. Se ambos tiverem decisões válidas, prevalece a de `UpdatedAtUtc`/`updatedAt` mais recente.
5. Uma versão diferente da vigente não concede consentimento; os opcionais permanecem desabilitados até nova decisão.
6. Logout, troca de clínica e refresh token não devem apagar nem alterar a decisão local. A troca de clínica deve disparar nova resolução da preferência referente ao tenant selecionado.

Esta proposta não foi implementada nesta entrega. Ela exige validação de produto e jurídica, definição de retenção/auditoria e uma mudança de banco deliberadamente versionada.
