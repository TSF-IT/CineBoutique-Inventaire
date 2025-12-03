import { defineConfig, globalIgnores } from 'eslint/config';
import inventoryWebConfig from './src/inventory-web/eslint.config.js';

export default defineConfig([
  globalIgnores([
    'node_modules',
    'dist',
    'dev-dist',
    'src/inventory-web/dev-dist/**',
    'src/inventory-web/dist/**',
    'build',
    'coverage',
    'reports',
    'logs',
    '.cache',
  ]),
  ...inventoryWebConfig,
]);
