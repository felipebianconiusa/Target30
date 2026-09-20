import { Routes } from '@angular/router';
import { Accounts } from './accounts/accounts';
import { BestCard } from './best-card/best-card';
import { Billing } from './billing/billing';
import { Cards } from './cards/cards';
import { CashFlow } from './cashflow/cashflow';
import { Dashboard } from './dashboard/dashboard';
import { Import } from './import/import';
import { Privacy } from './legal/privacy';
import { Terms } from './legal/terms';
import { Settings } from './settings/settings';
import { Transactions } from './transactions/transactions';

export const routes: Routes = [
  { path: '', component: Dashboard },
  { path: 'accounts', component: Accounts },
  { path: 'transactions', component: Transactions },
  { path: 'cards', component: Cards },
  { path: 'best-card', component: BestCard },
  { path: 'cashflow', component: CashFlow },
  { path: 'import', component: Import },
  { path: 'billing', component: Billing },
  { path: 'terms', component: Terms },
  { path: 'privacy', component: Privacy },
  { path: 'settings', component: Settings },
];
