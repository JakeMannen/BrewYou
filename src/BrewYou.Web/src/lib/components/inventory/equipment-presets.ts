export interface AllInOnePreset {
	name: string;
	capacityLiters: number;
	mashTunDeadSpaceLiters: number;
	boilOffRatePerHour: number;
	trubLossLiters: number;
}

export const ALL_IN_ONE_PRESETS: AllInOnePreset[] = [
	{
		name: 'Grainfather G30',
		capacityLiters: 30,
		mashTunDeadSpaceLiters: 3.5,
		boilOffRatePerHour: 3.0,
		trubLossLiters: 2.0
	},
	{
		name: 'Grainfather G40',
		capacityLiters: 40,
		mashTunDeadSpaceLiters: 4.5,
		boilOffRatePerHour: 3.5,
		trubLossLiters: 2.5
	},
	{
		name: 'Grainfather G70',
		capacityLiters: 70,
		mashTunDeadSpaceLiters: 8.0,
		boilOffRatePerHour: 4.0,
		trubLossLiters: 3.5
	},
	{
		name: 'BrewZilla 35L Gen 4',
		capacityLiters: 35,
		mashTunDeadSpaceLiters: 3.5,
		boilOffRatePerHour: 3.0,
		trubLossLiters: 2.0
	},
	{
		name: 'BrewZilla 65L Gen 4',
		capacityLiters: 65,
		mashTunDeadSpaceLiters: 6.0,
		boilOffRatePerHour: 4.5,
		trubLossLiters: 3.5
	},
	{
		name: 'Speidel Braumeister 20L',
		capacityLiters: 25,
		mashTunDeadSpaceLiters: 2.5,
		boilOffRatePerHour: 2.5,
		trubLossLiters: 1.5
	},
	{
		name: 'Brewtools B40pro',
		capacityLiters: 46,
		mashTunDeadSpaceLiters: 3.0,
		boilOffRatePerHour: 3.5,
		trubLossLiters: 2.0
	},
	{
		name: 'Anvil Foundry 10.5',
		capacityLiters: 40,
		mashTunDeadSpaceLiters: 2.5,
		boilOffRatePerHour: 3.0,
		trubLossLiters: 1.5
	}
];
