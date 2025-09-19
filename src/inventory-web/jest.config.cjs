/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  setupFilesAfterEnv: ['<rootDir>/setupTests.ts'],
  moduleNameMapper: {
    '^.+\\.(css|less|scss|sass)$': 'identity-obj-proxy',
    '^.+\\.(svg|png|jpg|jpeg|gif)$': '<rootDir>/test/__mocks__/fileMock.ts',
    '^/.*\\.(svg|png|jpg|jpeg|gif)$': '<rootDir>/test/__mocks__/fileMock.ts'
  },
  testPathIgnorePatterns: ['/node_modules/', '/dist/'],
  transform: {
    '^.+\\.(ts|tsx)$': ['ts-jest', { tsconfig: './tsconfig.test.json' }],
  },
};
