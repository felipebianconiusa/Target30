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

Verificado ponta a ponta contra o sandbox real do Plaid (link-token → exchange → 48 transações sincronizadas).

## Próximos passos

- [ ] Tela de listagem das contas conectadas + extrato consolidado no Angular
- [ ] Guardar transações localmente (hoje `transactions/sync` busca do Plaid a cada chamada, sem persistir)
- [ ] Autenticação de usuário (hoje é single-user, `ClientUserId` fixo em "target30-user")
