import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ACCOUNTS, BUDGET, FORECAST, NET_WORTH_HISTORY, RECURRING, categoryById } from '../core/mock-data';
import { LineChart } from '../shared/charts';

@Component({
  selector: 'lynx-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, LineChart],
  template: `
    <h1>Dashboard</h1>
    <p class="sub">October 2026 at a glance</p>

    <div class="grid cols-4">
      <div class="card"><div class="stat-label">Net worth</div><div class="stat-value">{{ netWorth | currency: 'EUR' }}</div><div class="pos">▲ 4.9% vs last month</div></div>
      <div class="card"><div class="stat-label">Income (Oct)</div><div class="stat-value pos">{{ 3650 | currency: 'EUR' }}</div></div>
      <div class="card"><div class="stat-label">Spending (Oct)</div><div class="stat-value neg">{{ 2590 | currency: 'EUR' }}</div></div>
      <div class="card"><div class="stat-label">Left in budget</div><div class="stat-value">{{ budgetLeft | currency: 'EUR' }}</div></div>
    </div>

    <div class="grid cols-2" style="margin-top:16px">
      <div class="card"><h2>Net worth, last 12 months</h2><div style="height:160px"><lynx-line-chart [values]="history" /></div></div>
      <div class="card"><h2>Projected balance, next 90 days</h2><div style="height:160px"><lynx-line-chart [values]="forecast" color="var(--good)" /></div></div>
    </div>

    <div class="grid cols-2" style="margin-top:16px">
      <div class="card">
        <h2>Budgets needing attention</h2>
        @for (b of attention; track b.categoryId) {
          <div style="margin-bottom:12px">
            <div class="row between"><span>{{ cat(b.categoryId) }}</span><span [class]="b.spent > b.budget ? 'neg' : 'muted'">{{ b.spent | currency: 'EUR' }} / {{ b.budget | currency: 'EUR' }}</span></div>
            <div class="progress"><span [class.over]="b.spent > b.budget" [style.width.%]="min(100, (b.spent / b.budget) * 100)"></span></div>
          </div>
        }
      </div>
      <div class="card">
        <h2>Upcoming bills</h2>
        <table class="table">
          @for (r of upcoming; track r.id) {
            <tr><td>{{ r.payee }}</td><td class="muted">{{ r.nextDue }}</td><td class="num neg">{{ r.amount | currency: 'EUR' }}</td></tr>
          }
        </table>
      </div>
    </div>
  `,
})
export class Dashboard {
  protected readonly history = NET_WORTH_HISTORY;
  protected readonly forecast = FORECAST;
  protected readonly netWorth = ACCOUNTS.reduce((s, a) => s + a.balance, 0);
  protected readonly budgetLeft = BUDGET.reduce((s, b) => s + (b.budget - b.spent), 0);
  protected readonly attention = BUDGET.filter((b) => b.spent / b.budget > 0.85);
  protected readonly upcoming = RECURRING.filter((r) => r.amount < 0).sort((a, b) => a.nextDue.localeCompare(b.nextDue)).slice(0, 4);
  protected cat(id: number) { return categoryById(id)?.name; }
  protected min = Math.min;
}
