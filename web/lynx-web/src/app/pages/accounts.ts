import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { ACCOUNTS } from '../core/mock-data';

@Component({
  selector: 'lynx-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Accounts') }}</h1><p class="sub">{{ i18n.t('Connected banks and manual accounts') }}</p></div>
      <div class="row"><button class="btn">{{ i18n.t('Import file…') }}</button><button class="btn primary">{{ i18n.t('+ Connect bank') }}</button></div>
    </div>
    <div class="grid cols-2">
      @for (a of accounts; track a.id) {
        <div class="card">
          <div class="row between">
            <div><strong>{{ i18n.t(a.name) }}</strong><div class="muted">{{ i18n.t(a.bank) }} · {{ a.iban }}</div></div>
            @switch (a.status) {
              @case ('ok') { <span class="badge good">{{ i18n.t('Synced') }}</span> }
              @case ('expiring') { <span class="badge warn">{{ i18n.t('Re-link in {n} days', { n: a.consentDaysLeft ?? 0 }) }}</span> }
              @default { <span class="badge">{{ i18n.t('Manual') }}</span> }
            }
          </div>
          <div class="stat-value" [class.neg]="a.balance < 0">{{ i18n.money(a.balance, a.currency) }}</div>
          <div class="row between muted"><span>{{ i18n.t('{type} · last sync {when}', { type: i18n.t(a.type), when: i18n.t(a.lastSynced) }) }}</span>
            @if (a.status !== 'manual') { <button class="btn">{{ i18n.t('Sync now') }}</button> }
          </div>
        </div>
      }
    </div>
  `,
})
export class Accounts {
  protected readonly i18n = inject(I18n);
  protected readonly accounts = ACCOUNTS;
}
