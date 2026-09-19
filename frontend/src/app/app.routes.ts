import { Routes } from '@angular/router';
import { Accounts } from './accounts/accounts';
import { BestCard } from './best-card/best-card';
import { Cards } from './cards/cards';
import { CashFlow } from './cashflow/cashflow';
import { Dashboard } from './dashboard/dashboard';
import { Settings } from './settings/settings';
import { Transactions } from './transactions/transactions';

export const routes: Routes = [
  { path: '', component: Dashboard },
  { path: 'accounts', component: Accounts },
  { path: 'transactions', component: Transactions },
  { path: 'cards', component: Cards },
  { path: 'best-card', component: BestCard },
  { path: 'cashflow', component: CashFlow },
  { path: 'settings', component: Settings },
];
