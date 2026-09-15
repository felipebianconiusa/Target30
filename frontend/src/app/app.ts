import { Component, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { GoogleLoginButton } from './auth/google-login-button/google-login-button';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, GoogleLoginButton],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  constructor(protected readonly authService: AuthService) {}

  ngOnInit(): void {
    this.authService.checkSession();
  }
}
