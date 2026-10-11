import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'lynx-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <aside>
      <div class="brand"><span class="logo" aria-hidden="true"></span>Lynx</div>
      <nav>
        @for (item of nav; track item.path) {
          <a [routerLink]="item.path" routerLinkActive="active">{{ item.label }}</a>
        }
      </nav>
      <div class="user muted">Signed in as you · <a href="#">Log out</a></div>
    </aside>
    <main>
      <span class="mock-banner">UI mock-up — all data is fake</span>
      <router-outlet />
    </main>
  `,
  styles: `
    :host { display: flex; min-height: 100vh; }
    aside { width: 230px; flex-shrink: 0; background: var(--surface); border-right: 1px solid var(--border); padding: 22px 14px; display: flex; flex-direction: column; gap: 18px; position: sticky; top: 0; height: 100vh; }
    .brand { font-size: 1.35rem; font-weight: 800; padding: 0 10px; display: flex; align-items: center; gap: 10px; }
    .logo { width: 30px; height: 30px; background: var(--text); -webkit-mask: url(/lynx-logo.png) center / contain no-repeat; mask: url(/lynx-logo.png) center / contain no-repeat; }
    nav { display: flex; flex-direction: column; gap: 3px; flex: 1; }
    nav a { display: flex; align-items: center; gap: 10px; padding: 9px 12px; border-radius: 10px; color: var(--muted); text-decoration: none; font-weight: 600; }
    nav a:hover { background: var(--surface-2); }
    nav a.active { background: var(--primary-soft); color: var(--primary); }
    .user { font-size: 0.8rem; padding: 0 10px; }
    main { flex: 1; padding: 28px 34px; min-width: 0; max-width: 1200px; }
    @media (max-width: 800px) {
      :host { flex-direction: column; }
      aside { width: 100%; height: auto; position: static; }
      nav { flex-direction: row; flex-wrap: wrap; }
      main { padding: 18px; }
    }
  `,
})
export class Shell {
  protected readonly nav = [
    { path: '/dashboard', label: 'Dashboard' },
    { path: '/accounts', label: 'Accounts' },
    { path: '/transactions', label: 'Transactions' },
    { path: '/budgets', label: 'Budgets' },
    { path: '/goals', label: 'Goals' },
    { path: '/forecast', label: 'Recurring & Forecast' },
    { path: '/reports', label: 'Reports' },
  ];
}
