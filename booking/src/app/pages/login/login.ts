import { Component, inject, signal, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';
import { ToastService } from '../../core/services/toast.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.html'
})
export class LoginComponent {
  authService = inject(AuthService);
  settingsService = inject(SettingsService);
  toast = inject(ToastService);
  readonly assetUrls = ASSET_URLS;

  username = '';
  password = '';
  loading = signal(false);
  private destroyRef = inject(DestroyRef);

  onSubmit() {
    if (this.loading()) return;
    this.loading.set(true);
    this.authService.login(this.username, this.password).pipe(
      finalize(() => this.loading.set(false)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (response) => this.authService.handleLoginResponse(response),
      error: (error) => {
        const message = error?.error?.message || 'اسم المستخدم أو كلمة المرور غير صحيحة';
        this.toast.show(message, 'error');
      }
    });
  }

  loginAsGuest() {
    this.username = 'guest';
    this.password = 'guest99999';
    this.onSubmit();
  }
}
