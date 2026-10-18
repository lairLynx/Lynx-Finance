import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { GOALS } from '../core/mock-data';

@Component({
  selector: 'lynx-goals',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Savings goals') }}</h1><p class="sub">{{ i18n.t('Progress and the monthly amount needed to hit each target') }}</p></div>
      <button class="btn primary">{{ i18n.t('+ New goal') }}</button>
    </div>
    <div class="grid cols-3">
      @for (g of goals; track g.id) {
        <div class="card">
          <div class="row between"><strong>{{ i18n.t(g.name) }}</strong><span class="badge" [class.good]="g.onTrack" [class.bad]="!g.onTrack">{{ i18n.t(g.onTrack ? 'On track' : 'Behind') }}</span></div>
          <div class="stat-value">{{ i18n.money(g.saved) }}</div>
          <div class="muted" style="margin-bottom:10px">{{ i18n.t('of {target} · by {date}', { target: i18n.money(g.target), date: i18n.date(g.targetDate) }) }}</div>
          <div class="progress"><span [style.width.%]="(g.saved / g.target) * 100"></span></div>
          <div class="muted" style="margin-top:10px">{{ i18n.t('Needs {amount} / month', { amount: i18n.money(g.monthlyNeeded) }) }}</div>
        </div>
      }
    </div>
  `,
})
export class Goals {
  protected readonly i18n = inject(I18n);
  protected readonly goals = GOALS;
}
