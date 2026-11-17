# Decisões técnicas iniciais

Este documento registra decisões de partida para orientar a implementação. Elas podem ser revistas quando surgirem evidências ou necessidades concretas. Não descrevem funcionalidades já implementadas.

## 1. Backend: C#/.NET

**Decisão:** usar C#/.NET no backend.

**Contexto:** a desenvolvedora possui nove anos de experiência em .NET e quer demonstrar capacidade sênior de identificar e resolver problemas operacionais.

**Trade-off:** aproveitar uma stack conhecida favorece profundidade e produtividade. Python pode ser atrativo para certos trabalhos de IA e dados, mas aprender uma stack nova não é necessário para integrar um provedor de IA nem é o objetivo principal deste projeto.

**Em aberto:** versão do .NET, bibliotecas e detalhes de hospedagem serão escolhidos na implementação, considerando suporte e necessidades reais.

## 2. Frontend: React + TypeScript + Vite

**Decisão:** começar com React e TypeScript, usando Vite.

**Contexto:** a experiência inicial é um workspace autenticado; não há necessidade identificada de renderização no servidor.

**Trade-off:** uma SPA reduz a infraestrutura inicial, mas exige decisões cuidadosas sobre autenticação, autorização e publicação. Next.js, sugerido no documento original, pode ser reavaliado caso surjam requisitos que justifiquem seus recursos de servidor.

## 3. Arquitetura: monólito modular

**Decisão:** iniciar com um backend em monólito modular, separando responsabilidades de forma pragmática.

**Contexto:** o produto começa para atuação individual e precisa evoluir em pequenas fatias.

**Trade-off:** um deploy unificado simplifica desenvolvimento e operação; as fronteiras dos módulos exigem disciplina. Não criar microserviços nem infraestrutura distribuída apenas para demonstrar complexidade.

**Responsabilidades previstas:** workspace e acesso; pacientes; registros clínicos; tarefas e acompanhamentos; agenda; recuperação e sugestões de IA; auditoria e governança. Separar lembretes, notificações e integração de mensagens quando esses fluxos forem necessários, não necessariamente como módulos ou serviços independentes desde o início.

## 4. Isolamento e dados sensíveis

**Decisão:** identificar o workspace no servidor e restringir todas as operações de dados ao workspace autorizado, mesmo que o produto comece com uma única profissional.

**Consequências:** não confiar em identificadores enviados pelo cliente como prova de autorização; testar acessos cruzados; evitar conteúdo clínico em logs técnicos; usar dados sintéticos. Registrar ações relevantes de forma proporcional, sem duplicar conteúdo clínico desnecessariamente.

**Em aberto:** mecanismo concreto de autenticação, políticas de retenção e exclusão, criptografia e requisitos para uso com dados reais. Essas escolhas precisam considerar LGPD, sigilo profissional, normas aplicáveis e fornecedores antes de um piloto real.

## 5. IA como apoio revisável

**Decisão:** a IA será apoio operacional e de recuperação de contexto, sem diagnóstico, recomendação de intervenção ou decisão clínica. Não faz parte da primeira fatia.

**Consequências:** preservar a distinção entre registro original, sugestão e ação aprovada. Sugestões devem ser revisáveis; tarefas e acompanhamentos derivados só são criados após aprovação explícita. Respostas contextuais devem referenciar registros que as fundamentam e declarar limites quando não houver evidência suficiente.

**Trade-off:** revisão humana e rastreabilidade adicionam etapas, mas evitam tratar uma saída de IA como decisão ou registro clínico original.

## 6. Infraestrutura e recuperação: iniciar pelo necessário

**Direção:** PostgreSQL é a opção sugerida no documento original para persistência; confirmar sua adoção ao desenhar a base. Quando a recuperação contextual entrar em escopo, começar avaliando busca textual com referências; busca vetorial ou híbrida é uma evolução se uma avaliação mostrar benefício.

**Trade-off:** uma solução inicial simples é mais fácil de operar e avaliar. Ela pode não recuperar bem termos semanticamente próximos; essa limitação deve ser testada, não presumida resolvida.

**Não antecipar:** pgvector, Redis, workers, outbox, orquestração de agentes e infraestrutura de notificações não são pré-requisitos da primeira fatia. Introduzi-los quando houver um fluxo e requisitos que os justifiquem.

## 7. Integrações adiadas

**Decisão de priorização:** começar pela interface web e adiar WhatsApp, notificações proativas e assistente conversacional geral.

Se a integração WhatsApp for retomada, a intenção original é usar a plataforma oficial e conversar apenas com a profissional, não com pacientes. O modelo de número — oficial do produto ou profissional conectado — permanece em aberto.

## Como manter este documento

Ao mudar uma decisão, registrar contexto, alternativas, consequências e motivo da mudança. Documentar somente o que foi decidido ou observado; não transformar opções futuras em requisitos já implementados.