# Política de segurança

O HemoDinks é um sistema de saúde multi-clínica. Levamos a sério qualquer relato que possa afetar a
confidencialidade dos dados de pacientes e o isolamento entre clínicas.

## Como relatar uma vulnerabilidade

**Não abra issue, discussão ou pull request público** para relatar uma falha de segurança.

Use o relato privado do GitHub:
[Report a vulnerability](https://github.com/Hemodinks/hemodinks-api/security/advisories/new)
(aba *Security* → *Report a vulnerability*). Só os mantenedores veem o relato.

Inclua, se possível:

- o componente afetado (rota, tela, workflow ou configuração);
- passos para reproduzir e o impacto esperado;
- a versão ou o commit em que a falha foi observada.

Não inclua dados reais de pacientes, CPF, senhas, tokens ou qualquer credencial no relato.
Se precisar demonstrar acesso indevido, use apenas dados de teste.

## O que esperar

- Confirmamos o recebimento assim que possível e mantemos você informado pelo próprio relato.
- A correção é publicada antes de qualquer divulgação dos detalhes da falha.
- Com sua autorização, damos crédito pelo relato no aviso de segurança publicado.

## Escopo

Este repositório e as versões em produção do HemoDinks. Testes que degradem o serviço, acessem dados
de terceiros ou usem engenharia social não são autorizados.
