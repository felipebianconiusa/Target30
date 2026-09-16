import { Routes } from '@angular/router';
import { Accounts } from './accounts/accounts';
import { Cards } from './cards/cards';
import { Dashboard } from './dashboard/dashboard';
import { Settings } from './settings/settings';
import { Transactions } from './transactions/transactions';

export const routes: Routes = [
  { path: '', component: Dashboard },
  { path: 'accounts', component: Accounts },
  { path: 'transactions', component: Transactions },
  { path: 'cards', component: Cards },
  { path: 'settings', component: Settings },
];
