import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { FORECAST, RECURRING } from '../core/mock-data';
import { LineChart } from '../shared/charts';

@Component({
  selector: 'lynx-forecast',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, LineChart],
  template: `
    <h1>Recurring &amp; Forecast</h1>
    <p class="sub">Confirmed recurring items drive the projected balance</p>
    <div class="card" style="margin-bottom:16px">
      <div class="row between"><h2>Projected balance</h2><div class="row"><button class="btn">30d</button><button class="btn primary">90d</button><button class="btn">180d</button></div></div>
      <div style="height:200px"><lynx-line-chart [values]="forecast" color="var(--good)" /></div>
      <div class="muted" style="margin-top:8px">Lowest point: <strong [class.neg]="lowest < 0">{{ lowest | currency: 'EUR' }}</strong></div>
    </div>
    <div class="card">
      <h2>Recurring items</h2>
      <table class="table">
        <thead><tr><th>Payee</th><th>Frequency</th><th>Next due</th><th class="num">Amount</th><th></th></tr></thead>
        @for (r of items; track r.id) {
          <tr>
            <td><strong>{{ r.payee }}</strong></td><td class="muted">{{ r.frequency }}</td><td class="muted">{{ r.nextDue }}</td>
            <td class="num" [class.pos]="r.amount > 0">{{ r.amount | currency: 'EUR' }}</td>
            <td class="num">@if (r.confirmed) { <span class="badge good">Confirmed</span> } @else { <span class="badge warn">Detected</span> <button class="btn">Confirm</button> }</td>
          </tr>
        }
      </table>
    </div>
  `,
})
export class Forecast {
  protected readonly forecast = FORECAST;
  protected readonly items = RECURRING;
  protected readonly lowest = Math.min(...FORECAST);
}
