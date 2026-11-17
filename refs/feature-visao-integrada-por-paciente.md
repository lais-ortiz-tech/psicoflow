# PsicoFlow — visão integrada por paciente

## Objetivo

Permitir que a psicóloga acesse, a partir da lista de pacientes, uma página que reúna o contexto necessário para acompanhar cada atendimento: **visão geral, histórico de sessões e pendências**.

A proposta é apoiar a continuidade entre sessões, reduzindo a necessidade de procurar informações em diferentes telas.

## Escopo

### 1. Navegação

Na tela **Pacientes**, o nome de cada paciente deve ser clicável e levar à sua página individual.

A página deve identificar o paciente e permitir o retorno à lista. As ações existentes de editar e arquivar devem continuar funcionando.

### 2. Visão geral

Exibir, quando disponíveis:

- Data da última sessão registrada, com acesso ao registro.
- Próximo compromisso agendado.
- Pendências em aberto, com seus respectivos prazos, quando informados.

Se não houver dados, apresentar uma mensagem clara. Caso a agenda ainda não esteja implementada, indicar que o próximo compromisso ainda não está disponível, sem exibir dados fictícios.

### 3. Histórico de sessões

Permitir que a psicóloga:

- Registre uma sessão informando a data e uma anotação livre.
- Consulte as sessões do paciente em ordem cronológica decrescente.
- Abra e edite um registro, preservando o histórico de versões.
- Consulte versões anteriores, identificando data e responsável pela alteração.

As anotações devem permanecer vinculadas ao paciente correto. Uma sessão registrada não depende da existência prévia de um compromisso na agenda.

### 4. Pendências

Permitir que a psicóloga crie manualmente pendências vinculadas ao paciente, informando:

- Descrição.
- Prazo opcional.
- Status: **em aberto** ou **concluída**.

Ela deve poder consultar, editar e concluir uma pendência. Pendências em aberto devem aparecer na visão geral até serem concluídas; as concluídas permanecem disponíveis na área de pendências.

## Fluxo principal

1. A psicóloga acessa a lista e seleciona o nome do paciente.
2. Na página individual, registra uma sessão e cria uma pendência para retomar um assunto.
3. Em um atendimento posterior, abre a página do mesmo paciente e encontra a pendência e o último registro.
4. Após tratar o assunto, marca a pendência como concluída.

## Regras de acesso e privacidade

- O servidor deve validar a autorização de acesso ao workspace **e ao paciente** em cada operação. Um identificador informado pelo cliente não é suficiente como autorização.
- Registros e pendências de um paciente não devem aparecer na página de outro.
- Conteúdo clínico não deve ser incluído em logs.
- Desenvolvimento, testes e demonstrações devem usar **dados sintéticos**.
- Antes de usar dados reais, avaliar requisitos de LGPD, sigilo profissional, normas aplicáveis e segurança da solução.

## Critérios de aceite

- O nome do paciente na listagem navega para sua página individual.
- A página apresenta visão geral, histórico de sessões e pendências, inclusive estados vazios compreensíveis.
- Uma sessão criada aparece apenas no histórico do paciente correspondente.
- Ao editar uma sessão, a versão anterior continua consultável.
- Uma pendência aberta aparece na visão geral do paciente; ao ser concluída, deixa de aparecer entre as abertas e permanece consultável.
- Uma tentativa de acessar dados de outro workspace ou de um paciente não autorizado é recusada pelo servidor.
- A ausência de agenda não impede o registro e a consulta de sessões e pendências.
- Editar e arquivar pacientes continuam funcionando como antes.

## Fora desta fase

- **IA:** nenhuma integração, resumo, busca inteligente ou sugestão automática será implementada agora. Uma segunda fase dependerá de determinação explícita da responsável pelo projeto.
- Financeiro e controle de pagamentos.
- Implementação completa de agenda, notificações e integrações externas.
- Exclusão de registros clínicos e pendências: suas regras devem ser definidas antes de implementar essa operação.

## Evidências para o portfólio

Documentar o problema observado com a psicóloga, a decisão de reunir contexto por paciente e o fluxo implementado. Demonstrar com dados sintéticos e registrar testes das regras de isolamento, versionamento e acompanhamento de pendências. Resultados de uso ou ganho de tempo devem ser apresentados somente após medição real.