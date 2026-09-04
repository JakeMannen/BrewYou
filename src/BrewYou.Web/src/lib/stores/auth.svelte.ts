import { api, setClientAuthToken } from '$lib/api/client';
import type { UserDto } from '$lib/types/api';

class AuthState {
  user = $state<UserDto | null>(null);
  token = $state<string | null>(null);
  isLoading = $state<boolean>(true);
  error = $state<string | null>(null);

  get isAuthenticated(): boolean {
    return this.user !== null;
  }

  async init() {
    this.isLoading = true;
    this.error = null;

    if (typeof window !== 'undefined') {
      const savedToken = localStorage.getItem('brewyou_token');
      if (savedToken) {
        this.token = savedToken;
        setClientAuthToken(savedToken);
        try {
          this.user = await api.auth.me();
          this.isLoading = false;
          return;
        } catch {
          // Token expired or invalid, try refresh
        }
      }

      // Try cookie-based refresh
      try {
        const authData = await api.auth.refresh();
        this.setAuth(authData.accessToken, authData.user);
      } catch {
        this.clearAuth();
      }
    }

    this.isLoading = false;
  }

  async login(email: string, password: string): Promise<boolean> {
    this.isLoading = true;
    this.error = null;
    try {
      const data = await api.auth.login({ email, password });
      this.setAuth(data.accessToken, data.user);
      return true;
    } catch (err: any) {
      this.error = err.message || 'Login failed. Please check your credentials.';
      return false;
    } finally {
      this.isLoading = false;
    }
  }

  async register(email: string, password: string, displayName: string): Promise<boolean> {
    this.isLoading = true;
    this.error = null;
    try {
      const data = await api.auth.register({ email, password, displayName });
      this.setAuth(data.accessToken, data.user);
      return true;
    } catch (err: any) {
      this.error = err.message || 'Registration failed.';
      return false;
    } finally {
      this.isLoading = false;
    }
  }

  async logout() {
    try {
      await api.auth.logout();
    } catch {
      // Ignore network errors on logout
    } finally {
      this.clearAuth();
    }
  }

  private setAuth(token: string, user: UserDto) {
    this.token = token;
    this.user = user;
    setClientAuthToken(token);
    if (typeof window !== 'undefined') {
      localStorage.setItem('brewyou_token', token);
    }
  }

  private clearAuth() {
    this.token = null;
    this.user = null;
    setClientAuthToken(null);
    if (typeof window !== 'undefined') {
      localStorage.removeItem('brewyou_token');
    }
  }
}

export const auth = new AuthState();
