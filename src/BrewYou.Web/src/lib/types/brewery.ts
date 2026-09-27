import type { BreweryRole } from './api';

export interface BrewerySetup {
	id: string;
	name: string;
	description?: string;
	isDefault?: boolean;
	equipmentCount?: number;
	currentUserRole?: BreweryRole;
	isOwner?: boolean;
	memberCount?: number;
	createdAt: string;
}
