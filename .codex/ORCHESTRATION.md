# Orquestração Codex — Hemodinks/hemodinks-api

Este diretório adiciona agentes especializados sem modificar `AGENTS.md` nem regras de negócio.

## Política de execução

1. Ler `AGENTS.md` e localizar os arquivos e testes pertinentes.
2. Classificar tarefa: **baixo** (texto/CSS ou ajuste localizado), **moderado** (fluxo único), **alto** (múltiplos componentes/contratos/banco), **crítico** (autenticação, autorização, JWT, sessão ou ClinicaId).
3. Tarefas baixas: agente principal diretamente; moderadas: agente principal e, quando útil, especialista; altas ou críticas: usar `architect` ou `security` para investigação independente. Testes proporcionais ao risco.
4. Não executar subagentes que editem arquivos coincidentes; limitar a dois ativos.
5. Para riscos críticos exigir testes negativos de isolamento de clínica e permissões, quando aplicável.

## Esforço

`medium` é usado por especialistas de implementação/teste; `high` por especialistas de análise crítica. O esforço é fixado por papel em cada arquivo, mas o nível do **agente principal** não é alterado automaticamente por estas instruções. `xhigh` só deve ser escolhido manualmente ou por uma configuração externa quando houver suporte confirmado pelo modelo; não é necessário para toda tarefa crítica.

## Validação local

Executar `codex --version` e verificar se a versão instalada reconhece configurações de agentes; em seguida abrir o repositório como projeto confiável, reiniciar a sessão e solicitar que o Codex liste os papéis disponíveis. A presença dos arquivos no GitHub não comprova, por si só, a ativação na instalação local. Nenhuma validação de runtime foi realizada por esta mudança remota.

## Limites

Sem alteração automática do seletor da sessão principal. Sem garantia de economia de tokens. Não há instalação de dependências, criação de serviços externos ou mudanças em CI/CD. A documentação de referência é https://developers.openai.com/ja-JP/docs/config-file/config-reference.

## Relatório

Exibir classificação e riscos, agentes realmente acionados (não presumidos), arquivos alterados, testes executados e pendências.
