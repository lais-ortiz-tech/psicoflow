# PsicoFlow

Primeira fatia: acesso a um workspace individual e cadastro mínimo de pacientes. Esta versão é uma demonstração local, não está pronta para uso com dados reais.

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
- Lista, busca, consulta individual, cadastro, edição e arquivamento de pacientes.
- Campos mínimos: nome obrigatório e contato opcional. Não há anotações clínicas nem data de nascimento.
- O workspace é obtido exclusivamente da identidade autenticada no servidor; identificadores de pacientes de outro workspace não são retornados.
- A listagem padrão omite arquivados; a opção “Mostrar arquivados” permite consultá-los.
- Dados persistidos localmente em `src/PsicoFlow.Api/data/patients.json` (ou no caminho configurado em `Storage:FilePath`).

Não foram incluídos logging/auditoria de eventos nem testes automatizados, conforme combinado. A conta única configurada é adequada apenas para demonstrar a fatia local; gerenciamento de usuários/workspaces, banco de dados de produção, proteção CSRF dedicada, políticas de retenção e implantação segura ainda exigem decisões antes de qualquer piloto. Use exclusivamente dados sintéticos e não insira informações de pacientes reais.

