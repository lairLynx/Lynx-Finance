import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { BUDGET, MONTHLY_INCOME_EXPENSE, NET_WORTH_HISTORY, categoryById } from '../core/mock-data';
import { BarChart, LineChart } from '../shared/charts';

@Component({
  selector: 'lynx-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BarChart, LineChart],
  template: `
    <h1>{{ i18n.t('Reports') }}</h1>
    <p class="sub">{{ i18n.t('Last 6 months') }}</p>
    <div class="grid cols-2">
      <div class="card">
        <h2>{{ i18n.t('Income vs. expenses') }}</h2>
        <lynx-bar-chart [data]="monthly" />
        <div class="row muted" style="margin-top:8px"><span class="badge good">{{ i18n.t('Income') }}</span><span class="badge bad">{{ i18n.t('Expenses') }}</span></div>
      </div>
      <div class="card"><h2>{{ i18n.t('Net worth over time') }}</h2><div style="height:200px"><lynx-line-chart [values]="netWorth" /></div></div>
    </div>
    <div class="card" style="margin-top:16px">
      <h2>{{ i18n.t('Spending by category (this month)') }}</h2>
      @for (s of spending; track s.name) {
        <div class="row" style="margin-bottom:10px">
          <span style="width:150px"><span class="dot" [style.background]="s.color"></span> {{ i18n.t(s.name) }}</span>
          <div class="progress" style="flex:1"><span [style.width.%]="(s.spent / maxSpent) * 100" [style.background]="s.color"></span></div>
          <span style="width:90px;text-align:right">{{ i18n.money(s.spent) }}</span>
        </div>
      }
    </div>
  `,
})
export class Reports {
  protected readonly i18n = inject(I18n);
  protected readonly monthly = MONTHLY_INCOME_EXPENSE;
  protected readonly netWorth = NET_WORTH_HISTORY;
  protected readonly spending = BUDGET.map((b) => ({ name: categoryById(b.categoryId)!.name, color: categoryById(b.categoryId)!.color, spent: b.spent })).sort((a, b) => b.spent - a.spent);
  protected readonly maxSpent = Math.max(...this.spending.map((s) => s.spent));
}
