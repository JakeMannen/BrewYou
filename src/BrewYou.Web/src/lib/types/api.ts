export type IngredientType = 'Fermentable' | 'Hop' | 'Yeast' | 'Other';

export type TargetVolumeBasis = 'Fermenter' | 'Packaged' | 'fermenter' | 'packaged';

export type MashStepType = 'Infusion' | 'Temperature' | 'Decoction';

export type FermentationStepType =
	'Primary' | 'DiacetylRest' | 'Ramp' | 'FreeRise' | 'Secondary' | 'ColdCrash' | 'Conditioning';

export type IngredientUsage =
	'Mash' | 'Boil' | 'Whirlpool' | 'Primary' | 'Secondary' | 'DryHop' | 'Bottling';

export interface ApiErrorDetail {
	field: string;
	issue: string;
}

export const API_ERROR_CODES = {
	DUPLICATE_NAME: 'DUPLICATE_NAME',
	QUOTA_EXCEEDED: 'QUOTA_EXCEEDED',
	NOT_FOUND: 'NOT_FOUND',
	VALIDATION_ERROR: 'VALIDATION_ERROR',
	UNAUTHORIZED: 'UNAUTHORIZED',
	EMAIL_CONFLICT: 'EMAIL_CONFLICT',
	INVALID_CREDENTIALS: 'INVALID_CREDENTIALS',
	TOKEN_EXPIRED: 'TOKEN_EXPIRED',
	INVALID_TOKEN: 'INVALID_TOKEN',
	REGISTRATION_FAILED: 'REGISTRATION_FAILED'
} as const;

export type ApiErrorCode = (typeof API_ERROR_CODES)[keyof typeof API_ERROR_CODES];

export interface ApiError {
	code: string;
	message: string;
	details?: ApiErrorDetail[] | null;
}

export interface PaginationMeta {
	page: number;
	limit: number;
	total: number;
	totalPages: number;
}

export interface ApiResponse<T = void> {
	success: boolean;
	data?: T;
	error?: ApiError | null;
	pagination?: PaginationMeta | null;
}

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
	stockAmount?: number;
	stockUnit?: string;
	isInStock?: boolean;
	form?: string | null;
}

export interface CreateIngredientRequest {
	name: string;
	type: IngredientType;
	potentialGravity?: number | null;
	colorSrm?: number | null;
	alphaAcidPercent?: number | null;
	attenuationPercent?: number | null;
	description?: string | null;
	initialStock?: number | null;
	stockUnit?: string | null;
	form?: string | null;
}

export interface UpdateIngredientStockRequest {
	amount: number;
	unit?: string | null;
}

export interface IngredientUsageRecipeDto {
	id: string;
	name: string;
}

export interface IngredientUsageDto {
	ingredientId: string;
	ingredientName: string;
	recipeCount: number;
	recipes: IngredientUsageRecipeDto[];
}

export interface RecipeIngredientInputDto {
	ingredientId: string;
	amount: number;
	unit: string;
	durationMinutes?: number | null;
	usage: IngredientUsage;
	notes?: string | null;
	form?: string | null;
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
	potentialGravity?: number | null;
	colorSrm?: number | null;
	alphaAcidPercent?: number | null;
	attenuationPercent?: number | null;
	form?: string | null;
}

export interface RecipeMashStepInputDto {
	stepOrder: number;
	name: string;
	type: MashStepType;
	temperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number | null;
	infuseAmountLiters?: number | null;
	notes?: string | null;
}

export interface RecipeMashStepOutputDto {
	id: string;
	recipeId: string;
	stepOrder: number;
	name: string;
	type: MashStepType;
	temperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number | null;
	infuseAmountLiters?: number | null;
	notes?: string | null;
}

export interface RecipeFermentationStepInputDto {
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	durationDays: number;
	rampTimeHours?: number | null;
	triggerGravity?: number | null;
	notes?: string | null;
}

export interface RecipeFermentationStepOutputDto {
	id: string;
	recipeId: string;
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	durationDays: number;
	rampTimeHours?: number | null;
	triggerGravity?: number | null;
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
	beerStyle?: string | null;
	batchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;
	isPublic: boolean;
	ingredients: RecipeIngredientInputDto[];
	mashSteps?: RecipeMashStepInputDto[] | null;
	fermentationSteps?: RecipeFermentationStepInputDto[] | null;
}

