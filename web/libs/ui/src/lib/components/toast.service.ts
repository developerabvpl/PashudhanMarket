import { Injectable, signal } from '@angular/core';

export type ToastTone = 'error' | 'success' | 'info';

export interface Toast {
  readonly id: number;
  readonly tone: ToastTone;
  /** i18n key or an already-translated string; the host renders it through translate. */
  readonly message: string;
  /** Optional detail, e.g. an API error code shown small under the message. */
  readonly detail?: string;
}

const DEFAULT_TIMEOUT_MS = 6000;

/**
 * Application-wide notifications. Signal-based so the host component re-renders without a
 * subscription, and so tests can assert on `toasts()` without faking time.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly items = signal<readonly Toast[]>([]);

  readonly toasts = this.items.asReadonly();

  error(message: string, detail?: string): number {
    return this.show('error', message, detail);
  }

  success(message: string, detail?: string): number {
    return this.show('success', message, detail);
  }

  info(message: string, detail?: string): number {
    return this.show('info', message, detail);
  }

  dismiss(id: number): void {
    this.items.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  clear(): void {
    this.items.set([]);
  }

  private show(tone: ToastTone, message: string, detail?: string): number {
    const id = this.nextId++;

    this.items.update((toasts) => [...toasts, { id, tone, message, detail }]);

    // No timer during SSR: it would keep the server render alive with nothing to show it to.
    if (typeof window !== 'undefined') {
      setTimeout(() => this.dismiss(id), DEFAULT_TIMEOUT_MS);
    }

    return id;
  }
}
