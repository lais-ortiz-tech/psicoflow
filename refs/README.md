# PsicoFlow

> Documentação inicial de produto. O projeto ainda será implementado e suas hipóteses precisam ser validadas com a profissional.

## Visão

O PsicoFlow será um workspace operacional para psicólogas e psicólogos que atuam de forma autônoma. A proposta é apoiar a organização e a continuidade do trabalho do consultório: registrar informações, recuperar contexto e acompanhar pendências ao longo do tempo.

O projeto também será um estudo de caso de portfólio sobre identificação de gaps operacionais, priorização, arquitetura e tratamento responsável de dados sensíveis.

## Problemas a validar

Os seguintes gaps são hipóteses de partida, não resultados de pesquisa já comprovados:

1. **Continuidade entre atendimentos:** informações e assuntos a retomar podem ficar dispersos ou depender da memória da profissional.
2. **Pendências sem acompanhamento:** assuntos relevantes e tarefas administrativas podem não se transformar em ações com responsável, prazo e conclusão visíveis.
3. **Organização da rotina:** compromissos e atividades administrativas exigem coordenação e podem gerar conflitos ou esquecimentos.
4. **Confiança e privacidade:** o tratamento de informações clínicas exige acesso restrito, rastreabilidade e cuidado com dados sensíveis.

Antes de generalizar essas hipóteses, observar a rotina da profissional, entender como ela trabalha hoje e identificar quais problemas realmente ocorrem e com que frequência.

## Escopo inicial priorizado

- **Workspace e acesso:** autenticação e isolamento das informações por workspace.
- **Pacientes e registros:** cadastro e consulta de pacientes; registros livres escritos pela profissional, histórico e versionamento.
- **Timeline e visão operacional:** acesso ao histórico, a pendências e a próximos compromissos.
- **Tarefas e acompanhamentos:** criar e acompanhar ações com status e prazo, inclusive assuntos a retomar.
- **Agenda básica:** criar, consultar, alterar e cancelar compromissos, com validação de conflitos.
- **Apoio contextual com IA:** recuperar informações e apresentar sugestões revisáveis com referências aos registros de origem; declarar quando não houver evidência suficiente.
- **Auditoria e governança:** controles proporcionais ao tratamento de dados sensíveis e ao estágio do produto.

Esse é o escopo priorizado, **não a primeira entrega completa**. A primeira fatia está descrita em [primeira-fatia.md](primeira-fatia.md).

## Limites de produto

- A profissional escreve suas próprias anotações; não há gravação nem transcrição de sessões.
- A IA não diagnostica, não recomenda intervenções e não toma decisões clínicas.
- Uma sugestão de IA permanece separada do registro original. Uma tarefa ou acompanhamento derivado dela só é criado após revisão e aprovação explícita da profissional.
- O WhatsApp é uma possibilidade futura para interação da profissional com o assistente, não um chatbot para pacientes. Sua integração, notificações proativas e assistente conversacional geral ficam fora da primeira etapa.
- Financeiro completo, cobrança, emissão automática de nota fiscal, multiusuário/equipes, aplicativo nativo e integrações externas de prontuário ou calendário ficam fora do escopo inicial. Uma tarefa administrativa manual, como lembrar de enviar uma nota, pode fazer parte das pendências.

## Direção técnica

Backend em **C#/.NET**, frontend em **React + TypeScript com Vite** e monólito modular como ponto de partida. As decisões, alternativas e pendências estão em [decisoes-tecnicas.md](decisoes-tecnicas.md).

## Privacidade e uso responsável

Usar somente **dados sintéticos** no desenvolvimento, nos testes, nas demonstrações e nos materiais públicos de portfólio. Dados clínicos são sensíveis: autenticação, autorização, isolamento de dados e logs sem conteúdo clínico devem orientar o desenho desde o início.

Antes de qualquer piloto com dados reais, avaliar LGPD, sigilo profissional, normas aplicáveis do CFP/CRP, segurança, retenção e contratos dos fornecedores. Nenhum mecanismo técnico isolado garante conformidade.

## Evidências para o portfólio

Ao longo da implementação, registrar problemas observados, decisões e alternativas, critérios de aceite, testes de regras importantes e fluxos demonstráveis. Comparar a rotina anterior com a solução apenas quando houver observações reais; não apresentar hipóteses, métricas ou resultados esperados como resultados alcançados.

O projeto ainda não possui instruções de execução: elas serão adicionadas quando a base estiver implementada.