export interface UpdateRecipeRequest {
	name: string;
	description?: string | null;
	beerStyle?: string | null;
	batchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;
	isPublic: boolean;
	ingredients: RecipeIngredientInputDto[];
	mashSteps?: RecipeMashStepInputDto[] | null;
	fermentationSteps?: RecipeFermentationStepInputDto[] | null;
}

export type RecipeScope = 'all' | 'mine' | 'shared';

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
	authorName?: string | null;
	isOwner?: boolean;
}

export interface RecipeDetailDto extends RecipeSummaryDto {
	boilTimeMinutes: number;
	efficiencyPercent: number;
	ingredients: RecipeIngredientOutputDto[];
	mashSteps: RecipeMashStepOutputDto[];
	fermentationSteps: RecipeFermentationStepOutputDto[];
}

export interface RecipeNameCheckResponse {
	exists: boolean;
}

export type VolumeUnit = 'Liters' | 'Gallons';
export type WeightUnit = 'Metric' | 'Imperial';
export type TemperatureUnit = 'Celsius' | 'Fahrenheit';
export type GravityUnit = 'SpecificGravity' | 'Plato';
export type ThemePreference = 'Dark' | 'Light' | 'System';

export interface UserPreferencesDto {
	language: string;
	volumeUnit: VolumeUnit;
	weightUnit: WeightUnit;
	temperatureUnit: TemperatureUnit;
	gravityUnit: GravityUnit;
	theme: ThemePreference;
	defaultBatchSizeLiters: number;
	defaultEfficiencyPercent: number;
	defaultBoilTimeMinutes: number;
	mqttHost?: string | null;
	mqttPort?: number | null;
	mqttUsername?: string | null;
	mqttPassword?: string | null;
	mqttCertificate?: string | null;
	hasMqttPassword?: boolean;
	mqttTopicPrefix?: string | null;
}

export interface UpdateUserPreferencesRequest {
	language?: string;
	volumeUnit?: VolumeUnit;
	weightUnit?: WeightUnit;
	temperatureUnit?: TemperatureUnit;
	gravityUnit?: GravityUnit;
	theme?: ThemePreference;
	defaultBatchSizeLiters?: number;
	defaultEfficiencyPercent?: number;
	defaultBoilTimeMinutes?: number;
	mqttHost?: string | null;
	mqttPort?: number | null;
	mqttUsername?: string | null;
	mqttPassword?: string | null;
	mqttCertificate?: string | null;
	mqttTopicPrefix?: string | null;
}

export interface MqttStatusResponse {
	configured: boolean;
	connected: boolean;
	host?: string | null;
	port?: number | null;
	error?: string | null;
}

export interface UpdateProfileRequest {
	displayName: string;
}

export interface UserDto {
	id: string;
	email: string;
	displayName: string;
	preferredLanguage: string;
	preferredVolumeUnit: VolumeUnit;
	createdAt?: string;
	preferences?: UserPreferencesDto;
}

export interface LoginRequest {
	email: string;
	password: string;
}

export interface RegisterRequest {
	email: string;
	password: string;
	displayName: string;
	preferredLanguage?: string;
	preferredVolumeUnit?: VolumeUnit;
}

export interface RefreshTokenRequest {
	refreshToken?: string;
}

export interface AuthResponse {
	accessToken: string;
	refreshToken: string;
	expiresAt: string;
	user: UserDto;
	isNewUser?: boolean;
}

export interface GoogleAuthRequest {
	idToken: string;
	preferredLanguage?: string;
}

export type EquipmentType = 'Boiler' | 'Fermenter' | 'Keg' | 'Sensor' | 'Other';

export type EquipmentSubtype =
	| 'AllInOne'
	| 'Pan'
	| 'Hlt'
	| 'HermsRims'
	| 'Bucket'
	| 'ConicalFermenter'
	| 'Carboy'
	| 'PressureFermenter'
	| 'StainlessBucket'
	| 'Cornelius'
	| 'Minikeg'
	| 'MiniBarrel'
	| 'PetKeg'
	| 'ISpindel'
	| 'Tilt'
	| 'GenericSensor'
	| 'Other';

