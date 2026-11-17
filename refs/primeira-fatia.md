# Primeira fatia: acesso ao workspace e pacientes

## Objetivo

Entregar a base mínima para uma profissional entrar em seu workspace e manter uma lista de pacientes isolada de outros workspaces. Esta fatia prepara o fluxo de continuidade, mas **ainda não comprova** que o problema de recuperação de contexto ou acompanhamento foi resolvido.

## Resultado demonstrável

Com dados sintéticos, uma profissional autenticada acessa seu workspace, cadastra um paciente, encontra e atualiza seu cadastro e pode arquivá-lo. Um acesso não autorizado ou pertencente a outro workspace não consegue consultar ou alterar esse paciente.

## Escopo

- Base executável do backend C#/.NET e do frontend React + TypeScript + Vite.
- Autenticação e acesso a um workspace individual.
- Cadastro, listagem, consulta, edição e arquivamento de pacientes.
- Isolamento dos dados por workspace no servidor.
- Tratamento de erros e validação de entradas essenciais.
- Registro de eventos relevantes de acesso ou alteração, sem dados clínicos em logs técnicos.
- Instruções para executar e testar a solução localmente com dados sintéticos.

Manter o cadastro de pacientes mínimo: definir os campos com a profissional e coletar apenas o necessário. Não incluir anotações clínicas nesta fatia.

## Critérios de aceite

1. Uma pessoa autenticada consegue acessar seu workspace e cadastrar um paciente com os campos obrigatórios definidos para a fatia.
2. A profissional consegue listar, localizar, consultar e editar pacientes do próprio workspace. Dados inválidos geram mensagens compreensíveis, sem gravação parcial.
3. A profissional consegue arquivar um paciente; ele deixa de aparecer na listagem padrão, sem apagar seu cadastro. O acesso a arquivados deve ser definido na interface para evitar que o registro fique inacessível.
4. Uma requisição sem autenticação não acessa dados protegidos. Uma profissional de outro workspace não consegue consultar, editar ou arquivar o paciente, inclusive ao informar diretamente seu identificador.
5. Os eventos relevantes de acesso e alteração são registráveis sem expor conteúdo clínico em logs técnicos. Falhas de operação são informadas, sem apresentar sucesso indevido.
6. A demonstração, os testes e os exemplos utilizam dados sintéticos; as instruções permitem executar a fatia localmente.

Os critérios são um ponto de partida e podem ser refinados com a profissional antes da implementação.

## Fora desta fatia

Registros clínicos, timeline, tarefas, agenda, IA, WhatsApp, notificações e integrações externas. Esses itens pertencem a etapas posteriores, mesmo quando constam no escopo priorizado do produto.

## Validação e evidência para portfólio

- Demonstrar o fluxo de cadastro até arquivamento com dados sintéticos.
- Automatizar testes para as regras da fatia, especialmente autenticação, isolamento entre workspaces, validação e arquivamento.
- Registrar a decisão de isolamento e seus trade-offs, com links para os testes e o fluxo demonstrável quando existirem.
- Conversar com a profissional para confirmar os campos mínimos e observar como ela identifica e encontra pacientes hoje. Registrar achados sem dados identificáveis.

Não afirmar economia de tempo nem melhoria de continuidade até haver um fluxo que as suporte e uma comparação observada.

## Próximas fatias candidatas

1. Registros livres e timeline por paciente, com preservação e versionamento das anotações.
2. Tarefas e acompanhamentos manuais com status e prazo.
3. Agenda básica e integração da visão operacional.
4. Recuperação contextual e sugestões de IA com fontes e aprovação humana.

A ordem poderá mudar após observar a rotina real e validar as necessidades da profissional.