import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ACCOUNTS, TRANSACTIONS, categoryById } from '../core/mock-data';

@Component({
  selector: 'lynx-transactions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe],
  template: `
    <div class="row between">
      <div><h1>Transactions</h1><p class="sub">{{ filtered().length }} transactions</p></div>
      <button class="btn primary">Import file…</button>
    </div>
    <div class="card">
      <div class="row" style="margin-bottom:12px">
        <input type="search" placeholder="Search payee or description…" (input)="query.set($any($event.target).value)" class="input" />
        <label class="muted"><input type="checkbox" (change)="onlyUncategorized.set($any($event.target).checked)" /> Uncategorized only</label>
      </div>
      <table class="table">
        <thead><tr><th>Date</th><th>Payee</th><th>Account</th><th>Category</th><th class="num">Amount</th></tr></thead>
        <tbody>
          @for (t of filtered(); track t.id) {
            <tr>
              <td class="muted">{{ t.date }}</td>
              <td><strong>{{ t.payee }}</strong><div class="muted">{{ t.description }}</div></td>
              <td class="muted">{{ account(t.accountId) }}</td>
              <td>
                @if (cat(t.categoryId); as c) { <span class="badge"><span class="dot" [style.background]="c.color"></span>{{ c.name }}</span> }
                @else { <span class="badge warn">Uncategorized</span> }
              </td>
              <td class="num" [class.pos]="t.amount > 0" [class.neg]="t.amount < 0">{{ t.amount | currency: 'EUR' }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `.input { flex: 1; padding: 9px 12px; border-radius: 10px; border: 1px solid var(--border); background: var(--surface-2); color: var(--text); font: inherit; }`,
})
export class Transactions {
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
