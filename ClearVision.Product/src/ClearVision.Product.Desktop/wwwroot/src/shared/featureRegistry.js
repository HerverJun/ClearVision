const FEATURE_STATES = Object.freeze({
    AVAILABLE: 'available',
    INTERNAL: 'internal',
    UNAVAILABLE: 'unavailable'
});

const FEATURE_REGISTRY = Object.freeze({
    'storage.pathPicker': Object.freeze({
        id: 'storage.pathPicker',
        state: FEATURE_STATES.UNAVAILABLE,
        enabled: false,
        badge: '暂未接入',
        buttonLabel: '暂未接入',
        title: '目录选择能力暂未接入',
        description: '当前版本暂不支持界面选择目录；请由管理员确认路径后手填，保存时会校验路径是否可访问。'
    }),
    'storage.immediateCleanup': Object.freeze({
        id: 'storage.immediateCleanup',
        state: FEATURE_STATES.UNAVAILABLE,
        enabled: false,
        badge: '暂未开放',
        buttonLabel: '暂未开放',
        title: '过期文件即时清理动作暂未接入',
        description: '当前版本仅支持保存清理策略；如需立即清理，请由管理员在数据目录按保留天数处理。'
    }),
    'operator.autotuneStrategies': Object.freeze({
        id: 'operator.autotuneStrategies',
        state: FEATURE_STATES.INTERNAL,
        enabled: true,
        badge: '内部能力',
        buttonLabel: '查看自动调参策略',
        title: '当前仅开放只读策略查看',
        description: '当前仅开放只读策略查看；任务入口、状态展示和结果应用流程尚未产品化。'
    }),
    'operator.circleSearchV2Tool': Object.freeze({
        id: 'operator.circleSearchV2Tool',
        state: FEATURE_STATES.AVAILABLE,
        enabled: true,
        badge: '已开放',
        buttonLabel: 'Circle Search V2',
        title: 'Circle Search V2 参数区与图上几何编辑已开放',
        description: 'CircleMeasurement 在 CaliperFitV2 方法下启用结构化参数区、搜索环几何编辑和同源 Scene 显示。'
    }),
    'project.demoCreation': Object.freeze({
        id: 'project.demoCreation',
        state: FEATURE_STATES.AVAILABLE,
        enabled: true,
        badge: '已开放',
        buttonLabel: '示例工程',
        title: 'Demo 工程创建与引导已接入主入口',
        description: '示例工程创建与引导已接入正式入口，可直接调用后端 Demo 工程接口。'
    }),
    'settings.reset': Object.freeze({
        id: 'settings.reset',
        state: FEATURE_STATES.AVAILABLE,
        enabled: true,
        badge: '已开放',
        buttonLabel: '恢复默认设置',
        title: '恢复默认设置会调用真实重置接口',
        description: '恢复默认设置会同时重置系统配置与 AI 模型配置，并回到服务端默认值。'
    })
});

const DEFAULT_FEATURE = Object.freeze({
    id: 'unknown',
    state: FEATURE_STATES.UNAVAILABLE,
    enabled: false,
    badge: '未知状态',
    buttonLabel: '',
    title: '',
    description: ''
});

export { FEATURE_STATES };

export function getFeatureMeta(featureId) {
    return FEATURE_REGISTRY[featureId] || DEFAULT_FEATURE;
}

export function isFeatureEnabled(featureId) {
    return getFeatureMeta(featureId).enabled !== false;
}

export function getFeatureBadge(featureId, fallback = '') {
    return getFeatureMeta(featureId).badge || fallback;
}

export function getFeatureDescription(featureId, fallback = '') {
    return getFeatureMeta(featureId).description || fallback;
}

export function getFeatureButtonLabel(featureId, fallback = '') {
    return getFeatureMeta(featureId).buttonLabel || fallback;
}

export function applyFeatureToButton(button, featureId, { fallbackLabel = '' } = {}) {
    if (!button) {
        return null;
    }

    const meta = getFeatureMeta(featureId);
    const label = meta.buttonLabel || fallbackLabel;
    if (label) {
        button.textContent = label;
    }

    button.title = meta.title || button.title || '';
    button.dataset.featureId = meta.id;
    button.dataset.featureState = meta.state;
    if (!meta.enabled) {
        button.disabled = true;
        button.setAttribute('aria-disabled', 'true');
    }

    return meta;
}
