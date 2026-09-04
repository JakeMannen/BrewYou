export type IngredientType = 'Fermentable' | 'Hop' | 'Yeast' | 'Other';

export type IngredientUsage =
  | 'Mash'
  | 'Boil'
  | 'Whirlpool'
  | 'Primary'
  | 'Secondary'
  | 'DryHop'
  | 'Bottling';

export interface IngredientDto {
  id: string;
  name: string;
  type: IngredientType;
  potentialGravity?: number | null;
  colorSrm?: number | null;
  alphaAcidPercent?: number | null;
  attenuationPercent?: number | null;
  description?: string | null;
  isCatalogItem: boolean;
}

export interface RecipeIngredientInputDto {
  ingredientId: string;
  amount: number;
  unit: string;
  durationMinutes?: number | null;
  usage: IngredientUsage;
  notes?: string | null;
}

export interface RecipeIngredientOutputDto {
  id: string;
  ingredientId: string;
  ingredientName: string;
  ingredientType: IngredientType;
  amount: number;
  unit: string;
  durationMinutes?: number | null;
  usage: IngredientUsage;
  notes?: string | null;
}

export interface CalculateRecipeRequest {
  batchSizeLiters: number;
  efficiencyPercent: number;
  boilTimeMinutes: number;
  ingredients: RecipeIngredientInputDto[];
}

export interface CalculateRecipeResponse {
  originalGravity: number;
  finalGravity: number;
  alcoholByVolume: number;
  bitternessIbu: number;
  colorSrm: number;
}

export interface CreateRecipeRequest {
  name: string;
  description?: string | null;
  beerStyle: string;
  batchSizeLiters: number;
  boilTimeMinutes: number;
  efficiencyPercent: number;
  isPublic: boolean;
  ingredients: RecipeIngredientInputDto[];
}

export interface RecipeSummaryDto {
  id: string;
  name: string;
  description?: string | null;
  beerStyle: string;
  batchSizeLiters: number;
  originalGravity: number;
  finalGravity: number;
  alcoholByVolume: number;
  bitternessIbu: number;
  colorSrm: number;
  isPublic: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface RecipeDetailDto extends RecipeSummaryDto {
  boilTimeMinutes: number;
  efficiencyPercent: number;
  ingredients: RecipeIngredientOutputDto[];
}

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  preferredLanguage: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserDto;
}
