/** Minimal inline icon set (24x24, 1.75 stroke). Keeps the bundle free of an icon dependency. */
import type { ReactNode, SVGProps } from 'react';

type IconComponent = (props: SVGProps<SVGSVGElement>) => JSX.Element;

function icon(paths: ReactNode): IconComponent {
  return function Icon(props: SVGProps<SVGSVGElement>) {
    return (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...props}>
        {paths}
      </svg>
    );
  };
}

export const Icons = {
  overview: icon(<><rect x="3" y="3" width="7" height="9" rx="1.5" /><rect x="14" y="3" width="7" height="5" rx="1.5" /><rect x="14" y="12" width="7" height="9" rx="1.5" /><rect x="3" y="16" width="7" height="5" rx="1.5" /></>),
  ingestion: icon(<><path d="M12 3v12m0 0 4-4m-4 4-4-4" /><path d="M4 17v1a3 3 0 0 0 3 3h10a3 3 0 0 0 3-3v-1" /></>),
  reconciliation: icon(<path d="M4 7h11m0 0-3-3m3 3-3 3M20 17H9m0 0 3-3m-3 3 3 3" />),
  cases: icon(<><rect x="3" y="7" width="18" height="13" rx="2" /><path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18" /></>),
  rules: icon(<><path d="M8 6h13M8 12h13M8 18h13" /><path d="m3 6 1 1 2-2M3 12l1 1 2-2M3 18l1 1 2-2" /></>),
  operations: icon(<path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />),
  sources: icon(<><ellipse cx="12" cy="6" rx="8" ry="3" /><path d="M4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6" /><path d="M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6" /></>),
  sun: icon(<><circle cx="12" cy="12" r="4" /><path d="M12 2v2m0 16v2M4.9 4.9l1.4 1.4m11.4 11.4 1.4 1.4M2 12h2m16 0h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></>),
  moon: icon(<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" />),
  logout: icon(<path d="M9 21H6a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3M16 17l5-5-5-5M21 12H9" />),
  refresh: icon(<path d="M21 12a9 9 0 1 1-2.6-6.4M21 3v6h-6" />),
  plus: icon(<path d="M12 5v14M5 12h14" />),
  upload: icon(<><path d="M12 16V4m0 0 4 4m-4-4-4 4" /><path d="M4 17v1a3 3 0 0 0 3 3h10a3 3 0 0 0 3-3v-1" /></>),
  play: icon(<path d="M6 4.5v15l13-7.5z" />),
  key: icon(<><circle cx="8" cy="15" r="4" /><path d="m10.8 12.2 8.7-8.7M15 7l2 2M18 4l2 2" /></>),
  external: icon(<path d="M14 4h6v6M20 4l-9 9M19 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h5" />),
  check: icon(<path d="m5 12 5 5L20 7" />),
  x: icon(<path d="M6 6l12 12M18 6 6 18" />),
  warning: icon(<path d="M12 9v4m0 4h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" />),
  menu: icon(<path d="M4 6h16M4 12h16M4 18h16" />),
  info: icon(<path d="M12 8h.01M11 12h1v4h1M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0Z" />),
  trash: icon(<path d="M4 7h16M10 11v6m4-6v6M6 7l1 13h10l1-13M9 7V4h6v3" />),
  edit: icon(<><path d="M4 20h4l10.5-10.5a2 2 0 0 0 0-2.8l-1.2-1.2a2 2 0 0 0-2.8 0L4 16v4Z" /><path d="m13.5 6.5 4 4" /></>),
} satisfies Record<string, IconComponent>;

export type IconName = keyof typeof Icons;
