import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// BASE_PATH is set by the deploy script for GitHub Pages project sites (e.g. "/VCR-AR-Explorer/").
export default defineConfig({
  base: process.env.BASE_PATH ?? '/',
  plugins: [react()],
  server: { fs: { allow: ['..'] } },
  build: {
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('@google/model-viewer')) return 'model-viewer';
          if (id.includes('node_modules/three')) return 'three';
        },
      },
    },
  },
});
