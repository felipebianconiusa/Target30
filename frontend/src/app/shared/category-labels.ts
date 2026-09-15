const LABELS: Record<string, string> = {
  INCOME: 'Renda',
  TRANSFER_IN: 'Transferência recebida',
  TRANSFER_OUT: 'Transferência enviada',
  LOAN_PAYMENTS: 'Pagamento de empréstimo',
  BANK_FEES: 'Tarifas bancárias',
  ENTERTAINMENT: 'Entretenimento',
  FOOD_AND_DRINK: 'Alimentação',
  GENERAL_MERCHANDISE: 'Compras em geral',
  HOME_IMPROVEMENT: 'Casa e reforma',
  MEDICAL: 'Saúde',
  PERSONAL_CARE: 'Cuidados pessoais',
  GENERAL_SERVICES: 'Serviços',
  GOVERNMENT_AND_NON_PROFIT: 'Governo e ONGs',
  TRANSPORTATION: 'Transporte',
  TRAVEL: 'Viagem',
  RENT_AND_UTILITIES: 'Aluguel e contas',
  OUTROS: 'Outros',
};

export const CATEGORY_FALLBACK_CODE = 'OUTROS';

export function translateCategory(code: string | null | undefined): string {
  const key = code ?? CATEGORY_FALLBACK_CODE;
  return (
    LABELS[key] ??
    key
      .replace(/_/g, ' ')
      .toLowerCase()
      .replace(/^\w/, (c) => c.toUpperCase())
  );
}
