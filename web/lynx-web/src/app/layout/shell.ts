import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { I18n } from '../core/i18n.service';
import { ThemeService } from '../core/theme.service';
import { LangSwitch } from '../shared/lang-switch';

@Component({
  selector: 'lynx-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, LangSwitch],
  template: `
    <aside>
      <div class="brand"><span class="logo" aria-hidden="true"></span>Lynx</div>
      <nav>
        @for (item of nav; track item.path) {
          <a [routerLink]="item.path" routerLinkActive="active">{{ i18n.t(item.label) }}</a>
        }
      </nav>
      <div class="user muted">{{ auth.email() }} · <a href="#" (click)="logout($event)">{{ i18n.t('Log out') }}</a></div>
    </aside>
    <main>
      <div class="topbar">
        <lynx-lang-switch />
        <button class="theme-switch" type="button" role="switch" [attr.aria-checked]="theme.theme() === 'dark'" [attr.aria-label]="i18n.t('Dark mode')" (click)="theme.toggle()">
          <span class="label">{{ i18n.t(theme.theme() === 'dark' ? 'Dark' : 'Light') }}</span>
          <span class="track"><span class="thumb"></span></span>
        </button>
      </div>
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
    .topbar { display: flex; justify-content: flex-end; align-items: center; gap: 14px; margin-bottom: 12px; }
    .theme-switch { display: flex; align-items: center; gap: 10px; background: none; border: none; color: var(--muted); font: inherit; font-size: 0.85rem; font-weight: 600; cursor: pointer; padding: 4px; }
    .track { width: 38px; height: 22px; border-radius: 999px; background: var(--surface-2); border: 1px solid var(--border); position: relative; transition: background 0.15s; }
    .thumb { position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: var(--muted); transition: transform 0.15s, background 0.15s; }
    .theme-switch[aria-checked='true'] .track { background: var(--primary-soft); }
    .theme-switch[aria-checked='true'] .thumb { transform: translateX(16px); background: var(--primary); }
    main { flex: 1; padding: 28px 34px; min-width: 0; }
    @media (max-width: 800px) {
      :host { flex-direction: column; }
      aside { width: 100%; height: auto; position: static; }
      nav { flex-direction: row; flex-wrap: wrap; }
      main { padding: 18px; }
    }
  `,
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly i18n = inject(I18n);
  private readonly router = inject(Router);

  protected async logout(event: Event) {
    event.preventDefault();
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  protected readonly nav = [
    { path: '/dashboard', label: 'Dashboard' },
    { path: '/accounts', label: 'Accounts' },
    { path: '/transactions', label: 'Transactions' },
    { path: '/categories', label: 'Categories' },
    { path: '/budgets', label: 'Budgets' },
    { path: '/goals', label: 'Goals' },
    { path: '/forecast', label: 'Recurring & Forecast' },
    { path: '/reports', label: 'Reports' },
  ];
}
