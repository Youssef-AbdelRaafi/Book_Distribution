import { Injectable, signal } from '@angular/core';
import { LS_DARK_MODE } from '../constants/local-storage-keys';

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  isDarkMode = signal(localStorage.getItem(LS_DARK_MODE) === 'true');

  constructor() {
    if (this.isDarkMode()) {
      document.documentElement.classList.add('dark');
    }
  }

  toggleDarkMode() {
    const newVal = !this.isDarkMode();
    this.isDarkMode.set(newVal);
    localStorage.setItem(LS_DARK_MODE, String(newVal));
    if (newVal) {
      document.documentElement.classList.add('dark');
    } else {
      document.documentElement.classList.remove('dark');
    }
  }
}
