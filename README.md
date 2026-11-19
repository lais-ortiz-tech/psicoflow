# PsicoFlow

Demonstração local de um workspace individual para organizar pacientes, agenda, histórico de sessões e pendências. Esta versão não está pronta para uso com dados reais.

## Requisitos

- .NET SDK 11
- Node.js 18 ou superior e npm

## Executar localmente

Abra dois terminais na raiz do repositório.

Terminal 1 — API:

```powershell
dotnet run --project src/PsicoFlow.Api
```

Terminal 2 — interface:

```powershell
cd web
npm install
npm run dev
```

Abra http://localhost:5173. A interface encaminha as chamadas `/api` para a API em `http://localhost:5090`.

Conta sintética local:

- E-mail: `demo@psicoflow.local`
- Senha: `PsicoFlow-Demo-2026!`

A conta está apenas em `appsettings.Development.json` para demonstração; não use essas credenciais nem esta configuração em produção. Para alterar a conta local, ajuste o arquivo de desenvolvimento. Em ambientes não locais, configure `DemoAccount:Email`, `DemoAccount:Password`, `DemoAccount:WorkspaceId` e `DemoAccount:DisplayName` por variáveis de ambiente ou por um provedor seguro de configuração.

## O que está incluído

- Autenticação por cookie HTTP-only e sessão protegida.
- Página inicial do workspace com resumo de pacientes ativos, compromissos futuros e atalhos para pacientes e agenda.
- Lista, busca, consulta individual, cadastro, edição e arquivamento de pacientes.
- Página individual do paciente com visão geral, histórico de sessões e pendências.
- Registro e edição de sessões com histórico de versões, data da alteração e responsável.
- Pendências vinculadas ao paciente, com prazo opcional, edição e conclusão.
- Agenda para criar, consultar, editar e cancelar compromissos vinculados a pacientes ativos.
- Validação de conflitos por sobreposição de horários em compromissos não cancelados do workspace; horários adjacentes são permitidos.
- Cadastro de pacientes com nome obrigatório, telefone, e-mail, nascimento, profissão, frequência, situação, dia e horário habituais, valor, queixa principal e observações. Contatos cadastrados anteriormente são preservados. A situação cadastral não arquiva o paciente nem cria compromissos ou lançamentos financeiros automaticamente.
- Sessões, versões e pendências são armazenadas no SQLite local e sempre consultadas no escopo do workspace e do paciente autenticado.
- O workspace é obtido exclusivamente da identidade autenticada no servidor; identificadores de pacientes de outro workspace não são retornados.
- A listagem padrão omite arquivados; a opção “Mostrar arquivados” permite consultá-los.
- Pacientes e compromissos persistidos localmente em SQLite, no arquivo `src/PsicoFlow.Api/data/psicoflow.db` (ou no caminho configurado em `Storage:DatabasePath`). Na primeira inicialização, pacientes existentes em `data/patients.json` (ou no caminho legado configurado em `Storage:FilePath`) são importados uma única vez; o JSON original é mantido como cópia.

O histórico de versões de sessões não substitui uma auditoria abrangente de acesso e eventos. A conta única configurada é adequada apenas para demonstrar a fatia local; antes de qualquer piloto, ainda precisam ser avaliados gerenciamento de usuários/workspaces, banco de dados de produção, proteção CSRF dedicada, controles de acesso, políticas de retenção e implantação segura. Use exclusivamente dados sintéticos e não insira informações de pacientes reais. O uso de dados clínicos reais exige avaliação de LGPD, sigilo profissional, normas aplicáveis e segurança da solução.