export const EQUIPMENT_SUBTYPES_BY_TYPE: Record<EquipmentType, EquipmentSubtype[]> = {
	Boiler: ['AllInOne', 'Pan', 'Hlt', 'HermsRims'],
	Fermenter: ['Bucket', 'ConicalFermenter', 'Carboy', 'PressureFermenter', 'StainlessBucket'],
	Keg: ['Cornelius', 'Minikeg', 'MiniBarrel', 'PetKeg'],
	Sensor: ['GenericSensor', 'Tilt', 'ISpindel'],
	Other: ['Other']
};

export const DEFAULT_SUBTYPE_BY_TYPE: Record<EquipmentType, EquipmentSubtype> = {
	Boiler: 'AllInOne',
	Fermenter: 'Bucket',
	Keg: 'Cornelius',
	Sensor: 'GenericSensor',
	Other: 'Other'
};

export const SUBTYPE_LABELS: Record<EquipmentSubtype, string> = {
	AllInOne: 'All in one',
	Pan: 'Pan',
	Hlt: 'Hot Liquor Tank (HLT)',
	HermsRims: 'HERMS / RIMS',
	Bucket: 'Bucket',
	ConicalFermenter: 'Conical fermenter',
	Carboy: 'Carboy / Demijohn',
	PressureFermenter: 'Pressure fermenter',
	StainlessBucket: 'Stainless bucket',
	Cornelius: 'Cornelius',
	Minikeg: 'Minikeg',
	MiniBarrel: 'Mini barrel',
	PetKeg: 'PET pressure keg',
	ISpindel: 'iSpindel',
	Tilt: 'Tilt Hydrometer',
	GenericSensor: 'Generic Sensor / Probe',
	Other: 'Other'
};

export const SUBTYPE_DESCRIPTIONS: Record<EquipmentSubtype, string> = {
	AllInOne: 'Electric brewing system (Grainfather, BrewZilla)',
	Pan: 'Traditional brew kettle & stock pot',
	Hlt: 'Dedicated hot liquor / sparge water heater',
	HermsRims: 'Recirculating heat exchanger module',
	Bucket: 'Food-grade plastic or stainless bucket',
	ConicalFermenter: 'Unitank fermenter with 60° dump cone',
	Carboy: 'Glass or PET narrow-neck carboy',
	PressureFermenter: 'Pressurized conical unitank (FermZilla, Kegmenter)',
	StainlessBucket: 'Stainless steel flat-bottom fermenter',
	Cornelius: 'Corny ball-lock / pin-lock keg (19L / 5 gal)',
	Minikeg: 'Pressurized party mini-keg',
	MiniBarrel: 'Wood aging barrel or craft cask',
	PetKeg: 'Reusable PET pressure keg (Oxebar / Snub Nose)',
	ISpindel: 'Wireless floating WiFi hydrometer and temperature capsule',
	Tilt: 'Wireless Bluetooth floating hydrometer and thermometer',
	GenericSensor: 'Digital temperature probe or external telemetry sensor',
	Other: 'Generic container or vessel'
};

export interface BrewerySetupDto {
	id: string;
	name: string;
	description?: string | null;
	isDefault: boolean;
	equipmentCount: number;
	defaultGrainAbsorptionRate: number;
	defaultBoilOffRatePerHour: number;
	defaultKettleTrubLossLiters: number;
	defaultFermenterLossLiters: number;
	defaultMashTunDeadSpaceLiters: number;
	coolingShrinkagePercent: number;
	defaultPackagingLossLiters: number;
	createdAt: string;
	updatedAt: string;
}

export interface CreateBrewerySetupRequest {
	name: string;
	description?: string | null;
	isDefault?: boolean;
	defaultGrainAbsorptionRate?: number | null;
	defaultBoilOffRatePerHour?: number | null;
	defaultKettleTrubLossLiters?: number | null;
	defaultFermenterLossLiters?: number | null;
	defaultMashTunDeadSpaceLiters?: number | null;
	coolingShrinkagePercent?: number | null;
	defaultPackagingLossLiters?: number | null;
}

export interface UpdateBrewerySetupRequest {
	name: string;
	description?: string | null;
	isDefault?: boolean | null;
	defaultGrainAbsorptionRate?: number | null;
	defaultBoilOffRatePerHour?: number | null;
	defaultKettleTrubLossLiters?: number | null;
	defaultFermenterLossLiters?: number | null;
	defaultMashTunDeadSpaceLiters?: number | null;
	coolingShrinkagePercent?: number | null;
	defaultPackagingLossLiters?: number | null;
}

