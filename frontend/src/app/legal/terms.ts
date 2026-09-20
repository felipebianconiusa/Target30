import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LEGAL } from './legal.config';

// RASCUNHO de modelo — não é aconselhamento jurídico. Revise com um advogado antes de usar.
@Component({
  selector: 'app-terms',
  imports: [RouterLink],
  styleUrl: './legal.scss',
  template: `
    <article class="legal">
      @if (legal.IS_DRAFT) {
        <p class="legal__draft">
          Rascunho de modelo para revisão jurídica — não vale como documento final.
        </p>
      }
      <h1>Termos de Uso</h1>
      <p class="legal__muted">Última atualização: {{ legal.UPDATED_AT }}</p>

      <h2>1. O serviço</h2>
      <p>
        O Target30 ("Serviço"), operado por {{ legal.COMPANY_NAME }} ("nós"), ajuda você a acompanhar contas e
        cartões, controlar a utilização de crédito, projetar o fluxo de caixa e receber alertas. Os dados
        bancários vêm de um provedor terceirizado (Plaid) com a sua autorização.
      </p>

      <h2>2. Não é aconselhamento financeiro</h2>
      <p>
        O Serviço é informativo. Sugestões (como qual cartão usar, quanto pagar antes do fechamento ou avisos de
        saldo) são estimativas baseadas nos dados disponíveis, que podem estar atrasados ou incompletos. Não
        constituem aconselhamento financeiro, de investimento, fiscal ou jurídico, nem garantem qualquer efeito
        no seu score de crédito. Decisões financeiras são de sua responsabilidade.
      </p>

      <h2>3. Sua conta</h2>
      <p>
        Você entra com sua conta Google e é responsável por manter o acesso seguro. Você declara ter idade legal
        para contratar e usar o Serviço apenas com contas e cartões que você tem o direito de acessar.
      </p>

      <h2>4. Assinatura, teste grátis e cancelamento</h2>
      <p>
        Novos usuários podem ter um período de teste grátis. Depois dele, recursos que dependem da conexão com
        seus bancos exigem assinatura paga, cobrada de forma recorrente pelo Stripe até você cancelar. Você pode
        cancelar a qualquer momento pelo portal de assinatura; o acesso segue até o fim do período já pago.
        Reembolsos: [DEFINIR A POLÍTICA DE REEMBOLSO]. Podemos alterar preços mediante aviso prévio.
      </p>

      <h2>5. Uso aceitável</h2>
      <p>
        Não use o Serviço para fins ilegais, para acessar dados de terceiros sem autorização, para sobrecarregar
        ou tentar burlar a segurança do Serviço, nem para revender o acesso.
      </p>

      <h2>6. Serviços de terceiros</h2>
      <p>
        O Serviço depende de terceiros, como Plaid (conexão bancária), Google (login) e Stripe (pagamentos).
        O uso deles está sujeito aos termos e políticas dessas empresas, e falhas ou indisponibilidade deles
        podem afetar o Serviço.
      </p>

      <h2>7. Disponibilidade e limitação de responsabilidade</h2>
      <p>
        O Serviço é fornecido "como está". Não garantimos operação ininterrupta nem dados livres de erros. Na
        máxima extensão permitida em lei, não respondemos por perdas indiretas ou decorrentes de decisões
        tomadas com base nas informações exibidas. [AJUSTAR O LIMITE DE RESPONSABILIDADE COM O ADVOGADO]
      </p>

      <h2>8. Seus dados e encerramento</h2>
      <p>
        Você pode exportar seus dados e apagar sua conta e todos os dados associados a qualquer momento em
        Configurações. Podemos suspender contas que violem estes termos. Veja a
        <a routerLink="/privacy">Política de Privacidade</a>.
      </p>

      <h2>9. Alterações</h2>
      <p>Podemos atualizar estes termos; mudanças relevantes serão avisadas no app ou por e-mail.</p>

      <h2>10. Contato e lei aplicável</h2>
      <p>Dúvidas: {{ legal.CONTACT_EMAIL }}. Lei e foro aplicáveis: [DEFINIR].</p>
    </article>
  `,
})
export class Terms {
  protected readonly legal = LEGAL;
}
