import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { FORECAST, RECURRING } from '../core/mock-data';
import { LineChart } from '../shared/charts';

@Component({
  selector: 'lynx-forecast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LineChart],
  template: `
    <h1>{{ i18n.t('Recurring & Forecast') }}</h1>
    <p class="sub">{{ i18n.t('Confirmed recurring items drive the projected balance') }}</p>
    <div class="card" style="margin-bottom:16px">
      <div class="row between"><h2>{{ i18n.t('Projected balance') }}</h2><div class="row"><button class="btn">{{ i18n.t('30d') }}</button><button class="btn primary">{{ i18n.t('90d') }}</button><button class="btn">{{ i18n.t('180d') }}</button></div></div>
      <div style="height:200px"><lynx-line-chart [values]="forecast" color="var(--good)" /></div>
      <div class="muted" style="margin-top:8px">{{ i18n.t('Lowest point:') }} <strong [class.neg]="lowest < 0">{{ i18n.money(lowest) }}</strong></div>
    </div>
    <div class="card">
      <h2>{{ i18n.t('Recurring items') }}</h2>
      <table class="table">
        <thead><tr><th>{{ i18n.t('Payee') }}</th><th>{{ i18n.t('Frequency') }}</th><th>{{ i18n.t('Next due') }}</th><th class="num">{{ i18n.t('Amount') }}</th><th></th></tr></thead>
        @for (r of items; track r.id) {
          <tr>
            <td><strong>{{ i18n.t(r.payee) }}</strong></td><td class="muted">{{ i18n.t(r.frequency) }}</td><td class="muted">{{ i18n.date(r.nextDue) }}</td>
            <td class="num" [class.pos]="r.amount > 0">{{ i18n.money(r.amount) }}</td>
            <td class="num">@if (r.confirmed) { <span class="badge good">{{ i18n.t('Confirmed') }}</span> } @else { <span class="badge warn">{{ i18n.t('Detected') }}</span> <button class="btn">{{ i18n.t('Confirm') }}</button> }</td>
          </tr>
        }
      </table>
    </div>
  `,
})
export class Forecast {
  protected readonly i18n = inject(I18n);
  protected readonly forecast = FORECAST;
  protected readonly items = RECURRING;
  protected readonly lowest = Math.min(...FORECAST);
}