export type EquipmentConnectionType = 'None' | 'HttpPush' | 'HttpPoll' | 'Mqtt';

export interface EquipmentConnectionConfig {
	url?: string;
	intervalSeconds?: number;
	jsonPath?: string;
	brokerHost?: string;
	brokerPort?: number;
	topic?: string;
	username?: string;
	password?: string;
	certificate?: string;
	useUserSettings?: boolean;
	hasCertificate?: boolean;
}

export interface EquipmentDto {
	id: string;
	brewerySetupId: string;
	name: string;
	type: EquipmentType;
	subtype: EquipmentSubtype;
	capacity: number;
	unit: VolumeUnit;
	capacityLiters: number;
	currentVolume: number;
	currentVolumeLiters: number;
	boilOffRatePerHour?: number | null;
	trubLossLiters?: number | null;
	mashTunDeadSpaceLiters?: number | null;
	packagingLossLiters?: number | null;
	fillPercentage: number;
	currentTemperatureC?: number | null;
	temperatureUpdatedAt?: string | null;
	currentSpecificGravity?: number | null;
	currentPressureBar?: number | null;
	currentBatteryPercent?: number | null;
	currentBatteryVoltage?: number | null;
	lastTelemetryAt?: string | null;
	latestMetricsJson?: string | null;
	connectionType?: EquipmentConnectionType;
	connectionToken?: string | null;
	connectionConfigJson?: string | null;
	description?: string | null;
	notes?: string | null;
	createdAt: string;
	updatedAt: string;
}

export interface EquipmentActiveBatchDto {
	id: string;
	batchCode: string;
	name: string;
	status: BatchStatus;
	currentStage: BrewStage;
}

export interface EquipmentReadingDto {
	id: string;
	equipmentId: string;
	timestamp: string;
	temperatureC: number;
	source?: string | null;
	createdAt: string;
	specificGravity?: number | null;
	pressureBar?: number | null;
	batteryPercent?: number | null;
	batteryVoltage?: number | null;
	tiltDegrees?: number | null;
	rssi?: number | null;
	metricsJson?: string | null;
}

export interface EquipmentTelemetryUpdateDto {
	equipmentId: string;
	equipmentName: string;
	temperatureC: number;
	timestamp: string;
	source?: string | null;
	batchId?: string | null;
	specificGravity?: number | null;
	currentVolumeLiters?: number | null;
	fillPercentage?: number | null;
	pressureBar?: number | null;
	batteryPercent?: number | null;
	batteryVoltage?: number | null;
	tiltDegrees?: number | null;
	rssi?: number | null;
	metricsJson?: string | null;
}

export interface TelemetryPollResult {
	success: boolean;
	temperatureC?: number | null;
	errorMessage?: string | null;
	responseSnippet?: string | null;
}

export interface CreateEquipmentRequest {
	brewerySetupId: string;
	name: string;
	type: EquipmentType;
	capacity: number;
	subtype?: EquipmentSubtype | null;
	currentVolume?: number | null;
	unit?: VolumeUnit;
	description?: string | null;
	notes?: string | null;
	boilOffRatePerHour?: number | null;
	trubLossLiters?: number | null;
	mashTunDeadSpaceLiters?: number | null;
	packagingLossLiters?: number | null;
	currentTemperatureC?: number | null;
	connectionType?: EquipmentConnectionType;
	connectionConfigJson?: string | null;
}

export interface UpdateEquipmentRequest {
	name: string;
	type: EquipmentType;
	capacity: number;
	brewerySetupId?: string | null;
	subtype?: EquipmentSubtype | null;
	currentVolume?: number | null;
	unit?: VolumeUnit;
	description?: string | null;
	notes?: string | null;
	boilOffRatePerHour?: number | null;
	trubLossLiters?: number | null;
	mashTunDeadSpaceLiters?: number | null;
	packagingLossLiters?: number | null;
	currentTemperatureC?: number | null;
	connectionType?: EquipmentConnectionType;
	connectionConfigJson?: string | null;
}

export interface UpdateVolumeUnitRequest {
	volumeUnit: VolumeUnit;
}

