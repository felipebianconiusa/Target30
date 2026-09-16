import { Lang } from '../i18n/translations';

export const CATEGORY_FALLBACK_CODE = 'OUTROS';

// Lista fixa dos primary categories do Plaid (personal_finance_category) + o fallback local.
// Usada pra montar as opções do filtro sem depender de ter os dados todos carregados no cliente.
export const ALL_CATEGORY_CODES: string[] = [
  'INCOME',
  'TRANSFER_IN',
  'TRANSFER_OUT',
  'LOAN_PAYMENTS',
  'BANK_FEES',
  'ENTERTAINMENT',
  'FOOD_AND_DRINK',
  'GENERAL_MERCHANDISE',
  'HOME_IMPROVEMENT',
  'MEDICAL',
  'PERSONAL_CARE',
  'GENERAL_SERVICES',
  'GOVERNMENT_AND_NON_PROFIT',
  'TRANSPORTATION',
  'TRAVEL',
  'RENT_AND_UTILITIES',
  CATEGORY_FALLBACK_CODE,
];

const LABELS: Record<Lang, Record<string, string>> = {
  pt: {
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
  },
  en: {
    INCOME: 'Income',
    TRANSFER_IN: 'Transfer in',
    TRANSFER_OUT: 'Transfer out',
    LOAN_PAYMENTS: 'Loan payment',
    BANK_FEES: 'Bank fees',
    ENTERTAINMENT: 'Entertainment',
    FOOD_AND_DRINK: 'Food and drink',
    GENERAL_MERCHANDISE: 'General merchandise',
    HOME_IMPROVEMENT: 'Home improvement',
    MEDICAL: 'Medical',
    PERSONAL_CARE: 'Personal care',
    GENERAL_SERVICES: 'Services',
    GOVERNMENT_AND_NON_PROFIT: 'Government and non-profit',
    TRANSPORTATION: 'Transportation',
    TRAVEL: 'Travel',
    RENT_AND_UTILITIES: 'Rent and utilities',
    OUTROS: 'Other',
  },
  es: {
    INCOME: 'Ingresos',
    TRANSFER_IN: 'Transferencia recibida',
    TRANSFER_OUT: 'Transferencia enviada',
    LOAN_PAYMENTS: 'Pago de préstamo',
    BANK_FEES: 'Comisiones bancarias',
    ENTERTAINMENT: 'Entretenimiento',
    FOOD_AND_DRINK: 'Comida y bebida',
    GENERAL_MERCHANDISE: 'Compras en general',
    HOME_IMPROVEMENT: 'Mejoras del hogar',
    MEDICAL: 'Salud',
    PERSONAL_CARE: 'Cuidado personal',
    GENERAL_SERVICES: 'Servicios',
    GOVERNMENT_AND_NON_PROFIT: 'Gobierno y ONG',
    TRANSPORTATION: 'Transporte',
    TRAVEL: 'Viajes',
    RENT_AND_UTILITIES: 'Alquiler y servicios',
    OUTROS: 'Otros',
  },
};

export function translateCategory(code: string | null | undefined, lang: Lang = 'pt'): string {
  const key = code ?? CATEGORY_FALLBACK_CODE;
  const dict = LABELS[lang] ?? LABELS.pt;
  return (
    dict[key] ??
    key
      .replace(/_/g, ' ')
      .toLowerCase()
      .replace(/^\w/, (c) => c.toUpperCase())
  );
}
