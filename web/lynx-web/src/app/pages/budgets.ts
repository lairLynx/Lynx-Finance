import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { BUDGET, categoryById } from '../core/mock-data';

@Component({
  selector: 'lynx-budgets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe],
  template: `
    <div class="row between">
      <div><h1>Budgets</h1><p class="sub">Planned vs. actual spending per category</p></div>
      <div class="row"><button class="btn">◀</button><strong>October 2026</strong><button class="btn">▶</button><button class="btn">Copy last month</button></div>
    </div>
    <div class="grid cols-3" style="margin-bottom:16px">
      <div class="card"><div class="stat-label">Budgeted</div><div class="stat-value">{{ totalBudget | currency: 'EUR' }}</div></div>
      <div class="card"><div class="stat-label">Spent</div><div class="stat-value">{{ totalSpent | currency: 'EUR' }}</div></div>
      <div class="card"><div class="stat-label">Remaining</div><div class="stat-value" [class.neg]="totalBudget < totalSpent" [class.pos]="totalBudget >= totalSpent">{{ totalBudget - totalSpent | currency: 'EUR' }}</div></div>
    </div>
    <div class="card">
      @for (b of lines; track b.categoryId) {
        <div style="margin-bottom:18px">
          <div class="row between">
            <span class="row"><span class="dot" [style.background]="cat(b.categoryId)?.color"></span><strong>{{ cat(b.categoryId)?.name }}</strong></span>
            <span>
              <span [class.neg]="b.spent > b.budget">{{ b.spent | currency: 'EUR' }}</span>
              <span class="muted"> of {{ b.budget | currency: 'EUR' }} ·
                @if (b.spent > b.budget) { <span class="neg">{{ b.spent - b.budget | currency: 'EUR' }} over</span> } @else { {{ b.budget - b.spent | currency: 'EUR' }} left }
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
  protected readonly lines = BUDGET;
  protected readonly totalBudget = BUDGET.reduce((s, b) => s + b.budget, 0);
  protected readonly totalSpent = BUDGET.reduce((s, b) => s + b.spent, 0);
  protected cat(id: number) { return categoryById(id); }
  protected min = Math.min;
}
