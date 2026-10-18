import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { I18n } from '../core/i18n.service';
import { ACCOUNTS, TRANSACTIONS, categoryById } from '../core/mock-data';

@Component({
  selector: 'lynx-transactions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Transactions') }}</h1><p class="sub">{{ i18n.t('{n} transactions', { n: filtered().length }) }}</p></div>
      <button class="btn primary">{{ i18n.t('Import file…') }}</button>
    </div>
    <div class="card">
      <div class="row" style="margin-bottom:12px">
        <input type="search" [placeholder]="i18n.t('Search payee or description…')" (input)="query.set($any($event.target).value)" class="input" />
        <label class="muted"><input type="checkbox" (change)="onlyUncategorized.set($any($event.target).checked)" /> {{ i18n.t('Uncategorized only') }}</label>
      </div>
      <table class="table">
        <thead><tr><th>{{ i18n.t('Date') }}</th><th>{{ i18n.t('Payee') }}</th><th>{{ i18n.t('Account') }}</th><th>{{ i18n.t('Category') }}</th><th class="num">{{ i18n.t('Amount') }}</th></tr></thead>
        <tbody>
          @for (t of filtered(); track t.id) {
            <tr>
              <td class="muted">{{ i18n.date(t.date) }}</td>
              <td><strong>{{ i18n.t(t.payee) }}</strong><div class="muted">{{ i18n.t(t.description) }}</div></td>
              <td class="muted">{{ i18n.t(account(t.accountId) ?? '') }}</td>
              <td>
                @if (cat(t.categoryId); as c) { <span class="badge"><span class="dot" [style.background]="c.color"></span>{{ i18n.t(c.name) }}</span> }
                @else { <span class="badge warn">{{ i18n.t('Uncategorized') }}</span> }
              </td>
              <td class="num" [class.pos]="t.amount > 0" [class.neg]="t.amount < 0">{{ i18n.money(t.amount) }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `.input { flex: 1; padding: 9px 12px; border-radius: 10px; border: 1px solid var(--border); background: var(--surface-2); color: var(--text); font: inherit; }`,
})
export class Transactions {
  protected readonly i18n = inject(I18n);
  protected readonly query = signal('');
  protected readonly onlyUncategorized = signal(false);
  protected readonly filtered = computed(() => {
    const q = this.query().toLowerCase();
    return TRANSACTIONS.filter(
      (t) =>
        (!q || t.payee.toLowerCase().includes(q) || t.description.toLowerCase().includes(q)) &&
        (!this.onlyUncategorized() || t.categoryId === null),
    );
  });
  protected cat(id: number | null) { return categoryById(id); }
  protected account(id: number) { return ACCOUNTS.find((a) => a.id === id)?.name; }
}
