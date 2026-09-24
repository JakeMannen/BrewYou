import type {
	ApiErrorDetail,
	ApiResponse,
	AuthResponse,
	BrewerySetupDto,
	CalculateRecipeRequest,
	CalculateRecipeResponse,
	CreateBrewerySetupRequest,
	CreateEquipmentRequest,
	CreateIngredientRequest,
	UpdateIngredientStockRequest,
	CreateRecipeRequest,
	UpdateRecipeRequest,
	EquipmentDto,
	EquipmentTelemetryUpdateDto,
	EquipmentActiveBatchDto,
	EquipmentReadingDto,
	TelemetryPollResult,
	EquipmentType,
	GoogleAuthRequest,
	IngredientDto,
	IngredientType,
	IngredientUsageDto,
	LoginRequest,
	MqttStatusResponse,
	RecipeDetailDto,
	RecipeNameCheckResponse,
	RefreshTokenRequest,
	RegisterRequest,
	RecipeScope,
	RecipeSummaryDto,
	UpdateBrewerySetupRequest,
	UpdateEquipmentRequest,
	UpdateProfileRequest,
	UpdateUserPreferencesRequest,
	UserDto,
	VolumeUnit,
	BatchSummaryDto,
	BatchDetailDto,
	BatchReadingDto,
	BatchIngredientDto,
	BatchMashStepDto,
	ToggleBatchMashStepRequest,
	BatchFermentationStepDto,
	ToggleBatchFermentationStepRequest,
	CreateBatchRequest,
	AdvanceBatchStageRequest,
	AddBatchReadingRequest,
	UpdateBatchRequest,
	NextBatchCodeResponse,
	BatchListQuery,
	CalculateWaterVolumeRequest,
	CalculateWaterVolumeResponse,
	BatchSensorAssignmentDto,
	BatchSensorAssignmentInput,
	BatchEquipmentReadingDto,
	LogBatchTemperatureRequest,
	CheckBatchStockRequest,
	BatchStockCheckResult
} from '$lib/types/api';

interface CustomWindow extends Window {
	__API_URL__?: string;
}

const API_BASE =
	typeof window !== 'undefined'
		? (window as unknown as CustomWindow).__API_URL__ || 'http://localhost:5000'
		: process.env.services__apiservice__http__0 ||
			process.env.services__apiservice__https__0 ||
			'http://localhost:5000';

export class ApiClientError extends Error {
	constructor(
		message: string,
		public status: number,
		public code?: string,
		public details?: ApiErrorDetail[] | null
	) {
		super(message);
		this.name = 'ApiClientError';
	}
}

let authToken: string | null =
	typeof window !== 'undefined' ? localStorage.getItem('brewyou_token') : null;

export function setClientAuthToken(token: string | null) {
	authToken = token;
}

export function getClientAuthToken(): string | null {
	return authToken;
}

type UnauthorizedHandler = () => void;
let unauthorizedHandler: UnauthorizedHandler | null = null;

export function onUnauthorized(handler: UnauthorizedHandler | null) {
	unauthorizedHandler = handler;
}

type TokenRefreshedHandler = (data: AuthResponse) => void;
let tokenRefreshedHandler: TokenRefreshedHandler | null = null;

export function onTokenRefreshed(handler: TokenRefreshedHandler | null) {
	tokenRefreshedHandler = handler;
}

let refreshPromise: Promise<string | null> | null = null;

async function attemptTokenRefresh(): Promise<string | null> {
	if (refreshPromise) {
		return refreshPromise;
	}

	refreshPromise = (async () => {
		try {
			const res = await fetch(`${API_BASE}/api/v1/auth/refresh`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				credentials: 'include',
				body: JSON.stringify({})
			});

			if (!res.ok) {
				return null;
			}

			let body: Record<string, unknown> | null = null;
			try {
				body = (await res.json()) as Record<string, unknown>;
			} catch {
				return null;
			}

			const envelope = body as unknown as ApiResponse<AuthResponse> | null;
			const data = (
				envelope && 'data' in envelope ? envelope.data : (body as unknown)
			) as AuthResponse | null;

			if (data?.accessToken) {
				setClientAuthToken(data.accessToken);
				if (typeof window !== 'undefined') {
					localStorage.setItem('brewyou_token', data.accessToken);
				}
				tokenRefreshedHandler?.(data);
				return data.accessToken;
			}

			return null;
		} catch {
			return null;
		} finally {
			refreshPromise = null;
		}
	})();

	return refreshPromise;
}

