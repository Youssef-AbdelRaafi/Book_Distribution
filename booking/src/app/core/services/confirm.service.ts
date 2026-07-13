import { Injectable, signal } from '@angular/core';
import { Observable } from 'rxjs';

export interface ConfirmState {
  message: string;
  resolve: (result: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  state = signal<ConfirmState | null>(null);

  confirm(message: string): Observable<boolean> {
    return new Observable<boolean>(observer => {
      const resolve = (result: boolean) => {
        cleanup();
        observer.next(result);
        observer.complete();
        this.state.set(null);
      };
      this.state.set({ message, resolve });

      const onKeyDown = (e: KeyboardEvent) => {
        if (e.key === 'Escape') resolve(false);
        if (e.key === 'Enter') resolve(true);
      };
      document.addEventListener('keydown', onKeyDown);

      const timeoutId = setTimeout(() => resolve(false), 30000);

      const cleanup = () => {
        document.removeEventListener('keydown', onKeyDown);
        clearTimeout(timeoutId);
      };
    });
  }
}
