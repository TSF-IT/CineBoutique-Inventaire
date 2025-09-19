import { render, screen } from '@testing-library/react';
import App from './App';

describe('App', () => {
  it('affiche le titre CinéBoutique - Inventaire', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: 'CinéBoutique - Inventaire' })).toBeInTheDocument();
  });
});
