export interface ParsedInteraction {
	required: boolean;
	message: string;
	code?: number;
}

export function extractInteraction(metricsJson?: string | null): ParsedInteraction {
	if (!metricsJson) return { required: false, message: '' };
	try {
		const raw = typeof metricsJson === 'string' ? JSON.parse(metricsJson) : metricsJson;
		if (!raw || typeof raw !== 'object') return { required: false, message: '' };

		const parseBool = (v: unknown): boolean => {
			if (typeof v === 'boolean') return v;
			if (typeof v === 'number') return v === 1;
			if (typeof v === 'string') return v.toLowerCase() === 'true' || v === '1';
			return false;
		};

		const parseNum = (v: unknown): number | null => {
			if (typeof v === 'number') return isNaN(v) ? null : v;
			if (typeof v === 'string') {
				const n = Number(v);
				return isNaN(n) ? null : n;
			}
			return null;
		};

		const code =
			parseNum(raw.interaction?.code) ??
			parseNum(raw.interaction_code) ??
			parseNum(raw.interactionCode);
		const hasActiveCode = code != null && code > 0;

		const required =
			parseBool(raw.interaction?.required) ||
			parseBool(raw.interaction_required) ||
			parseBool(raw.interactionRequired) ||
			hasActiveCode;

		const message =
			raw.interaction?.message ??
			raw.interaction_message ??
			raw.interactionMessage ??
			raw.prompt ??
			raw.alert ??
			'';

		return { required, message: String(message), code: code ?? undefined };
	} catch {
		return { required: false, message: '' };
	}
}
