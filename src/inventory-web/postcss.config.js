const normalizePlugin = (module) => module?.default ?? module;

const isModuleNotFound = (error) =>
  error?.code === 'ERR_MODULE_NOT_FOUND' || error?.code === 'MODULE_NOT_FOUND';

const loadTailwindPlugin = async () => {
  try {
    return normalizePlugin(await import('@tailwindcss/postcss'));
  } catch (error) {
    if (!isModuleNotFound(error)) {
      throw error;
    }

    const legacyPlugin = normalizePlugin(await import('tailwindcss').catch(() => null));

    if (typeof legacyPlugin === 'function' && legacyPlugin?.postcss === true) {
      return legacyPlugin;
    }

    throw new Error(
      'Le plugin PostCSS Tailwind est introuvable. Installez "@tailwindcss/postcss" (ou Tailwind CSS < 4) puis relancez Vite.'
    );
  }
};

const [tailwindcss, autoprefixer] = await Promise.all([
  loadTailwindPlugin(),
  import('autoprefixer').then(normalizePlugin),
]);

export default {
  plugins: [tailwindcss, autoprefixer],
};
