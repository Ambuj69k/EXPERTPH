import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: the React app calls /api/* on its own origin and Vite forwards to the .NET API.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5101', changeOrigin: true }, // PharmaOS.Api "http" launch profile
    },
  },
});
