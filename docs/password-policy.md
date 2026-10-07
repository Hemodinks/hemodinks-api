# Política de novas senhas

A regra está em Application/Security/NewPasswordPolicy.cs e depende de ICompromisedPasswordLookup. A implementação LocalCompromisedPasswordLookup, em Infrastructure, consulta exclusivamente uma base pública incorporada no assembly. Não consulta usuários ou clínicas, não usa filtros tenant e não faz chamadas externas. O registro de dependências fica na API, que também traduz indisponibilidade em HTTP 503 com código password_policy_unavailable.

## Decisão e cobertura

Escolhida uma base local versionada: SecLists, arquivo 100k-most-used-passwords-NCSC.txt, com **99.840 linhas** após a limpeza feita pelo mantenedor. Fonte fixada no commit 47cd752f4323f703e304104173633ee31462b9b3:
https://github.com/danielmiessler/SecLists/blob/47cd752f4323f703e304104173633ee31462b9b3/Passwords/Common-Credentials/100k-most-used-passwords-NCSC.txt

São senhas comuns presentes em vazamentos, não uma cópia completa ou atualizada em tempo real de todos os vazamentos. Uma senha ausente significa apenas que não consta nesta versão da base; não equivale a uma garantia de segurança. Licença MIT e atribuição acompanham a base em Security/PasswordData/LICENSE-SecLists.txt.

Alternativa avaliada: Pwned Passwords oferece API gratuita por k-anonimato (https://haveibeenpwned.com/API/v3#PwnedPasswords). Usa somente os primeiros cinco caracteres hexadecimais de SHA-1 calculado no backend e comparação local dos sufixos; esse SHA-1 não é o hash de armazenamento. Oferece cobertura maior e atualização pelo provedor, mas exige saída de rede, timeout, tratamento de falha, proteção da telemetria HTTP e avaliação de privacidade do prefixo. Não foi ativada nesta entrega. Uma base completa local teria maior custo operacional de distribuição, armazenamento e atualização. A interface permite substituir o mecanismo sem mover a regra para endpoints.

## Comportamento

- Novas senhas: 8 a 500 unidades UTF-16 (limite técnico compatível com o formulário atual), sem exigência de letras maiúsculas, números ou símbolos. Mantido o mínimo existente.
- Comparação exata, ordinal: não aplica trim, conversão de caixa ou normalização Unicode. Frases-senha, colagem e gerenciadores continuam permitidos. Variantes são bloqueadas quando estão explicitamente na base; não há normalização oculta.
- A credencial compartilhada antiga continua proibida; sua comparação SHA-256 preexistente foi centralizada e estendida aos demais caminhos. Esse digest não é usado para armazenar senhas.
- Hash PBKDF2 existente, salt, custo e atualização de hash não foram alterados.
- Sem MFA, expiração periódica de senha, redefinição em massa ou consulta no login. Rehash de senha existente também não consulta a política.
- Rejeição: HTTP 400 com código estável password_compromised e mensagem orientando outra senha ou frase-senha, sem dados da conta.

## Fluxos

| Fluxo | Aplicação |
| --- | --- |
| Usuário/paciente novo | Valida credencial aleatória gerada antes de gravar o usuário |
| Troca autenticada / primeiro acesso com senha atual | ChangePasswordCommandHandler |
| Confirmação de recuperação / convite de primeiro acesso por token | ConfirmPasswordResetCommandHandler |
| Recuperação por credencial temporária | Valida geração e CompleteAsync; AuthenticateAsync não consulta |
| Criação de equipe | TeamUseCases.CreateAsync preserva a senha sem trim |
| Provisionamento de clínica | Valida administrador e equipe inicial antes de iniciar gravações |
| Alteração administrativa da clínica | Valida nova senha do administrador e nova equipe antes de mutações |
| Seed administrativo | Valida Seed:InitialPassword ou valor gerado antes de gerar usuários |

Copiar um hash de uma identidade existente para seu vínculo autorizado e atualizar o custo do hash não definem uma nova senha. PINs de operador têm política e fluxo próprios, fora desta alteração.

## Indisponibilidade e privacidade

A base é carregada uma vez, sob demanda e com segurança para concorrência, verificando SHA-256 do arquivo completo e quantidade mínima de entradas. Somente dados públicos de referência são mantidos em memória; não há cache de candidatos ou resultados por senha. Não há logs, métricas ou transmissão de senhas candidatas ou hashes completos na implementação da consulta.

Ausência, corrupção ou falha de leitura retorna Unavailable. A Application recusa a definição de senha; a API responde 503, sem afirmar que verificou a senha e sem consumir o token ou substituir o hash. Um seed necessário também falha fechado. O login existente não carrega nem consulta a base. Após corrigir o artefato, reiniciar o processo para recarregar a referência; não existe fallback silencioso de aceitação.

Não há acesso a dados clínicos ou novos serviços pagos. Não foi alterada configuração de produção nem realizado deploy.

## Manutenção

1. Revisar periodicamente (sugestão: trimestralmente) a fonte e a cobertura, incluindo variantes em português. Não exportar senhas dos usuários para construir a base.
2. Fixar o novo commit da fonte, revisar licença e obter somente a lista pública. Não atualizar automaticamente durante build, startup ou requisições.
3. Substituir Security/PasswordData/ncsc-100k.txt, preservando os bytes originais; .gitattributes desativa conversão de fim de linha neste arquivo.
4. Calcular SHA-256 com Get-FileHash e atualizar ExpectedSha256 em LocalCompromisedPasswordLookup. Registrar commit, contagem e checksum neste documento. Revisar conteúdo e contagem para não substituir acidentalmente por página HTML ou lista vazia.
5. Executar testes de NewPasswordPolicy, PasswordPolicy_, recuperação, provisionamento, isolamento e login, incluindo artefato publicado. A publicação deve incluir o recurso incorporado e a licença.
6. Revisar a atualização por PR e publicar somente com autorização do ambiente. Não reavaliar senhas existentes nem exigir troca em massa.

Checksum atual: C2E5696882C603B76BB67A47EE970897E5A76FC4C3F5547ABE3D0CA340C576E0. Referência incorporada em 2026-10-07.
