import { createMemoryHistory, type Router } from 'vue-router';
import { createStudioRouter } from '@/app/router';

export function createTestRouter(): Router {
  return createStudioRouter(createMemoryHistory());
}
