import { describe, expect, it } from 'vitest';
import { parseBeerXml } from './beerxml';

describe('BeerXML Parser', () => {
	it('parses valid BeerXML 1.0 recipe with unit conversions', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Centennial Blonde</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <STYLE>
      <NAME>Blonde Ale</NAME>
    </STYLE>
    <BATCH_SIZE>20.0</BATCH_SIZE>
    <BOIL_TIME>60</BOIL_TIME>
    <EFFICIENCY>75.0</EFFICIENCY>
    <NOTES>Crisp blonde ale recipe.</NOTES>
    <FERMENTABLES>
      <FERMENTABLE>
        <NAME>Pale Malt (2-Row)</NAME>
        <AMOUNT>4.0</AMOUNT>
        <COLOR>3.0</COLOR>
        <YIELD>78.0</YIELD>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Carahell</NAME>
        <AMOUNT>0.5</AMOUNT>
        <COLOR>10.0</COLOR>
        <YIELD>75.0</YIELD>
      </FERMENTABLE>
    </FERMENTABLES>
    <HOPS>
      <HOP>
        <NAME>Centennial</NAME>
        <AMOUNT>0.025</AMOUNT>
        <ALPHA>10.0</ALPHA>
        <USE>Boil</USE>
        <TIME>60</TIME>
      </HOP>
      <HOP>
        <NAME>Cascade</NAME>
        <AMOUNT>0.035</AMOUNT>
        <ALPHA>6.0</ALPHA>
        <USE>Dry Hop</USE>
        <TIME>3</TIME>
      </HOP>
    </HOPS>
    <YEASTS>
      <YEAST>
        <NAME>SafAle US-05</NAME>
        <ATTENUATION>80.0</ATTENUATION>
      </YEAST>
    </YEASTS>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		expect(recipes).toHaveLength(1);

		const recipe = recipes[0];
		expect(recipe.name).toBe('Centennial Blonde');
		expect(recipe.beerStyle).toBe('Blonde Ale');
		expect(recipe.batchSizeLiters).toBe(20.0);
		expect(recipe.boilTimeMinutes).toBe(60);
		expect(recipe.efficiencyPercent).toBe(75.0);
		expect(recipe.description).toBe('Crisp blonde ale recipe.');

		// Fermentables check
		const fermentables = recipe.ingredients.filter((i) => i.type === 'Fermentable');
		expect(fermentables).toHaveLength(2);
		expect(fermentables[0].name).toBe('Pale Malt (2-Row)');
		expect(fermentables[0].amount).toBe(4.0);
		expect(fermentables[0].unit).toBe('kg');
		expect(fermentables[1].name).toBe('Carahell');
		expect(fermentables[1].amount).toBe(0.5);

		// Hops conversion check: 0.025 kg -> 25 g, 0.035 kg -> 35 g
		const hops = recipe.ingredients.filter((i) => i.type === 'Hop');
		expect(hops).toHaveLength(2);
		expect(hops[0].name).toBe('Centennial');
		expect(hops[0].amount).toBe(25);
		expect(hops[0].unit).toBe('g');
		expect(hops[0].usage).toBe('Boil');

		expect(hops[1].name).toBe('Cascade');
		expect(hops[1].amount).toBe(35);
		expect(hops[1].unit).toBe('g');
		expect(hops[1].usage).toBe('DryHop');

		// Yeast check
		const yeasts = recipe.ingredients.filter((i) => i.type === 'Yeast');
		expect(yeasts).toHaveLength(1);
		expect(yeasts[0].name).toBe('SafAle US-05');
		expect(recipe.mashSteps).toHaveLength(1);
		expect(recipe.mashSteps[0].name).toBe('Saccharification Rest');
		expect(recipe.mashSteps[0].temperatureC).toBe(65.0);
		expect(recipe.mashSteps[0].durationMinutes).toBe(60);
	});

	it('parses multi-step mash profile from BeerXML', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>German Pilsner</NAME>
    <STYLE><NAME>German Pils</NAME></STYLE>
    <BATCH_SIZE>20.0</BATCH_SIZE>
    <MASH>
      <NAME>Hochkurz Mash</NAME>
      <MASH_STEPS>
        <MASH_STEP>
          <NAME>Protein Rest</NAME>
          <VERSION>1</VERSION>
          <TYPE>Infusion</TYPE>
          <STEP_TEMP>52.0</STEP_TEMP>
          <STEP_TIME>20</STEP_TIME>
          <RAMP_TIME>5</RAMP_TIME>
          <INFUSE_AMOUNT>15.0</INFUSE_AMOUNT>
          <DESCRIPTION>Degrade proteins and beta-glucans</DESCRIPTION>
        </MASH_STEP>
        <MASH_STEP>
          <NAME>Beta Amylase</NAME>
          <VERSION>1</VERSION>
          <TYPE>Temperature</TYPE>
          <STEP_TEMP>63.0</STEP_TEMP>
          <STEP_TIME>40</STEP_TIME>
        </MASH_STEP>
        <MASH_STEP>
          <NAME>Mash Out</NAME>
          <VERSION>1</VERSION>
          <TYPE>Temperature</TYPE>
          <STEP_TEMP>76.0</STEP_TEMP>
          <STEP_TIME>10</STEP_TIME>
        </MASH_STEP>
      </MASH_STEPS>
    </MASH>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		expect(recipes).toHaveLength(1);
		const steps = recipes[0].mashSteps;
		expect(steps).toHaveLength(3);

		expect(steps[0].stepOrder).toBe(1);
		expect(steps[0].name).toBe('Protein Rest');
		expect(steps[0].type).toBe('Infusion');
		expect(steps[0].temperatureC).toBe(52.0);
		expect(steps[0].durationMinutes).toBe(20);
		expect(steps[0].rampTimeMinutes).toBe(5);
		expect(steps[0].infuseAmountLiters).toBe(15.0);
		expect(steps[0].notes).toBe('Degrade proteins and beta-glucans');

		expect(steps[1].stepOrder).toBe(2);
		expect(steps[1].name).toBe('Beta Amylase');
		expect(steps[1].type).toBe('Temperature');
		expect(steps[1].temperatureC).toBe(63.0);
		expect(steps[1].durationMinutes).toBe(40);

		expect(steps[2].stepOrder).toBe(3);
		expect(steps[2].name).toBe('Mash Out');
		expect(steps[2].temperatureC).toBe(76.0);
		expect(steps[2].durationMinutes).toBe(10);
	});

	it('rejects XML with DOCTYPE definitions to defend against XXE attacks', () => {
		const maliciousXml = `<?xml version="1.0"?>
<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
<RECIPES>
  <RECIPE>
    <NAME>&xxe;</NAME>
  </RECIPE>
</RECIPES>`;

		expect(() => parseBeerXml(maliciousXml)).toThrow(/DOCTYPE or ENTITY/);
	});

	it('rejects XML with ENTITY definitions to defend against entity bombs', () => {
		const entityBombXml = `<RECIPES>
<!ENTITY lol "lol">
<RECIPE><NAME>&lol;</NAME></RECIPE>
</RECIPES>`;

		expect(() => parseBeerXml(entityBombXml)).toThrow(/DOCTYPE or ENTITY/);
	});

	it('throws an error for empty content', () => {
		expect(() => parseBeerXml('')).toThrow(/empty/);
	});

	it('normalizes EBC color values to canonical SRM', () => {
		const xml = `<?xml version="1.0"?>
<RECIPES>
  <RECIPE>
    <NAME>Munich Export</NAME>
    <FERMENTABLES>
      <FERMENTABLE>
        <NAME>Munich Malt</NAME>
        <AMOUNT>4.0</AMOUNT>
        <COLOR>20.0</COLOR>
        <COLOR_UNIT>EBC</COLOR_UNIT>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Carafa Special III</NAME>
        <AMOUNT>0.2</AMOUNT>
        <COLOR>1400.0</COLOR>
      </FERMENTABLE>
    </FERMENTABLES>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		expect(recipes).toHaveLength(1);
		const f1 = recipes[0].ingredients[0];
		const f2 = recipes[0].ingredients[1];

		// 20 EBC / 1.97 ≈ 10.15 -> 10.2
		expect(f1.colorSrm).toBeCloseTo(10.2, 1);
		// 1400 (>600 threshold) / 1.97 ≈ 710.7
		expect(f2.colorSrm).toBeCloseTo(710.7, 1);
	});

	it('throws an error for missing root tag', () => {
		expect(() => parseBeerXml('<SOME_OTHER_XML></SOME_OTHER_XML>')).toThrow(/Missing <RECIPES>/);
	});

	it('parses fermentation temperatures and stages from BeerXML recipe', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Czech Pilsner</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <PRIMARY_TEMP>10.0</PRIMARY_TEMP>
    <PRIMARY_AGE>12</PRIMARY_AGE>
    <SECONDARY_TEMP>16.0</SECONDARY_TEMP>
    <SECONDARY_AGE>3</SECONDARY_AGE>
    <TERTIARY_TEMP>2.0</TERTIARY_TEMP>
    <TERTIARY_AGE>14</TERTIARY_AGE>
    <FERMENTABLES>
      <FERMENTABLE>
        <NAME>Pilsner Malt</NAME>
        <AMOUNT>5.0</AMOUNT>
      </FERMENTABLE>
    </FERMENTABLES>
    <YEASTS>
      <YEAST>
        <NAME>Saflager W-34/70</NAME>
      </YEAST>
    </YEASTS>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		expect(recipes).toHaveLength(1);
		const fSteps = recipes[0].fermentationSteps;
		expect(fSteps).toBeDefined();
		expect(fSteps.length).toBeGreaterThanOrEqual(3);
		expect(fSteps[0].type).toBe('Primary');
		expect(fSteps[0].targetTemperatureC).toBe(10.0);
		expect(fSteps[0].durationDays).toBe(12);

		expect(fSteps[1].type).toBe('Secondary');
		expect(fSteps[1].targetTemperatureC).toBe(16.0);
		expect(fSteps[1].durationDays).toBe(3);

		expect(fSteps[2].type).toBe('ColdCrash');
		expect(fSteps[2].targetTemperatureC).toBe(2.0);
		expect(fSteps[2].durationDays).toBe(14);
	});

	it('derives default fermentation temperature from yeast min/max temperature tags when no stages present', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Yeast Temp Fallback</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <FERMENTABLES><FERMENTABLE><NAME>Malt</NAME><AMOUNT>4</AMOUNT></FERMENTABLE></FERMENTABLES>
    <YEASTS>
      <YEAST>
        <NAME>Kveik</NAME>
        <MIN_TEMPERATURE>30.0</MIN_TEMPERATURE>
        <MAX_TEMPERATURE>40.0</MAX_TEMPERATURE>
      </YEAST>
    </YEASTS>
  </RECIPE>
</RECIPES>`;
		const recipes = parseBeerXml(xml);
		expect(recipes[0].fermentationSteps).toHaveLength(1);
		expect(recipes[0].fermentationSteps[0].targetTemperatureC).toBe(35.0);
	});

	it('parses user recipe Fränkisches Rotbier - Röd Zwickl with mash steps and yeast fermentation temperature', () => {
		const userRecipeXml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Fränkisches Rotbier - Röd Zwickl</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <BREWER>Hemmabryggaren</BREWER>
    <BATCH_SIZE>20.0</BATCH_SIZE>
    <BOIL_SIZE>25.0</BOIL_SIZE>
    <BOIL_TIME>60</BOIL_TIME>
    <EFFICIENCY>75.0</EFFICIENCY>
    <STYLE>
      <NAME>Kellerbier: Amber Kellerbier</NAME>
      <CATEGORY>Amber Malty European Lager</CATEGORY>
      <VERSION>1</VERSION>
      <CATEGORY_NUMBER>07</CATEGORY_NUMBER>
      <STYLE_LETTER>A</STYLE_LETTER>
      <STYLE_GUIDE>BJCP 2021</STYLE_GUIDE>
      <TYPE>Lager</TYPE>
    </STYLE>
    <FERMENTABLES>
      <FERMENTABLE>
        <NAME>Münchermalt Typ 1 (Weyermann)</NAME>
        <VERSION>1</VERSION>
        <TYPE>Grain</TYPE>
        <AMOUNT>2.2</AMOUNT>
        <YIELD>78.0</YIELD>
        <COLOR>15.0</COLOR>
        <ORIGIN>Tyskland</ORIGIN>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Pilsnermalt (Weyermann)</NAME>
        <VERSION>1</VERSION>
        <TYPE>Grain</TYPE>
        <AMOUNT>2.0</AMOUNT>
        <YIELD>81.0</YIELD>
        <COLOR>3.5</COLOR>
        <ORIGIN>Tyskland</ORIGIN>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Caraamber (Weyermann)</NAME>
        <VERSION>1</VERSION>
        <TYPE>Grain</TYPE>
        <AMOUNT>0.3</AMOUNT>
        <YIELD>75.0</YIELD>
        <COLOR>70.0</COLOR>
        <ORIGIN>Tyskland</ORIGIN>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Carared (Weyermann)</NAME>
        <VERSION>1</VERSION>
        <TYPE>Grain</TYPE>
        <AMOUNT>0.3</AMOUNT>
        <YIELD>75.0</YIELD>
        <COLOR>50.0</COLOR>
        <ORIGIN>Tyskland</ORIGIN>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Melanoidinmalt (Weyermann)</NAME>
        <VERSION>1</VERSION>
        <TYPE>Grain</TYPE>
        <AMOUNT>0.2</AMOUNT>
        <YIELD>75.0</YIELD>
        <COLOR>70.0</COLOR>
        <ORIGIN>Tyskland</ORIGIN>
      </FERMENTABLE>
    </FERMENTABLES>
    <HOPS>
      <HOP>
        <NAME>Hallertauer Mittelfrüh</NAME>
        <VERSION>1</VERSION>
        <ALPHA>4.0</ALPHA>
        <AMOUNT>0.035</AMOUNT>
        <USE>Boil</USE>
        <TIME>60.0</TIME>
      </HOP>
      <HOP>
        <NAME>Spalter Select</NAME>
        <VERSION>1</VERSION>
        <ALPHA>4.5</ALPHA>
        <AMOUNT>0.025</AMOUNT>
        <USE>Boil</USE>
        <TIME>20.0</TIME>
      </HOP>
      <HOP>
        <NAME>Hallertauer Mittelfrüh</NAME>
        <VERSION>1</VERSION>
        <ALPHA>4.0</ALPHA>
        <AMOUNT>0.020</AMOUNT>
        <USE>Boil</USE>
        <TIME>5.0</TIME>
      </HOP>
    </HOPS>
    <YEASTS>
      <YEAST>
        <NAME>Saflager W-34/70</NAME>
        <VERSION>1</VERSION>
        <TYPE>Lager</TYPE>
        <FORM>Dry</FORM>
        <AMOUNT>0.023</AMOUNT>
        <AMOUNT_IS_WEIGHT>TRUE</AMOUNT_IS_WEIGHT>
        <LABORATORY>Fermentis</LABORATORY>
        <PRODUCT_ID>W-34/70</PRODUCT_ID>
        <MIN_TEMPERATURE>9.0</MIN_TEMPERATURE>
        <MAX_TEMPERATURE>15.0</MAX_TEMPERATURE>
        <ATTENUATION>83.0</ATTENUATION>
      </YEAST>
    </YEASTS>
    <MASH>
      <NAME>Enkel infusionsmäskning med utmäskning</NAME>
      <VERSION>1</VERSION>
      <GRAIN_TEMP>20.0</GRAIN_TEMP>
      <MASH_STEPS>
        <MASH_STEP>
          <NAME>Mäskning</NAME>
          <VERSION>1</VERSION>
          <TYPE>Infusion</TYPE>
          <STEP_TEMP>67.0</STEP_TEMP>
          <STEP_TIME>60.0</STEP_TIME>
          <INFUSE_AMOUNT>15.0</INFUSE_AMOUNT>
        </MASH_STEP>
        <MASH_STEP>
          <NAME>Utmäskning</NAME>
          <VERSION>1</VERSION>
          <TYPE>Temperature</TYPE>
          <STEP_TEMP>78.0</STEP_TEMP>
          <STEP_TIME>10.0</STEP_TIME>
        </MASH_STEP>
      </MASH_STEPS>
    </MASH>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(userRecipeXml);
		expect(recipes).toHaveLength(1);
		const recipe = recipes[0];

		expect(recipe.name).toBe('Fränkisches Rotbier - Röd Zwickl');
		expect(recipe.beerStyle).toBe('Kellerbier: Amber Kellerbier');
		expect(recipe.batchSizeLiters).toBe(20.0);
		expect(recipe.boilTimeMinutes).toBe(60);
		expect(recipe.efficiencyPercent).toBe(75.0);

		// Verify 2 mash steps parsed accurately
		expect(recipe.mashSteps).toHaveLength(2);
		expect(recipe.mashSteps[0].name).toBe('Mäskning');
		expect(recipe.mashSteps[0].temperatureC).toBe(67.0);
		expect(recipe.mashSteps[0].durationMinutes).toBe(60);
		expect(recipe.mashSteps[0].type).toBe('Infusion');
		expect(recipe.mashSteps[0].infuseAmountLiters).toBe(15.0);

		expect(recipe.mashSteps[1].name).toBe('Utmäskning');
		expect(recipe.mashSteps[1].temperatureC).toBe(78.0);
		expect(recipe.mashSteps[1].durationMinutes).toBe(10);
		expect(recipe.mashSteps[1].type).toBe('Temperature');

		// Verify fermentation temp derived from Saflager W-34/70 (9.0°C to 15.0°C -> 12.0°C)
		expect(recipe.fermentationSteps).toHaveLength(1);
		expect(recipe.fermentationSteps[0].targetTemperatureC).toBe(12.0);
		expect(recipe.fermentationSteps[0].type).toBe('Primary');

		// Verify hops are in grams
		const hops = recipe.ingredients.filter((i) => i.type === 'Hop');
		expect(hops).toHaveLength(3);
		expect(hops[0].amount).toBe(35); // 0.035 kg -> 35 g
		expect(hops[1].amount).toBe(25); // 0.025 kg -> 25 g
		expect(hops[2].amount).toBe(20); // 0.020 kg -> 20 g
	});

	it('auto-converts Fahrenheit temperatures in mash steps and fermentation stages', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Fahrenheit Recipe</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <PRIMARY_TEMP>68.0</PRIMARY_TEMP>
    <PRIMARY_AGE>14</PRIMARY_AGE>
    <MASH>
      <MASH_STEPS>
        <MASH_STEP>
          <NAME>Sac Rest</NAME>
          <STEP_TEMP>152.6</STEP_TEMP>
          <STEP_TIME>60</STEP_TIME>
        </MASH_STEP>
      </MASH_STEPS>
    </MASH>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		expect(recipes[0].mashSteps[0].temperatureC).toBe(67.0); // (152.6 - 32) * 5/9 = 67.0
		expect(recipes[0].fermentationSteps[0].targetTemperatureC).toBe(20.0); // (68.0 - 32) * 5/9 = 20.0
	});

	it('supports explicit POTENTIAL tag in both SG and PPG format', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Potential XML Test</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <FERMENTABLES>
      <FERMENTABLE>
        <NAME>Pilsner Malt</NAME>
        <AMOUNT>4.5</AMOUNT>
        <POTENTIAL>1.038</POTENTIAL>
      </FERMENTABLE>
      <FERMENTABLE>
        <NAME>Munich Malt</NAME>
        <AMOUNT>1.0</AMOUNT>
        <POTENTIAL>37</POTENTIAL>
      </FERMENTABLE>
    </FERMENTABLES>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		const pilsner = recipes[0].ingredients.find((i) => i.name === 'Pilsner Malt');
		const munich = recipes[0].ingredients.find((i) => i.name === 'Munich Malt');

		expect(pilsner?.potentialGravity).toBe(1.038);
		expect(munich?.potentialGravity).toBe(1.037);
	});

	it('parses hop forms (Pellet, Leaf, Plug) and yeast forms (Dry, Liquid, Slant, Culture)', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Form Test Recipe</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <HOPS>
      <HOP>
        <NAME>Centennial Pellet</NAME>
        <AMOUNT>0.020</AMOUNT>
        <FORM>Pellet</FORM>
      </HOP>
      <HOP>
        <NAME>Cascade Whole Leaf</NAME>
        <AMOUNT>0.030</AMOUNT>
        <FORM>Leaf</FORM>
      </HOP>
      <HOP>
        <NAME>Goldings Plug</NAME>
        <AMOUNT>0.015</AMOUNT>
        <FORM>Plug</FORM>
      </HOP>
    </HOPS>
    <YEASTS>
      <YEAST>
        <NAME>Dry Yeast</NAME>
        <FORM>Dry</FORM>
        <AMOUNT>0.0115</AMOUNT>
      </YEAST>
      <YEAST>
        <NAME>Liquid Yeast</NAME>
        <FORM>Liquid</FORM>
        <AMOUNT>0.125</AMOUNT>
      </YEAST>
      <YEAST>
        <NAME>Slant Yeast</NAME>
        <FORM>Slant</FORM>
        <AMOUNT>0.005</AMOUNT>
      </YEAST>
      <YEAST>
        <NAME>Culture Yeast</NAME>
        <FORM>Culture</FORM>
        <AMOUNT>1.0</AMOUNT>
      </YEAST>
    </YEASTS>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		const hops = recipes[0].ingredients.filter((i) => i.type === 'Hop');
		expect(hops[0].form).toBe('Pellet');
		expect(hops[1].form).toBe('Leaf');
		expect(hops[2].form).toBe('Plug');

		const yeasts = recipes[0].ingredients.filter((i) => i.type === 'Yeast');
		expect(yeasts[0].form).toBe('Dry');
		expect(yeasts[1].form).toBe('Liquid');
		expect(yeasts[2].form).toBe('Slant');
		expect(yeasts[3].form).toBe('Culture');
	});

	it('accurately converts BeerXML yeast amount in kg to grams and liters to ml or packages', () => {
		const xml = `<?xml version="1.0" encoding="UTF-8"?>
<RECIPES>
  <RECIPE>
    <NAME>Yeast Amount Calculation</NAME>
    <VERSION>1</VERSION>
    <TYPE>All Grain</TYPE>
    <YEASTS>
      <YEAST>
        <NAME>Double Dry Pitch (0.023 kg)</NAME>
        <FORM>Dry</FORM>
        <AMOUNT>0.023</AMOUNT>
        <AMOUNT_IS_WEIGHT>TRUE</AMOUNT_IS_WEIGHT>
      </YEAST>
      <YEAST>
        <NAME>Single Dry Pitch (0.0115 kg)</NAME>
        <FORM>Dry</FORM>
        <AMOUNT>0.0115</AMOUNT>
      </YEAST>
      <YEAST>
        <NAME>Direct Grams Exporter (22 g)</NAME>
        <FORM>Dry</FORM>
        <AMOUNT>22</AMOUNT>
      </YEAST>
      <YEAST>
        <NAME>Liquid Smack Pack (0.125 L)</NAME>
        <FORM>Liquid</FORM>
        <AMOUNT>0.125</AMOUNT>
        <AMOUNT_IS_WEIGHT>FALSE</AMOUNT_IS_WEIGHT>
      </YEAST>
      <YEAST>
        <NAME>Liquid Vial Pack (1.0)</NAME>
        <FORM>Liquid</FORM>
        <AMOUNT>1.0</AMOUNT>
      </YEAST>
    </YEASTS>
  </RECIPE>
</RECIPES>`;

		const recipes = parseBeerXml(xml);
		const yeasts = recipes[0].ingredients.filter((i) => i.type === 'Yeast');

		// 0.023 kg -> 23 g
		expect(yeasts[0].amount).toBe(23);
		expect(yeasts[0].unit).toBe('g');

		// 0.0115 kg -> 11.5 g
		expect(yeasts[1].amount).toBe(11.5);
		expect(yeasts[1].unit).toBe('g');

		// 22 g -> 22 g
		expect(yeasts[2].amount).toBe(22);
		expect(yeasts[2].unit).toBe('g');

		// 0.125 L -> 125 ml
		expect(yeasts[3].amount).toBe(125);
		expect(yeasts[3].unit).toBe('ml');

		// 1.0 liquid pack -> 1 pkg
		expect(yeasts[4].amount).toBe(1);
		expect(yeasts[4].unit).toBe('pkg');
	});
});
