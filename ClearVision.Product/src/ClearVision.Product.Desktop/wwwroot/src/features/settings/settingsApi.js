import httpClient from '../../core/messaging/httpClient.js';

const settingsApi = {
    loadSettings: () => httpClient.get('/settings'),
    saveSettings: config => httpClient.put('/settings', config),
    resetSettings: () => httpClient.post('/settings/reset'),
    getDiskUsage: path => httpClient.get(`/settings/disk-usage?path=${encodeURIComponent(path || '')}`),
    getDatabaseStatus: () => httpClient.get('/settings/database/status'),
    repairDatabase: () => httpClient.post('/settings/database/repair', {}),
    backupDatabase: () => httpClient.post('/settings/database/backup', {}),
    restoreDatabase: backupPath => httpClient.post('/settings/database/restore', { backupPath }),
    cleanupDatabaseHistory: retentionDays => httpClient.post('/settings/database/cleanup', { retentionDays }),

    loadUsers: () => httpClient.get('/users'),
    createUser: payload => httpClient.post('/users', payload),
    updateUser: (id, payload) => httpClient.put(`/users/${id}`, payload),
    deleteUser: id => httpClient.delete(`/users/${id}`),
    resetUserPassword: (id, payload) => httpClient.post(`/users/${id}/reset-password`, payload),
    changePassword: payload => httpClient.post('/auth/change-password', payload),

    listAiModels: () => httpClient.get('/ai/models'),
    createAiModel: payload => httpClient.post('/ai/models', payload),
    updateAiModel: (id, payload) => httpClient.put(`/ai/models/${id}`, payload),
    deleteAiModel: id => httpClient.delete(`/ai/models/${id}`),
    activateAiModel: id => httpClient.post(`/ai/models/${id}/activate`, {}),
    setDefaultPlannerAiModel: id => httpClient.post(`/ai/models/${id}/default-planner`, {}),
    setDefaultShadowEvalAiModel: id => httpClient.post(`/ai/models/${id}/default-shadow-eval`, {}),
    testAiModel: id => httpClient.post(`/ai/models/${id}/test`, {}),
    resolveAiReasoningSupport: payload => httpClient.post('/ai/reasoning-support', payload),
    loadRuntimePreviewPilotConfig: () => httpClient.get('/settings/runtime-preview-pilot/config'),
    saveRuntimePreviewPilotConfig: payload => httpClient.put('/settings/runtime-preview-pilot/config', payload),
    loadRuntimePreviewPilotCatalog: () => httpClient.get('/settings/runtime-preview-pilot/catalog'),
    checkRuntimePreviewPilotReadiness: payload => httpClient.post('/settings/runtime-preview-pilot/readiness', payload),
    listRuntimePreviewPilotSessions: () => httpClient.get('/settings/runtime-preview-pilot/sessions'),
    createRuntimePreviewPilotSession: payload => httpClient.post('/settings/runtime-preview-pilot/sessions', payload),
    simulateRuntimePreviewPilotSession: payload => httpClient.post('/settings/runtime-preview-pilot/sessions/simulate', payload),
    loadRuntimePreviewPilotSessionReport: sessionId => httpClient.get(`/settings/runtime-preview-pilot/sessions/${encodeURIComponent(sessionId)}/report`),
    replayRuntimePreviewPilotSession: sessionId => httpClient.get(`/settings/runtime-preview-pilot/sessions/${encodeURIComponent(sessionId)}/replay`),
    exportRuntimePreviewPilotSessionReport: sessionId => httpClient.get(`/settings/runtime-preview-pilot/sessions/${encodeURIComponent(sessionId)}/report/export`),
    cancelRuntimePreviewPilotSession: sessionId => httpClient.post(`/settings/runtime-preview-pilot/sessions/${encodeURIComponent(sessionId)}/cancel`, {}),
    generateRuntimePreviewDeployReadiness: payload => httpClient.post('/settings/runtime-preview-pilot/sessions/deploy-readiness', payload),
    generateRuntimePreviewPackageReadiness: payload => httpClient.post('/settings/runtime-preview-pilot/sessions/package-readiness', payload),
    cleanupRuntimePreviewPilotRetention: payload => httpClient.post('/settings/runtime-preview-pilot/retention/cleanup', payload),
    loadRuntimePreviewScenarioEvidence: () => httpClient.get('/settings/runtime-preview-pilot/scenario-evidence'),
    loadRuntimePreviewScenarioCorpus: () => httpClient.get('/settings/runtime-preview-pilot/scenario-corpus'),
    loadRuntimePreviewAgentExplanationBenchmark: () => httpClient.get('/settings/runtime-preview-pilot/agent-explanation-benchmark'),
    loadRuntimePreviewGovernanceIndex: () => httpClient.get('/settings/runtime-preview-pilot/governance/index'),
    exportRuntimePreviewGovernance: () => httpClient.get('/settings/runtime-preview-pilot/governance/export'),
    lookupRuntimePreviewGovernance: ({ sessionId = '', reportId = '', caseId = '' } = {}) =>
        httpClient.get(`/settings/runtime-preview-pilot/governance/lookup?sessionId=${encodeURIComponent(sessionId)}&reportId=${encodeURIComponent(reportId)}&caseId=${encodeURIComponent(caseId)}`),

    loadPlcSettings: () => httpClient.get('/plc/settings'),
    savePlcSettings: payload => httpClient.put('/plc/settings', payload),
    testPlcConnection: payload => httpClient.post('/plc/test-connection', payload),

    loadStationCommunicationSettings: () => httpClient.get('/station-communication/settings'),
    saveStationCommunicationSettings: payload => httpClient.put('/station-communication/settings', payload),
    revealStationToken: () => httpClient.post('/station-communication/token', { operation: 'reveal' }),
    regenerateStationToken: () => httpClient.post('/station-communication/token', { operation: 'regenerate' }),

    listCameraBindings: () => httpClient.get('/cameras/bindings'),
    saveCameraBindings: payload => httpClient.put('/cameras/bindings', payload),
    discoverCameras: endpoint => httpClient.get(endpoint),
    learnEnterTriggerDevice: payload => httpClient.post('/trigger-input/learn-enter-device', payload),
    listSerialPhotoelectricPorts: () => httpClient.get('/trigger-input/serial-photoelectric-ports'),
    testSerialPhotoelectric: payload => httpClient.post('/trigger-input/test-serial-photoelectric', payload),
    startContinuousPreview: payload => httpClient.post('/cameras/continuous-preview/start', payload),
    stopContinuousPreview: payload => httpClient.post('/cameras/continuous-preview/stop', payload),
    fetchContinuousPreviewFrame: (sessionId, cacheKey, options) =>
        httpClient.getForBlob(`/cameras/continuous-preview/frame/${encodeURIComponent(sessionId)}?_=${cacheKey}`, options),
    softTriggerCapture: (payload, options) => httpClient.postForBlob('/cameras/soft-trigger-capture', payload, options)
};

export default settingsApi;
