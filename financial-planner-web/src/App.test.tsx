import { render, screen } from '@testing-library/react';
import { expect, test } from '@rstest/core';
import React from 'react';
import App from './App';

test('renders the application baseline', () => {
  render(<App />);

  expect(screen.getByRole('heading', { name: 'Financial Planner' })).toBeDefined();
});
