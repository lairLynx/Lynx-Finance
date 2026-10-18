import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../core/auth.service';
import { FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';
import { BudgetsData, PlanningService } from '../core/planning.service';
import { ViewControls } from '../shared/view-controls';

@Component({
  selector: 'lynx-budgets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, ViewControls],
  template: `
    <div class="row between wrap">
      <div><h1>{{ i18n.t('Budgets') }}</h1><p class="sub">{{ i18n.t('Planned vs. actual spending per category') }}</p></div>
      <div class="row wrap">
        @if (data(); as d) {
          <lynx-view-controls [data]="d" />
          @if (d.canCopy) { <button class="btn" type="button" (click)="copyPrevious()">{{ i18n.t('Copy last month') }}</button> }
        }
      </div>
    </div>

    @if (error()) { <div class="notice bad" style="margin-bottom:16px">{{ i18n.t(error()) }}</div> }

    @if (data(); as d) {
      @if (!d.currency) {
        <div class="card empty">
          {{ i18n.t('Nothing to show yet.') }}
          <a routerLink="/accounts">{{ i18n.t('Create an account and import a statement.') }}</a>
        </div>
      } @else {
        <div class="grid cols-3" style="margin-bottom:16px">
          <div class="card"><div class="stat-label">{{ i18n.t('Budgeted') }}</div><div class="stat-value">{{ money(d.budgeted) }}</div></div>
          <div class="card"><div class="stat-label">{{ i18n.t('Spent') }}</div><div class="stat-value">{{ money(d.spent) }}</div>
            @if (d.totalSpent > d.spent) { <div class="muted">{{ i18n.t('{amount} total spending including categories without a budget', { amount: money(d.totalSpent) }) }}</div> }
          </div>
          <div class="card"><div class="stat-label">{{ i18n.t('Remaining') }}</div>
            <div class="stat-value" [class.neg]="d.budgeted < d.spent" [class.pos]="d.budgeted >= d.spent && d.budgeted > 0">{{ money(d.budgeted - d.spent) }}</div>
          </div>
        </div>

        <div class="card" style="margin-bottom:16px">
          @for (b of d.lines; track b.id) {
            <div class="line">
              <div class="row between">
                <span class="row"><span class="dot" [style.background]="b.color"></span><strong>{{ i18n.t(b.name) }}</strong></span>
                <span class="row">
                  <span [class.neg]="b.spent > b.budget">{{ money(b.spent) }}</span>
                  <span class="muted">{{ i18n.t('of') }}</span>
                  <input class="input amount" type="number" min="0" step="1" [value]="b.budget" [attr.aria-label]="i18n.t('Budget')"
                    (change)="save(d, b.categoryId, $any($event.target).value)" />
                  <button class="btn link" type="button" [attr.aria-label]="i18n.t('Delete')" (click)="save(d, b.categoryId, '0')">✕</button>
                </span>
              </div>
              <div class="progress"><span [class.over]="b.spent > b.budget" [style.width.%]="percent(b.spent, b.budget)"></span></div>
              <div class="muted note">{{ b.spent > b.budget ? i18n.t('{amount} over', { amount: money(b.spent - b.budget) }) : i18n.t('{amount} left', { amount: money(b.budget - b.spent) }) }}</div>
            </div>
          } @empty {
            <div class="muted">{{ i18n.t('No budgets for this month yet. Add one below.') }}</div>
          }
        </div>

        <form class="card" style="margin-bottom:16px" (ngSubmit)="add(d)">
          <h2>{{ i18n.t('Add a budget') }}</h2>
          <div class="row wrap">
            <select class="input" name="category" [(ngModel)]="newCategory" [attr.aria-label]="i18n.t('Category')">
              <option [ngValue]="null" disabled>{{ i18n.t('Choose a category') }}</option>
              @for (c of available(); track c.id) { <option [ngValue]="c.id">{{ i18n.t(c.name) }}</option> }
            </select>
            <input class="input" name="amount" type="number" min="1" step="1" [(ngModel)]="newAmount" [placeholder]="i18n.t('Monthly amount') + ' (' + d.currency + ')'" />
            <button class="btn primary" type="submit" [disabled]="!newCategory || !newAmount">{{ i18n.t('Add') }}</button>
          </div>
        </form>

        @if (d.unbudgeted.length) {
          <div class="card">
            <h2>{{ i18n.t('Spending without a budget') }}</h2>
            <table class="table">
              @for (u of d.unbudgeted; track u.categoryId) {
                <tr>
                  <td><span class="dot" [style.background]="u.color"></span> {{ i18n.t(u.name) }}</td>
                  <td class="num neg">{{ money(u.amount) }}</td>
                  <td class="num">
                    @if (u.categoryId !== null) {
                      <button class="btn" type="button" (click)="save(d, u.categoryId, ceil(u.amount).toString())">{{ i18n.t('Set budget') }}</button>
                    } @else {
                      <a routerLink="/transactions" class="muted">{{ i18n.t('Categorize them') }}</a>
                    }
                  </td>
                </tr>
              }
            </table>
          </div>
        }
      }
    }
  `,
  styles: `
    .wrap { flex-wrap: wrap; gap: 12px; }
    .line { margin-bottom: 18px; }
    .line:last-child { margin-bottom: 0; }
    .amount { width: 100px; padding: 5px 8px; text-align: right; }
    .note { font-size: 0.8rem; margin-top: 4px; }
    .empty a { display: block; margin-top: 8px; }
  `,
})
export class Budgets {
  protected readonly finance = inject(FinanceService);
  protected readonly planning = inject(PlanningService);
  protected readonly i18n = inject(I18n);

  protected readonly data = signal<BudgetsData | null>(null);
  protected readonly error = signal('');
  protected newCategory: number | null = null;
  protected newAmount: number | null = null;
  private loadSeq = 0;

  /** Expense categories that don't have a budget yet this month. */
  protected readonly available = computed(() => {
    const used = new Set(this.data()?.lines.map((l) => l.categoryId));
    return this.finance.categories().filter((c) => !c.isIncome && !used.has(c.id));
  });

  constructor() {
    this.finance.refreshCategories().catch(() => undefined);
    effect(() => {
      this.finance.currencyChoice();
      this.finance.monthChoice();
      untracked(() => this.load());
    });
  }

  protected money(value: number) { return this.i18n.money(value, this.data()?.currency ?? 'EUR'); }
  protected percent(spent: number, budget: number) { return budget > 0 ? Math.min(100, (spent / budget) * 100) : 0; }
  protected ceil = Math.ceil;

  protected async save(d: BudgetsData, categoryId: number, value: string) {
    const amount = Number(value);
    if (!Number.isFinite(amount) || amount < 0) return;
    await this.run(() => this.planning.setBudget(d.month, categoryId, d.currency!, amount));
  }

  protected async add(d: BudgetsData) {
    if (!this.newCategory || !this.newAmount) return;
    await this.run(() => this.planning.setBudget(d.month, this.newCategory!, d.currency!, Number(this.newAmount)));
    this.newCategory = null;
    this.newAmount = null;
  }

  protected async copyPrevious() {
    const d = this.data();
    if (!d?.currency) return;
    const [y, m] = d.month.split('-').map(Number);
    const prev = new Date(y, m - 2, 1);
    const from = `${prev.getFullYear()}-${String(prev.getMonth() + 1).padStart(2, '0')}`;
    await this.run(() => this.planning.copyBudgets(from, d.month, d.currency!));
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
      const data = await this.planning.budgets();
      if (seq !== this.loadSeq) return;
      this.data.set(data);
    } catch (err) {
      if (seq === this.loadSeq) this.error.set(errorMessage(err));
    }
  }
}
