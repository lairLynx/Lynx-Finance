import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../core/auth.service';
import { FinanceService, Overview } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { BarChart, LineChart } from '../shared/charts';
import { ViewControls } from '../shared/view-controls';

@Component({
  selector: 'lynx-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LineChart, BarChart, ViewControls],
  template: `
    <div class="row between wrap">
      <div>
        <h1>{{ i18n.t('Dashboard') }}</h1>
        <p class="sub">{{ data() ? i18n.t('{month} at a glance', { month: i18n.monthLong(data()!.month) }) : '' }}</p>
      </div>
      @if (data(); as d) { <lynx-view-controls [data]="d" /> }
    </div>

    @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }

    @if (data(); as d) {
      @if (!d.currency) {
        <div class="card empty">
          {{ i18n.t('Nothing to show yet.') }}
          <a routerLink="/accounts">{{ i18n.t('Create an account and import a statement.') }}</a>
        </div>
      } @else {
        <div class="grid cols-4">
          <div class="card">
            <div class="stat-label">{{ i18n.t('Net worth') }}</div>
            <div class="stat-value">{{ money(d.netWorth) }}</div>
            @if (change(); as c) {
              <div [class]="c.delta >= 0 ? 'pos' : 'neg'">{{ c.delta >= 0 ? '▲' : '▼' }} {{ money(c.delta < 0 ? -c.delta : c.delta) }}@if (c.pct !== null) { ({{ i18n.percent(c.pct < 0 ? -c.pct : c.pct) }}) } {{ i18n.t('vs previous month') }}</div>
            }
          </div>
          <div class="card"><div class="stat-label">{{ i18n.t('Income') }}</div><div class="stat-value pos">{{ money(d.income) }}</div></div>
          <div class="card"><div class="stat-label">{{ i18n.t('Spending') }}</div><div class="stat-value neg">{{ money(d.expense) }}</div></div>
          <div class="card">
            <div class="stat-label">{{ i18n.t('Net cash flow') }}</div>
            <div class="stat-value" [class.pos]="d.income - d.expense > 0" [class.neg]="d.income - d.expense < 0">{{ money(d.income - d.expense) }}</div>
          </div>
        </div>

        @if (d.transfers > 0) {
          <div class="muted caption">{{ i18n.t('{amount} moved between your own accounts is not counted as income or spending.', { amount: money(d.transfers) }) }}</div>
        }

        @if (d.uncategorizedCount > 0) {
          <div class="notice" style="margin-top:16px">
            {{ i18n.t('{n} transactions this month have no category.', { n: d.uncategorizedCount }) }}
            <a routerLink="/transactions">{{ i18n.t('Categorize them') }}</a>
          </div>
        }

        <div class="grid cols-2" style="margin-top:16px">
          <div class="card">
            <h2>{{ i18n.t('Net worth, last 12 months') }}</h2>
            <div style="height:160px"><lynx-line-chart [values]="balanceValues()" /></div>
            <div class="row between muted caption"><span>{{ i18n.monthShort(d.balances[0].month, true) }}</span><span>{{ i18n.monthShort(d.balances[d.balances.length - 1].month, true) }}</span></div>
          </div>
          <div class="card">
            <h2>{{ i18n.t('Income vs. expenses') }}</h2>
            <lynx-bar-chart [data]="bars()" />
          </div>
        </div>

        <div class="grid cols-2" style="margin-top:16px">
          <div class="card">
            <h2>{{ i18n.t('Spending by category') }}</h2>
            @for (c of d.categories.slice(0, 6); track c.categoryId) {
              <div style="margin-bottom:12px">
                <div class="row between"><span><span class="dot" [style.background]="c.color"></span> {{ i18n.t(c.name) }}</span><span class="muted">{{ money(c.amount) }}</span></div>
                <div class="progress"><span [style.width.%]="(c.amount / d.categories[0].amount) * 100" [style.background]="c.color"></span></div>
              </div>
            } @empty {
              <div class="muted">{{ i18n.t('No spending this month.') }}</div>
            }
          </div>
          <div class="card">
            <h2>{{ i18n.t('Recent transactions') }}</h2>
            <table class="table">
              @for (t of d.recent; track t.id) {
                <tr>
                  <td><strong>{{ t.payee }}</strong><div class="muted">{{ i18n.date(t.date) }}</div></td>
                  <td class="num" [class.pos]="t.amount > 0" [class.neg]="t.amount < 0">{{ money(t.amount) }}</td>
                </tr>
              } @empty {
                <tr><td class="muted">{{ i18n.t('No transactions yet.') }}</td></tr>
              }
            </table>
          </div>
        </div>

        <div class="card" style="margin-top:16px">
          <h2>{{ i18n.t('Accounts') }} ({{ d.currency }})</h2>
          <table class="table">
            @for (a of d.accounts; track a.id) {
              <tr>
                <td><strong>{{ a.name }}</strong><div class="muted">{{ a.bank || '—' }}</div></td>
                <td class="num" [class.neg]="a.balance < 0">{{ money(a.balance) }}</td>
              </tr>
            }
          </table>
          @if (d.currencies.length > 1) {
            <div class="muted caption">{{ i18n.t('Amounts in different currencies are shown separately and never converted.') }}</div>
          }
        </div>
      }
    }
  `,
  styles: `
    .wrap { flex-wrap: wrap; gap: 12px; }
    .caption { font-size: 0.78rem; margin-top: 6px; }
    .notice a { margin-left: 6px; }
    .empty a { display: block; margin-top: 8px; }
  `,
})
export class Dashboard {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  protected readonly data = signal<Overview | null>(null);
  protected readonly error = signal('');
  private loadSeq = 0;

  protected readonly balanceValues = computed(() => this.data()?.balances.map((b) => b.balance) ?? []);
  protected readonly bars = computed(() =>
    (this.data()?.months ?? []).slice(-6).map((m) => ({ month: this.i18n.monthShort(m.month), income: m.income, expense: m.expense })));
  protected readonly change = computed(() => {
    const d = this.data();
    if (!d || d.months.length < 2) return null;
    const delta = d.netWorth - d.previousNetWorth;
    return { delta, pct: d.previousNetWorth !== 0 ? delta / Math.abs(d.previousNetWorth) : null };
  });

  constructor() {
    effect(() => {
      this.finance.currencyChoice();
      this.finance.monthChoice();
      untracked(() => this.load());
    });
  }

  protected money(value: number) { return this.i18n.money(value, this.data()?.currency ?? 'EUR'); }

  private async load() {
    const seq = ++this.loadSeq;
    try {
      const data = await this.finance.overview(12);
      if (seq !== this.loadSeq) return;
      this.data.set(data);
      this.error.set('');
    } catch (err) {
      if (seq === this.loadSeq) this.error.set(errorMessage(err));
    }
  }
}
