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

## Plaid — endpoints já implementados

`PlaidController` (via [Going.Plaid](https://github.com/viceroypenguin/Going.Plaid)):

- `POST /api/plaid/link-token` — gera o `link_token` que o frontend usa para abrir o Plaid Link
- `POST /api/plaid/exchange-token` — troca o `public_token` (retornado pelo Link) por um `access_token`
- `GET /api/plaid/transactions?accessToken=...` — sincroniza transações (`transactions/sync`)

Faltam duas coisas antes de funcionar de verdade:

1. Credenciais reais de sandbox em `appsettings.Development.json` (seção `Plaid`)
2. Persistir o `access_token` por usuário — hoje o exchange só retorna o `itemId`, ele não guarda o token em lugar nenhum ainda

## Próximos passos

- [ ] Criar conta Plaid (sandbox) e obter `ClientId`/`Secret`
- [ ] Definir persistência (EF Core + provider a escolher) e guardar o `access_token` por usuário
- [ ] Tela de conexão de contas (Plaid Link) no Angular
- [ ] Tela inicial: extrato consolidado de contas/cartões