export type BatchStatus =
	'Planned' | 'Brewing' | 'Fermenting' | 'Conditioning' | 'Completed' | 'Archived';
export type BrewStage = 'Mash' | 'Boil' | 'Ferment' | 'Condition' | 'Package';
export type StageStatus = 'Pending' | 'InProgress' | 'Completed' | 'Skipped';

export interface BatchSummaryDto {
	id: string;
	batchCode: string;
	name: string;
	recipeId?: string | null;
	beerStyle: string;
	status: BatchStatus;
	currentStage: BrewStage;
	brewDate: string;
	daysActive: number;
	targetOg: number;
	measuredOg?: number | null;
	currentGravity?: number | null;
	targetFg: number;
	measuredFg?: number | null;
	alcoholByVolume?: number | null;
	colorSrm: number;
	vesselTempC?: number | null;
	fermenterId?: string | null;
	fermenterName?: string | null;
	targetBatchSizeLiters: number;
	measuredBatchSizeLiters?: number | null;
	createdAt: string;
	updatedAt: string;
}

export interface BatchReadingDto {
	id: string;
	batchId: string;
	timestamp: string;
	specificGravity: number;
	temperatureC?: number | null;
	notes?: string | null;
	createdAt: string;
	alcoholByVolume?: number | null;
}

export interface BatchIngredientDto {
	id: string;
	batchId: string;
	sourceIngredientId?: string | null;
	name: string;
	type: IngredientType;
	amount: number;
	unit: string;
	additionStage: IngredientUsage;
	additionTimeMinutes?: number | null;
	isChecked: boolean;
	isDeducted?: boolean;
	notes?: string | null;
	form?: string | null;
}

export interface BatchStageHistoryDto {
	id: string;
	batchId: string;
	stage: BrewStage;
	status: StageStatus;
	startedAt?: string | null;
	completedAt?: string | null;
	durationMinutes?: number | null;
	notes?: string | null;
}

export interface BatchMashStepDto {
	id: string;
	batchId: string;
	recipeMashStepId?: string | null;
	stepOrder: number;
	name: string;
	type: MashStepType;
	targetTemperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number | null;
	infuseAmountLiters?: number | null;
	actualTemperatureC?: number | null;
	actualDurationMinutes?: number | null;
	isCompleted: boolean;
	completedAt?: string | null;
	notes?: string | null;
}

export interface BatchMashStepInputDto {
	stepOrder: number;
	name: string;
	type: MashStepType;
	targetTemperatureC: number;
	durationMinutes: number;
	rampTimeMinutes?: number | null;
	infuseAmountLiters?: number | null;
	notes?: string | null;
}

export interface ToggleBatchMashStepRequest {
	isCompleted: boolean;
	actualTemperatureC?: number | null;
	actualDurationMinutes?: number | null;
	notes?: string | null;
}

export interface BatchFermentationStepDto {
	id: string;
	batchId: string;
	recipeFermentationStepId?: string | null;
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	actualTemperatureC?: number | null;
	durationDays: number;
	rampTimeHours?: number | null;
	triggerGravity?: number | null;
	isCompleted: boolean;
	startedAt?: string | null;
	completedAt?: string | null;
	notes?: string | null;
}

export interface BatchFermentationStepInputDto {
	stepOrder: number;
	name: string;
	type: FermentationStepType;
	targetTemperatureC: number;
	durationDays: number;
	rampTimeHours?: number | null;
	triggerGravity?: number | null;
	notes?: string | null;
}

export interface ToggleBatchFermentationStepRequest {
	isCompleted: boolean;
	actualTemperatureC?: number | null;
	notes?: string | null;
}

export interface BatchSensorAssignmentDto {
	id: string;
	equipmentId: string;
	equipmentName?: string | null;
	equipmentSubtype?: EquipmentSubtype | null;
	stage?: BrewStage | null;
	batchMashStepId?: string | null;
	stepName?: string | null;
}

export interface BatchSensorAssignmentInput {
	equipmentId: string;
	stage?: BrewStage | null;
	batchMashStepId?: string | null;
}

export interface BatchDetailDto {
	id: string;
	userId: string;
	recipeId?: string | null;
	recipeName?: string | null;
	batchCode: string;
	name: string;
	beerStyle: string;
	status: BatchStatus;
	currentStage: BrewStage;
	brewDate: string;
	daysActive: number;

