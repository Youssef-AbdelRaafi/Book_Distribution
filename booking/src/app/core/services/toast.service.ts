import { Injectable, signal } from '@angular/core';

export interface Toast {
  message: string;
  type: 'success' | 'error' | 'info';
  id: number;
}

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  toasts = signal<Toast[]>([]);
  private idCounter = 0;
  private timeouts = new Map<number, ReturnType<typeof setTimeout>>();

  show(message: string, type: 'success' | 'error' | 'info' = 'info') {
    const id = ++this.idCounter;
    this.toasts.update(t => [...t, { message, type, id }]);
    this.timeouts.set(id, setTimeout(() => this.remove(id), 3000));
  }

  remove(id: number) {
    const timeout = this.timeouts.get(id);
    if (timeout) { clearTimeout(timeout); this.timeouts.delete(id); }
    this.toasts.update(t => t.filter(toast => toast.id !== id));
  }
}
