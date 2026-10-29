import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': 'http://127.0.0.1:5283' },
  },
  build: {
    outDir: '../ResumeAgent.Api/wwwroot',
    emptyOutDir: true,
  },
});
