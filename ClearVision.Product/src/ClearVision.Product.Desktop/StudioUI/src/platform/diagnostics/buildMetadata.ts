export interface StudioUiBuildMetadata {
  readonly name: string;
  readonly version: string;
  readonly basePath: '/studio/';
  readonly mode: string;
}

export const studioUiBuildMetadata: StudioUiBuildMetadata = Object.freeze({
  ...__STUDIO_UI_BUILD__
});
