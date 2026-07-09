import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { HeaderComponent } from './layout/header/header';
import { ToastService } from './core/services/toast.service';
import { filter } from 'rxjs/operators';
import { SettingsService } from './core/services/settings.service';
import { AuthService } from './core/services/auth.service';
import { AppDataService } from './core/services/app-data.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, HeaderComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('booking');
  public toastService = inject(ToastService);
  private router = inject(Router);
  private settingsService = inject(SettingsService);
  private authService = inject(AuthService);
  private appData = inject(AppDataService);

  isLoginPage = signal(false);
  isDarkMode = signal(false);

  constructor() {
    if (this.authService.isAuthenticated()) {
      this.appData.loadAuthenticatedData();
    }

    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      this.isLoginPage.set(event.urlAfterRedirects.includes('/login'));
    });
  }
}