	targetOg: number;
	targetFg: number;
	targetAbv: number;
	targetIbu: number;
	targetColorSrm: number;
	targetBatchSizeLiters: number;
	boilTimeMinutes: number;
	efficiencyPercent: number;

	measuredOg?: number | null;
	currentGravity?: number | null;
	measuredFg?: number | null;
	alcoholByVolume?: number | null;
	brewhouseEfficiency?: number | null;
	measuredBatchSizeLiters?: number | null;
	pitchTemperatureC?: number | null;

	boilerId?: string | null;
	boilerName?: string | null;
	fermenterId?: string | null;
	fermenterName?: string | null;
	packagingVesselId?: string | null;
	packagingVesselName?: string | null;

	notes?: string | null;
	createdAt: string;
	updatedAt: string;
	completedAt?: string | null;

	readings: BatchReadingDto[];
	ingredients: BatchIngredientDto[];
	mashSteps: BatchMashStepDto[];
	fermentationSteps: BatchFermentationStepDto[];
	stageHistory: BatchStageHistoryDto[];
	sensorAssignments: BatchSensorAssignmentDto[];
	volumeProfile?: BatchVolumeProfileDto | null;
}

export interface BatchVolumeProfileDto {
	totalWaterLiters: number;
	strikeWaterLiters: number;
	spargeWaterLiters: number;
	targetPreBoilVolumeLiters: number;
	measuredPreBoilVolumeLiters?: number | null;
	measuredPreBoilGravity?: number | null;
	targetPostBoilVolumeLiters: number;
	measuredPostBoilVolumeLiters?: number | null;
	targetFermenterVolumeLiters: number;
	measuredFermenterVolumeLiters?: number | null;
	targetPackagedVolumeLiters: number;
	measuredPackagedVolumeLiters?: number | null;
	boilOffRatePerHour: number;
	grainAbsorptionRateLPerKg: number;
	kettleTrubLossLiters: number;
	fermenterTrubLossLiters: number;
	mashTunDeadSpaceLiters: number;
	coolingShrinkagePercent: number;
	packagingLossLiters: number;
}

export interface BatchIngredientInputDto {
	name: string;
	type: IngredientType;
	amount: number;
	unit: string;
	additionStage: IngredientUsage;
	additionTimeMinutes?: number | null;
	notes?: string | null;
	form?: string | null;
}

export interface CreateBatchRequest {
	recipeId?: string | null;
	batchCode?: string | null;
	name: string;
	beerStyle?: string | null;
	brewDate?: string | null;
	targetBatchSizeLiters?: number | null;
	targetOg?: number | null;
	targetFg?: number | null;
	targetAbv?: number | null;
	targetIbu?: number | null;
	targetColorSrm?: number | null;
	boilTimeMinutes?: number | null;
	efficiencyPercent?: number | null;
	boilerId?: string | null;
	fermenterId?: string | null;
	measuredOg?: number | null;
	pitchTemperatureC?: number | null;
	notes?: string | null;
	customIngredients?: BatchIngredientInputDto[] | null;
	customMashSteps?: BatchMashStepInputDto[] | null;
	customFermentationSteps?: BatchFermentationStepInputDto[] | null;
	boilOffRatePerHour?: number | null;
	kettleTrubLossLiters?: number | null;
	mashTunDeadSpaceLiters?: number | null;
	fermenterLossLiters?: number | null;
	spargeEnabled?: boolean | null;
	packagingVesselId?: string | null;
	targetVolumeBasis?: TargetVolumeBasis | null;
	packagingLossLiters?: number | null;
	sensorAssignments?: BatchSensorAssignmentInput[] | null;
}

export interface AdvanceBatchStageRequest {
	targetStage: BrewStage;
	measuredOg?: number | null;
	measuredBatchSizeLiters?: number | null;
	pitchTemperatureC?: number | null;
	packagingVesselId?: string | null;
	measuredFg?: number | null;
	notes?: string | null;
	measuredPreBoilVolumeLiters?: number | null;
	measuredPreBoilGravity?: number | null;
	measuredPostBoilVolumeLiters?: number | null;
	measuredPackagedVolumeLiters?: number | null;
}

export interface AddBatchReadingRequest {
	timestamp?: string | null;
	specificGravity: number;
	temperatureC?: number | null;
	notes?: string | null;
}

