import { Component, signal, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, Router, NavigationEnd } from '@angular/router';
import { HeaderComponent } from './layout/header/header';
import { ToastService } from './core/services/toast.service';
import { ConfirmService } from './core/services/confirm.service';
import { filter } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SettingsService } from './core/services/settings.service';
import { AuthService } from './core/services/auth.service';
import { AppDataService } from './core/services/app-data.service';
import { ThemeService } from './core/services/theme.service';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, RouterOutlet, HeaderComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  trackById = (i: number, item: any) => item?.id ?? i;
  public toastService = inject(ToastService);
  public confirmService = inject(ConfirmService);
  private router = inject(Router);
  private settingsService = inject(SettingsService);
  private authService = inject(AuthService);
  private appData = inject(AppDataService);
  private themeService = inject(ThemeService);

  isLoginPage = signal(false);
  isDarkMode = this.themeService.isDarkMode;

  private destroyRef = inject(DestroyRef);

  constructor() {
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((event: NavigationEnd) => {
      this.isLoginPage.set(event.urlAfterRedirects.includes('/login'));
    });
  }
}
