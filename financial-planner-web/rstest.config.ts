import { defineConfig } from '@rstest/core';

export default defineConfig({
  testEnvironment: 'jsdom',
  exclude: ['e2e/**'],
});
