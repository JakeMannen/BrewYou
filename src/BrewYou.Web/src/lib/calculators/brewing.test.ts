import { describe, expect, it } from 'vitest';
import { calculateBrewMetrics, srmToHexColor, type CalculatorItem } from './brewing';

describe('calculateBrewMetrics', () => {
  it('calculates expected OG and color for 5kg base malt in 20L', () => {
    const items: CalculatorItem[] = [
      {
        ingredient: {
          id: '1',
          name: 'Pale Malt',
          type: 'Fermentable',
          potentialGravity: 1.037,
          colorSrm: 2.0,
          isCatalogItem: true
        },
        amount: 5.0,
        usage: 'Mash'
      }
    ];

    const result = calculateBrewMetrics(20, 75, 60, items);

    expect(result.originalGravity).toBeGreaterThan(1.05);
    expect(result.originalGravity).toBeLessThan(1.065);
    expect(result.colorSrm).toBeGreaterThan(0);
  });

  it('calculates FG and ABV with yeast attenuation', () => {
    const items: CalculatorItem[] = [
      {
        ingredient: {
          id: '1',
          name: 'Pale Malt',
          type: 'Fermentable',
          potentialGravity: 1.037,
          colorSrm: 2.0,
          isCatalogItem: true
        },
        amount: 5.0,
        usage: 'Mash'
      },
      {
        ingredient: {
          id: '2',
          name: 'SafAle US-05',
          type: 'Yeast',
          attenuationPercent: 80,
          isCatalogItem: true
        },
        amount: 11.5,
        usage: 'Primary'
      }
    ];

    const result = calculateBrewMetrics(20, 75, 60, items);

    expect(result.finalGravity).toBeLessThan(result.originalGravity);
    expect(result.finalGravity).toBeGreaterThanOrEqual(1.008);
    expect(result.alcoholByVolume).toBeGreaterThan(5.0);
    expect(result.alcoholByVolume).toBeLessThan(6.5);
  });

  it('calculates Tinseth IBU with boil hops', () => {
    const items: CalculatorItem[] = [
      {
        ingredient: {
          id: '1',
          name: 'Pale Malt',
          type: 'Fermentable',
          potentialGravity: 1.037,
          isCatalogItem: true
        },
        amount: 5.0,
        usage: 'Mash'
      },
      {
        ingredient: {
          id: '2',
          name: 'Citra',
          type: 'Hop',
          alphaAcidPercent: 12.0,
          isCatalogItem: true
        },
        amount: 30.0,
        durationMinutes: 60,
        usage: 'Boil'
      }
    ];

    const result = calculateBrewMetrics(20, 75, 60, items);

    expect(result.bitternessIbu).toBeGreaterThan(30);
    expect(result.bitternessIbu).toBeLessThan(65);
  });

  it('maps SRM values to valid hex color strings', () => {
    expect(srmToHexColor(2)).toMatch(/^#[0-9a-f]{6}$/i);
    expect(srmToHexColor(30)).toMatch(/^#[0-9a-f]{6}$/i);
  });
});
