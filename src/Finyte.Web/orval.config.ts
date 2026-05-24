import { defineConfig } from 'orval'

export default defineConfig({
  finyte: {
    input: 'http://localhost:5073/openapi/v1.json',
    output: {
      target: './src/api/generated/finyteApi.ts',
      client: 'react-query',
      httpClient: 'axios',
      override: {
        mutator: {
          path: './src/api/httpClient.ts',
          name: 'httpClient',
        },
      },
    },
  },
})
