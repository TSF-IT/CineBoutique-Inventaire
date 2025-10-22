/** @type {import('tailwindcss').Config} */
const config = {
  darkMode: ['class', '[data-theme="dark"]'],
  theme: {
    extend: {
      boxShadow: {
        'elev-1': '0 1px 2px rgba(16,24,40,.04), 0 4px 12px rgba(16,24,40,.06)',
        'elev-2': '0 2px 6px rgba(16,24,40,.06), 0 10px 24px rgba(16,24,40,.10)',
      },
      borderRadius: {
        xxl: '24px',
      },
      colors: {
        surface: 'var(--surface)',
        surfaceMuted: 'var(--surface-muted)',
        textStrong: 'var(--text-strong)',
        textMuted: 'var(--text-muted)',
        stroke: 'var(--stroke)',
        primary: '#6366f1',
        product: {
          50: '#ECF9F8',
          200: '#B6E6E1',
          600: '#138D83',
          700: '#0F766E',
        },
      },
    },
  },
}

export default config
