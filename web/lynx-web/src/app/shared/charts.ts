import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { I18n } from '../core/i18n.service';

/** Simple inline-SVG line/area chart. */
@Component({
  selector: 'lynx-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" class="chart">
      <path [attr.d]="area()" [attr.fill]="color()" opacity="0.12" />
      <path [attr.d]="line()" fill="none" [attr.stroke]="color()" stroke-width="2.5" vector-effect="non-scaling-stroke" />
      @if (zeroY() !== null) {
        <line x1="0" [attr.y1]="zeroY()" [attr.x2]="width" [attr.y2]="zeroY()" stroke="var(--bad)" stroke-dasharray="4 4" vector-effect="non-scaling-stroke" />
      }
    </svg>
  `,
  styles: `.chart { width: 100%; height: 100%; display: block; }`,
})
export class LineChart {
  readonly values = input.required<number[]>();
  readonly color = input('var(--primary)');
  readonly height = 160;
  readonly width = 600;

  private readonly bounds = computed(() => {
    const v = this.values();
    const min = Math.min(0, ...v);
    const max = Math.max(...v);
    return { min, max: max === min ? min + 1 : max };
  });

  private y(value: number): number {
    const { min, max } = this.bounds();
    return this.height - 8 - ((value - min) / (max - min)) * (this.height - 16);
  }

  protected readonly line = computed(() => {
    const v = this.values();
    const step = this.width / Math.max(1, v.length - 1);
    return v.map((val, i) => `${i === 0 ? 'M' : 'L'}${(i * step).toFixed(1)},${this.y(val).toFixed(1)}`).join(' ');
  });

  protected readonly area = computed(() => `${this.line()} L${this.width},${this.height} L0,${this.height} Z`);

  protected readonly zeroY = computed(() => (this.bounds().min < 0 ? this.y(0) : null));
}

/** Grouped income-vs-expense bars. */
@Component({
  selector: 'lynx-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bars">
      @for (d of data(); track d.month) {
        <div class="group">
          <div class="pair">
            <div class="bar income" [style.height.%]="(d.income / max()) * 100" [title]="i18n.t('Income') + ' ' + d.income"></div>
            <div class="bar expense" [style.height.%]="(d.expense / max()) * 100" [title]="i18n.t('Expenses') + ' ' + d.expense"></div>
          </div>
          <span class="label">{{ i18n.t(d.month) }}</span>
        </div>
      }
    </div>
  `,
  styles: `
    .bars { display: flex; align-items: flex-end; gap: 14px; height: 200px; }
    .group { flex: 1; display: flex; flex-direction: column; align-items: center; height: 100%; justify-content: flex-end; gap: 6px; }
    .pair { display: flex; align-items: flex-end; gap: 4px; height: 100%; width: 100%; justify-content: center; }
    .bar { width: 38%; border-radius: 5px 5px 0 0; min-height: 2px; }
    .income { background: var(--good); }
    .expense { background: var(--bad); }
    .label { font-size: 0.78rem; color: var(--muted); }
  `,
})
export class BarChart {
  protected readonly i18n = inject(I18n);
  readonly data = input.required<{ month: string; income: number; expense: number }[]>();
  protected readonly max = computed(() => Math.max(...this.data().flatMap((d) => [d.income, d.expense])));
}
