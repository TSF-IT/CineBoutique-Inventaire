import autoprefixer from 'autoprefixer';

const normalizePlugin = (module) => module.default ?? module;

const tailwindcss = await import('@tailwindcss/postcss')
  .then(normalizePlugin)
  .catch(async (error) => {
    if (error?.code !== 'ERR_MODULE_NOT_FOUND' && error?.code !== 'MODULE_NOT_FOUND') {
      throw error;
    }

    return import('tailwindcss').then(normalizePlugin);
  });

export default {
  plugins: [tailwindcss, autoprefixer],
};
