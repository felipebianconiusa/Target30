import { Card } from './cards.service';

export type CardSetupField = 'closingDay' | 'limit' | 'dueDate';

// O que falta configurar num cartão pra ele aparecer direito no "melhor cartão", na projeção
// de utilização e no Fluxo de Caixa. Cada banco informa (ou não) coisas diferentes via Plaid:
// Capital One não manda limite; BofA/OnePay não mandam dia de fechamento nem vencimento.
export function cardSetupNeeds(card: Card): CardSetupField[] {
  const needs: CardSetupField[] = [];
  if (card.statementClosingDay === null) needs.push('closingDay');
  if (!(card.creditLimit > 0)) needs.push('limit');
  if (card.nextPaymentDueDate === null) needs.push('dueDate');
  return needs;
}
