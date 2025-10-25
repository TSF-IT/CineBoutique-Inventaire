/** @type {import('tailwindcss').Config} */
const config = {
  darkMode: ['class', '[data-theme="dark"]'],
  theme: {
    extend: {
      screens: {
        xs: '360px',
        '2xl': '1440px',
        '3xl': '1920px',
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'BlinkMacSystemFont', '"Segoe UI"', 'sans-serif'],
      },
      boxShadow: {
        'elev-1': '0 1px 2px rgba(16,24,40,.04), 0 4px 12px rgba(16,24,40,.06)',
        'elev-2': '0 2px 6px rgba(16,24,40,.06), 0 10px 24px rgba(16,24,40,.10)',
        soft: 'var(--shadow-soft)',
        panel: 'var(--cb-card-shadow)',
        'panel-soft': 'var(--cb-card-shadow-soft)',
        focus: 'var(--focus-ring-shadow)',
        fab: '0 26px 52px -24px rgba(79, 70, 229, 0.55)',
      },
      borderRadius: {
        xl: '1rem',
        '2xl': '1.5rem',
        '3xl': '2rem',
        xxl: '24px',
      },
      colors: {
        brand: {
          50: 'var(--color-brand-50)',
          100: 'var(--color-brand-100)',
          200: 'var(--color-brand-200)',
          300: 'var(--color-brand-300)',
          400: 'var(--color-brand-400)',
          DEFAULT: 'var(--color-brand-500)',
          500: 'var(--color-brand-500)',
          600: 'var(--color-brand-600)',
          700: 'var(--color-brand-700)',
          800: 'var(--color-brand-800)',
          900: 'var(--color-brand-900)',
        },
        surface: {
          DEFAULT: 'var(--cb-surface)',
          soft: 'var(--cb-surface-soft)',
          strong: 'var(--cb-surface-strong)',
        },
        border: {
          DEFAULT: 'var(--cb-border)',
          strong: 'var(--cb-border-strong)',
        },
        text: {
          DEFAULT: 'var(--cb-text)',
          strong: 'var(--cb-text)',
          muted: 'var(--cb-muted)',
        },
        stroke: 'var(--stroke)',
        primary: 'var(--color-brand-500)',
        focus: 'var(--focus-ring-border)',
        backdrop: 'var(--cb-backdrop)',
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
