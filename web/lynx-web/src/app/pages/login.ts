import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService, errorMessage } from '../core/auth.service';
import { I18n } from '../core/i18n.service';
import { LangSwitch } from '../shared/lang-switch';

@Component({
  selector: 'lynx-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, LangSwitch],
  template: `
    <lynx-lang-switch class="lang" />
    <form class="card" (ngSubmit)="submit()">
      <div class="brand"><span class="logo" aria-hidden="true"></span>Lynx</div>
      @if (setupRequired()) {
        <h1>{{ i18n.t('Create your account') }}</h1>
        <p class="sub">{{ i18n.t("First run: choose the email and password you'll use to sign in. At least 12 characters; a passphrase works well.") }}</p>
      } @else {
        <h1>{{ i18n.t('Sign in') }}</h1>
        <p class="sub">{{ i18n.t('Welcome back.') }}</p>
      }

      <label>{{ i18n.t('Email') }}<input type="email" name="email" [(ngModel)]="email" autocomplete="username" required /></label>
      <label>{{ i18n.t('Password') }}
        <input type="password" name="password" [(ngModel)]="password" [autocomplete]="setupRequired() ? 'new-password' : 'current-password'" required minlength="12" />
      </label>
      @if (setupRequired()) {
        <label>{{ i18n.t('Confirm password') }}<input type="password" name="confirm" [(ngModel)]="confirm" autocomplete="new-password" required /></label>
      }

      @if (error()) { <div class="error">{{ i18n.t(error()) }}</div> }
      <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t(setupRequired() ? 'Create account' : 'Sign in') }}</button>
    </form>
  `,
  styles: `
    .lang { position: absolute; top: 20px; right: 24px; }
    :host { position: relative; display: grid; place-items: center; min-height: 100vh; padding: 20px; }
    form { width: 100%; max-width: 400px; display: flex; flex-direction: column; gap: 14px; }
    .brand { font-size: 1.35rem; font-weight: 800; display: flex; align-items: center; gap: 10px; }
    .logo { width: 34px; height: 34px; background: var(--text); -webkit-mask: url(/lynx-logo.png) center / contain no-repeat; mask: url(/lynx-logo.png) center / contain no-repeat; }
    label { display: flex; flex-direction: column; gap: 6px; font-weight: 600; font-size: 0.85rem; }
    input { padding: 10px 12px; border-radius: 10px; border: 1px solid var(--border); background: var(--surface-2); color: var(--text); font: inherit; font-weight: 400; }
    .error { background: var(--bad-soft); color: var(--bad); padding: 8px 12px; border-radius: 8px; font-size: 0.85rem; }
    h1, .sub { margin: 0; }
  `,
})
export class Login implements OnInit {
  protected readonly i18n = inject(I18n);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly setupRequired = signal(false);
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected email = '';
  protected password = '';
  protected confirm = '';

  async ngOnInit() {
    try {
      const status = await this.auth.load(true);
      if (status.authenticated) await this.router.navigateByUrl('/');
      this.setupRequired.set(status.setupRequired);
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }

  async submit() {
    this.error.set('');
    if (this.setupRequired() && this.password !== this.confirm) {
      this.error.set('Passwords do not match.');
      return;
    }
    this.busy.set(true);
    try {
      if (this.setupRequired()) await this.auth.setup(this.email, this.password);
      else await this.auth.login(this.email, this.password);
      await this.router.navigateByUrl('/');
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
