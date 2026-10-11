import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { Shell } from './layout/shell';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./pages/login').then((m) => m.Login) },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
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
