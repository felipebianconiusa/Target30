# Target30

Dashboard pessoal de finanças: conecta em bancos e cartões via [Plaid](https://plaid.com/docs/) e centraliza faturas e transações numa tela única, substituindo o controle manual em planilha.

## Estrutura

```
Target30.slnx
src/
  Target30.Api/     # ASP.NET Core Web API (.NET 10) — integração Plaid, persistência
frontend/            # Angular (a criar — ver abaixo)
```

## Backend (.NET)

```bash
dotnet run --project src/Target30.Api
```

Configure as credenciais do Plaid em `src/Target30.Api/appsettings.Development.json` (não versionado — veja `.gitignore`):

```json
{
  "Plaid": {
    "ClientId": "seu-client-id",
    "Secret": "seu-secret-sandbox",
    "Environment": "sandbox"
  }
}
```

Nunca coloque credenciais reais em `appsettings.json` (esse arquivo é versionado).

## Frontend (Angular)

```bash
cd frontend
ng serve
```

Abre em `http://localhost:4200`. O dev server tem proxy configurado (`proxy.conf.json`) redirecionando `/api/*` para `https://localhost:7059` (API .NET), e a API já está com CORS liberado para `http://localhost:4200`.

## Persistência (EF Core + SQLite)

O `access_token` do Plaid nunca volta pro frontend — fica guardado no banco (`target30.db`, gitignored) e é resolvido no servidor a partir do `itemId`.

```bash
cd src/Target30.Api
dotnet ef database update   # cria/atualiza target30.db (dotnet tool install --global dotnet-ef, se precisar)
```

As migrations rodam automaticamente também no startup da API (`Database.Migrate()` em `Program.cs`).

## Plaid — endpoints já implementados

`PlaidController` (via [Going.Plaid](https://github.com/viceroypenguin/Going.Plaid)):

- `POST /api/plaid/link-token` — gera o `link_token` que o frontend usa para abrir o Plaid Link
- `POST /api/plaid/exchange-token` — troca o `public_token` por um `access_token` e persiste como `PlaidItem`
- `GET /api/plaid/items` — lista as instituições já conectadas (sem expor o `access_token`)
- `GET /api/plaid/items/{itemId}/transactions` — sincroniza transações (`transactions/sync`) do item

Verificado ponta a ponta contra o sandbox real do Plaid.

## Autenticação (Google Sign-In)

Login via [Google Identity Services](https://developers.google.com/identity/gsi/web) — o frontend recebe um ID token do Google, o backend valida (`Google.Apis.Auth`) e abre uma sessão por cookie. Todas as rotas de `PlaidController` são `[Authorize]` e filtradas pelo `UserId` (o `sub` do Google) — cada usuário só vê suas próprias contas.

Configuração necessária:

1. Crie um **OAuth Client ID** (tipo *Web application*) em [console.cloud.google.com/apis/credentials](https://console.cloud.google.com/apis/credentials), com `http://localhost:4200` em *Authorized JavaScript origins*. Não precisa de Client Secret nem de redirect URI — o fluxo usa só o ID token.
2. Cole o Client ID em dois lugares:
   - `frontend/src/app/auth/google-client-id.ts`
   - `src/Target30.Api/appsettings.Development.json` → `Authentication:Google:ClientId`

## Acesso pelo celular (opcional)

O app roda no seu PC; pra usar no celular (ou receber os alertas com o PC desligado) ele precisa estar acessível de fora. O que já está pronto no projeto:

- **PWA**: o app é instalável ("Adicionar à tela inicial") quando aberto por HTTPS.
- **Um processo só**: `scripts/publish-local.ps1` gera o build do Angular e copia pra `src/Target30.Api/wwwroot`; com isso a própria API serve o app em `https://localhost:7059` (mesma origem, sem `ng serve`). Sem `wwwroot`, nada muda.
- **Lista de emails permitidos**: `Authentication:AllowedEmails` (array) em `appsettings.Development.json`/variável de ambiente. Com a lista, só esses emails (verificados pelo Google) conseguem entrar; **sem a lista, qualquer conta Google entra**. Configure **antes** de expor o app, senão um estranho poderia logar e conectar contas usando as suas chaves do Plaid.

```json
"Authentication": { "AllowedEmails": [ "voce@gmail.com" ] }
```

O que **não** está feito (depende de você escolher e criar conta): expor o endereço fora do localhost. Opções sem servidor na nuvem: um túnel (Tailscale Funnel/Serve ou Cloudflare Tunnel) apontando pra `https://localhost:7059`. Depois disso, adicione o novo endereço em *Authorized JavaScript origins* do OAuth Client do Google. O banco (`target30.db`) guarda os access tokens do Plaid: mantenha-o no seu PC/servidor de confiança e fora do git.
## Agent Skills

Skills do [skills.sh](https://skills.sh) instalados pro Claude Code (EF Core, ASP.NET Core, Angular, revisão de segurança) — `.agents/` e `.claude/skills/` são gerados localmente e não ficam no repo (os symlinks de `.claude/skills` são de caminho absoluto, não portáveis entre máquinas). `skills-lock.json` fica versionado como referência de quais skills o projeto usa.

Depois de clonar, reinstale (o `npx skills experimental_install` sozinho não recria a integração com o Claude Code, então é melhor repetir os `add`):

```bash
npx skills add dotnet/skills --skill optimizing-ef-core-queries -y
npx skills add dotnet/skills --skill dotnet-webapi -y
npx skills add dotnet/skills --skill writing-mstest-tests -y
npx skills add dotnet/skills --skill test-gap-analysis -y
npx skills add dotnet/skills --skill migrate-nullable-references -y
npx skills add github/awesome-copilot --skill dotnet-best-practices -y
npx skills add github/awesome-copilot --skill dotnet-design-pattern-review -y
npx skills add github/awesome-copilot --skill ef-core -y
npx skills add angular/angular --skill angular-developer -y
npx skills add getsentry/skills --skill security-review -y
npx skills add openai/skills --skill security-best-practices -y
```

## Próximos passos

- [ ] Guardar transações localmente (hoje `transactions/sync` busca do Plaid a cada chamada, sem persistir)
- [ ] Tela de settings/perfil (hoje só tem o botão "Sair" no header)
