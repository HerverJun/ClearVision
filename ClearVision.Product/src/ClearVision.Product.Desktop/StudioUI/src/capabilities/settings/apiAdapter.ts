import type { ApiTransport } from '@/platform/api';
import {
  buildGenericSectionWritePayload,
  type GenericSettingsSection,
  type SettingsWriteTaskContext
} from './contracts';
import {
  decodeAiModelsProjectionV1,
  decodeSettingsProjectionV1,
  decodeStationCommunicationProjectionV1,
  type AiModelsProjectionV1,
  type SettingsProjectionV1,
  type StationCommunicationProjectionV1
} from './decoder';

export interface SettingsApiAdapter {
  readGenericProjection(signal?: AbortSignal): Promise<SettingsProjectionV1>;
  readStationCommunication(signal?: AbortSignal): Promise<StationCommunicationProjectionV1>;
  readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1>;
  prepareGenericSectionWrite(
    section: GenericSettingsSection,
    value: Readonly<Record<string, unknown>>
  ): Readonly<Record<string, unknown>>;
}

function signalOptions(signal?: AbortSignal): Readonly<{ readonly signal?: AbortSignal }> {
  return signal ? { signal } : {};
}

export function createSettingsApiAdapter(api: ApiTransport): SettingsApiAdapter {
  return Object.freeze({
    async readGenericProjection(signal?: AbortSignal): Promise<SettingsProjectionV1> {
      return decodeSettingsProjectionV1(await api.get('settings', signalOptions(signal)));
    },
    async readStationCommunication(signal?: AbortSignal): Promise<StationCommunicationProjectionV1> {
      return decodeStationCommunicationProjectionV1(
        await api.get('station-communication/settings', signalOptions(signal))
      );
    },
    async readAiModels(signal?: AbortSignal): Promise<AiModelsProjectionV1> {
      return decodeAiModelsProjectionV1(await api.get('ai/models', signalOptions(signal)));
    },
    prepareGenericSectionWrite(
      section: GenericSettingsSection,
      value: Readonly<Record<string, unknown>>
    ): Readonly<Record<string, unknown>> {
      return buildGenericSectionWritePayload(section, value);
    }
  });
}

/**
 * Keeps future writer signatures tied to the shared transport context without
 * providing a concrete section save method in G1.
 */
export type SettingsSectionWriterContext = SettingsWriteTaskContext;
