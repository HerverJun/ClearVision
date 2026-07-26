import 'vue-router';

declare module 'vue-router' {
  interface RouteMeta {
    readonly title?: string;
    readonly breadcrumb?: string;
    readonly requiresSession?: boolean;
    readonly public?: boolean;
    readonly setupOnly?: boolean;
    readonly allowedRoles?: readonly string[];
    readonly productProfile?: 'default' | 'stations-read';
    readonly requiredFeatureFlag?: string;
    readonly internal?: boolean;
    readonly workspaceMode?: boolean;
  }
}

export {};
