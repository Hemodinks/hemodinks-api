# Verificação em duas etapas (MFA TOTP) — #150

Opcional, por adesão, para **Administrador** e **SuperAdministrador**. Os demais perfis não veem a opção
(`elegivel: false`) e recebem `403` ao tentar configurar.

## Fluxo

1. `POST /api/users/me/mfa/configuracao` gera um segredo novo (pendente) e devolve `segredo` e `otpAuthUri`
   para o aplicativo autenticador (Google Authenticator, Microsoft Authenticator, 1Password etc.).
2. `POST /api/users/me/mfa/ativacao` com o primeiro código ativa o MFA e devolve **uma única vez** os
   10 códigos de recuperação (uso único, guardados só como hash SHA-256).
3. Login: com MFA ativo, `POST /api/users/authenticate` com a senha certa devolve só `mfaDesafio`
   (`token`, `expiraEm`): nenhum token de acesso, cookie ou dado do usuário. O desafio vale 5 minutos,
   aceita 5 tentativas, é de uso único e pertence à clínica do login.
4. `POST /api/users/authenticate/mfa` com `desafio` e `codigo` (TOTP de 6 dígitos ou código de recuperação)
   abre a sessão exatamente como o login sem MFA.
5. `POST /api/users/me/mfa/desativacao` exige senha **e** código.

Login coletivo de equipe não usa MFA.

## Segurança

- O segredo fica cifrado pelo Data Protection (mesmo anel de chaves protegido por certificado do CPF, propósito
  `HemodinksAPI.Mfa.TotpSecret.v1`). Nunca vai para log nem volta em resposta depois da configuração.
- TOTP RFC 6238 (SHA-1, 30 s, 6 dígitos), tolerância de ±1 passo e anti-replay: um código já aceito não serve
  de novo, nem dentro da mesma janela de 30 s.
- Falhas no segundo fator contam no bloqueio de conta, como senha errada. `/authenticate/mfa` tem limite de taxa
  por desafio e por IP (`AuthenticationRateLimiting`); configuração, ativação e desativação usam a política
  `PasswordReset`.
- Auditoria da plataforma: `mfa.enable`, `mfa.disable`, `mfa.reset`, `mfa.failure`, `mfa.recovery_code_used`.

## Contingência

- **Perdeu o celular**: entre com um código de recuperação e desative/reative o MFA.
- **Perdeu celular e códigos**: um SuperAdministrador da mesma clínica usa
  `POST /api/users/{id}/mfa/redefinicao`. O MFA é limpo, a `SecurityVersion` muda (todas as sessões do titular
  caem) e a ação fica na auditoria. Usuário de outra clínica responde `404`.
- **"Código inválido" com o código certo**: confira o horário automático do celular (o servidor tolera ±30 s).
  Logo depois de ativar, o mesmo código não serve no login; espere o próximo.
- **Anel de chaves do Data Protection perdido ou trocado**: os segredos ficam ilegíveis (log de erro
  "Segredo de MFA ilegivel", sem dado sensível) e o login com MFA falha. Restaure o anel de chaves
  (`docs/issue-184-backup-recovery.md`) ou redefina o MFA dos administradores afetados.

## Deploy

Migration aditiva `20261124140539_Schema_Mfa` (colunas novas em `UsuariosGlobais` com padrão `false`/`NULL` e
tabelas `MfaCodigosRecuperacao` e `MfaLoginDesafios`). A versão antiga da API ignora as colunas, então durante a
troca blue/green ela ainda aceitaria login só com senha: só ative o MFA depois que a versão nova estiver sozinha.
Enquanto nenhum administrador ativar o MFA, o login não muda. O front precisa tratar `mfaDesafio` antes de alguém
ativar.
