import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LEGAL } from './legal.config';

// RASCUNHO de modelo — não é aconselhamento jurídico. Revise com um advogado antes de usar.
@Component({
  selector: 'app-privacy',
  imports: [RouterLink],
  styleUrl: './legal.scss',
  template: `
    <article class="legal">
      @if (legal.IS_DRAFT) {
        <p class="legal__draft">
          Rascunho de modelo para revisão jurídica — não vale como documento final.
        </p>
      }
      <h1>Política de Privacidade</h1>
      <p class="legal__muted">Última atualização: {{ legal.UPDATED_AT }}</p>

      <h2>1. Quem somos</h2>
      <p>
        {{ legal.COMPANY_NAME }} opera o Target30. Contato sobre privacidade: {{ legal.CONTACT_EMAIL }}.
      </p>

      <h2>2. Dados que coletamos</h2>
      <ul>
        <li>
          <strong>Conta:</strong> nome, e-mail e foto do seu perfil Google, quando você entra.
        </li>
        <li>
          <strong>Dados financeiros (via Plaid, com a sua autorização):</strong> instituições, contas e cartões,
          saldos, limites, datas de fatura, e transações (data, valor, descrição, categoria).
        </li>
        <li>
          <strong>Dados que você informa:</strong> apelidos, dono do cartão, metas, orçamentos, contas
          recorrentes, taxas de recompensa e configurações.
        </li>
        <li>
          <strong>Cobrança:</strong> processada pelo Stripe; não recebemos nem guardamos o número do seu cartão.
        </li>
        <li>
          <strong>Técnicos:</strong> cookie de sessão necessário para manter você conectado.
        </li>
      </ul>

      <h2>3. Para que usamos</h2>
      <p>
        Para exibir suas finanças, calcular projeções e alertas, enviar e-mails e notificações que você ativou,
        operar a assinatura e manter a segurança do Serviço. Não vendemos seus dados e não os usamos para
        publicidade.
      </p>

      <h2>4. Com quem compartilhamos</h2>
      <p>
        Plaid (conexão com seus bancos — veja a
        <a href="https://plaid.com/legal/#end-user-privacy-policy" target="_blank" rel="noopener">política do Plaid</a>),
        Stripe (pagamentos), Google (login) e o provedor de e-mail/hospedagem que usamos. Compartilhamos apenas o
        necessário e podemos divulgar dados quando a lei exigir.
      </p>

      <h2>5. Como protegemos</h2>
      <p>
        Conexão criptografada (HTTPS), tokens de acesso aos bancos criptografados no banco de dados, acesso
        restrito e backups. Nenhum sistema é totalmente seguro; avise-nos se notar algo estranho.
        [DETALHAR MEDIDAS REAIS DA HOSPEDAGEM]
      </p>

      <h2>6. Por quanto tempo guardamos</h2>
      <p>
        Enquanto sua conta existir. Ao desconectar um banco, apagamos os dados dele; ao apagar a conta, apagamos
        todos os seus dados (backups são renovados e descartados em até [PRAZO] dias).
      </p>

      <h2>7. Seus direitos e escolhas</h2>
      <p>
        Você pode exportar seus dados, desconectar bancos, apagar a conta e revogar o acesso do Plaid a qualquer
        momento, em Configurações. Conforme sua região (por exemplo, LGPD, CCPA), você também pode pedir acesso,
        correção ou exclusão pelo e-mail acima.
      </p>

      <h2>8. Crianças</h2>
      <p>O Serviço não é destinado a menores de 18 anos.</p>

      <h2>9. Mudanças</h2>
      <p>Avisaremos sobre mudanças relevantes. Veja também os <a routerLink="/terms">Termos de Uso</a>.</p>
    </article>
  `,
})
export class Privacy {
  protected readonly legal = LEGAL;
}
