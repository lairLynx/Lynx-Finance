import { Routes } from '@angular/router';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./pages/dashboard').then((m) => m.Dashboard) },
      { path: 'accounts', loadComponent: () => import('./pages/accounts').then((m) => m.Accounts) },
      { path: 'transactions', loadComponent: () => import('./pages/transactions').then((m) => m.Transactions) },
      { path: 'budgets', loadComponent: () => import('./pages/budgets').then((m) => m.Budgets) },
      { path: 'goals', loadComponent: () => import('./pages/goals').then((m) => m.Goals) },
      { path: 'forecast', loadComponent: () => import('./pages/forecast').then((m) => m.Forecast) },
      { path: 'reports', loadComponent: () => import('./pages/reports').then((m) => m.Reports) },
    ],
  },
  { path: '**', redirectTo: '' },
];
