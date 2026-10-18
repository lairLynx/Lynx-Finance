import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/auth.service';
import { CategoryDto, FinanceService } from '../core/finance.service';
import { I18n } from '../core/i18n.service';

@Component({
  selector: 'lynx-categories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="row between">
      <div><h1>{{ i18n.t('Categories') }}</h1><p class="sub">{{ i18n.t('Used to group transactions in budgets and reports') }}</p></div>
      <button class="btn primary" type="button" (click)="startNew()">{{ i18n.t('+ New category') }}</button>
    </div>

    @if (form(); as f) {
      <form class="card" style="margin-bottom:16px" (ngSubmit)="save()">
        <h2>{{ editingId() === null ? i18n.t('New category') : i18n.t('Edit category') }}</h2>
        <div class="form-grid">
          <label>{{ i18n.t('Name') }}<input class="input" name="name" [(ngModel)]="f.name" required maxlength="60" /></label>
          <label>{{ i18n.t('Color') }}<input class="input swatch" name="color" type="color" [(ngModel)]="f.color" /></label>
          <label class="check"><input type="checkbox" name="income" [(ngModel)]="f.isIncome" /> {{ i18n.t('Income category') }}</label>
        </div>
        @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }
        <div class="row" style="margin-top:12px">
          <button class="btn primary" type="submit" [disabled]="busy()">{{ i18n.t('Save') }}</button>
          <button class="btn" type="button" (click)="form.set(null)">{{ i18n.t('Cancel') }}</button>
        </div>
      </form>
    }

    <div class="card">
      <table class="table">
        <tbody>
          @for (c of finance.categories(); track c.id) {
            <tr>
              <td><span class="badge"><span class="dot" [style.background]="c.color"></span>{{ i18n.t(c.name) }}</span></td>
              <td class="muted">{{ c.isIncome ? i18n.t('Income') : i18n.t('Expense') }}</td>
              <td class="num">
                <button class="btn link" type="button" (click)="edit(c)">{{ i18n.t('Edit') }}</button>
                <button class="btn link" type="button" (click)="remove(c)">{{ i18n.t('Delete') }}</button>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .swatch { padding: 2px; height: 38px; }
    label.check { flex-direction: row; align-items: center; gap: 8px; align-self: end; padding-bottom: 8px; }
  `,
})
export class Categories implements OnInit {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  protected readonly editingId = signal<number | null>(null);
  protected readonly form = signal<{ name: string; color: string; isIncome: boolean } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  async ngOnInit() {
    await this.finance.refreshCategories().catch(() => undefined);
  }

  protected startNew() {
    this.editingId.set(null);
    this.error.set('');
    this.form.set({ name: '', color: '#4f8cff', isIncome: false });
  }

  protected edit(c: CategoryDto) {
    this.editingId.set(c.id);
    this.error.set('');
    this.form.set({ name: c.name, color: c.color, isIncome: c.isIncome });
  }

  protected async save() {
    const f = this.form();
    if (!f) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.finance.saveCategory(this.editingId(), f);
      this.form.set(null);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(c: CategoryDto) {
    if (!confirm(this.i18n.t('Delete category "{name}"? Its transactions become uncategorized.', { name: this.i18n.t(c.name) }))) return;
    await this.finance.deleteCategory(c.id);
  }
}
