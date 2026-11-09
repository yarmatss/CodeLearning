import { Injectable, signal } from '@angular/core';

export interface DialogConfig {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  type?: 'info' | 'warning' | 'error' | 'success';
}

export interface DialogResult {
  confirmed: boolean;
}

interface PendingDialog {
  config: DialogConfig;
  resolve: (result: DialogResult) => void;
}

/**
 * Shows one dialog at a time. A dialog requested while another is open waits in a queue and
 * opens once the current one closes, so no caller's promise is left unresolved.
 */
@Injectable({
  providedIn: 'root'
})
export class DialogService {
  readonly isOpen = signal(false);
  readonly config = signal<DialogConfig | null>(null);

  private readonly queue: PendingDialog[] = [];
  private active: PendingDialog | null = null;

  confirm(config: DialogConfig): Promise<DialogResult> {
    return new Promise<DialogResult>((resolve) => {
      this.queue.push({ config, resolve });
      if (!this.active) {
        this.showNext();
      }
    });
  }

  alert(message: string, title: string = 'Information', type: DialogConfig['type'] = 'info'): Promise<DialogResult> {
    return this.confirm({
      title,
      message,
      confirmText: 'OK',
      type
    });
  }

  close(confirmed: boolean): void {
    const current = this.active;
    this.active = null;
    current?.resolve({ confirmed });
    this.showNext();
  }

  /** Dismisses the open dialog and every queued one, answering each as cancelled. */
  reset(): void {
    const pending = [this.active, ...this.queue.splice(0)];
    this.active = null;
    this.isOpen.set(false);
    this.config.set(null);
    for (const dialog of pending) {
      dialog?.resolve({ confirmed: false });
    }
  }

  private showNext(): void {
    const next = this.queue.shift() ?? null;
    this.active = next;
    this.config.set(next?.config ?? null);
    this.isOpen.set(next !== null);
  }
}
