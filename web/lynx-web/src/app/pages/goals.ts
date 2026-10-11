import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { GOALS } from '../core/mock-data';

@Component({
  selector: 'lynx-goals',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe],
  template: `
    <div class="row between">
      <div><h1>Savings goals</h1><p class="sub">Progress and the monthly amount needed to hit each target</p></div>
      <button class="btn primary">+ New goal</button>
    </div>
    <div class="grid cols-3">
      @for (g of goals; track g.id) {
        <div class="card">
          <div class="row between"><strong>{{ g.name }}</strong><span class="badge" [class.good]="g.onTrack" [class.bad]="!g.onTrack">{{ g.onTrack ? 'On track' : 'Behind' }}</span></div>
          <div class="stat-value">{{ g.saved | currency: 'EUR' }}</div>
          <div class="muted" style="margin-bottom:10px">of {{ g.target | currency: 'EUR' }} · by {{ g.targetDate }}</div>
          <div class="progress"><span [style.width.%]="(g.saved / g.target) * 100"></span></div>
          <div class="muted" style="margin-top:10px">Needs {{ g.monthlyNeeded | currency: 'EUR' }} / month</div>
        </div>
      }
    </div>
  `,
})
export class Goals {
  protected readonly goals = GOALS;
}
