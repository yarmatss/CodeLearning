import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DialogService } from '../../../core/services/dialog.service';

@Component({
  selector: 'app-dialog',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (dialogService.isOpen()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50">
        <div class="max-w-md rounded-lg bg-white p-6 shadow-xl">
          <h2 class="mb-4 text-xl font-bold text-gray-900">
            {{ dialogService.config()?.title }}
          </h2>
          <p class="mb-6 text-gray-700">
            {{ dialogService.config()?.message }}
          </p>
          <div class="flex justify-end gap-3">
            @if (dialogService.config()?.cancelText) {
              <button
                (click)="dialogService.close(false)"
                class="rounded bg-gray-200 px-4 py-2 text-gray-800 hover:bg-gray-300"
              >
                {{ dialogService.config()?.cancelText }}
              </button>
            }
            <button
              (click)="dialogService.close(true)"
              [class]="getButtonClass()"
            >
              {{ dialogService.config()?.confirmText || 'OK' }}
            </button>
          </div>
        </div>
      </div>
    }
  `
})
export class DialogComponent {
  readonly dialogService = inject(DialogService);

  getButtonClass(): string {
    const type = this.dialogService.config()?.type || 'info';
    const baseClass = 'rounded px-4 py-2 text-white ';
    
    switch (type) {
      case 'error':
        return baseClass + 'bg-red-600 hover:bg-red-700';
      case 'warning':
        return baseClass + 'bg-yellow-600 hover:bg-yellow-700';
      case 'success':
        return baseClass + 'bg-green-600 hover:bg-green-700';
      default:
        return baseClass + 'bg-blue-600 hover:bg-blue-700';
    }
  }
}
