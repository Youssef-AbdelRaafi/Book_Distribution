import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.html'
})
export class LoginComponent {
  authService = inject(AuthService);
  settingsService = inject(SettingsService);
  readonly assetUrls = ASSET_URLS;

  username = '';
  password = '';

  onSubmit() {
    this.authService.login(this.username, this.password);
  }
}
