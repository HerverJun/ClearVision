import { normalizeTheme } from '../../core/theme/theme.js';

export function installSettingsNormalizers(SettingsView) {
    Object.assign(SettingsView.prototype, {
        normalizeCameraTriggerMode(value) {
            const normalized = String(value || '').trim().toLowerCase();
            if (normalized === 'continuous') return 'Continuous';
            if (normalized === 'external' || normalized === 'hardware' || normalized === 'externalsignal') return 'External';
            return 'Software';
        }
        ,
        normalizeHardwareTriggerSource(value) {
            const normalized = String(value || '').trim();
            return normalized || 'Line0';
        }
        ,
        normalizeSoftwareTriggerSource(value) {
            const normalized = String(value || '').trim().toLowerCase();
            if (['enterphotoelectric', 'keyboardenter', 'usbenter', 'enter', 'photoelectricenter'].includes(normalized)) {
                return 'EnterPhotoelectric';
            }
            if (['serialphotoelectric', 'comphotoelectric', 'serial', 'com'].includes(normalized)) {
                return 'SerialPhotoelectric';
            }
            return 'Manual';
        }
        ,
        normalizeCameraTargetFrameRate(value) {
            const parsed = Number.parseInt(String(value ?? ''), 10);
            if (!Number.isFinite(parsed) || parsed <= 0) {
                return 30;
            }

            return Math.min(120, Math.max(1, parsed));
        }
        ,
        normalizeCameraPixelFormat(value) {
            const normalized = String(value || '')
                .trim()
                .replace(/[-_\s]/g, '')
                .toLowerCase();
            if (['rgb', 'rgb8'].includes(normalized)) return 'RGB8';
            if (['bgr', 'bgr8'].includes(normalized)) return 'BGR8';
            if (['bayerrg', 'bayerrg8'].includes(normalized)) return 'BayerRG8';
            if (['bayergb', 'bayergb8'].includes(normalized)) return 'BayerGB8';
            if (['bayergr', 'bayergr8'].includes(normalized)) return 'BayerGR8';
            if (['bayerbg', 'bayerbg8'].includes(normalized)) return 'BayerBG8';
            if (['mono', 'mono8', 'monochrome', 'gray', 'gray8', 'grey', 'grey8', 'moon'].includes(normalized)) return 'Mono8';
            return 'Mono8';
        }
        ,
        getCameraPixelFormatLabel(value) {
            const format = this.normalizeCameraPixelFormat(value);
            const labels = {
                Mono8: 'Mono8',
                RGB8: 'RGB8',
                BGR8: 'BGR8',
                BayerRG8: 'Bayer RG8',
                BayerGB8: 'Bayer GB8',
                BayerGR8: 'Bayer GR8',
                BayerBG8: 'Bayer BG8'
            };
            return labels[format] || 'Mono8';
        }
        ,
        normalizeEnterDebounceMs(value) {
            const parsed = Number.parseInt(String(value ?? ''), 10);
            if (!Number.isFinite(parsed)) {
                return 200;
            }

            return Math.min(5000, Math.max(0, parsed));
        }
        ,
        normalizeEnterTimeoutMs(value) {
            const parsed = Number.parseInt(String(value ?? ''), 10);
            if (!Number.isFinite(parsed) || parsed <= 0) {
                return 30000;
            }

            return Math.min(600000, Math.max(100, parsed));
        }
        ,
        normalizeSerialBaudRate(value) {
            const parsed = Number.parseInt(String(value ?? ''), 10);
            return Number.isFinite(parsed) && parsed > 0 ? parsed : 9600;
        }
        ,
        normalizeSerialDebounceMs(value) {
            return this.normalizeEnterDebounceMs(value);
        }
        ,
        normalizeSerialTimeoutMs(value) {
            return this.normalizeEnterTimeoutMs(value);
        }
        ,
        normalizeSerialPhotoelectricPortInfo(port) {
            const portName = String(port?.portName ?? port?.PortName ?? '').trim().toUpperCase();
            if (!portName) {
                return null;
            }

            const displayName = String(port?.displayName ?? port?.DisplayName ?? portName).trim() || portName;
            const isRecommended = (port?.isRecommended ?? port?.IsRecommended) === true;
            return { portName, displayName, isRecommended };
        }
        ,
        cloneCommunicationConfig(config) {
            return JSON.parse(JSON.stringify(config || this.getDefaultConfig().communication));
        }
        ,
        cloneTcpCommunicationConfig(config) {
            return JSON.parse(JSON.stringify(config || this.getDefaultConfig().tcpCommunication));
        }
        ,
        normalizeTcpMode(mode) {
            return String(mode || '').trim().toLowerCase() === 'server' ? 'Server' : 'Client';
        }
        ,
        normalizeTcpEncoding(encoding) {
            const normalized = String(encoding || '').trim().replace(/[-_\s]/g, '').toUpperCase();
            if (normalized === 'ASCII') return 'ASCII';
            if (normalized === 'GBK') return 'GBK';
            if (normalized === 'HEX') return 'HEX';
            return 'UTF8';
        }
        ,
        normalizeTcpFrameMode(frameMode) {
            const normalized = String(frameMode || '').trim().replace(/[-_\s]/g, '').toLowerCase();
            if (normalized === 'line') return 'Line';
            if (normalized === 'fixedlength') return 'FixedLength';
            if (normalized === 'hex') return 'Hex';
            return 'Raw';
        }
        ,
        normalizeTcpLineEnding(lineEnding) {
            const normalized = String(lineEnding || '').trim().toUpperCase();
            if (['CR', 'LF', 'CRLF'].includes(normalized)) return normalized;
            return 'None';
        }
        ,
        normalizeTcpPort(value) {
            const parsed = Number.parseInt(`${value ?? ''}`, 10);
            return Number.isInteger(parsed) && parsed >= 0 && parsed <= 65535 ? parsed : 0;
        }
        ,
        normalizeTcpTimeout(value) {
            const parsed = Number.parseInt(`${value ?? ''}`, 10);
            if (!Number.isInteger(parsed) || parsed <= 0) return 5000;
            return Math.min(600000, Math.max(100, parsed));
        }
        ,
        normalizeTcpProfile(profile = {}) {
            const id = String(profile?.id || '').trim() || `tcp_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`;
            return {
                id,
                name: String(profile?.name || 'TCP Profile').trim() || 'TCP Profile',
                enabled: profile?.enabled === true,
                mode: this.normalizeTcpMode(profile?.mode),
                remoteHost: String(profile?.remoteHost || '127.0.0.1').trim() || '127.0.0.1',
                remotePort: this.normalizeTcpPort(profile?.remotePort),
                localHost: String(profile?.localHost || '127.0.0.1').trim() || '127.0.0.1',
                localPort: this.normalizeTcpPort(profile?.localPort),
                encoding: this.normalizeTcpEncoding(profile?.encoding),
                frameMode: this.normalizeTcpFrameMode(profile?.frameMode),
                fixedLength: this.normalizeTcpPort(profile?.fixedLength),
                lineEnding: this.normalizeTcpLineEnding(profile?.lineEnding),
                timeoutMs: this.normalizeTcpTimeout(profile?.timeoutMs),
                keepAlive: profile?.keepAlive === true,
                reconnect: profile?.reconnect !== false,
                connectOnStartup: profile?.connectOnStartup === true,
                description: String(profile?.description || '').trim()
            };
        }
        ,
        normalizeTcpCommunicationConfig(tcpCommunication) {
            const sourceProfiles = Array.isArray(tcpCommunication?.profiles) ? tcpCommunication.profiles : [];
            return {
                profiles: sourceProfiles.map(profile => this.normalizeTcpProfile(profile))
            };
        }
        ,
        normalizePlcProtocol(protocol) {
            const candidate = `${protocol || ''}`.trim().toUpperCase();
            if (candidate === 'MC' || candidate === 'MITSUBISHIMC') return 'MC';
            if (candidate === 'FINS' || candidate === 'OMRONFINS') return 'FINS';
            return 'S7';
        }
        ,
        getPlcProfileKey(protocol = null) {
            const normalized = this.normalizePlcProtocol(protocol || this.config?.communication?.activeProtocol);
            if (normalized === 'MC') return 'mc';
            if (normalized === 'FINS') return 'fins';
            return 's7';
        }
        ,
        normalizePlcMappings(mappings) {
            if (!Array.isArray(mappings)) return [];
            return mappings
                .map(item => ({
                    name: item?.name || '',
                    address: item?.address || '',
                    dataType: item?.dataType || 'Bool',
                    description: item?.description || '',
                    canWrite: !!item?.canWrite
                }))
                .filter(item => item.name || item.address || item.description);
        }
        ,
        normalizePlcProfile(profile, defaults, includeS7Fields = false) {
            const normalized = {
                ipAddress: `${profile?.ipAddress || ''}`.trim(),
                port: Number.isFinite(Number.parseInt(`${profile?.port ?? ''}`, 10))
                    ? Number.parseInt(`${profile?.port ?? ''}`, 10)
                    : defaults.port,
                mappings: this.normalizePlcMappings(profile?.mappings ?? defaults.mappings)
            };

            if (includeS7Fields) {
                normalized.cpuType = `${profile?.cpuType || defaults.cpuType || 'S7-1200'}`.trim() || 'S7-1200';
                normalized.rack = Number.isFinite(Number.parseInt(`${profile?.rack ?? ''}`, 10))
                    ? Number.parseInt(`${profile?.rack ?? ''}`, 10)
                    : defaults.rack;
                normalized.slot = Number.isFinite(Number.parseInt(`${profile?.slot ?? ''}`, 10))
                    ? Number.parseInt(`${profile?.slot ?? ''}`, 10)
                    : defaults.slot;
            }

            return normalized;
        }
        ,
        normalizeCommunicationConfig(communication) {
            const defaults = this.getDefaultConfig().communication;
            const normalized = {
                activeProtocol: this.normalizePlcProtocol(communication?.activeProtocol || communication?.protocol || defaults.activeProtocol),
                heartbeatIntervalMs: Number.isFinite(Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10))
                    && Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10) > 0
                    ? Number.parseInt(`${communication?.heartbeatIntervalMs ?? ''}`, 10)
                    : defaults.heartbeatIntervalMs,
                s7: this.normalizePlcProfile(communication?.s7 || {}, defaults.s7, true),
                mc: this.normalizePlcProfile(communication?.mc || {}, defaults.mc),
                fins: this.normalizePlcProfile(communication?.fins || {}, defaults.fins)
            };

            const hasProtocolProfiles = !!communication?.s7 || !!communication?.mc || !!communication?.fins;
            const legacyIp = `${communication?.plcIpAddress || communication?.ipAddress || ''}`.trim();
            const legacyPort = Number.parseInt(`${communication?.plcPort ?? communication?.port ?? ''}`, 10);
            const legacyMappings = this.normalizePlcMappings(communication?.mappings);
            if (!hasProtocolProfiles && (legacyIp || Number.isFinite(legacyPort) || legacyMappings.length > 0)) {
                const profileKey = this.getPlcProfileKey(normalized.activeProtocol);
                normalized[profileKey] = {
                    ...normalized[profileKey],
                    ipAddress: legacyIp || normalized[profileKey].ipAddress,
                    port: Number.isFinite(legacyPort) ? legacyPort : normalized[profileKey].port,
                    mappings: legacyMappings.length > 0 ? legacyMappings : normalized[profileKey].mappings
                };
            }

            return normalized;
        }
        ,
        normalizeAppConfig(config) {
            const defaults = this.getDefaultConfig();
            return {
                ...defaults,
                ...config,
                general: {
                    ...defaults.general,
                    ...(config?.general || {}),
                    theme: normalizeTheme(config?.general?.theme, defaults.general.theme)
                },
                communication: this.normalizeCommunicationConfig(config?.communication),
                tcpCommunication: this.normalizeTcpCommunicationConfig(config?.tcpCommunication),
                storage: { ...defaults.storage, ...(config?.storage || {}) },
                runtime: { ...defaults.runtime, ...(config?.runtime || {}) },
                security: { ...defaults.security, ...(config?.security || {}) },
                cameras: (Array.isArray(config?.cameras) ? config.cameras : (defaults.cameras || [])).map(binding => ({
                    ...binding,
                    pixelFormat: this.normalizeCameraPixelFormat(binding?.pixelFormat ?? binding?.PixelFormat),
                    triggerMode: this.normalizeCameraTriggerMode(binding?.triggerMode ?? binding?.TriggerMode),
                    hardwareTriggerSource: this.normalizeHardwareTriggerSource(binding?.hardwareTriggerSource ?? binding?.HardwareTriggerSource),
                    softwareTriggerSource: this.normalizeSoftwareTriggerSource(binding?.softwareTriggerSource ?? binding?.SoftwareTriggerSource),
                    enterPhotoelectricDebounceMs: this.normalizeEnterDebounceMs(binding?.enterPhotoelectricDebounceMs ?? binding?.EnterPhotoelectricDebounceMs),
                    enterPhotoelectricTimeoutMs: this.normalizeEnterTimeoutMs(binding?.enterPhotoelectricTimeoutMs ?? binding?.EnterPhotoelectricTimeoutMs),
                    ignoreEnterTriggerWhileBusy: (binding?.ignoreEnterTriggerWhileBusy ?? binding?.IgnoreEnterTriggerWhileBusy) !== false,
                    enterPhotoelectricDeviceId: String(binding?.enterPhotoelectricDeviceId ?? binding?.EnterPhotoelectricDeviceId ?? '').trim(),
                    serialPhotoelectricPortName: String(binding?.serialPhotoelectricPortName ?? binding?.SerialPhotoelectricPortName ?? '').trim(),
                    serialPhotoelectricBaudRate: this.normalizeSerialBaudRate(binding?.serialPhotoelectricBaudRate ?? binding?.SerialPhotoelectricBaudRate),
                    serialPhotoelectricDebounceMs: this.normalizeSerialDebounceMs(binding?.serialPhotoelectricDebounceMs ?? binding?.SerialPhotoelectricDebounceMs),
                    serialPhotoelectricTimeoutMs: this.normalizeSerialTimeoutMs(binding?.serialPhotoelectricTimeoutMs ?? binding?.SerialPhotoelectricTimeoutMs),
                    ignoreSerialPhotoelectricTriggerWhileBusy: (binding?.ignoreSerialPhotoelectricTriggerWhileBusy ?? binding?.IgnoreSerialPhotoelectricTriggerWhileBusy) !== false,
                    targetFrameRateFps: this.normalizeCameraTargetFrameRate(binding?.targetFrameRateFps ?? binding?.TargetFrameRateFps)
                })),
                activeCameraId: config?.activeCameraId || defaults.activeCameraId || ''
            };
        }
        ,
        escapeHtml(value) {
            return String(value ?? '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/\"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }
        ,
        normalizeAiReasoning(reasoning) {
            const mode = `${reasoning?.mode || 'auto'}`.toLowerCase();
            const effort = `${reasoning?.effort || 'medium'}`.toLowerCase();
            return {
                mode: ['auto', 'off', 'on'].includes(mode) ? mode : 'auto',
                effort: ['low', 'medium', 'high', 'xhigh'].includes(effort) ? effort : 'medium'
            };
        }
        ,
        normalizeAiWireApi(wireApi) {
            const normalized = `${wireApi || 'chat_completions'}`.trim().toLowerCase().replace(/[-/]/g, '_');
            return normalized === 'responses' || normalized === 'response' ? 'responses' : 'chat_completions';
        }
        ,
        normalizeAiProtocol(protocol, provider = '') {
            const normalized = `${protocol || ''}`.trim().toLowerCase();
            if (['anthropic', 'azure_openai', 'ollama_native', 'openai_compatible'].includes(normalized)) {
                return normalized;
            }
            const providerText = `${provider || ''}`.toLowerCase();
            if (providerText.includes('anthropic')) return 'anthropic';
            if (providerText.includes('azure')) return 'azure_openai';
            if (providerText.includes('ollama')) return 'ollama_native';
            return 'openai_compatible';
        }
        ,
        normalizeAiAuthMode(authMode, protocol = 'openai_compatible') {
            const normalized = `${authMode || ''}`.trim().toLowerCase();
            if (['bearer', 'header_key', 'none'].includes(normalized)) {
                return normalized;
            }
            const normalizedProtocol = this.normalizeAiProtocol(protocol);
            if (normalizedProtocol === 'ollama_native') return 'none';
            if (normalizedProtocol === 'anthropic' || normalizedProtocol === 'azure_openai') return 'header_key';
            return 'bearer';
        }
        ,
        normalizeAiRoleName(role) {
            const normalized = `${role || 'generation'}`.trim().toLowerCase().replace(/_/g, '-');
            if (normalized === 'planner') return 'planner';
            if (normalized === 'vision-agent-shadow-eval' || normalized === 'shadow-eval') return 'vision-agent-shadow-eval';
            if (['generation', 'reasoning', 'fallback', 'validation', 'vision'].includes(normalized)) return normalized;
            return 'generation';
        }
        ,
        normalizeAiRoleBindings(roles, modelRole = null) {
            const source = Array.isArray(roles) ? roles : [];
            if (modelRole) source.push(modelRole);
            const normalized = [...new Set(source.map(role => this.normalizeAiRoleName(role)))];
            return normalized.length > 0 ? normalized : ['generation'];
        }
        ,
        getMaskedAiKey(hasApiKey, maskedValue = '') {
            if (maskedValue) return String(maskedValue);
            return hasApiKey ? '********' : '';
        }
        ,
        getDefaultAiReasoningSupport() {
            return {
                familyId: 'unknown',
                familyName: 'Unknown',
                allowedModes: ['auto'],
                allowedEfforts: ['medium'],
                supportsExplicitMode: false,
                supportsEffort: false,
                isModelLockedOn: false,
                helpText: '当前模型族未识别，建议保持 Auto，以免覆盖厂商默认行为。'
            };
        }
        ,
        normalizeAiReasoningSupport(support) {
            const fallback = this.getDefaultAiReasoningSupport();
            const allowedModes = Array.isArray(support?.allowedModes) ? support.allowedModes : fallback.allowedModes;
            const allowedEfforts = Array.isArray(support?.allowedEfforts) ? support.allowedEfforts : fallback.allowedEfforts;
            const normalizedModes = allowedModes
                .map(mode => `${mode || ''}`.toLowerCase())
                .filter(mode => ['auto', 'off', 'on'].includes(mode));
            const normalizedEfforts = allowedEfforts
                .map(effort => `${effort || ''}`.toLowerCase())
                .filter(effort => ['low', 'medium', 'high', 'xhigh'].includes(effort));
            const finalModes = normalizedModes.length > 0 ? [...new Set(normalizedModes)] : fallback.allowedModes;
            const finalEfforts = normalizedEfforts.length > 0 ? [...new Set(normalizedEfforts)] : fallback.allowedEfforts;

            return {
                familyId: support?.familyId || fallback.familyId,
                familyName: support?.familyName || fallback.familyName,
                allowedModes: finalModes,
                allowedEfforts: finalEfforts,
                supportsExplicitMode: finalModes.some(mode => mode === 'on' || mode === 'off'),
                supportsEffort: finalEfforts.length > 1 || finalEfforts[0] !== 'medium',
                isModelLockedOn: finalModes.includes('on') && !finalModes.includes('off'),
                helpText: support?.helpText || fallback.helpText
            };
        }
        ,
        getDefaultConfig() {
            return {
                general: { softwareTitle: 'ClearVision', theme: 'dark', autoStart: false },
                communication: {
                    activeProtocol: 'S7',
                    heartbeatIntervalMs: 1000,
                    s7: {
                        ipAddress: '192.168.0.1',
                        port: 102,
                        cpuType: 'S7-1200',
                        rack: 0,
                        slot: 1,
                        mappings: []
                    },
                    mc: {
                        ipAddress: '192.168.3.1',
                        port: 5002,
                        mappings: []
                    },
                    fins: {
                        ipAddress: '192.168.250.1',
                        port: 9600,
                        mappings: []
                    }
                },
                tcpCommunication: {
                    profiles: []
                },
                storage: { imageSavePath: 'D:\\VisionData\\Images', savePolicy: 'NgOnly', retentionDays: 30, minFreeSpaceGb: 5 },
                runtime: {
                    autoRun: false,
                    stopOnConsecutiveNg: 0,
                    missingMaterialTimeoutSeconds: 120,
                    applyProtectionRules: true
                },
                security: {
                    passwordMinLength: 6,
                    sessionTimeoutMinutes: 30,
                    loginFailureLockoutCount: 5
                },
                cameras: [],
                activeCameraId: ''
            };
        }

    });
}
