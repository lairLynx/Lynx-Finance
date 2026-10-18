import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { ACCOUNTS, BUDGET, FORECAST, NET_WORTH_HISTORY, RECURRING, categoryById } from '../core/mock-data';
import { LineChart } from '../shared/charts';

@Component({
  selector: 'lynx-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LineChart],
  template: `
    <h1>{{ i18n.t('Dashboard') }}</h1>
    <p class="sub">{{ i18n.t('October 2026 at a glance') }}</p>

    <div class="grid cols-4">
      <div class="card"><div class="stat-label">{{ i18n.t('Net worth') }}</div><div class="stat-value">{{ i18n.money(netWorth) }}</div><div class="pos">{{ i18n.t('▲ 4.9% vs last month') }}</div></div>
      <div class="card"><div class="stat-label">{{ i18n.t('Income (Oct)') }}</div><div class="stat-value pos">{{ i18n.money(3650) }}</div></div>
      <div class="card"><div class="stat-label">{{ i18n.t('Spending (Oct)') }}</div><div class="stat-value neg">{{ i18n.money(2590) }}</div></div>
      <div class="card"><div class="stat-label">{{ i18n.t('Left in budget') }}</div><div class="stat-value">{{ i18n.money(budgetLeft) }}</div></div>
    </div>

    <div class="grid cols-2" style="margin-top:16px">
      <div class="card"><h2>{{ i18n.t('Net worth, last 12 months') }}</h2><div style="height:160px"><lynx-line-chart [values]="history" /></div></div>
      <div class="card"><h2>{{ i18n.t('Projected balance, next 90 days') }}</h2><div style="height:160px"><lynx-line-chart [values]="forecast" color="var(--good)" /></div></div>
    </div>

    <div class="grid cols-2" style="margin-top:16px">
      <div class="card">
        <h2>{{ i18n.t('Budgets needing attention') }}</h2>
        @for (b of attention; track b.categoryId) {
          <div style="margin-bottom:12px">
            <div class="row between"><span>{{ i18n.t(cat(b.categoryId) ?? '') }}</span><span [class]="b.spent > b.budget ? 'neg' : 'muted'">{{ i18n.money(b.spent) }} / {{ i18n.money(b.budget) }}</span></div>
            <div class="progress"><span [class.over]="b.spent > b.budget" [style.width.%]="min(100, (b.spent / b.budget) * 100)"></span></div>
          </div>
        }
      </div>
      <div class="card">
        <h2>{{ i18n.t('Upcoming bills') }}</h2>
        <table class="table">
          @for (r of upcoming; track r.id) {
            <tr><td>{{ i18n.t(r.payee) }}</td><td class="muted">{{ i18n.date(r.nextDue) }}</td><td class="num neg">{{ i18n.money(r.amount) }}</td></tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class Dashboard {
  protected readonly i18n = inject(I18n);
  protected readonly history = NET_WORTH_HISTORY;
  protected readonly forecast = FORECAST;
  protected readonly netWorth = ACCOUNTS.reduce((s, a) => s + a.balance, 0);
  protected readonly budgetLeft = BUDGET.reduce((s, b) => s + (b.budget - b.spent), 0);
  protected readonly attention = BUDGET.filter((b) => b.spent / b.budget > 0.85);
  protected readonly upcoming = RECURRING.filter((r) => r.amount < 0).sort((a, b) => a.nextDue.localeCompare(b.nextDue)).slice(0, 4);
  protected cat(id: number) { return categoryById(id)?.name; }
  protected min = Math.min;
}
