import { Routes } from '@angular/router';
import { Accounts } from './accounts/accounts';
import { Dashboard } from './dashboard/dashboard';
import { Transactions } from './transactions/transactions';

export const routes: Routes = [
  { path: '', component: Dashboard },
  { path: 'accounts', component: Accounts },
  { path: 'transactions', component: Transactions },
];
