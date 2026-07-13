import { Routes } from '@angular/router';
import { SinglePageComponent } from './pages/single-page/single-page';
import { LoginComponent } from './pages/login/login';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  { path: '', redirectTo: 'single-page', pathMatch: 'full' },
  { path: 'single-page', component: SinglePageComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: 'login' }
];
