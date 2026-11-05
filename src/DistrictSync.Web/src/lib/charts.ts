import { useTheme } from './theme';

/**
 * Chart colours validated for both surfaces (categorical slots in fixed order, an ordinal blue ramp
 * for priority, and theme-matched chrome). Text in charts always uses ink tokens, never series colours.
 */
export interface ChartTheme {
  series: string[];
  ordinal: string[];
  grid: string;
  axis: string;
  surface: string;
  tooltipBg: string;
  tooltipBorder: string;
  ink: string;
  ink2: string;
}

const LIGHT: ChartTheme = {
  series: ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4'],
  ordinal: ['#86b6ef', '#5598e7', '#2a78d6', '#1c5cab'],
  grid: '#e1e0d9',
  axis: '#c3c2b7',
  surface: '#fcfcfb',
  tooltipBg: '#fcfcfb',
  tooltipBorder: '#e1e0d9',
  ink: '#0b0b0b',
  ink2: '#52514e',
};

const DARK: ChartTheme = {
  series: ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181'],
  ordinal: ['#86b6ef', '#3987e5', '#256abf', '#184f95'],
  grid: '#2c2c2a',
  axis: '#383835',
  surface: '#1a1a19',
  tooltipBg: '#232322',
  tooltipBorder: '#383835',
  ink: '#f0efec',
  ink2: '#c3c2b7',
};

export function useChartTheme(): ChartTheme {
  const { resolved } = useTheme();
  return resolved === 'dark' ? DARK : LIGHT;
}

export function tooltipStyle(t: ChartTheme) {
  return {
    contentStyle: { background: t.tooltipBg, border: `1px solid ${t.tooltipBorder}`, borderRadius: 6, fontSize: 12, color: t.ink, boxShadow: '0 4px 16px rgba(0,0,0,0.12)' },
    labelStyle: { color: t.ink2, fontWeight: 600, marginBottom: 4 },
    itemStyle: { color: t.ink, padding: 0 },
  };
}
