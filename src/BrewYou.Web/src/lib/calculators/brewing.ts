import type { CalculateRecipeResponse, IngredientDto, IngredientUsage } from '$lib/types/api';

const KG_TO_LBS = 2.20462262;
const LITERS_TO_GALLONS = 0.264172052;

export interface CalculatorItem {
  ingredient: IngredientDto;
  amount: number; // kg for grains, grams for hops/yeast
  durationMinutes?: number | null;
  usage: IngredientUsage;
}

export function calculateBrewMetrics(
  batchSizeLiters: number,
  efficiencyPercent: number,
  boilTimeMinutes: number,
  items: CalculatorItem[]
): CalculateRecipeResponse {
  const volumeGallons = Math.max(0.1, batchSizeLiters * LITERS_TO_GALLONS);
  const eff = Math.max(0.1, efficiencyPercent / 100);

  let totalGravityPoints = 0;
  let totalMcu = 0;
  let yeastAttenuation: number | null = null;

  for (const item of items) {
    if (!item.ingredient) continue;

    if (item.ingredient.type === 'Fermentable') {
      const weightLbs = item.amount * KG_TO_LBS;
      const ppg = item.ingredient.potentialGravity
        ? (item.ingredient.potentialGravity - 1.0) * 1000
        : 37;

      totalGravityPoints += (weightLbs * ppg * eff) / volumeGallons;

      const color = item.ingredient.colorSrm ?? 2.0;
      totalMcu += (weightLbs * color) / volumeGallons;
    } else if (item.ingredient.type === 'Yeast') {
      if (item.ingredient.attenuationPercent) {
        yeastAttenuation = item.ingredient.attenuationPercent;
      }
    }
  }

  const ogPoints = Math.max(0, totalGravityPoints);
  const og = 1.0 + ogPoints / 1000.0;

  const attenuation = (yeastAttenuation ?? 75.0) / 100.0;
  const fg = 1.0 + (og - 1.0) * (1.0 - attenuation);
  const abv = Math.max(0, (og - fg) * 131.25);

  // Bitterness (Tinseth)
  let totalIbu = 0;
  const bivalence = 1.65 * Math.pow(0.000125, og - 1.0);

  for (const item of items) {
    if (
      item.ingredient?.type === 'Hop' &&
      (item.usage === 'Boil' || item.usage === 'Mash')
    ) {
      const boilMins = item.durationMinutes ?? boilTimeMinutes;
      const timeFactor = (1.0 - Math.exp(-0.04 * Math.max(0, boilMins))) / 4.15;
      const utilization = bivalence * timeFactor;

      const alpha = (item.ingredient.alphaAcidPercent ?? 5.0) / 100.0;
      const alphaMgL = (item.amount * alpha * 1000.0) / Math.max(0.1, batchSizeLiters);

      totalIbu += utilization * alphaMgL;
    }
  }

  // SRM (Morey)
  const srm = totalMcu > 0 ? 1.4922 * Math.pow(totalMcu, 0.6859) : 0;

  return {
    originalGravity: Number(og.toFixed(3)),
    finalGravity: Number(fg.toFixed(3)),
    alcoholByVolume: Number(abv.toFixed(2)),
    bitternessIbu: Number(totalIbu.toFixed(1)),
    colorSrm: Number(srm.toFixed(1))
  };
}

/**
 * Returns a CSS hex color code representing the beer color for a given SRM value
 */
export function srmToHexColor(srm: number): string {
  if (srm <= 1) return '#f8f5b8';
  if (srm <= 2) return '#f6f1a8';
  if (srm <= 3) return '#ece67a';
  if (srm <= 4) return '#e5d34e';
  if (srm <= 6) return '#d4bc2b';
  if (srm <= 8) return '#bf9224';
  if (srm <= 10) return '#b0761a';
  if (srm <= 13) return '#975412';
  if (srm <= 17) return '#7a370b';
  if (srm <= 20) return '#5e2307';
  if (srm <= 24) return '#481905';
  if (srm <= 29) return '#351204';
  if (srm <= 35) return '#240c03';
  return '#120501';
}
