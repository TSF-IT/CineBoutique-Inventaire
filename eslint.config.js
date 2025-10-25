// Added by ESLint setup
import path from 'node:path';
// Added by ESLint setup
import { fileURLToPath } from 'node:url';

// Added by ESLint setup
import { FlatCompat } from '@eslint/eslintrc';
// Added by ESLint setup
import js from '@eslint/js';
// Added by ESLint setup
import { defineConfig } from 'eslint/config';

// Added by ESLint setup
import inventoryWebConfig from './src/inventory-web/eslint.config.js';

// Added by ESLint setup
const __filename = fileURLToPath(import.meta.url);
// Added by ESLint setup
const __dirname = path.dirname(__filename);

// Added by ESLint setup
const compat = new FlatCompat({
  // Added by ESLint setup
  baseDirectory: __dirname,
  // Added by ESLint setup
  recommendedConfig: js.configs.recommended,
  // Added by ESLint setup
  allConfig: js.configs.all,
});

// Added by ESLint setup
const typeScriptConfig = compat.config({
  // Added by ESLint setup
  root: true,
  // Added by ESLint setup
  ignorePatterns: [
    // Added by ESLint setup
    'node_modules/',
    // Added by ESLint setup
    'dist/',
    // Added by ESLint setup
    'dev-dist/',
    // Added by ESLint setup
    'build/',
    // Added by ESLint setup
    'coverage/',
    // Added by ESLint setup
    '.cache/',
    // Added by ESLint setup
    'playwright-report/',
    // Added by ESLint setup
    '**/*.d.ts',
  ],
  // Added by ESLint setup
  parser: '@typescript-eslint/parser',
  // Added by ESLint setup
  parserOptions: { ecmaVersion: 2022, sourceType: 'module', project: false },
  // Added by ESLint setup
  plugins: ['@typescript-eslint', 'import'],
  // Added by ESLint setup
  extends: [
    // Added by ESLint setup
    'eslint:recommended',
    // Added by ESLint setup
    'plugin:@typescript-eslint/recommended',
    // Added by ESLint setup
    'plugin:import/recommended',
    // Added by ESLint setup
    'plugin:import/typescript',
  ],
  // Added by ESLint setup
  settings: {
    // Added by ESLint setup
    'import/resolver': {
      // Added by ESLint setup
      typescript: {
        // Added by ESLint setup
        project: './src/inventory-web/tsconfig.json',
      },
    },
  },
  // Added by ESLint setup
  rules: {
    // Added by ESLint setup
    '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
    // Added by ESLint setup
    'import/order': ['warn', { alphabetize: { order: 'asc', caseInsensitive: true }, 'newlines-between': 'always' }],
  },
});

// Added by ESLint setup
export default defineConfig([
  // Added by ESLint setup
  ...typeScriptConfig,
  // Added by ESLint setup
  ...inventoryWebConfig,
]);
