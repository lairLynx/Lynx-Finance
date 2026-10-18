import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/auth.service';
import { AccountDto, AccountType, FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { ImportPanel } from '../shared/import-panel';

const TYPE_LABELS: Record<AccountType, string> = { Current: 'Current', Savings: 'Savings', CreditCard: 'Credit card', Cash: 'Cash' };

@Component({
  selector: 'lynx-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ImportPanel],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Accounts') }}</h1><p class="sub">{{ i18n.t('Bank accounts and cash wallets') }}</p></div>
      <div class="row">
        <button class="btn" type="button" [disabled]="!finance.accounts().length" (click)="importFor.set(0)">{{ i18n.t('Import file…') }}</button>
        <button class="btn primary" type="button" (click)="startNew()">{{ i18n.t('+ New account') }}</button>
      </div>
    </div>

    @if (importFor() !== null) {
      <lynx-import-panel [accountId]="importFor() || null" (closed)="importFor.set(null)" />
    }

    @if (form(); as f) {
      <form class="card" style="margin-bottom:16px" (ngSubmit)="save()">
        <h2>{{ editingId() === null ? i18n.t('New account') : i18n.t('Edit account') }}</h2>
        <div class="form-grid">
          <label>{{ i18n.t('Name') }}<input class="input" name="name" [(ngModel)]="f.name" required maxlength="100" /></label>
          <label>{{ i18n.t('Bank') }}<input class="input" name="bank" [(ngModel)]="f.bank" maxlength="100" /></label>
          <label>IBAN<input class="input" name="iban" [(ngModel)]="f.iban" maxlength="40" /></label>
          <label>{{ i18n.t('Type') }}
            <select class="input" name="type" [(ngModel)]="f.type">
              @for (t of types; track t) { <option [value]="t">{{ i18n.t(label(t)) }}</option> }
            </select>
          </label>
          <label>{{ i18n.t('Currency') }}<input class="input" name="currency" [(ngModel)]="f.currency" maxlength="3" required /></label>
          <label>{{ i18n.t('Opening balance') }}<input class="input" name="opening" type="number" step="0.01" [(ngModel)]="f.openingBalance" /></label>
        </div>
        @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }
        <div class="row" style="margin-top:12px">
          <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t('Save') }}</button>
          <button class="btn" type="button" (click)="form.set(null)">{{ i18n.t('Cancel') }}</button>
        </div>
      </form>
    }

    @if (loaded() && !finance.accounts().length && !form()) {
      <div class="card empty">{{ i18n.t('No accounts yet. Create one, then import a statement from your bank.') }}</div>
    }

    <div class="grid cols-2">
      @for (a of finance.accounts(); track a.id) {
        <div class="card">
          <div class="row between">
            <div><strong>{{ a.name }}</strong><div class="muted">{{ [a.bank, a.iban].filter(x => x).join(' · ') || '—' }}</div></div>
            <span class="badge">{{ i18n.t(label(a.type)) }}</span>
          </div>
          <div class="stat-value" [class.neg]="a.balance < 0">{{ i18n.money(a.balance, a.currency) }}</div>
          <div class="muted" style="margin-bottom:10px">
            {{ i18n.t('{n} transactions', { n: a.transactionCount }) }}
            @if (a.lastTransactionDate) { · {{ i18n.t('latest {date}', { date: i18n.date(a.lastTransactionDate) }) }} }
          </div>
          <div class="row">
            <button class="btn" type="button" (click)="importFor.set(a.id)">{{ i18n.t('Import file…') }}</button>
            <button class="btn" type="button" (click)="edit(a)">{{ i18n.t('Edit') }}</button>
            <button class="btn link" type="button" (click)="remove(a)">{{ i18n.t('Delete') }}</button>
          </div>
        </div>
      }
    </div>
  `,
})
export class Accounts implements OnInit {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  protected readonly types: AccountType[] = ['Current', 'Savings', 'CreditCard', 'Cash'];
  protected readonly loaded = signal(false);
  protected readonly importFor = signal<number | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly form = signal<{ name: string; bank: string; iban: string; type: AccountType; currency: string; openingBalance: number } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  async ngOnInit() {
    try { await this.finance.refreshAccounts(); } catch { /* shown as empty list; the interceptor handles 401 */ }
    this.loaded.set(true);
  }

  protected label(t: AccountType) { return TYPE_LABELS[t]; }

  protected startNew() {
    this.editingId.set(null);
    this.error.set('');
    this.form.set({ name: '', bank: '', iban: '', type: 'Current', currency: 'EUR', openingBalance: 0 });
  }

  protected edit(a: AccountDto) {
    this.editingId.set(a.id);
    this.error.set('');
    this.form.set({ name: a.name, bank: a.bank ?? '', iban: a.iban ?? '', type: a.type, currency: a.currency, openingBalance: a.openingBalance });
  }

  protected async save() {
    const f = this.form();
    if (!f) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.finance.saveAccount(this.editingId(), { ...f, openingBalance: Number(f.openingBalance) || 0 });
      this.form.set(null);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(a: AccountDto) {
    if (!confirm(this.i18n.t('Delete "{name}" and all of its transactions?', { name: a.name }))) return;
    await this.finance.deleteAccount(a.id);
  }
}
