import type {
  AuthResponse,
  CalculateRecipeRequest,
  CalculateRecipeResponse,
  CreateRecipeRequest,
  IngredientDto,
  IngredientType,
  RecipeDetailDto,
  RecipeSummaryDto,
  UserDto
} from '$lib/types/api';

const API_BASE =
  typeof window !== 'undefined'
    ? (window as any).__API_URL__ || 'http://localhost:5000'
    : process.env.services__apiservice__http__0 ||
      process.env.services__apiservice__https__0 ||
      'http://localhost:5000';

let authToken: string | null = null;

export function setClientAuthToken(token: string | null) {
  authToken = token;
}

export function getClientAuthToken(): string | null {
  return authToken;
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers || {});
  headers.set('Content-Type', 'application/json');

  if (authToken) {
    headers.set('Authorization', `Bearer ${authToken}`);
  }

  const res = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers,
    credentials: 'include'
  });

  if (!res.ok) {
    let errorMessage = `HTTP error ${res.status}`;
    try {
      const errorJson = await res.json();
      errorMessage = errorJson.message || errorJson.title || errorMessage;
    } catch {
      // Non-JSON response
    }
    throw new Error(errorMessage);
  }

  if (res.status === 204) {
    return null as unknown as T;
  }

  return res.json();
}

export const api = {
  auth: {
    async register(data: { email: string; password: string; displayName: string }): Promise<AuthResponse> {
      return request<AuthResponse>('/api/v1/auth/register', {
        method: 'POST',
        body: JSON.stringify(data)
      });
    },

    async login(data: { email: string; password: string }): Promise<AuthResponse> {
      return request<AuthResponse>('/api/v1/auth/login', {
        method: 'POST',
        body: JSON.stringify(data)
      });
    },

    async refresh(refreshToken?: string): Promise<AuthResponse> {
      return request<AuthResponse>('/api/v1/auth/refresh', {
        method: 'POST',
        body: JSON.stringify({ refreshToken })
      });
    },

    async me(): Promise<UserDto> {
      return request<UserDto>('/api/v1/auth/me');
    },

    async logout(): Promise<void> {
      return request<void>('/api/v1/auth/logout', { method: 'POST' });
    }
  },

  ingredients: {
    async list(type?: IngredientType, search?: string): Promise<IngredientDto[]> {
      const params = new URLSearchParams();
      if (type) params.set('type', type);
      if (search) params.set('search', search);
      const query = params.toString() ? `?${params.toString()}` : '';
      return request<IngredientDto[]>(`/api/v1/ingredients${query}`);
    },

    async getById(id: string): Promise<IngredientDto> {
      return request<IngredientDto>(`/api/v1/ingredients/${id}`);
    },

    async create(data: Partial<IngredientDto>): Promise<IngredientDto> {
      return request<IngredientDto>('/api/v1/ingredients', {
        method: 'POST',
        body: JSON.stringify(data)
      });
    }
  },

  recipes: {
    async calculate(req: CalculateRecipeRequest): Promise<CalculateRecipeResponse> {
      return request<CalculateRecipeResponse>('/api/v1/recipes/calculate', {
        method: 'POST',
        body: JSON.stringify(req)
      });
    },

    async list(): Promise<RecipeSummaryDto[]> {
      return request<RecipeSummaryDto[]>('/api/v1/recipes');
    },

    async getById(id: string): Promise<RecipeDetailDto> {
      return request<RecipeDetailDto>(`/api/v1/recipes/${id}`);
    },

    async create(req: CreateRecipeRequest): Promise<RecipeDetailDto> {
      return request<RecipeDetailDto>('/api/v1/recipes', {
        method: 'POST',
        body: JSON.stringify(req)
      });
    },

    async delete(id: string): Promise<void> {
      return request<void>(`/api/v1/recipes/${id}`, { method: 'DELETE' });
    }
  }
};
