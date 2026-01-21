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

@Injectable({
  providedIn: 'root'
})
export class DialogService {
  readonly isOpen = signal(false);
  readonly config = signal<DialogConfig | null>(null);
  private resolveCallback: ((result: DialogResult) => void) | null = null;

  confirm(config: DialogConfig): Promise<DialogResult> {
    this.config.set(config);
    this.isOpen.set(true);

    return new Promise<DialogResult>((resolve) => {
      this.resolveCallback = resolve;
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
    this.isOpen.set(false);
    if (this.resolveCallback) {
      this.resolveCallback({ confirmed });
      this.resolveCallback = null;
    }
    this.config.set(null);
  }
}
