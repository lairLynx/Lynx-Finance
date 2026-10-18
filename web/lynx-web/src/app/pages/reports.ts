import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../core/auth.service';
import { FinanceService, Overview } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { BarChart, LineChart } from '../shared/charts';
import { ViewControls } from '../shared/view-controls';

@Component({
  selector: 'lynx-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, BarChart, LineChart, ViewControls],
  template: `
    <div class="row between wrap">
      <div>
        <h1>{{ i18n.t('Reports') }}</h1>
        <p class="sub">{{ i18n.t('Last {n} months', { n: range() }) }}</p>
      </div>
      <div class="row wrap">
        <select class="input" [ngModel]="range()" (ngModelChange)="range.set(+$event)" [attr.aria-label]="i18n.t('Period')">
          <option [ngValue]="6">{{ i18n.t('Last {n} months', { n: 6 }) }}</option>
          <option [ngValue]="12">{{ i18n.t('Last {n} months', { n: 12 }) }}</option>
          <option [ngValue]="24">{{ i18n.t('Last {n} months', { n: 24 }) }}</option>
        </select>
        @if (data(); as d) { <lynx-view-controls [data]="d" /> }
      </div>
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
          <div class="card"><div class="stat-label">{{ i18n.t('Income') }}</div><div class="stat-value pos">{{ money(d.rangeIncome) }}</div></div>
          <div class="card"><div class="stat-label">{{ i18n.t('Expenses') }}</div><div class="stat-value neg">{{ money(d.rangeExpense) }}</div></div>
          <div class="card">
            <div class="stat-label">{{ i18n.t('Net cash flow') }}</div>
            <div class="stat-value" [class.pos]="d.rangeIncome > d.rangeExpense" [class.neg]="d.rangeIncome < d.rangeExpense">{{ money(d.rangeIncome - d.rangeExpense) }}</div>
          </div>
          <div class="card">
            <div class="stat-label">{{ i18n.t('Savings rate') }}</div>
            <div class="stat-value">{{ d.rangeIncome > 0 ? i18n.percent((d.rangeIncome - d.rangeExpense) / d.rangeIncome) : '—' }}</div>
          </div>
        </div>

        <div class="grid cols-2" style="margin-top:16px">
          <div class="card">
            <h2>{{ i18n.t('Income vs. expenses') }}</h2>
            <lynx-bar-chart [data]="bars()" />
            <div class="row muted" style="margin-top:8px"><span class="badge good">{{ i18n.t('Income') }}</span><span class="badge bad">{{ i18n.t('Expenses') }}</span></div>
          </div>
          <div class="card">
            <h2>{{ i18n.t('Net worth over time') }}</h2>
            <div style="height:200px"><lynx-line-chart [values]="balanceValues()" /></div>
            <div class="row between muted caption">
              <span>{{ i18n.monthShort(d.balances[0].month, true) }}: {{ money(d.balances[0].balance) }}</span>
              <span>{{ i18n.monthShort(d.balances[d.balances.length - 1].month, true) }}: {{ money(d.balances[d.balances.length - 1].balance) }}</span>
            </div>
          </div>
        </div>

        <div class="grid cols-2" style="margin-top:16px">
          <div class="card">
            <h2>{{ i18n.t('Spending by category') }} · {{ i18n.monthLong(d.month) }}</h2>
            @for (c of d.categories; track c.categoryId) {
              <div class="row" style="margin-bottom:10px">
                <span class="label"><span class="dot" [style.background]="c.color"></span> {{ i18n.t(c.name) }}</span>
                <div class="progress" style="flex:1"><span [style.width.%]="(c.amount / d.categories[0].amount) * 100" [style.background]="c.color"></span></div>
                <span class="amount">{{ money(c.amount) }}</span>
                <span class="pct muted">{{ i18n.percent(c.amount / d.expense) }}</span>
              </div>
            } @empty {
              <div class="muted">{{ i18n.t('No spending this month.') }}</div>
            }
          </div>
          <div class="card">
            <h2>{{ i18n.t('Top payees') }} · {{ i18n.monthLong(d.month) }}</h2>
            <table class="table">
              @for (p of d.topPayees; track p.payee) {
                <tr>
                  <td><strong>{{ p.payee }}</strong><div class="muted">{{ i18n.t('{n} transactions', { n: p.count }) }}</div></td>
                  <td class="num neg">{{ money(p.amount) }}</td>
                </tr>
              } @empty {
                <tr><td class="muted">{{ i18n.t('No spending this month.') }}</td></tr>
              }
            </table>
          </div>
        </div>
        @if (d.currencies.length > 1) {
          <div class="muted caption" style="margin-top:12px">{{ i18n.t('Amounts in different currencies are shown separately and never converted.') }}</div>
        }
      }
    }
  `,
  styles: `
    .wrap { flex-wrap: wrap; gap: 12px; }
    .caption { font-size: 0.78rem; margin-top: 6px; }
    .label { width: 150px; flex-shrink: 0; }
    .amount { width: 110px; text-align: right; flex-shrink: 0; }
    .pct { width: 50px; text-align: right; flex-shrink: 0; font-size: 0.8rem; }
    .empty a { display: block; margin-top: 8px; }
  `,
})
export class Reports {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  protected readonly range = signal(6);
  protected readonly data = signal<Overview | null>(null);
  protected readonly error = signal('');
  private loadSeq = 0;

  protected readonly balanceValues = computed(() => this.data()?.balances.map((b) => b.balance) ?? []);
  protected readonly bars = computed(() => {
    const months = this.data()?.months ?? [];
    return months.map((m) => ({ month: this.i18n.monthShort(m.month, months.length > 12), income: m.income, expense: m.expense }));
  });

  constructor() {
    effect(() => {
      this.finance.currencyChoice();
      this.finance.monthChoice();
      this.range();
      untracked(() => this.load());
    });
  }

  protected money(value: number) { return this.i18n.money(value, this.data()?.currency ?? 'EUR'); }

  private async load() {
    const seq = ++this.loadSeq;
    try {
      const data = await this.finance.overview(this.range());
      if (seq !== this.loadSeq) return;
      this.data.set(data);
      this.error.set('');
    } catch (err) {
      if (seq === this.loadSeq) this.error.set(errorMessage(err));
    }
  }
}
