import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../core/auth.service';
import { FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { Forecast as ForecastData, PlanningService, RecurringFrequency, RecurringItem, RecurringList, RecurringStatus } from '../core/planning.service';
import { LineChart } from '../shared/charts';
import { ViewControls, ViewState } from '../shared/view-controls';

interface ItemForm { accountId: number; payee: string; amount: number | null; frequency: RecurringFrequency; nextDue: string }

const FREQUENCIES: RecurringFrequency[] = ['Weekly', 'Biweekly', 'Monthly', 'Quarterly', 'Yearly'];
const RANGES = [30, 90, 180, 365];

@Component({
  selector: 'lynx-forecast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, LineChart, ViewControls],
  template: `
    <div class="row between wrap">
      <div>
        <h1>{{ i18n.t('Recurring & Forecast') }}</h1>
        <p class="sub">{{ i18n.t('Confirmed recurring items drive the projected balance') }}</p>
      </div>
      @if (view(); as v) { <lynx-view-controls [data]="v" [monthStepper]="false" /> }
    </div>

    @if (error()) { <div class="notice bad" style="margin-bottom:16px">{{ i18n.t(error()) }}</div> }

    @if (list(); as l) {
      @if (!l.currency) {
        <div class="card empty">
          {{ i18n.t('Nothing to show yet.') }}
          <a routerLink="/accounts">{{ i18n.t('Create an account and import a statement.') }}</a>
        </div>
      } @else {
        @if (forecast(); as f) {
          <div class="card" style="margin-bottom:16px">
            <div class="row between wrap">
              <h2>{{ i18n.t('Projected balance') }}</h2>
              <div class="row wrap">
                <label class="muted"><input type="checkbox" [checked]="includeVariable()" (change)="includeVariable.set($any($event.target).checked)" /> {{ i18n.t('Include typical everyday spending') }}</label>
                <div class="row">
                  @for (r of ranges; track r) {
                    <button class="btn" type="button" [class.primary]="days() === r" (click)="days.set(r)">{{ i18n.t(r + 'd') }}</button>
                  }
                </div>
              </div>
            </div>
            <div style="height:200px"><lynx-line-chart [values]="f.points" color="var(--good)" /></div>
            <div class="row between muted caption"><span>{{ i18n.date(f.startDate) }}</span><span>{{ i18n.date(addDays(f.startDate, f.days)) }}</span></div>

            <div class="grid cols-3" style="margin-top:12px">
              <div><div class="stat-label">{{ i18n.t('Balance today') }}</div><div class="stat-value small">{{ money(f.startBalance) }}</div></div>
              <div>
                <div class="stat-label">{{ i18n.t('Lowest point') }}</div>
                <div class="stat-value small" [class.neg]="f.lowest < 0">{{ money(f.lowest) }}</div>
                <div class="muted caption">{{ i18n.date(f.lowestDate) }}</div>
              </div>
              <div><div class="stat-label">{{ i18n.t('In {n} days', { n: f.days }) }}</div><div class="stat-value small" [class.neg]="f.endBalance < 0">{{ money(f.endBalance) }}</div></div>
            </div>
            @if (includeVariable() && f.dailyVariable < 0) {
              <div class="muted caption" style="margin-top:8px">{{ i18n.t('Assumes about {amount} a day of other spending, based on the last 90 days.', { amount: money(-f.dailyVariable) }) }}</div>
            }
            @if (!confirmedCount()) {
              <div class="notice" style="margin-top:12px">{{ i18n.t('Nothing is confirmed yet, so the forecast only shows everyday spending. Confirm items below to include them.') }}</div>
            }
          </div>

          @if (upcoming().length) {
            <div class="card" style="margin-bottom:16px">
              <h2>{{ i18n.t('Coming up') }}</h2>
              <table class="table">
                @for (e of upcoming(); track $index) {
                  <tr>
                    <td class="muted">{{ i18n.date(e.date) }}</td>
                    <td>{{ e.payee }}</td>
                    <td class="num" [class.pos]="e.amount > 0" [class.neg]="e.amount < 0">{{ money(e.amount) }}</td>
                  </tr>
                }
              </table>
            </div>
          }
        }

        @if (form(); as f) {
          <form class="card" style="margin-bottom:16px" (ngSubmit)="save()">
            <h2>{{ editingId() === null ? i18n.t('New recurring item') : i18n.t('Edit recurring item') }}</h2>
            <div class="form-grid">
              <label>{{ i18n.t('Account') }}
                <select class="input" name="account" [(ngModel)]="f.accountId" [disabled]="editingId() !== null">
                  @for (a of accountsInCurrency(); track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
                </select>
              </label>
              <label>{{ i18n.t('Payee') }}<input class="input" name="payee" [(ngModel)]="f.payee" required maxlength="200" /></label>
              <label>{{ i18n.t('Amount') }} ({{ i18n.t('negative = expense') }})<input class="input" name="amount" type="number" step="0.01" [(ngModel)]="f.amount" required /></label>
              <label>{{ i18n.t('Frequency') }}
                <select class="input" name="frequency" [(ngModel)]="f.frequency">
                  @for (q of frequencies; track q) { <option [value]="q">{{ i18n.t(q) }}</option> }
                </select>
              </label>
              <label>{{ i18n.t('Next due') }}<input class="input" name="nextDue" type="date" [(ngModel)]="f.nextDue" required /></label>
            </div>
            @if (formError()) { <div class="notice bad" role="alert">{{ i18n.t(formError()) }}</div> }
            <div class="row" style="margin-top:12px">
              <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t('Save') }}</button>
              <button class="btn" type="button" (click)="form.set(null)">{{ i18n.t('Cancel') }}</button>
            </div>
          </form>
        }

        <div class="card">
          <div class="row between wrap">
            <h2>{{ i18n.t('Recurring items') }}</h2>
            <div class="row">
              <button class="btn" type="button" [disabled]="busy()" (click)="scan()">{{ i18n.t('Scan transactions') }}</button>
              <button class="btn primary" type="button" (click)="startNew()">{{ i18n.t('+ Add item') }}</button>
            </div>
          </div>
          @if (notice()) { <div class="notice good" style="margin-bottom:12px">{{ i18n.t(notice(), { n: noticeCount() }) }}</div> }

          @if (!active().length) {
            <div class="empty">{{ i18n.t('No recurring items yet. Scan your transactions or add one manually.') }}</div>
          } @else {
            <table class="table">
              <thead><tr><th>{{ i18n.t('Payee') }}</th><th>{{ i18n.t('Frequency') }}</th><th>{{ i18n.t('Next due') }}</th><th class="num">{{ i18n.t('Amount') }}</th><th></th></tr></thead>
              @for (r of active(); track r.id) {
                <tr>
                  <td><strong>{{ r.payee }}</strong><div class="muted">{{ accountName(r.accountId) }}</div></td>
                  <td class="muted">{{ i18n.t(r.frequency) }}</td>
                  <td class="muted">{{ i18n.date(r.nextDue) }}</td>
                  <td class="num" [class.pos]="r.amount > 0" [class.neg]="r.amount < 0">{{ money(r.amount) }}</td>
                  <td class="num nowrap">
                    @if (r.status === 'Confirmed') {
                      <span class="badge good">{{ i18n.t('Confirmed') }}</span>
                    } @else {
                      <span class="badge warn">{{ i18n.t('Detected') }}</span>
                      <button class="btn" type="button" (click)="setStatus(r, 'Confirmed')">{{ i18n.t('Confirm') }}</button>
                    }
                    <button class="btn link" type="button" (click)="edit(r)">{{ i18n.t('Edit') }}</button>
                    @if (r.isManual) {
                      <button class="btn link" type="button" (click)="remove(r)">{{ i18n.t('Delete') }}</button>
                    } @else {
                      <button class="btn link" type="button" (click)="setStatus(r, 'Dismissed')">{{ i18n.t('Dismiss') }}</button>
                    }
                  </td>
                </tr>
              }
            </table>
          }

          @if (dismissed().length) {
            <details style="margin-top:12px">
              <summary class="muted">{{ i18n.t('Dismissed ({n})', { n: dismissed().length }) }}</summary>
              <table class="table">
                @for (r of dismissed(); track r.id) {
                  <tr>
                    <td>{{ r.payee }}</td>
                    <td class="num muted">{{ money(r.amount) }}</td>
                    <td class="num"><button class="btn link" type="button" (click)="setStatus(r, 'Detected')">{{ i18n.t('Restore') }}</button></td>
                  </tr>
                }
              </table>
            </details>
          }
        </div>
      }
    }
  `,
  styles: `
    .wrap { flex-wrap: wrap; gap: 12px; }
    .caption { font-size: 0.78rem; margin-top: 6px; }
    .stat-value.small { font-size: 1.3rem; }
    .nowrap { white-space: nowrap; }
    .empty a { display: block; margin-top: 8px; }
  `,
})
export class Forecast {
  protected readonly finance = inject(FinanceService);
  protected readonly planning = inject(PlanningService);
  protected readonly i18n = inject(I18n);

  protected readonly frequencies = FREQUENCIES;
  protected readonly ranges = RANGES;

  protected readonly list = signal<RecurringList | null>(null);
  protected readonly forecast = signal<ForecastData | null>(null);
  protected readonly days = signal(90);
  protected readonly includeVariable = signal(true);
  protected readonly error = signal('');
  /** Validation and server errors for the open form, shown inside it. */
  protected readonly formError = signal('');
  protected readonly busy = signal(false);
  protected readonly notice = signal('');
  protected readonly noticeCount = signal(0);

  protected readonly editingId = signal<number | null>(null);
  protected readonly form = signal<ItemForm | null>(null);
  private loadSeq = 0;

  protected readonly view = computed<ViewState | null>(() => {
    const l = this.list();
    return l ? { currencies: l.currencies, currency: l.currency, month: '', firstMonth: null, lastMonth: null } : null;
  });
  protected readonly active = computed(() => (this.list()?.items ?? []).filter((r) => r.status !== 'Dismissed'));
  protected readonly dismissed = computed(() => (this.list()?.items ?? []).filter((r) => r.status === 'Dismissed'));
  protected readonly confirmedCount = computed(() => this.active().filter((r) => r.status === 'Confirmed').length);
  protected readonly upcoming = computed(() => (this.forecast()?.events ?? []).slice(0, 8));
  protected readonly accountsInCurrency = computed(() =>
    this.finance.accounts().filter((a) => (a.currency === 'LEI' ? 'RON' : a.currency) === this.list()?.currency));

  constructor() {
    this.finance.refreshAccounts().catch(() => undefined);
    effect(() => {
      this.finance.currencyChoice();
      this.days();
      this.includeVariable();
      untracked(() => this.load());
    });
  }

  protected money(value: number) { return this.i18n.money(value, this.list()?.currency ?? 'EUR'); }
  protected accountName(id: number) { return this.finance.accountById().get(id)?.name ?? ''; }

  protected addDays(iso: string, days: number) {
    const d = new Date(`${iso}T00:00:00`);
    d.setDate(d.getDate() + days);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  protected startNew() {
    this.editingId.set(null);
    this.error.set('');
    this.formError.set('');
    this.notice.set('');
    this.form.set({ accountId: this.accountsInCurrency()[0]?.id ?? 0, payee: '', amount: null, frequency: 'Monthly', nextDue: this.addDays(new Date().toISOString().slice(0, 10), 7) });
  }

  protected edit(r: RecurringItem) {
    this.editingId.set(r.id);
    this.error.set('');
    this.formError.set('');
    this.form.set({ accountId: r.accountId, payee: r.payee, amount: r.amount, frequency: r.frequency, nextDue: r.nextDue });
  }

  protected async save() {
    const f = this.form();
    if (!f) return;

    const amount = Number(f.amount);
    if (!f.accountId) return this.formError.set('Choose an account.');
    if (!f.payee?.trim()) return this.formError.set('Payee is required (max 200 characters).');
    if (f.amount === null || f.amount === undefined || !Number.isFinite(amount) || amount === 0)
      return this.formError.set('Enter an amount, e.g. -800.50 for an expense.');
    if (!f.nextDue) return this.formError.set('Next due date is required.');

    this.busy.set(true);
    this.formError.set('');
    try {
      const editing = this.editingId();
      // Saving an edited suggestion also confirms it: the user has reviewed it.
      await this.planning.saveRecurring(editing, { ...f, payee: f.payee.trim(), amount, ...(editing !== null ? { status: 'Confirmed' as RecurringStatus } : {}) });
      this.form.set(null);
      this.error.set('');
      this.notice.set('Recurring item saved.');
      await this.load();
    } catch (err) {
      this.formError.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async setStatus(r: RecurringItem, status: RecurringStatus) {
    await this.run(() => this.planning.saveRecurring(r.id, { status }));
  }

  protected async remove(r: RecurringItem) {
    if (!confirm(this.i18n.t('Delete "{name}"?', { name: r.payee }))) return;
    await this.run(() => this.planning.deleteRecurring(r.id));
  }

  protected async scan() {
    this.busy.set(true);
    try {
      const { added } = await this.planning.detectRecurring();
      this.notice.set(added > 0 ? '{n} new recurring items found. Review and confirm them below.' : 'No new recurring items found.');
      this.noticeCount.set(added);
      await this.load();
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<unknown>) {
    try {
      await action();
      this.error.set('');
    } catch (err) {
      this.error.set(errorMessage(err));
    }
    await this.load();
  }

  private async load() {
    const seq = ++this.loadSeq;
    try {
      const [list, forecast] = await Promise.all([this.planning.recurring(), this.planning.forecast(this.days(), this.includeVariable())]);
      if (seq !== this.loadSeq) return;
      this.list.set(list);
      this.forecast.set(forecast);
    } catch (err) {
      if (seq === this.loadSeq) this.error.set(errorMessage(err));
    }
  }
}
