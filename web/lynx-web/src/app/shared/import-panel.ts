import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../core/auth.service';
import { FinanceService, ImportResult } from '../core/finance.service';
import { I18n } from '../core/i18n.service';

/** Pick an account and a CSV/OFX/CAMT file, upload it, and report how many rows were new. */
@Component({
  selector: 'lynx-import-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="card panel">
      <div class="row between">
        <h2>{{ i18n.t('Import transactions') }}</h2>
        <button class="btn" type="button" (click)="closed.emit()">{{ i18n.t('Close') }}</button>
      </div>
      <p class="muted">{{ i18n.t('Upload a CSV, OFX, CAMT (XML) or Banca Transilvania PDF statement. Rows already imported are skipped.') }}</p>
      <div class="row wrap">
        <select class="input" [ngModel]="targetId()" (ngModelChange)="targetId.set(+$event)" [attr.aria-label]="i18n.t('Account')">
          @for (a of finance.accounts(); track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
        </select>
        <input class="input file" type="file" accept=".csv,.txt,.tsv,.ofx,.qfx,.xml,.pdf" (change)="file.set($any($event.target).files?.[0] ?? null)" />
        <button class="btn primary" type="button" [disabled]="busy() || !file() || !targetId()" (click)="upload()">{{ i18n.t('Import') }}</button>
      </div>
      @if (error()) { <div class="notice bad">{{ i18n.t(error()) }}</div> }
      @if (result(); as r) {
        <div class="notice good">
          {{ i18n.t('Imported {imported} of {total} rows ({duplicates} already present).', { imported: r.imported, total: r.total, duplicates: r.duplicates }) }}
          @if (r.transfersMarked > 0) { {{ i18n.t('{n} transactions were marked as transfers between your accounts.', { n: r.transfersMarked }) }} }
        </div>
      }
    </div>
  `,
  styles: `
    .panel { margin-bottom: 16px; }
    .wrap { flex-wrap: wrap; }
    .file { flex: 1; min-width: 200px; }
    p { margin: 0 0 12px; }
  `,
})
export class ImportPanel {
  protected readonly finance = inject(FinanceService);
  protected readonly i18n = inject(I18n);

  readonly accountId = input<number | null>(null);
  readonly imported = output<ImportResult>();
  readonly closed = output<void>();

  protected readonly targetId = signal<number>(0);
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly result = signal<ImportResult | null>(null);

  ngOnInit() {
    this.targetId.set(this.accountId() ?? this.finance.accounts()[0]?.id ?? 0);
  }

  protected async upload() {
    const file = this.file();
    if (!file) return;
    this.busy.set(true);
    this.error.set('');
    this.result.set(null);
    try {
      const result = await this.finance.importFile(this.targetId(), file);
      this.result.set(result);
      this.imported.emit(result);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.busy.set(false);
    }
  }
}
