import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';

/** The parts of a server response the controls need (satisfied by Overview and the budgets payload). */
export interface ViewState {
  currencies: string[];
  currency: string | null;
  month: string;
  firstMonth: string | null;
  lastMonth: string | null;
}

/** Currency tabs plus a month stepper, shared by the dashboard and reports. State lives in FinanceService. */
@Component({
  selector: 'lynx-view-controls',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="controls">
      @if (data().currencies.length) {
        <div class="seg" role="group" [attr.aria-label]="i18n.t('Currency')">
          @for (c of data().currencies; track c) {
            <button type="button" [class.on]="c === data().currency" (click)="finance.chooseCurrency(c)">{{ c }}</button>
          }
        </div>
        @if (monthStepper()) {
        <div class="stepper">
          <button class="btn" type="button" [disabled]="!canPrev()" [attr.aria-label]="i18n.t('Previous month')" (click)="step(-1)">◀</button>
          <strong>{{ i18n.monthLong(data().month) }}</strong>
          <button class="btn" type="button" [disabled]="!canNext()" [attr.aria-label]="i18n.t('Next month')" (click)="step(1)">▶</button>
        </div>
        }
      }
    </div>
  `,
  styles: `
    .controls { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
    .seg { display: inline-flex; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
    .seg button { font: inherit; font-weight: 600; font-size: 0.85rem; padding: 6px 12px; border: none; background: var(--surface); color: var(--muted); cursor: pointer; }
    .seg button + button { border-left: 1px solid var(--border); }
    .seg button.on { background: var(--primary-soft); color: var(--primary); }
    .stepper { display: flex; align-items: center; gap: 10px; }
    .stepper strong { min-width: 130px; text-align: center; }
  `,
})
export class ViewControls {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  readonly data = input.required<ViewState>();
  /** Hide the month stepper for pages that only need the currency choice. */
  readonly monthStepper = input(true);

  protected readonly canPrev = computed(() => !!this.data().firstMonth && this.data().month > this.data().firstMonth!);
  protected readonly canNext = computed(() => !!this.data().lastMonth && this.data().month < this.data().lastMonth!);

  protected step(delta: number) {
    const [y, m] = this.data().month.split('-').map(Number);
    const d = new Date(y, m - 1 + delta, 1);
    this.finance.monthChoice.set(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`);
  }
}