async function request<T>(path: string, options: RequestInit = {}, isRetry = false): Promise<T> {
	// Pre-emptive waiting: If a silent refresh is already in flight, wait for it before dispatching
	if (refreshPromise && !path.startsWith('/api/v1/auth/')) {
		await refreshPromise;
	}

	const headers = new Headers(options.headers || {});
	headers.set('Content-Type', 'application/json');

	const token =
		authToken ?? (typeof window !== 'undefined' ? localStorage.getItem('brewyou_token') : null);
	if (token) {
		headers.set('Authorization', `Bearer ${token}`);
	}

	const res = await fetch(`${API_BASE}${path}`, {
		...options,
		headers,
		credentials: 'include'
	});

	if (res.status === 204) {
		return null as unknown as T;
	}

	let body: Record<string, unknown> | null = null;
	try {
		body = (await res.json()) as Record<string, unknown>;
	} catch {
		// Non-JSON response
	}

	if (!res.ok) {
		let errorMessage = `HTTP error ${res.status}`;
		let errorCode: string | undefined;
		let errorDetails: ApiErrorDetail[] | null | undefined;
		if (body) {
			const errorObj = body.error as
				{ code?: string; message?: string; details?: ApiErrorDetail[] | null } | undefined;
			if (errorObj?.message) {
				errorMessage = errorObj.message;
				errorCode = errorObj.code;
				errorDetails = errorObj.details;
			} else if (typeof body.message === 'string') {
				errorMessage = body.message;
			} else if (typeof body.title === 'string') {
				errorMessage = body.title;
			}
		}

		if (res.status === 401) {
			// Do not attempt refresh on auth endpoints (/login, /register, /google, /refresh)
			if (path.startsWith('/api/v1/auth/')) {
				if (path.startsWith('/api/v1/auth/refresh')) {
					if (token) {
						unauthorizedHandler?.();
					}
				}
				throw new ApiClientError(errorMessage, res.status, errorCode, errorDetails);
			}

			// If this was already a retry attempt, do not retry again to prevent infinite loops
			if (isRetry) {
				if (token) {
					unauthorizedHandler?.();
				}
				throw new ApiClientError(errorMessage, res.status, errorCode, errorDetails);
			}

			// Attempt silent token refresh
			const newToken = await attemptTokenRefresh();
			if (newToken) {
				const retryHeaders = new Headers(options.headers || {});
				retryHeaders.set('Content-Type', 'application/json');
				retryHeaders.set('Authorization', `Bearer ${newToken}`);
				return request<T>(path, { ...options, headers: retryHeaders }, true);
			}

			// Refresh failed (token expired, revoked, or absent)
			if (token) {
				unauthorizedHandler?.();
			}
		}

		throw new ApiClientError(errorMessage, res.status, errorCode, errorDetails);
	}

	// If response is wrapped in standard uniform envelope { success, data, error }
	if (body && typeof body === 'object' && 'success' in body) {
		const envelope = body as unknown as ApiResponse<T>;
		if (!envelope.success) {
			throw new ApiClientError(
				envelope.error?.message || 'Operation failed.',
				res.status,
				envelope.error?.code,
				envelope.error?.details
			);
		}
		return envelope.data as T;
	}

	return body as unknown as T;
}

