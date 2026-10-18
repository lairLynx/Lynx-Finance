import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { BUDGET, categoryById } from '../core/mock-data';

@Component({
  selector: 'lynx-budgets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Budgets') }}</h1><p class="sub">{{ i18n.t('Planned vs. actual spending per category') }}</p></div>
      <div class="row"><button class="btn">◀</button><strong>{{ i18n.monthYear(2026, 9) }}</strong><button class="btn">▶</button><button class="btn">{{ i18n.t('Copy last month') }}</button></div>
    </div>
    <div class="grid cols-3" style="margin-bottom:16px">
      <div class="card"><div class="stat-label">{{ i18n.t('Budgeted') }}</div><div class="stat-value">{{ i18n.money(totalBudget) }}</div></div>
      <div class="card"><div class="stat-label">{{ i18n.t('Spent') }}</div><div class="stat-value">{{ i18n.money(totalSpent) }}</div></div>
      <div class="card"><div class="stat-label">{{ i18n.t('Remaining') }}</div><div class="stat-value" [class.neg]="totalBudget < totalSpent" [class.pos]="totalBudget >= totalSpent">{{ i18n.money(totalBudget - totalSpent) }}</div></div>
    </div>
    <div class="card">
      @for (b of lines; track b.categoryId) {
        <div style="margin-bottom:18px">
          <div class="row between">
            <span class="row"><span class="dot" [style.background]="cat(b.categoryId)?.color"></span><strong>{{ i18n.t(cat(b.categoryId)?.name ?? '') }}</strong></span>
            <span>
              <span [class.neg]="b.spent > b.budget">{{ i18n.money(b.spent) }}</span>
              <span class="muted"> {{ i18n.t('of {amount}', { amount: i18n.money(b.budget) }) }} ·
                @if (b.spent > b.budget) { <span class="neg">{{ i18n.t('{amount} over', { amount: i18n.money(b.spent - b.budget) }) }}</span> } @else { {{ i18n.t('{amount} left', { amount: i18n.money(b.budget - b.spent) }) }} }
              </span>
            </span>
          </div>
          <div class="progress"><span [class.over]="b.spent > b.budget" [style.width.%]="min(100, (b.spent / b.budget) * 100)"></span></div>
        </div>
      }
    </div>
  `,
})
export class Budgets {
  protected readonly i18n = inject(I18n);
  protected readonly lines = BUDGET;
  protected readonly totalBudget = BUDGET.reduce((s, b) => s + b.budget, 0);
  protected readonly totalSpent = BUDGET.reduce((s, b) => s + b.spent, 0);
  protected cat(id: number) { return categoryById(id); }
  protected min = Math.min;
}
