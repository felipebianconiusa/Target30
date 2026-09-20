# Do app pessoal ao produto — checklist

O código já tem: teste grátis + assinatura via Stripe (desligada por padrão), limite de bancos por usuário, tokens do Plaid criptografados, lista de espera pública, Termos/Privacidade (**rascunhos**), backup automático e CI. O que segue é o que **depende de você** (contas, empresa, revisão jurídica) e de decisões de negócio. Nada aqui é aconselhamento jurídico ou fiscal.

## 1. Validar antes de gastar mais (1–3 semanas)

- [ ] Publicar a página inicial (a lista de espera já funciona) e levar tráfego só do nicho: brasileiros nos EUA construindo crédito (grupos, YouTube/Instagram, indicações).
- [ ] Meta mínima para seguir: **30+ e-mails** e **10 conversas de 20 min**. Menos que isso = ajustar a promessa, não construir mais.
- [ ] Roteiro das conversas: (1) como controla a utilização hoje? (2) quando foi a última vez que o score caiu por causa disso? (3) quanto pagaria por mês para isso resolvido? (4) toparia conectar o banco a um app novo? o que te faria confiar? (5) usaria por celular?
- [ ] Anote preço dito, objeções de confiança e bancos/cartões que usam (para saber se o Plaid cobre).

## 2. Custos e preço (faça a conta antes de cobrar)

O Plaid cobra por conta/conexão conectada por mês e por produto (Transactions, Liabilities, e extras como o `refresh`). **Confira os valores no seu painel/contrato do Plaid** — eu não tenho o seu preço.

```
custo por usuário/mês ≈ (bancos conectados × custo Plaid por conexão/mês) + Stripe (~2,9% + US$0,30) + hospedagem/e-mail
preço mínimo ≈ custo por usuário ÷ (1 − margem desejada)
```

- [ ] Preencher a conta com 2 bancos/usuário (média) e 4 (usuário pesado).
- [ ] Definir `Billing:MaxItemsPerUser` para o pior caso ainda dar lucro.
- [ ] Decidir o preço (`PriceLabel` só exibe; o preço real é o do Stripe) e a política de reembolso (Termos §4).

## 3. Plaid para outros usuários

- [ ] No Dashboard do Plaid, completar o **Application profile**: nome da empresa, logo, URL da **política de privacidade** e dos termos (as páginas `/privacy` e `/terms` são públicas), caso de uso.
- [ ] Responder o questionário de segurança/privacidade do Plaid e pedir o acesso de produção para uso por terceiros (o app hoje está em produção **para as suas contas**). Ter em mãos: onde os dados ficam, criptografia (tokens criptografados ✔), controle de acesso, backups ✔, resposta a incidentes.
- [ ] Implementar o fluxo de **reconexão** (`ITEM_LOGIN_REQUIRED`/update mode do Link) — hoje o app só avisa que a conexão precisa de atenção.
- [ ] Configurar **webhooks do Plaid** (`SYNC_UPDATES_AVAILABLE`) quando o app estiver num endereço público, para sincronizar na hora em vez de a cada 6h.

## 4. Hospedagem e segurança (antes de qualquer terceiro)

- [ ] Servidor sempre ligado com HTTPS (o app já serve o front pela API: `scripts/publish-local.ps1`). Opções: VPS pequeno com Caddy/Nginx, ou um serviço de contêiner. **Não** expor o seu PC.
- [ ] `Authentication:AllowedEmails` deixa de valer para o público — para usuários externos, remova a lista e ligue `Billing:Enabled` (o dono fica em `Billing:ExemptEmails`).
- [ ] Segredos só em variáveis de ambiente/gerenciador de segredos: Plaid, Stripe, SMTP, Google. Rotacionar qualquer chave que já tenha ido parar em chat/log.
- [ ] Guardar a pasta `keys/` (chaves dos tokens) **separada** do banco e dos backups; testar restaurar um backup + chaves em outra máquina.
- [ ] Trocar SQLite por Postgres se houver muitos usuários simultâneos (SQLite serve bem para dezenas).
- [ ] Monitoramento e alerta de queda (UptimeRobot etc.), logs sem dados financeiros, plano de resposta a incidente (quem avisa quem, em quanto tempo).
- [ ] Revisão de segurança independente antes de escalar (mesmo que pontual).

## 5. Jurídico e empresa

- [ ] Abrir a entidade (LLC/CNPJ, conforme sua situação de residência e vistos) — **falar com contador/advogado**, principalmente pelas regras de trabalho/visto nos EUA para renda de negócio próprio.
- [ ] Revisar Termos e Privacidade com advogado (LGPD, CCPA/leis estaduais, GLBA/FTC Safeguards podem se aplicar a apps que tratam dados financeiros) e preencher os `[PLACEHOLDERS]` + `legal.config.ts` (`IS_DRAFT = false`).
- [ ] Manter o aviso "não é aconselhamento financeiro"; **não** prometer efeito no score.
- [ ] Se um dia houver afiliados de cartão: divulgar a comissão de forma clara (FTC) e separar visualmente da recomendação.
- [ ] Stripe: ativar Stripe Tax se for cobrar de estados com imposto sobre software; ativar o portal do cliente.

## 5b. Operação

- [ ] E-mail de suporte (o mesmo do `LEGAL.CONTACT_EMAIL`) e prazo de resposta prometido.
- [ ] Rotina de "apagar minha conta" testada (Configurações já apaga os dados) e prazo de descarte dos backups descrito na Privacidade.
- [ ] Beta fechado: 10–20 pessoas da lista, preço reduzido, uma conversa por semana com cada uma.

## 6. Ordem sugerida

1. Validação (seção 1) → decisão de seguir.
2. Conta do preço (2) → hospedagem (4) → Plaid produção terceiros (3) → jurídico (5).
3. Ligar cobrança com chaves de **teste** do Stripe, fazer o beta, depois trocar para `sk_live_`.