export const api = {
	auth: {
		async register(data: RegisterRequest): Promise<AuthResponse> {
			return request<AuthResponse>('/api/v1/auth/register', {
				method: 'POST',
				body: JSON.stringify(data)
			});
		},

		async login(data: LoginRequest): Promise<AuthResponse> {
			return request<AuthResponse>('/api/v1/auth/login', {
				method: 'POST',
				body: JSON.stringify(data)
			});
		},

		async google(data: GoogleAuthRequest): Promise<AuthResponse> {
			return request<AuthResponse>('/api/v1/auth/google', {
				method: 'POST',
				body: JSON.stringify(data)
			});
		},

		async refresh(refreshToken?: string | RefreshTokenRequest): Promise<AuthResponse> {
			const body = typeof refreshToken === 'string' ? { refreshToken } : refreshToken || {};
			return request<AuthResponse>('/api/v1/auth/refresh', {
				method: 'POST',
				body: JSON.stringify(body)
			});
		},

		async me(): Promise<UserDto> {
			return request<UserDto>('/api/v1/auth/me');
		},

		async updateLanguage(language: string): Promise<UserDto> {
			return request<UserDto>('/api/v1/auth/me/language', {
				method: 'PUT',
				body: JSON.stringify({ language })
			});
		},

		async updateVolumeUnit(volumeUnit: VolumeUnit): Promise<UserDto> {
			return request<UserDto>('/api/v1/auth/me/volume-unit', {
				method: 'PUT',
				body: JSON.stringify({ volumeUnit })
			});
		},

		async updatePreferences(req: UpdateUserPreferencesRequest): Promise<UserDto> {
			return request<UserDto>('/api/v1/auth/me/preferences', {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async getMqttStatus(host?: string, port?: number): Promise<MqttStatusResponse> {
			const params = new URLSearchParams();
			if (host) params.set('host', host);
			if (port != null) params.set('port', port.toString());
			const query = params.toString() ? `?${params.toString()}` : '';
			return request<MqttStatusResponse>(`/api/v1/auth/me/mqtt-status${query}`);
		},

		async updateProfile(req: UpdateProfileRequest): Promise<UserDto> {
			return request<UserDto>('/api/v1/auth/me/profile', {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async logout(): Promise<void> {
			return request<void>('/api/v1/auth/logout', { method: 'POST' });
		}
	},

	ingredients: {
		async list(
			type?: IngredientType,
			search?: string,
			page?: number,
			limit = 200,
			inStock?: boolean
		): Promise<IngredientDto[]> {
			const params = new URLSearchParams();
			if (type) params.set('type', type);
			if (search) params.set('search', search);
			if (page) params.set('page', page.toString());
			if (limit) params.set('limit', limit.toString());
			if (inStock !== undefined) params.set('inStock', inStock.toString());
			const query = params.toString() ? `?${params.toString()}` : '';
			return request<IngredientDto[]>(`/api/v1/ingredients${query}`);
		},

		async getById(id: string): Promise<IngredientDto> {
			return request<IngredientDto>(`/api/v1/ingredients/${id}`);
		},

		async create(data: CreateIngredientRequest): Promise<IngredientDto> {
			return request<IngredientDto>('/api/v1/ingredients', {
				method: 'POST',
				body: JSON.stringify(data)
			});
		},

		async updateStock(id: string, data: UpdateIngredientStockRequest): Promise<IngredientDto> {
			return request<IngredientDto>(`/api/v1/ingredients/${id}/stock`, {
				method: 'PUT',
				body: JSON.stringify(data)
			});
		},

		async getUsage(id: string): Promise<IngredientUsageDto> {
			return request<IngredientUsageDto>(`/api/v1/ingredients/${id}/usage`);
		},

		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/ingredients/${id}`, { method: 'DELETE' });
		}
	},

	equipment: {
		async list(
			type?: EquipmentType,
			search?: string,
			page?: number,
			limit?: number,
			setupId?: string
		): Promise<EquipmentDto[]> {
			const params = new URLSearchParams();
			if (type) params.set('type', type);
			if (search) params.set('search', search);
			if (page) params.set('page', page.toString());
			if (limit) params.set('limit', limit.toString());
			if (setupId) params.set('setupId', setupId);
			const query = params.toString() ? `?${params.toString()}` : '';
			return request<EquipmentDto[]>(`/api/v1/inventory/equipment${query}`);
		},

		async getById(id: string): Promise<EquipmentDto> {
			return request<EquipmentDto>(`/api/v1/inventory/equipment/${id}`);
		},

		async create(req: CreateEquipmentRequest): Promise<EquipmentDto> {
			return request<EquipmentDto>('/api/v1/inventory/equipment', {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async update(id: string, req: UpdateEquipmentRequest): Promise<EquipmentDto> {
			return request<EquipmentDto>(`/api/v1/inventory/equipment/${id}`, {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/inventory/equipment/${id}`, { method: 'DELETE' });
		},

		async getActiveBatches(id: string): Promise<EquipmentActiveBatchDto[]> {
			return request<EquipmentActiveBatchDto[]>(`/api/v1/inventory/equipment/${id}/active-batches`);
		},

		async regenerateToken(id: string): Promise<EquipmentDto> {
			return request<EquipmentDto>(`/api/v1/inventory/equipment/${id}/regenerate-token`, {
				method: 'POST'
			});
		},

		async getReadings(id: string, limit?: number): Promise<EquipmentReadingDto[]> {
			const query = limit ? `?limit=${limit}` : '';
			return request<EquipmentReadingDto[]>(`/api/v1/inventory/equipment/${id}/readings${query}`);
		},

		async testPoll(id: string): Promise<TelemetryPollResult> {
			return request<TelemetryPollResult>(`/api/v1/inventory/equipment/${id}/test-poll`, {
				method: 'POST'
			});
		},

		async streamTelemetry(options: {
			onReading: (reading: EquipmentTelemetryUpdateDto) => void;
			onError?: (err: Error) => void;
			signal?: AbortSignal;
		}): Promise<void> {
			const url = `${API_BASE}/api/v1/inventory/equipment/telemetry-stream`;

			const headers: Record<string, string> = {
				Accept: 'text/event-stream'
			};
			const token =
				authToken ?? (typeof window !== 'undefined' ? localStorage.getItem('brewyou_token') : null);
			if (token) {
				headers.Authorization = `Bearer ${token}`;
			}

			try {
				const response = await fetch(url, {
					headers,
					signal: options.signal
				});

				if (!response.ok) {
					throw new ApiClientError(
						`Failed to connect to equipment telemetry stream: ${response.statusText}`,
						response.status
					);
				}

				if (!response.body) {
					throw new Error('ReadableStream not supported on response.');
				}

				const reader = response.body.getReader();
				const decoder = new TextDecoder();
				let buffer = '';

				while (!options.signal?.aborted) {
					const { done, value } = await reader.read();
					if (done) break;

					buffer += decoder.decode(value, { stream: true });
					const lines = buffer.split('\n');
					buffer = lines.pop() ?? '';

					let currentEvent = 'message';
					let currentData = '';

					for (const line of lines) {
						if (line.startsWith('event:')) {
							currentEvent = line.slice(6).trim();
						} else if (line.startsWith('data:')) {
							currentData += line.slice(5).trim();
						} else if (line === '' && currentData) {
							if (currentEvent === 'reading') {
								try {
									const parsed: EquipmentTelemetryUpdateDto = JSON.parse(currentData);
									options.onReading(parsed);
								} catch (parseErr) {
									console.error('Failed to parse equipment telemetry reading SSE JSON', parseErr);
								}
							}
							currentEvent = 'message';
							currentData = '';
						}
					}
				}
			} catch (err: unknown) {
				if (options.signal?.aborted) return;
				options.onError?.(err instanceof Error ? err : new Error(String(err)));
			}
		}
	},

	brewerySetups: {
		async list(): Promise<BrewerySetupDto[]> {
			return request<BrewerySetupDto[]>('/api/v1/brewery-setups');
		},

		async getById(id: string): Promise<BrewerySetupDto> {
			return request<BrewerySetupDto>(`/api/v1/brewery-setups/${id}`);
		},

		async create(req: CreateBrewerySetupRequest): Promise<BrewerySetupDto> {
			return request<BrewerySetupDto>('/api/v1/brewery-setups', {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async update(id: string, req: UpdateBrewerySetupRequest): Promise<BrewerySetupDto> {
			return request<BrewerySetupDto>(`/api/v1/brewery-setups/${id}`, {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/brewery-setups/${id}`, { method: 'DELETE' });
		},

		async setDefault(id: string): Promise<BrewerySetupDto> {
			return request<BrewerySetupDto>(`/api/v1/brewery-setups/${id}/set-default`, {
				method: 'POST'
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

		async calculateWater(req: CalculateWaterVolumeRequest): Promise<CalculateWaterVolumeResponse> {
			return request<CalculateWaterVolumeResponse>('/api/v1/recipes/calculate-water', {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async list(page?: number, limit?: number, scope?: RecipeScope): Promise<RecipeSummaryDto[]> {
			const params = new URLSearchParams();
			if (page) params.set('page', page.toString());
			if (limit) params.set('limit', limit.toString());
			if (scope && scope !== 'all') params.set('scope', scope);
			const query = params.toString() ? `?${params.toString()}` : '';
			return request<RecipeSummaryDto[]>(`/api/v1/recipes${query}`);
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

		async update(id: string, req: UpdateRecipeRequest): Promise<RecipeDetailDto> {
			return request<RecipeDetailDto>(`/api/v1/recipes/${id}`, {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/recipes/${id}`, { method: 'DELETE' });
		},

		async checkName(name: string): Promise<boolean> {
			const params = new URLSearchParams();
			params.set('name', name);
			const res = await request<RecipeNameCheckResponse>(
				`/api/v1/recipes/check-name?${params.toString()}`
			);
			return res.exists;
		}
	},

	batches: {
		async list(query?: BatchListQuery): Promise<BatchSummaryDto[]> {
			const params = new URLSearchParams();
			if (query?.status) params.set('status', query.status);
			if (query?.search) params.set('search', query.search);
			if (query?.page) params.set('page', query.page.toString());
			if (query?.limit) params.set('limit', query.limit.toString());
			const qs = params.toString() ? `?${params.toString()}` : '';
			return request<BatchSummaryDto[]>(`/api/v1/batches${qs}`);
		},

		async getNextCode(date?: string): Promise<NextBatchCodeResponse> {
			const params = new URLSearchParams();
			if (date) params.set('date', date);
			const qs = params.toString() ? `?${params.toString()}` : '';
			return request<NextBatchCodeResponse>(`/api/v1/batches/next-code${qs}`);
		},

		async getById(id: string): Promise<BatchDetailDto> {
			return request<BatchDetailDto>(`/api/v1/batches/${id}`);
		},

		async create(req: CreateBatchRequest): Promise<BatchDetailDto> {
			return request<BatchDetailDto>('/api/v1/batches', {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async advanceStage(id: string, req: AdvanceBatchStageRequest): Promise<BatchDetailDto> {
			return request<BatchDetailDto>(`/api/v1/batches/${id}/stage`, {
				method: 'PATCH',
				body: JSON.stringify(req)
			});
		},

		async checkStock(req: CheckBatchStockRequest): Promise<BatchStockCheckResult> {
			return request<BatchStockCheckResult>('/api/v1/batches/check-stock', {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async addReading(id: string, req: AddBatchReadingRequest): Promise<BatchReadingDto> {
			return request<BatchReadingDto>(`/api/v1/batches/${id}/readings`, {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async toggleIngredient(
			id: string,
			ingredientId: string,
			isChecked: boolean
		): Promise<BatchIngredientDto> {
			return request<BatchIngredientDto>(
				`/api/v1/batches/${id}/ingredients/${ingredientId}?isChecked=${isChecked}`,
				{
					method: 'PATCH'
				}
			);
		},

		async toggleMashStep(
			id: string,
			stepId: string,
			req: ToggleBatchMashStepRequest
		): Promise<BatchMashStepDto> {
			return request<BatchMashStepDto>(`/api/v1/batches/${id}/mash-steps/${stepId}`, {
				method: 'PATCH',
				body: JSON.stringify(req)
			});
		},

		async toggleFermentationStep(
			id: string,
			stepId: string,
			req: ToggleBatchFermentationStepRequest
		): Promise<BatchFermentationStepDto> {
			return request<BatchFermentationStepDto>(
				`/api/v1/batches/${id}/fermentation-steps/${stepId}`,
				{
					method: 'PATCH',
					body: JSON.stringify(req)
				}
			);
		},

		async update(id: string, req: UpdateBatchRequest): Promise<BatchDetailDto> {
			return request<BatchDetailDto>(`/api/v1/batches/${id}`, {
				method: 'PUT',
				body: JSON.stringify(req)
			});
		},

		async delete(id: string): Promise<void> {
			return request<void>(`/api/v1/batches/${id}`, { method: 'DELETE' });
		},

		async getEquipmentReadings(
			id: string,
			params?: { stage?: string; stepId?: string }
		): Promise<BatchEquipmentReadingDto[]> {
			const query = new URLSearchParams();
			if (params?.stage) query.set('stage', params.stage);
			if (params?.stepId) query.set('stepId', params.stepId);
			const qs = query.toString() ? `?${query.toString()}` : '';
			return request<BatchEquipmentReadingDto[]>(`/api/v1/batches/${id}/equipment-readings${qs}`);
		},

		async updateSensors(
			id: string,
			sensorInputs: BatchSensorAssignmentInput[]
		): Promise<BatchSensorAssignmentDto[]> {
			return request<BatchSensorAssignmentDto[]>(`/api/v1/batches/${id}/sensors`, {
				method: 'PUT',
				body: JSON.stringify(sensorInputs)
			});
		},

		async logEquipmentReading(
			id: string,
			req: LogBatchTemperatureRequest
		): Promise<BatchEquipmentReadingDto> {
			return request<BatchEquipmentReadingDto>(`/api/v1/batches/${id}/equipment-readings`, {
				method: 'POST',
				body: JSON.stringify(req)
			});
		},

		async streamEquipmentReadings(
			id: string,
			options: {
				onReading: (reading: BatchEquipmentReadingDto) => void;
				onError?: (err: Error) => void;
				signal?: AbortSignal;
				stage?: string;
			}
		): Promise<void> {
			const query = new URLSearchParams();
			if (options.stage) query.set('stage', options.stage);
			const qs = query.toString() ? `?${query.toString()}` : '';
			const url = `${API_BASE}/api/v1/batches/${id}/telemetry-stream${qs}`;

			const headers: Record<string, string> = {
				Accept: 'text/event-stream'
			};
			if (authToken) {
				headers.Authorization = `Bearer ${authToken}`;
			}

			try {
				const response = await fetch(url, {
					headers,
					signal: options.signal
				});

				if (!response.ok) {
					throw new ApiClientError(
						`Failed to connect to telemetry stream: ${response.statusText}`,
						response.status
					);
				}

				if (!response.body) {
					throw new Error('ReadableStream not supported on response.');
				}

				const reader = response.body.getReader();
				const decoder = new TextDecoder();
				let buffer = '';

				while (!options.signal?.aborted) {
					const { done, value } = await reader.read();
					if (done) break;

					buffer += decoder.decode(value, { stream: true });
					const lines = buffer.split('\n');
					buffer = lines.pop() ?? '';

					let currentEvent = 'message';
					let currentData = '';

					for (const line of lines) {
						if (line.startsWith('event:')) {
							currentEvent = line.slice(6).trim();
						} else if (line.startsWith('data:')) {
							currentData += line.slice(5).trim();
						} else if (line === '' && currentData) {
							if (currentEvent === 'reading') {
								try {
									const parsed: BatchEquipmentReadingDto = JSON.parse(currentData);
									options.onReading(parsed);
								} catch (parseErr) {
									console.error('Failed to parse telemetry reading SSE JSON', parseErr);
								}
							}
							currentEvent = 'message';
							currentData = '';
						}
					}
				}
			} catch (err: unknown) {
				if (options.signal?.aborted) return;
				options.onError?.(err instanceof Error ? err : new Error(String(err)));
			}
		}
	}
};
