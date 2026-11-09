import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/** Previous/next pager for a `PagedResult`. Hidden when everything fits on one page. */
@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (totalPages() > 1) {
      <nav class="mt-6 flex items-center justify-between gap-4" aria-label="Pagination">
        <button
          type="button"
          (click)="go(page() - 1)"
          [disabled]="!hasPrevious() || disabled()"
          class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          ← Previous
        </button>
        <span class="text-sm text-gray-600">
          Page {{ page() }} of {{ totalPages() }} · {{ totalCount() }} total
        </span>
        <button
          type="button"
          (click)="go(page() + 1)"
          [disabled]="!hasNext() || disabled()"
          class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Next →
        </button>
      </nav>
    }
  `
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly totalCount = input<number>(0);
  readonly disabled = input<boolean>(false);

  readonly pageChange = output<number>();

  readonly hasPrevious = computed(() => this.page() > 1);
  readonly hasNext = computed(() => this.page() < this.totalPages());

  go(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.page()) {
      return;
    }
    this.pageChange.emit(page);
  }
}
