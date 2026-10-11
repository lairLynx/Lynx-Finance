import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ACCOUNTS } from '../core/mock-data';

@Component({
  selector: 'lynx-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe],
  template: `
    <div class="row between">
      <div><h1>Accounts</h1><p class="sub">Connected banks and manual accounts</p></div>
      <div class="row"><button class="btn">Import file…</button><button class="btn primary">+ Connect bank</button></div>
    </div>
    <div class="grid cols-2">
      @for (a of accounts; track a.id) {
        <div class="card">
          <div class="row between">
            <div><strong>{{ a.name }}</strong><div class="muted">{{ a.bank }} · {{ a.iban }}</div></div>
            @switch (a.status) {
              @case ('ok') { <span class="badge good">Synced</span> }
              @case ('expiring') { <span class="badge warn">Re-link in {{ a.consentDaysLeft }} days</span> }
              @default { <span class="badge">Manual</span> }
            }
          </div>
          <div class="stat-value" [class.neg]="a.balance < 0">{{ a.balance | currency: a.currency }}</div>
          <div class="row between muted"><span>{{ a.type }} · last sync {{ a.lastSynced }}</span>
            @if (a.status !== 'manual') { <button class="btn">Sync now</button> }
          </div>
        </div>
      }
    </div>
  `,
})
export class Accounts {
  protected readonly accounts = ACCOUNTS;
}