export interface UpdateBatchRequest {
	name?: string | null;
	beerStyle?: string | null;
	notes?: string | null;
	measuredOg?: number | null;
	measuredFg?: number | null;
	measuredBatchSizeLiters?: number | null;
	pitchTemperatureC?: number | null;
	boilerId?: string | null;
	fermenterId?: string | null;
	packagingVesselId?: string | null;
	measuredPreBoilVolumeLiters?: number | null;
	measuredPreBoilGravity?: number | null;
	measuredPostBoilVolumeLiters?: number | null;
	measuredPackagedVolumeLiters?: number | null;
	sensorAssignments?: BatchSensorAssignmentInput[] | null;
}

export interface NextBatchCodeResponse {
	batchCode: string;
}

export interface BatchListQuery {
	status?: BatchStatus;
	search?: string;
	page?: number;
	limit?: number;
}

export interface CalculateWaterVolumeRequest {
	batchSizeLiters: number;
	boilTimeMinutes: number;
	totalGrainWeightKg: number;
	targetBasis?: TargetVolumeBasis | null;
	spargeEnabled?: boolean | null;
	boilOffRatePerHourLiters?: number | null;
	kettleTrubLossLiters?: number | null;
	grainAbsorptionRateLPerKg?: number | null;
	fermenterLossLiters?: number | null;
	mashTunDeadSpaceLiters?: number | null;
	coolingShrinkagePercent?: number | null;
	packagingLossLiters?: number | null;
	mashThicknessLitersPerKg?: number | null;
}

export interface CalculateWaterVolumeResponse {
	strikeWaterLiters: number;
	spargeWaterLiters: number;
	totalWaterLiters: number;
	estimatedPreBoilVolumeLiters: number;
	estimatedPostBoilVolumeLiters: number;
	estimatedIntoFermenterVolumeLiters: number;
	estimatedPackagedVolumeLiters: number;
	grainAbsorptionLossLiters: number;
	boilOffLossLiters: number;
	kettleTrubLossLiters: number;
	shrinkageLossLiters: number;
	fermenterLossLiters: number;
	packagingLossLiters: number;
}

export interface BatchEquipmentReadingDto {
	id: string;
	equipmentId: string;
	equipmentName: string;
	batchId: string;
	stage?: BrewStage | null;
	batchMashStepId?: string | null;
	stepName?: string | null;
	temperatureC: number;
	timestamp: string;
	source?: string | null;
	targetTemperatureC?: number | null;
	notes?: string | null;
	specificGravity?: number | null;
	pressureBar?: number | null;
	batteryPercent?: number | null;
	batteryVoltage?: number | null;
	tiltDegrees?: number | null;
	rssi?: number | null;
	metricsJson?: string | null;
}

export interface BrewYouTelemetryPayload {
	timestamp?: string;
	temperature?: number;
	tempUnit?: 'C' | 'F' | 'c' | 'f' | '°C' | '°F';
	gravity?: number;
	gravityUnit?: 'SG' | 'Plato' | 'Brix' | 'Points';
	pressure?: number;
	pressureUnit?: 'bar' | 'psi' | 'kPa';
	battery?: number;
	batteryUnit?: 'V' | '%';
	tilt?: number;
	tiltUnit?: 'deg';
	rssi?: number;
	ph?: number;
	volume?: number;
	volumeUnit?: 'L' | 'gal';
	raw?: Record<string, unknown>;
}

export interface LogBatchTemperatureRequest {
	temperatureC: number;
	equipmentId?: string | null;
	stage?: BrewStage | null;
	batchMashStepId?: string | null;
	stepName?: string | null;
	timestamp?: string | null;
	notes?: string | null;
}

export interface CheckBatchStockRequest {
	recipeId: string;
	targetBatchSizeLiters: number;
	targetVolumeBasis?: TargetVolumeBasis | null;
	fermenterLossLiters?: number | null;
	packagingLossLiters?: number | null;
}

export interface IngredientShortageDto {
	ingredientId: string;
	name: string;
	type: IngredientType;
	requiredAmount: number;
	stockAmount: number;
	unit: string;
	deficit: number;
}

export interface BatchStockCheckResult {
	hasShortage: boolean;
	shortages: IngredientShortageDto[];
}
