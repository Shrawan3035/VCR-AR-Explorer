// Typed access to the shared project data (../../data is also used by Unity and the poster).
import raw from '../../data/components.json';
import flowRaw from '../../data/flow_paths.json';

export interface ComponentInfo {
  id: string;
  number: number;
  node: string;
  main: boolean;
  illustrative?: boolean;
  line?: boolean;
  name: string;
  subtitle: string;
  side: string;
  function: string;
  principle: string;
  inlet: string;
  outlet: string;
  cycleRole: string;
  application: string;
  student: string;
  heat?: 'rejected' | 'absorbed';
  explode: [number, number, number];
}

export interface Stage {
  id: number;
  title: string;
  from: string;
  component: string;
  text: string;
  ideal: string;
  real: string;
}

export type FlowState = 'compression' | 'hp_vapour' | 'hp_condensing' | 'hp_liquid' | 'lp_mix' | 'lp_evaporating' | 'lp_vapour';

export interface FlowSegment {
  component: string;
  state: FlowState;
  points: [number, number, number][];
}

export const project = raw as unknown as {
  title: string;
  disclaimer: string;
  cycleOrder: string[];
  labData: Record<string, string> & { team?: { name: string; roll: string }[] };
  stages: Stage[];
  components: ComponentInfo[];
};

export const components = project.components;
export const team = project.labData.team ?? [];
export const branch = project.labData.branch ?? '';
export const guide = project.labData.guide ?? '';
export const institution = project.labData.institution ?? '';
export const teamLine = team.map((m) => `${m.name} (${m.roll})`).join(' · ');
export const stages = project.stages;
export const flowSegments = (flowRaw as unknown as { segments: FlowSegment[] }).segments;

export const byId = (id: string | null | undefined) => components.find((c) => c.id === id);
export const byNode = (node: string) => components.find((c) => c.node === node);

/** Colour per refrigerant state (red = hot / high pressure, blue = cold / low pressure). */
export const stateColor: Record<FlowState, string> = {
  compression: '#e0583a',
  hp_vapour: '#e0442a',
  hp_condensing: '#ec7d3a',
  hp_liquid: '#f0a33c',
  lp_mix: '#5ccbe6',
  lp_evaporating: '#3aa6e0',
  lp_vapour: '#2f7fd0',
};

export const stateLabel: Record<FlowState, string> = {
  compression: 'Being compressed',
  hp_vapour: 'High-pressure hot vapour',
  hp_condensing: 'Condensing (vapour → liquid)',
  hp_liquid: 'High-pressure liquid',
  lp_mix: 'Low-pressure liquid–vapour mix',
  lp_evaporating: 'Evaporating (liquid → vapour)',
  lp_vapour: 'Low-pressure vapour',
};

export const asset = (p: string) => `${import.meta.env.BASE_URL}${p.replace(/^\//, '')}`;
export const MODEL_URL = asset('models/VCR_System.glb');
export const PDF_URL = asset('VCR-AR-Explorer-A3.pdf');
