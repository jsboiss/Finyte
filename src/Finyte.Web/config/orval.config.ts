import { defineConfig } from 'orval'

export default defineConfig({
  finyte: {
    input: 'http://localhost:5073/openapi/v1.json',
    output: {
      target: '../app/api/generated/finyteApi.ts',
      client: 'react-query',
      httpClient: 'axios',
      override: {
        mutator: {
          path: '../app/api/httpClient.ts',
          name: 'httpClient',
        },
      },
    },
  },
})
