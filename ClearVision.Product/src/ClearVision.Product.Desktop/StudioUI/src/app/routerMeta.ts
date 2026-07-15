import 'vue-router';

declare module 'vue-router' {
  interface RouteMeta {
    readonly title?: string;
    readonly breadcrumb?: string;
    readonly requiresSession?: boolean;
    readonly internal?: boolean;
  }
}

export {};
