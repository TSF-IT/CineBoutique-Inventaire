/** @type {import('tailwindcss').Config} */
const config = {
  content: [
    './index.html',
    './src/**/*.{js,jsx,ts,tsx}',
    './src/**/*.css',
  ],
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
        primary: {
          50: '#EEF2FF',
          100: '#E0E7FF',
          200: '#C7D2FE',
          300: '#A5B4FC',
          400: '#818CF8',
          500: '#6366F1',
          600: '#4F46E5',
          700: '#4338CA',
          800: '#3730A3',
          900: '#312E81',
          DEFAULT: '#6366F1',
        },
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
