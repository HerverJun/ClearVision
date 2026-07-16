import { getOperatorTypeDisplayName } from '../../shared/operatorDisplayNames.js';
import webMessageBridge from '../../core/messaging/webMessageBridge.js';
import httpClient from '../../core/messaging/httpClient.js';
import {
    collectEffectiveRequiredParameterErrors,
    getParameterEffectiveState,
    normalizeAcquisitionSourceType
} from '../../shared/parameterDependencyRules.js';
import RoiEditorPanel from './roiEditorPanel.js';
import { createCameraPreviewInputContext } from './previewCoordinator.js';
import {
    getOperatorRoiConfig,
    isCircleSearchV2ToolEnabled,
    isNPointCalibrationWorkbenchEnabled
} from './roiEditorSupport.mjs';

export const PROPERTY_PANEL_CAPABILITY_OWNER_ID = 'property-panel-capability-v2';

function resolveElement(target) {
    if (!target) {
        return null;
    }

    if (typeof target === 'string') {
        return typeof document !== 'undefined' ? document.getElementById(target) : null;
    }

    return target;
}

function escapeHtml(value) {
    const div = document.createElement('div');
    div.textContent = value == null ? '' : String(value);
    return div.innerHTML;
}

function escapeAttribute(value) {
    return escapeHtml(value).replace(/"/g, '&quot;');
}

function isElementEffectivelyDisabled(element) {
    return Boolean(element?.disabled) || element?.getAttribute?.('aria-disabled') === 'true';
}

function getPickerActionLabel(label, fallbackTarget) {
    const trimmedLabel = String(label || '').trim();
    if (trimmedLabel) {
        return `选择${trimmedLabel}`;
    }

    return `选择${String(fallbackTarget || '参数').trim()}`;
}

const LOCALIZED_DISABLED_REASONS = Object.freeze({
    'File path is disabled while SourceType is camera.': '相机模式下已禁用文件路径。',
    'Camera id is disabled in file mode or when CameraBindingId is set.': '文件模式或已选择相机绑定时，相机选择已禁用。',
    'Camera binding is disabled in file mode or when CameraId is set.': '文件模式或已选择相机时，相机绑定已禁用。',
    'Camera exposure is disabled for file acquisition.': '文件采集模式下已禁用相机曝光。',
    'Camera gain is disabled for file acquisition.': '文件采集模式下已禁用相机增益。',
    'Trigger mode is disabled for file acquisition.': '文件采集模式下已禁用触发模式。',
    'Template path is disabled when TemplateId is selected.': '已选择模板 ID，模板路径已禁用。',
    'TemplateId is disabled when TemplatePath is selected.': '已填写模板路径，模板 ID 已禁用。',
    'ROI X is disabled when UseRoi is false.': '未启用 ROI 时，ROI X 已禁用。',
    'ROI Y is disabled when UseRoi is false.': '未启用 ROI 时，ROI Y 已禁用。',
    'ROI width is disabled when UseRoi is false.': '未启用 ROI 时，ROI 宽度已禁用。',
    'ROI height is disabled when UseRoi is false.': '未启用 ROI 时，ROI 高度已禁用。',
    'Origin X is only editable when OriginMode is Custom.': '仅自定义原点模式可编辑 Origin X。',
    'Origin Y is only editable when OriginMode is Custom.': '仅自定义原点模式可编辑 Origin Y。',
    'Angle search is disabled when EnablePoseSearch is false.': '未启用姿态搜索时，角度搜索已禁用。',
    'Angle extent is disabled when EnablePoseSearch is false.': '未启用姿态搜索时，角度范围已禁用。',
    'Angle step is disabled when EnablePoseSearch is false.': '未启用姿态搜索时，角度步长已禁用。',
    'Scale search is disabled when EnablePoseSearch is false.': '未启用姿态搜索时，尺度搜索已禁用。',
    'Scale step is disabled when EnablePoseSearch is false.': '未启用姿态搜索时，尺度步长已禁用。',
    'ModelPath is disabled when ModelId or ModelCatalogPath is selected.': '已选择模型 ID 或模型目录时，模型路径已禁用。',
    'ModelId is disabled when ModelPath or ModelCatalogPath is selected.': '已填写模型路径或模型目录时，模型 ID 已禁用。',
    'ModelCatalogPath is disabled when ModelPath or ModelId is selected.': '已填写模型路径或模型 ID 时，模型目录已禁用。',
    'GPU device id is disabled when UseGpu is false.': '未启用 GPU 时，GPU 设备 ID 已禁用。',
    'Internal NMS is owned by the exported model when OutputFormat is EndToEndNms.': '端到端 NMS 输出由模型接管，内部 NMS 已禁用。',
    'NMS IoU is disabled when model-side NMS is trusted or internal NMS is off.': '使用模型侧 NMS 或关闭内部 NMS 时，NMS IoU 已禁用。',
    'Channel is disabled when a concrete output channel id is selected.': '已选择具体输出通道 ID 时，通道已禁用。',
    'OutputChannel is disabled when Channel or OutputChannelId is selected.': '已选择通道或输出通道 ID 时，输出通道已禁用。',
    'OutputChannelId is disabled when Channel or OutputChannel is selected.': '已选择通道或输出通道时，输出通道 ID 已禁用。',
    'File path is enabled only for file output.': '仅文件输出模式可编辑文件路径。',
    'Output path is enabled only for file output.': '仅文件输出模式可编辑输出路径。',
    'PLC metadata is enabled only for PLC output review.': '仅 PLC 输出复核模式可编辑 PLC 元数据。'
});

function getLocalizedDisabledReason(reason) {
    const rawReason = String(reason || '').trim();
    if (!rawReason) {
        return '';
    }

    return LOCALIZED_DISABLED_REASONS[rawReason] || rawReason;
}

function getParameterName(parameter) {
    return String(parameter?.name ?? parameter?.Name ?? '').trim();
}

function getParameterInputId(name) {
    return `param-${String(name ?? '').trim()}`;
}

function normalizeParameterName(name) {
    return String(name || '').trim().toLowerCase();
}

function getParameterLabel(parameter, fallbackName = '') {
    return String(
        parameter?.displayName ??
        parameter?.DisplayName ??
        parameter?.label ??
        parameter?.Label ??
        parameter?.name ??
        parameter?.Name ??
        fallbackName ??
        '参数'
    );
}

function getParameterDataType(parameter) {
    return String(
        parameter?.dataType ??
        parameter?.DataType ??
        parameter?.type ??
        parameter?.Type ??
        'string'
    ).trim().toLowerCase();
}

function isPathLikeParameter(parameter) {
    const normalizedName = normalizeParameterName(getParameterName(parameter));
    if (!normalizedName || normalizedName === 'ipaddress') {
        return false;
    }

    return normalizedName === 'filepath' ||
        normalizedName === 'outputpath' ||
        normalizedName === 'savepath' ||
        normalizedName.endsWith('filepath') ||
        normalizedName.endsWith('templatepath') ||
        normalizedName.endsWith('modelpath') ||
        normalizedName.endsWith('catalogpath') ||
        normalizedName.endsWith('labelspath') ||
        normalizedName.endsWith('bankpath') ||
        normalizedName.endsWith('path');
}

function resolveParameterControlType(parameter) {
    const dataType = getParameterDataType(parameter);
    if (dataType === 'file') {
        return 'file';
    }

    if (dataType === 'camerabinding') {
        return 'cameraBinding';
    }

    if (dataType === 'string' && isPathLikeParameter(parameter)) {
        return 'file';
    }

    return dataType;
}

function getParameterDescription(parameter) {
    return String(parameter?.description ?? parameter?.Description ?? '').trim();
}

function getParameterValue(parameter) {
    return parameter?.value ?? parameter?.Value ?? parameter?.defaultValue ?? parameter?.DefaultValue ?? null;
}

function getParameterRangeValue(parameter, ...keys) {
    for (const key of keys) {
        if (parameter?.[key] !== undefined && parameter?.[key] !== null && parameter?.[key] !== '') {
            const parsed = Number(parameter[key]);
            if (Number.isFinite(parsed)) {
                return parsed;
            }
        }
    }

    return null;
}

function shouldRenderParameterSlider(parameter) {
    if (!parameter || typeof parameter !== 'object') {
        return false;
    }

    const metadata = new Map(
        Object.entries(parameter).map(([key, value]) => [String(key).toLowerCase(), value])
    );

    if (metadata.get('showslider') === true) {
        return true;
    }

    return ['uicontrol', 'control', 'editor'].some(key =>
        String(metadata.get(key) ?? '').trim().toLowerCase() === 'slider'
    );
}

function isEmptyValue(value) {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

function normalizeCameraTriggerMode(value) {
    const normalized = String(value || '').trim().toLowerCase();
    if (normalized.includes('continuous') || normalized.includes('连续')) {
        return 'Continuous';
    }
    if (normalized.includes('external') || normalized.includes('外部') || normalized.includes('hardware')) {
        return 'External';
    }
    return 'Software';
}

function blobToBase64(blob) {
    if (!blob) {
        return Promise.resolve(null);
    }

    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
            const result = String(reader.result || '');
            const commaIndex = result.indexOf(',');
            resolve(commaIndex >= 0 ? result.slice(commaIndex + 1) : result);
        };
        reader.onerror = () => reject(reader.error || new Error('读取单帧图像失败。'));
        reader.readAsDataURL(blob);
    });
}

function parseNumericValue(rawValue) {
    if (isEmptyValue(rawValue)) {
        return { empty: true, valid: false, value: null };
    }

    const value = Number(String(rawValue).trim());
    return {
        empty: false,
        valid: Number.isFinite(value),
        value: Number.isFinite(value) ? value : null
    };
}

function toBoolean(value) {
    if (typeof value === 'boolean') {
        return value;
    }

    return String(value).toLowerCase() === 'true';
}

function getParameterOptions(parameter) {
    const options = parameter?.options ?? parameter?.Options ?? [];
    return Array.isArray(options) ? options : [];
}

function findParameterByName(parameters = [], name = '') {
    const normalizedName = normalizeParameterName(name);
    return (Array.isArray(parameters) ? parameters : [])
        .find(parameter => normalizeParameterName(getParameterName(parameter)) === normalizedName) || null;
}

function getParameterLabelByName(parameters = [], name = '') {
    return getParameterLabel(findParameterByName(parameters, name), name);
}

function getValueByName(parameters = [], values = null, name = '') {
    const normalizedName = normalizeParameterName(name);
    if (!normalizedName) {
        return null;
    }

    if (values && typeof values === 'object') {
        const valueKey = Object.keys(values).find(key => normalizeParameterName(key) === normalizedName);
        if (valueKey !== undefined) {
            return values[valueKey];
        }
    }

    const parameter = findParameterByName(parameters, name);
    return parameter ? getParameterValue(parameter) : null;
}

function getNonEmptyMutuallyExclusiveNames(operator, parameters = [], values = null, names = []) {
    return names.filter(name => {
        const value = getValueByName(parameters, values, name);
        if (!isEmptyValue(value)) {
            return true;
        }

        const parameter = findParameterByName(parameters, name);
        return !parameter && !isEmptyValue(getValueByName(operator?.parameters || [], values, name));
    });
}

function getFormValue(input) {
    const dataType = String(input.dataset.type || '').toLowerCase();
    if (dataType === 'boolean' || dataType === 'bool') {
        return Boolean(input.checked);
    }

    if (dataType === 'int' || dataType === 'integer') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid && Number.isInteger(parsed.value) ? parsed.value : input.value);
    }

    if (dataType === 'double' || dataType === 'float' || dataType === 'number') {
        const parsed = parseNumericValue(input.value);
        return parsed.empty ? null : (parsed.valid ? parsed.value : input.value);
    }

    return input.value;
}

function getPanelParameterEffectiveState(operator, paramOrName, options = {}) {
    const state = getParameterEffectiveState(operator, paramOrName, options);
    const groupNames = Array.isArray(state.rule?.atLeastOneOf) ? state.rule.atLeastOneOf : [];
    if (!state.effectiveDisabled || groupNames.length < 2) {
        return state;
    }

    const parameterName = state.parameterName;
    const parameters = Array.isArray(operator?.parameters) ? operator.parameters : [];
    const nonEmptyNames = getNonEmptyMutuallyExclusiveNames(operator, parameters, options.values || null, groupNames);
    if (
        nonEmptyNames.length > 1 &&
        nonEmptyNames.some(name => normalizeParameterName(name) === normalizeParameterName(parameterName))
    ) {
        return {
            ...state,
            effectiveDisabled: false,
            disabledReason: ''
        };
    }

    return state;
}

function validationErrorTargetsParameter(error, parameterName) {
    const normalizedParameterName = normalizeParameterName(parameterName);
    if (!normalizedParameterName) {
        return false;
    }

    const names = [
        error?.name,
        ...(Array.isArray(error?.parameterNames) ? error.parameterNames : [])
    ];

    return names.some(name => normalizeParameterName(name) === normalizedParameterName);
}

function isBlockingValidationErrorForParameter(error, parameterName) {
    if (!validationErrorTargetsParameter(error, parameterName)) {
        return false;
    }

    return error?.kind !== 'required' && error?.kind !== 'atLeastOneOf';
}

function buildValidationErrorMessage(error, operator, parameters = [], values = null) {
    const operatorType = String(operator?.type || operator?.operatorType || '').trim();
    const names = Array.isArray(error?.parameterNames) && error.parameterNames.length > 0
        ? error.parameterNames
        : [error?.name].filter(Boolean);
    const normalizedNames = names.map(normalizeParameterName);

    if (operatorType === 'ImageAcquisition') {
        const sourceType = normalizeAcquisitionSourceType(getValueByName(parameters, values, 'SourceType'));
        if (sourceType === 'file' && normalizedNames.includes('filepath')) {
            return '请先配置文件路径';
        }

        if (
            sourceType === 'camera' &&
            (normalizedNames.includes('cameraid') || normalizedNames.includes('camerabindingid'))
        ) {
            return normalizedNames.length > 1
                ? '请先选择相机或相机绑定'
                : '请先选择相机';
        }
    }

    if (
        operatorType === 'EdgeDetection' &&
        error?.kind === 'atLeastOneOf' &&
        normalizedNames.includes('edgemodelpath') &&
        normalizedNames.includes('edgemodelid') &&
        normalizedNames.includes('modelcatalogpath')
    ) {
        return 'ONNX 边缘检测需要选择模型路径、模型 ID 或模型目录之一';
    }

    if (error?.kind === 'required') {
        const label = getParameterLabelByName(parameters, error.name);
        return `${label} 必填`;
    }

    if (error?.kind === 'atLeastOneOf' && names.length > 0) {
        const labels = names.map(name => getParameterLabelByName(parameters, name));
        return `请至少填写 ${labels.join(' / ')} 中的一项`;
    }

    return error?.message || '参数校验失败';
}

function collectMutuallyExclusiveParameterErrors(operator, parameters = [], values = null) {
    const errors = [];
    const handledGroups = new Set();

    parameters.forEach(parameter => {
        const state = getPanelParameterEffectiveState(operator, parameter, { values });
        const rule = state.rule;
        const names = Array.isArray(rule?.atLeastOneOf) ? rule.atLeastOneOf : [];
        if (!rule?.mutuallyExclusiveGroup || names.length < 2) {
            return;
        }

        const groupKey = `${rule.mutuallyExclusiveGroup}:${names.map(normalizeParameterName).sort().join('|')}`;
        if (handledGroups.has(groupKey)) {
            return;
        }
        handledGroups.add(groupKey);

        const nonEmptyNames = getNonEmptyMutuallyExclusiveNames(operator, parameters, values, names);
        if (nonEmptyNames.length < 2) {
            return;
        }

        const labels = nonEmptyNames.map(name => getParameterLabelByName(parameters, name));
        errors.push({
            name: nonEmptyNames[0],
            parameterNames: nonEmptyNames,
            kind: 'mutuallyExclusive',
            message: `请只保留 ${labels.join(' / ')} 中的一项`
        });
    });

    return errors;
}

function getParameterWriteFailureMessage(result, parameters = []) {
    if (!result) {
        return '参数写入失败：未收到写入结果';
    }

    if (result.updated !== false) {
        return '';
    }

    const reason = String(result.reason || '').trim();
    if (!reason || reason === 'no_change' || reason === 'node_not_found') {
        return '';
    }

    if (reason === 'parameter_not_found') {
        const names = Array.isArray(result.missingParameters)
            ? result.missingParameters.filter(Boolean)
            : [];
        const labels = names.map(name => getParameterLabelByName(parameters, name));
        return labels.length > 0
            ? `参数写入失败：缺少参数 ${labels.join(' / ')}`
            : '参数写入失败：缺少目标参数';
    }

    const detail = result.message || result.errorMessage || result.error || reason;
    return `参数写入失败：${String(detail)}`;
}

function getParameterWriteExceptionMessage(error) {
    return `参数写入失败：${error?.message || '未知错误'}`;
}

export class PropertyPanelCapabilityOwner {
    constructor(container, {
        propertyAdapter,
        showToast = () => {},
        previewCoordinator = null,
        previewResourcesEnabled = true,
        onOpenPreviewImage = () => {},
        onCapturePreviewInput = () => {},
        getPreviewInputImageSource = () => null,
        circleSearchV2ToolEnabled = undefined,
        nPointCalibrationWorkbenchEnabled = undefined
    } = {}) {
        this.container = resolveElement(container);
        if (!this.container) {
            throw new Error('PropertyPanelCapabilityOwner requires a container.');
        }
        if (!propertyAdapter) {
            throw new Error('PropertyPanelCapabilityOwner requires a property adapter.');
        }

        this.propertyAdapter = propertyAdapter;
        this.showToast = typeof showToast === 'function' ? showToast : () => {};
        this.previewCoordinator = previewCoordinator;
        this.previewResourcesEnabled = previewResourcesEnabled !== false;
        this.onOpenPreviewImage = typeof onOpenPreviewImage === 'function'
            ? onOpenPreviewImage
            : () => {};
        this.onCapturePreviewInput = typeof onCapturePreviewInput === 'function'
            ? onCapturePreviewInput
            : () => {};
        this.getPreviewInputImageSource = typeof getPreviewInputImageSource === 'function'
            ? getPreviewInputImageSource
            : () => null;
        this.circleSearchV2ToolEnabled = circleSearchV2ToolEnabled;
        this.nPointCalibrationWorkbenchEnabled = nPointCalibrationWorkbenchEnabled;
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.disposed = false;
        this.unsubscribes = [];
        this.cameraBindingsCache = [];
        this.cameraBindingsLoadingPromise = null;
        this.cameraBindingsLoadToken = 0;
        this.roiEditorPanel = null;
        this.geometryImageBounds = null;
        this.cameraCapturePending = false;
        this.cameraCaptureAbortController = null;
        this.lastCameraCaptureMessage = '';

        this.handleContainerChange = this.handleContainerChange.bind(this);
        this.handleContainerClick = this.handleContainerClick.bind(this);
        this.handleContainerInput = this.handleContainerInput.bind(this);
        this.handleContainerKeyDown = this.handleContainerKeyDown.bind(this);
        this.handleContainerSubmit = this.handleContainerSubmit.bind(this);
        this.handleFilePickedEvent = this.handleFilePickedEvent.bind(this);

        this.container.dataset.propertyPanelOwner = PROPERTY_PANEL_CAPABILITY_OWNER_ID;
        this.container.addEventListener('change', this.handleContainerChange);
        this.container.addEventListener('click', this.handleContainerClick);
        this.container.addEventListener('input', this.handleContainerInput);
        this.container.addEventListener('keydown', this.handleContainerKeyDown);
        this.container.addEventListener('submit', this.handleContainerSubmit);

        this.unsubscribes.push(
            webMessageBridge.on('FilePickedEvent', this.handleFilePickedEvent),
            this.propertyAdapter.subscribeSelectedNode((operator, state) => {
                this.handleSelectedNodeChanged(operator, state);
            }),
            this.propertyAdapter.subscribeFlowChanges((state) => {
                this.handleFlowChanged(state);
            })
        );

        this.render();
    }

    handleSelectedNodeChanged(operator, state = {}) {
        if (this.disposed) {
            return;
        }

        this.cancelCameraCapture();
        this.lastCameraCaptureMessage = '';
        this.currentOperator = operator || null;
        this.currentNodeId = operator?.id || null;
        this.currentConnection = this.currentNodeId
            ? null
            : this.propertyAdapter.getSelectedConnectionSnapshot?.(state?.selectedConnectionId) || null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    handleFlowChanged() {
        if (this.disposed || !this.currentNodeId) {
            if (!this.currentNodeId && this.currentConnection) {
                const refreshedConnection = this.propertyAdapter.getSelectedConnectionSnapshot?.(this.currentConnection.id);
                if (!refreshedConnection) {
                    this.currentConnection = null;
                    this.render();
                } else {
                    this.currentConnection = refreshedConnection;
                    this.render();
                }
            }
            return;
        }

        const operator = this.propertyAdapter.getSelectedOperatorSnapshot(this.currentNodeId);
        if (!operator) {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.currentConnection = null;
            this.validationErrors = [];
            this.statusMessage = '';
            this.lastChangedParameterName = null;
            this.dirty = false;
            this.render();
            return;
        }

        this.currentOperator = operator;
        this.currentConnection = null;
        this.render();
    }

    handleContainerSubmit(event) {
        event.preventDefault();
        this.applyChanges();
    }

    handleContainerClick(event) {
        if (this.disposed) {
            return;
        }

        const colorPreview = event.target?.closest?.('.color-preview-box');
        if (colorPreview) {
            event.preventDefault();
            this.openColorPicker(colorPreview);
            return;
        }

        const clearRoiButton = event.target?.closest?.('[data-property-geometry-clear-roi]');
        if (clearRoiButton) {
            event.preventDefault();
            if (!clearRoiButton.disabled) {
                this.handleGeometryClearRoi();
            }
            return;
        }

        const captureFrameButton = event.target?.closest?.('[data-property-camera-capture-frame]');
        if (captureFrameButton) {
            event.preventDefault();
            if (!isElementEffectivelyDisabled(captureFrameButton)) {
                void this.captureSelectedCameraFrame();
            }
            return;
        }

        const button = event.target?.closest?.('.btn-pick-file');
        if (!button) {
            return;
        }

        event.preventDefault();
        if (isElementEffectivelyDisabled(button)) {
            return;
        }

        const parameterName = button.dataset.param || '';
        webMessageBridge.sendMessage('PickFileCommand', {
            parameterName,
            filter: this.getFilePickerFilter(parameterName)
        });
    }

    handleContainerKeyDown(event) {
        if (this.disposed || (event.key !== 'Enter' && event.key !== ' ')) {
            return;
        }

        const colorPreview = event.target?.closest?.('.color-preview-box');
        if (!colorPreview) {
            return;
        }

        event.preventDefault();
        this.openColorPicker(colorPreview);
    }

    handleContainerInput(event) {
        const slider = event.target?.closest?.('.param-slider');
        if (slider) {
            this.syncNumberInputFromSlider(slider);
            return;
        }

        const numberInput = event.target?.closest?.('input[type="number"][data-property-parameter="true"]');
        if (numberInput) {
            this.syncSliderFromNumberInput(numberInput);
        }

        const colorInput = event.target?.closest?.('input[type="color"][data-property-parameter="true"]');
        if (colorInput) {
            const wrapper = colorInput.closest('.color-picker-wrapper');
            const preview = wrapper?.querySelector('.color-preview-box');
            const valueText = wrapper?.querySelector('.color-value');
            if (preview) {
                preview.style.backgroundColor = colorInput.value;
            }
            if (valueText) {
                valueText.textContent = colorInput.value;
            }
        }
    }

    openColorPicker(colorPreview) {
        if (!colorPreview || colorPreview.getAttribute('aria-disabled') === 'true') {
            return;
        }

        const input = colorPreview
            .closest('.color-picker-wrapper')
            ?.querySelector('input[type="color"][data-property-parameter="true"]');
        if (!input || input.disabled || input.readOnly) {
            return;
        }

        input.focus?.();
        input.click?.();
    }

    handleFilePickedEvent(event) {
        if (this.disposed || !this.currentNodeId) {
            return;
        }

        const payload = event?.payload || event?.data || event || {};
        const isCancelled = Boolean(payload.IsCancelled ?? payload.isCancelled);
        if (isCancelled) {
            return;
        }

        const parameterName = String(payload.ParameterName ?? payload.parameterName ?? '').trim();
        const filePathRaw = payload.FilePath ?? payload.filePath;
        const filePath = filePathRaw == null ? '' : String(filePathRaw);
        if (!parameterName) {
            console.warn('[PropertyPanelCapabilityOwner] FilePickedEvent missing parameterName:', payload);
            return;
        }

        const input = this.findParamInput(parameterName);
        if (!input) {
            console.warn('[PropertyPanelCapabilityOwner] File parameter input not found:', parameterName);
            return;
        }

        if (isElementEffectivelyDisabled(input)) {
            return;
        }

        if (this.isImageAcquisitionOperator() && normalizeParameterName(parameterName) === 'filepath') {
            const sourceTypeInput = this.findParamInput('SourceType', 'sourceType');
            if (sourceTypeInput && normalizeAcquisitionSourceType(sourceTypeInput.value) !== 'file') {
                const fileOption = Array.from(sourceTypeInput.options || [])
                    .find(option => normalizeAcquisitionSourceType(option.value) === 'file');
                sourceTypeInput.value = fileOption?.value || 'File';
            }
        }

        input.value = filePath;
        this.handleContainerChange({ target: input });
    }

    findNumberInputForSlider(slider) {
        const parent = slider?.parentElement;
        if (!parent) {
            return null;
        }

        const selector = 'input[type="number"][data-property-parameter="true"]';
        const sliderName = String(slider.name || slider.getAttribute?.('name') || '').trim();
        const candidates = Array.from(parent.querySelectorAll?.(selector) || []);
        if (sliderName) {
            const namedInput = candidates.find(input =>
                String(input.name || input.getAttribute?.('name') || '').trim() === sliderName
            );
            if (namedInput) {
                return namedInput;
            }
        }

        return candidates[0] || parent.querySelector?.(selector) || null;
    }

    findSliderForNumberInput(input) {
        const parent = input?.parentElement;
        if (!parent) {
            return null;
        }

        const inputName = String(input.name || input.getAttribute?.('name') || '').trim();
        const candidates = Array.from(parent.querySelectorAll?.('.param-slider') || []);
        if (inputName) {
            const namedSlider = candidates.find(slider =>
                String(slider.name || slider.getAttribute?.('name') || '').trim() === inputName
            );
            if (namedSlider) {
                return namedSlider;
            }
        }

        return candidates[0] || parent.querySelector?.('.param-slider') || null;
    }

    syncNumberInputFromSlider(slider) {
        const numberInput = this.findNumberInputForSlider(slider);
        if (numberInput && !numberInput.disabled && !numberInput.readOnly) {
            numberInput.value = slider.value;
        }

        return numberInput;
    }

    syncSliderFromNumberInput(input) {
        const slider = this.findSliderForNumberInput(input);
        if (slider && !slider.disabled && !slider.readOnly) {
            slider.value = input.value;
        }

        return slider;
    }

    handleContainerChange(event) {
        const slider = event.target?.closest?.('.param-slider');
        const input = slider
            ? this.syncNumberInputFromSlider(slider)
            : event.target?.closest?.('[data-property-parameter="true"]');
        if (!input || !this.currentNodeId || this.disposed) {
            return;
        }
        if (isElementEffectivelyDisabled(input)) {
            return;
        }

        if (!slider && input.type === 'number') {
            this.syncSliderFromNumberInput(input);
        }

        const values = this.collectNormalizedFormValues(input.name);
        const parameterName = input.name;
        if (['sourcetype', 'cameraid', 'camerabindingid'].includes(normalizeParameterName(parameterName))) {
            this.lastCameraCaptureMessage = '';
        }
        const errors = this.validateValues(values);
        const hasBlockingError = errors.some(error => isBlockingValidationErrorForParameter(error, parameterName));
        if (hasBlockingError) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        const writeValues = this.buildWriteValues(parameterName, values);
        if (Object.keys(writeValues).length === 0) {
            this.validationErrors = errors;
            this.statusMessage = errors.length > 0 ? '参数校验失败' : '参数未变更';
            this.lastChangedParameterName = null;
            this.dirty = false;
            this.syncParameterDependencyControls({
                clearFilePathWhenCamera: normalizeParameterName(parameterName) === 'sourcetype'
            });
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        let result;
        try {
            result = this.propertyAdapter.writeParameters(this.currentNodeId, writeValues);
        } catch (error) {
            this.validationErrors = errors;
            this.statusMessage = getParameterWriteExceptionMessage(error);
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        const writeFailureMessage = getParameterWriteFailureMessage(result, this.currentOperator?.parameters || []);
        if (writeFailureMessage) {
            this.validationErrors = errors;
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        const updated = result?.updated === true;
        this.validationErrors = errors;
        this.statusMessage = errors.length > 0 ? '参数校验失败' : (updated ? '参数已更新' : '参数未变更');
        this.lastChangedParameterName = parameterName;
        this.dirty = updated;
        Object.entries(writeValues).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.syncParameterDependencyControls({
            clearFilePathWhenCamera: normalizeParameterName(parameterName) === 'sourcetype'
        });
        this.renderValidationErrors();
        this.updateStatus();
    }

    isImageAcquisitionOperator() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim() === 'ImageAcquisition';
    }

    cancelCameraCapture() {
        this.cameraCaptureAbortController?.abort?.();
        this.cameraCaptureAbortController = null;
        this.cameraCapturePending = false;
    }

    getImageAcquisitionCaptureContext(values = null) {
        const currentValues = values || this.collectNormalizedFormValues();
        const sourceEntry = Object.entries(currentValues)
            .find(([name]) => normalizeParameterName(name) === 'sourcetype');
        const cameraEntry = Object.entries(currentValues)
            .find(([name]) => ['cameraid', 'camerabindingid'].includes(normalizeParameterName(name)) && !isEmptyValue(currentValues[name]));
        const sourceType = normalizeAcquisitionSourceType(sourceEntry?.[1]);
        const cameraBindingId = String(cameraEntry?.[1] || '').trim();
        const binding = this.cameraBindingsCache.find(item =>
            String(item?.id || '').trim().toLowerCase() === cameraBindingId.toLowerCase()) || null;
        const triggerMode = normalizeCameraTriggerMode(binding?.triggerMode || binding?.TriggerMode);

        return {
            sourceType,
            cameraBindingId,
            binding,
            triggerMode,
            canCapture: this.isImageAcquisitionOperator() && sourceType === 'camera' && Boolean(cameraBindingId)
        };
    }

    refreshImageAcquisitionCaptureControls() {
        const section = this.container?.querySelector?.('[data-property-camera-capture-section]');
        if (!section) {
            return;
        }

        const context = this.getImageAcquisitionCaptureContext();
        const button = section.querySelector('[data-property-camera-capture-frame]');
        const bindingLabel = section.querySelector('[data-property-camera-capture-binding]');
        const hint = section.querySelector('[data-property-camera-capture-hint]');

        if (button) {
            button.disabled = this.cameraCapturePending || !context.canCapture;
            button.setAttribute('aria-disabled', button.disabled ? 'true' : 'false');
            button.textContent = this.cameraCapturePending ? '正在获取单帧...' : '获取单帧图像';
        }
        if (bindingLabel) {
            bindingLabel.textContent = context.cameraBindingId
                ? `${context.binding?.displayName || context.binding?.name || context.cameraBindingId} · ${context.triggerMode}`
                : '尚未选择相机';
        }
        if (hint && !this.cameraCapturePending) {
            hint.textContent = this.lastCameraCaptureMessage || (context.sourceType !== 'camera'
                ? '将采集源切换为“相机”后可获取单帧。'
                : (!context.cameraBindingId
                    ? '请先选择相机绑定。'
                    : '单帧会直接送往预览工作台，并作为后续 ROI/图像算子的预览输入。'));
            hint.classList.toggle('is-error', this.lastCameraCaptureMessage.startsWith('获取单帧失败') || !context.canCapture);
        }
    }

    async fetchCameraBindingFrame(context, signal) {
        if (context.triggerMode === 'Continuous' || context.triggerMode === 'External') {
            const session = await httpClient.post('/cameras/continuous-preview/start', {
                cameraBindingId: context.cameraBindingId
            }, { signal });
            const sessionId = session?.sessionId || session?.SessionId;
            if (!sessionId) {
                throw new Error('相机共享帧会话启动失败。');
            }

            try {
                return await httpClient.getForBlob(
                    `/cameras/continuous-preview/frame/${encodeURIComponent(sessionId)}?_=${Date.now()}`,
                    { signal, cache: 'no-store' });
            } finally {
                await httpClient.post('/cameras/continuous-preview/stop', { sessionId }).catch(() => {});
            }
        }

        return await httpClient.postForBlob('/cameras/soft-trigger-capture', {
            cameraBindingId: context.cameraBindingId
        }, { signal });
    }

    async captureSelectedCameraFrame() {
        if (this.cameraCapturePending || !this.currentNodeId || !this.currentOperator) {
            return;
        }

        const context = this.getImageAcquisitionCaptureContext();
        if (!context.canCapture) {
            this.showToast(context.sourceType !== 'camera' ? '请先将采集源切换为相机' : '请先选择相机绑定', 'warning');
            this.refreshImageAcquisitionCaptureControls();
            return;
        }

        const captureNodeId = this.currentNodeId;
        const abortController = typeof AbortController !== 'undefined' ? new AbortController() : null;
        this.cameraCaptureAbortController = abortController;
        this.cameraCapturePending = true;
        this.lastCameraCaptureMessage = '';
        const hint = this.container.querySelector('[data-property-camera-capture-hint]');
        if (hint) {
            hint.textContent = `正在从 ${context.binding?.displayName || context.cameraBindingId} 获取单帧...`;
            hint.classList.remove('is-error');
        }
        this.refreshImageAcquisitionCaptureControls();

        try {
            const { blob, headers } = await this.fetchCameraBindingFrame(context, abortController?.signal);
            if (!blob || blob.size === 0) {
                throw new Error('相机未返回图像数据。');
            }
            const imageBase64 = await blobToBase64(blob);
            if (!imageBase64) {
                throw new Error('相机图像转换失败。');
            }
            if (this.disposed || captureNodeId !== this.currentNodeId) {
                return;
            }

            const width = Number(headers?.get?.('X-Image-Width')) || null;
            const height = Number(headers?.get?.('X-Image-Height')) || null;
            const frame = createCameraPreviewInputContext(this.currentOperator, {
                imageBase64,
                cameraBindingId: headers?.get?.('X-Camera-Id') || context.cameraBindingId,
                triggerMode: headers?.get?.('X-Trigger-Mode') || context.triggerMode,
                width,
                height,
                source: 'camera-single-frame',
                capturedAtUtc: new Date().toISOString()
            });

            this.previewCoordinator?.publishExternalFrame?.(this.currentOperator, frame);
            this.onCapturePreviewInput(frame);
            const sizeText = width && height ? ` ${width}×${height}` : '';
            this.statusMessage = `已获取单帧${sizeText}，已送往预览工作台`;
            this.lastCameraCaptureMessage = `${this.statusMessage}；后续 ROI 算子可直接使用该图像进行区域选择。`;
            this.showToast(this.statusMessage, 'success');
            this.updateStatus();
            if (hint) {
                hint.textContent = this.lastCameraCaptureMessage;
                hint.classList.remove('is-error');
            }
        } catch (error) {
            if (error?.name === 'AbortError') {
                return;
            }
            const message = error?.message || '未知错误';
            this.statusMessage = `获取单帧失败：${message}`;
            this.lastCameraCaptureMessage = this.statusMessage;
            this.showToast(this.statusMessage, 'error');
            this.updateStatus();
            if (hint) {
                hint.textContent = this.statusMessage;
                hint.classList.add('is-error');
            }
        } finally {
            if (this.cameraCaptureAbortController === abortController) {
                this.cameraCaptureAbortController = null;
            }
            this.cameraCapturePending = false;
            this.refreshImageAcquisitionCaptureControls();
        }
    }

    findParamInput(...names) {
        const normalizedNames = names.map(normalizeParameterName).filter(Boolean);
        if (normalizedNames.length === 0) {
            return null;
        }

        return Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .find(input => normalizedNames.includes(normalizeParameterName(input.name))) || null;
    }

    getCurrentParameterByName(name) {
        const normalizedName = normalizeParameterName(name);
        return (this.currentOperator?.parameters || [])
            .find(parameter => normalizeParameterName(getParameterName(parameter)) === normalizedName) || null;
    }

    updateCurrentOperatorParameterValue(name, value) {
        const parameter = this.getCurrentParameterByName(name);
        if (!parameter) {
            return;
        }

        parameter.value = value;
        if (Object.prototype.hasOwnProperty.call(parameter, 'Value')) {
            parameter.Value = value;
        }
    }

    normalizeImageAcquisitionValues(values, changedName = '') {
        if (!this.isImageAcquisitionOperator() || !values) {
            return values;
        }

        const nextValues = { ...values };
        const sourceKey = Object.keys(nextValues).find(key => normalizeParameterName(key) === 'sourcetype');
        const filePathKey = Object.keys(nextValues).find(key => normalizeParameterName(key) === 'filepath');
        if (!sourceKey) {
            return nextValues;
        }

        const normalizedSource = normalizeAcquisitionSourceType(nextValues[sourceKey]);
        if (normalizedSource === 'camera' && filePathKey && normalizeParameterName(changedName) === 'sourcetype') {
            nextValues[filePathKey] = '';
        }

        if (normalizeParameterName(changedName) === 'filepath' && filePathKey && nextValues[filePathKey]) {
            nextValues[sourceKey] = this.findFileSourceValue(sourceKey);
        }

        return nextValues;
    }

    findFileSourceValue(sourceKey = 'SourceType') {
        const sourceInput = this.findParamInput(sourceKey, 'SourceType', 'sourceType');
        if (!sourceInput) {
            return 'File';
        }

        const fileOption = Array.from(sourceInput.options || [])
            .find(option => normalizeAcquisitionSourceType(option.value) === 'file');
        return fileOption?.value || sourceInput.value || 'File';
    }

    buildWriteValues(parameterName, values) {
        const writeValues = {
            [parameterName]: values[parameterName]
        };

        if (!this.isImageAcquisitionOperator()) {
            return this.filterWritableValues(writeValues, values);
        }

        const normalizedParameterName = normalizeParameterName(parameterName);
        const sourceKey = Object.keys(values).find(key => normalizeParameterName(key) === 'sourcetype');
        const filePathKey = Object.keys(values).find(key => normalizeParameterName(key) === 'filepath');

        if (sourceKey && normalizedParameterName === 'filepath') {
            writeValues[sourceKey] = values[sourceKey];
        }

        if (sourceKey && filePathKey && normalizedParameterName === 'sourcetype' && normalizeAcquisitionSourceType(values[sourceKey]) === 'camera') {
            writeValues[filePathKey] = '';
        }

        return this.filterWritableValues(writeValues, values);
    }

    filterWritableValues(writeValues = {}, values = {}) {
        return Object.fromEntries(
            Object.entries(writeValues)
                .filter(([name, value]) => this.shouldWriteParameterValue(name, value, values))
        );
    }

    shouldWriteParameterValue(name, value, values = {}) {
        if (!this.currentOperator) {
            return true;
        }

        const parameter = this.getCurrentParameterByName(name);
        const state = getPanelParameterEffectiveState(this.currentOperator, parameter || name, { values });
        if (!state.effectiveDisabled) {
            return true;
        }

        const currentValue = parameter ? getParameterValue(parameter) : null;
        return !isEmptyValue(currentValue) && isEmptyValue(value);
    }

    buildEffectiveWriteValues(values = {}) {
        const writeValues = {};

        Object.entries(values).forEach(([name, value]) => {
            if (this.shouldWriteParameterValue(name, value, values)) {
                writeValues[name] = value;
            }
        });

        return writeValues;
    }

    shouldHideDisabledParameter(input, state) {
        void input;
        void state;
        return false;
    }

    updateParameterRuleHint(group, state) {
        if (!group) {
            return;
        }

        let hint = group.querySelector('[data-parameter-rule-hint="true"]');
        hint?.remove();
    }

    syncParameterDependencyControls(options = {}) {
        if (!this.currentOperator) {
            return;
        }

        const values = this.collectNormalizedFormValues();
        Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .filter(Boolean)
            .filter((input, index, all) => all.indexOf(input) === index)
            .forEach(input => {
                const parameter = this.getCurrentParameterByName(input.name);
                const state = getPanelParameterEffectiveState(this.currentOperator, parameter || input.name, { values });
                const group = input.closest('.form-group');
                const pickerButton = group?.querySelector('.btn-pick-file');
                const colorPreview = group?.querySelector('.color-preview-box');
                const label = group?.querySelector('.form-label');
                const requiredMark = label?.querySelector('.required');

                if (
                    this.isImageAcquisitionOperator() &&
                    state.effectiveDisabled &&
                    normalizeParameterName(input.name) === 'filepath' &&
                    options.clearFilePathWhenCamera === true &&
                    input.value
                ) {
                    input.value = '';
                    this.updateCurrentOperatorParameterValue(input.name, '');
                }

                input.disabled = state.effectiveDisabled;
                input.setAttribute('aria-disabled', state.effectiveDisabled ? 'true' : 'false');
                if (pickerButton) {
                    pickerButton.disabled = state.effectiveDisabled;
                    pickerButton.setAttribute('aria-disabled', state.effectiveDisabled ? 'true' : 'false');
                }
                group?.querySelectorAll('.param-slider').forEach(slider => {
                    slider.disabled = state.effectiveDisabled;
                    slider.setAttribute('aria-disabled', state.effectiveDisabled ? 'true' : 'false');
                });
                if (colorPreview) {
                    colorPreview.setAttribute('aria-disabled', state.effectiveDisabled ? 'true' : 'false');
                    colorPreview.setAttribute('tabindex', state.effectiveDisabled ? '-1' : '0');
                }

                group?.classList.toggle('hidden', this.shouldHideDisabledParameter(input, state));
                group?.classList.toggle('is-rule-disabled', state.effectiveDisabled);
                group?.setAttribute('data-effective-disabled', state.effectiveDisabled ? 'true' : 'false');
                group?.setAttribute('data-effective-required', state.effectiveRequired ? 'true' : 'false');
                this.updateParameterRuleHint(group, state);

                if (label && state.effectiveRequired && !requiredMark) {
                    label.insertAdjacentHTML('beforeend', ' <span class="required">*</span>');
                } else if (!state.effectiveRequired && requiredMark) {
                    requiredMark.remove();
                }
            });
        this.syncOutputAvailabilitySummary(values);
        this.refreshImageAcquisitionCaptureControls();
    }

    syncImageAcquisitionSourceControls(options = {}) {
        this.syncParameterDependencyControls(options);
        this.refreshImageAcquisitionCaptureControls();
    }

    getFilePickerFilter(parameterName = '') {
        const normalizedName = normalizeParameterName(parameterName);
        if (
            normalizedName.includes('model') ||
            normalizedName.includes('embedding') ||
            normalizedName.includes('onnx')
        ) {
            return 'Model Files|*.onnx;*.pt;*.pth;*.engine;*.xml;*.bin|All Files|*.*';
        }

        if (
            normalizedName.includes('label') ||
            normalizedName.includes('catalog') ||
            normalizedName.includes('bank') ||
            normalizedName.includes('json')
        ) {
            return 'Data Files|*.json;*.txt;*.yaml;*.yml;*.csv;*.bin|All Files|*.*';
        }

        if (
            normalizedName === 'filepath' ||
            normalizedName.includes('image') ||
            normalizedName.includes('template')
        ) {
            return 'Image Files|*.bmp;*.jpg;*.png;*.jpeg;*.tif;*.tiff|All Files|*.*';
        }

        return 'All Files|*.*';
    }

    async loadCameraBindingsForSelects(forceRefresh = false) {
        const loadToken = ++this.cameraBindingsLoadToken;
        const cameraSelects = this.container.querySelectorAll('select[data-camera-binding-select="true"]');
        if (cameraSelects.length === 0) {
            return;
        }

        try {
            const bindings = await this.fetchCameraBindings(forceRefresh);
            if (this.disposed || loadToken !== this.cameraBindingsLoadToken) {
                return;
            }
            this.populateCameraBindingSelects(cameraSelects, bindings);
        } catch (error) {
            if (this.disposed || loadToken !== this.cameraBindingsLoadToken) {
                return;
            }
            console.error('[PropertyPanelCapabilityOwner] Failed to load camera bindings:', error);
            this.populateCameraBindingSelects(cameraSelects, [], error?.message || 'Unknown error');
        }
    }

    async fetchCameraBindings(forceRefresh = false) {
        if (!forceRefresh && this.cameraBindingsCache.length > 0) {
            return this.cameraBindingsCache;
        }

        if (!forceRefresh && this.cameraBindingsLoadingPromise) {
            return this.cameraBindingsLoadingPromise;
        }

        this.cameraBindingsLoadingPromise = (async () => {
            const bindings = await httpClient.get('/cameras/bindings');
            this.cameraBindingsCache = Array.isArray(bindings) ? bindings : [];
            return this.cameraBindingsCache;
        })();

        try {
            return await this.cameraBindingsLoadingPromise;
        } finally {
            this.cameraBindingsLoadingPromise = null;
        }
    }

    populateCameraBindingSelects(selects, bindings, errorMessage = '') {
        const hasBindings = Array.isArray(bindings) && bindings.length > 0;

        selects.forEach(select => {
            const selectedCameraId = select.dataset.currentValue || select.value || '';
            let optionsHtml = '<option value="">-- Select camera --</option>';

            if (hasBindings) {
                optionsHtml += bindings.map(binding => `
                    <option value="${escapeAttribute(binding.id)}">
                        ${escapeHtml(binding.displayName || binding.name || binding.id)} (${escapeHtml(binding.serialNumber || '-')})
                    </option>
                `).join('');
            } else if (errorMessage) {
                optionsHtml += '<option value="" disabled>Load failed</option>';
            } else {
                optionsHtml += '<option value="" disabled>No camera bindings</option>';
            }

            select.innerHTML = optionsHtml;
            if (hasBindings && bindings.some(binding => String(binding.id) === String(selectedCameraId))) {
                select.value = selectedCameraId;
            } else {
                select.value = '';
            }

            const hint = select.closest('.form-group')?.querySelector('[data-camera-binding-hint]');
            if (!hint) {
                return;
            }

            if (hasBindings) {
                hint.remove();
                return;
            }

            hint.textContent = errorMessage
                ? `Failed to load camera bindings: ${errorMessage}`
                : 'No camera bindings are available.';
            hint.classList.add('error');
        });
        this.refreshImageAcquisitionCaptureControls();
    }

    setOperator(operator) {
        this.handleSelectedNodeChanged(operator);
    }

    shouldRenderParameterSlider(parameter) {
        return shouldRenderParameterSlider(parameter);
    }

    clear() {
        this.teardownGeometryEditor();
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
        this.validationErrors = [];
        this.statusMessage = '';
        this.lastChangedParameterName = null;
        this.dirty = false;
        this.render();
    }

    collectFormValues() {
        const form = this.container.querySelector('[data-property-capability-form="true"]');
        if (!form) {
            return {};
        }

        const values = {};
        form.querySelectorAll('[data-property-parameter="true"]').forEach(input => {
            if (!input.name) {
                return;
            }

            values[input.name] = getFormValue(input);
        });

        return values;
    }

    collectNormalizedFormValues(changedName = '') {
        return this.normalizeImageAcquisitionValues(this.collectFormValues(), changedName);
    }

    isCircleSearchV2ToolFeatureEnabled() {
        return isCircleSearchV2ToolEnabled({
            circleSearchV2ToolEnabled: this.circleSearchV2ToolEnabled
        });
    }

    isNPointCalibrationWorkbenchFeatureEnabled() {
        return isNPointCalibrationWorkbenchEnabled({
            nPointCalibrationWorkbenchEnabled: this.nPointCalibrationWorkbenchEnabled
        });
    }

    getGeometryEditorOptions() {
        return {
            circleSearchV2ToolEnabled: this.isCircleSearchV2ToolFeatureEnabled(),
            nPointCalibrationWorkbenchEnabled: this.isNPointCalibrationWorkbenchFeatureEnabled()
        };
    }

    getGeometryConfig(operator = this.currentOperator) {
        return getOperatorRoiConfig(operator, this.getGeometryEditorOptions());
    }

    getCurrentOperatorType() {
        return String(this.currentOperator?.type || this.currentOperator?.operatorType || '').trim();
    }

    isCaliperToolOperator() {
        return this.getCurrentOperatorType() === 'CaliperTool';
    }

    getGeometryEditorOperator() {
        if (!this.isCaliperToolOperator() || !this.currentNodeId) {
            return this.currentOperator;
        }

        return this.propertyAdapter.getCaliperSearchRegionOperatorSnapshot?.(this.currentNodeId) ||
            this.currentOperator;
    }

    buildGeometryWriteValues(values = {}) {
        const config = this.getGeometryConfig();
        return {
            ...(config?.commitValues || {}),
            ...values
        };
    }

    getCaliperSearchRegionStatus(result) {
        if (!result) {
            return 'CaliperTool.SearchRegion 写入失败：未收到写入结果。';
        }

        if (result?.updated !== false) {
            return '卡尺搜索区域已通过 RectangleRegion 连接更新';
        }

        switch (result?.reason) {
            case 'search_region_connected_to_non_rectangle_region':
                return 'CaliperTool.SearchRegion 已连接到非 RectangleRegion 节点，请先断开该连接或改用 RectangleRegion。';
            case 'search_region_connection_failed':
                return 'RectangleRegion 创建后连接 CaliperTool.SearchRegion 失败，已回滚新节点。';
            case 'rectangle_region_output_not_found':
                return 'RectangleRegion 缺少 Rectangle 输出端口，已回滚新节点。';
            case 'search_region_port_not_found':
                return 'CaliperTool.SearchRegion 输入端口不存在，无法写入搜索区域。';
            case 'flow_canvas_mutation_unavailable':
                return '当前画布不支持自动创建 SearchRegion 连接。';
            case 'rectangle_region_create_failed':
                return 'RectangleRegion 创建失败，搜索区域未写入。';
            default:
                return '卡尺搜索区域未变更。';
        }
    }

    writeInputValue(input, rawValue) {
        const type = String(input.dataset.type || '').toLowerCase();
        if (type === 'boolean' || type === 'bool') {
            input.checked = toBoolean(rawValue);
            return;
        }

        input.value = rawValue ?? '';

        const slider = input.parentElement?.querySelector('.param-slider');
        if (slider) {
            slider.value = input.value;
        }

        if (input.type === 'color') {
            const wrapper = input.closest('.color-picker-wrapper');
            const preview = wrapper?.querySelector('.color-preview-box');
            const valueText = wrapper?.querySelector('.color-value');
            if (preview) {
                preview.style.backgroundColor = input.value;
            }
            if (valueText) {
                valueText.textContent = input.value;
            }
        }
    }

    applyValuesToForm(values = {}) {
        Object.entries(values).forEach(([name, value]) => {
            const input = this.findParamInput(name);
            if (input) {
                this.writeInputValue(input, value);
            }
        });
    }

    handleGeometryChanged(values = {}, phase = 'dragging') {
        if (!this.currentNodeId || !this.currentOperator || this.disposed) {
            return;
        }

        const writeValues = this.buildGeometryWriteValues(values);
        this.applyValuesToForm(writeValues);

        if (phase !== 'commit') {
            this.statusMessage = '图上几何拖动中，松开后写回参数';
            this.updateStatus();
            return;
        }

        if (this.isCaliperToolOperator()) {
            let result;
            try {
                result = this.propertyAdapter.upsertCaliperSearchRegion?.(this.currentNodeId, writeValues);
            } catch (error) {
                this.validationErrors = [];
                this.statusMessage = getParameterWriteExceptionMessage(error);
                this.dirty = false;
                this.renderValidationErrors();
                this.updateStatus();
                return;
            }
            this.statusMessage = this.getCaliperSearchRegionStatus(result);
            this.dirty = result?.updated === true;
            this.updateStatus();
            if (result?.updated === true && result?.operator) {
                this.roiEditorPanel?.refreshFromOperator?.({ forceSyncOverlay: true });
            }
            return;
        }

        let result;
        try {
            result = this.propertyAdapter.writeParameters(this.currentNodeId, writeValues);
        } catch (error) {
            const writeFailureMessage = getParameterWriteExceptionMessage(error);
            this.validationErrors = [];
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }
        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        const writeFailureMessage = getParameterWriteFailureMessage(result, this.currentOperator?.parameters || []);
        if (writeFailureMessage) {
            this.validationErrors = [];
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        Object.entries(writeValues).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.validationErrors = [];
        this.statusMessage = '图上几何已写回参数';
        this.lastChangedParameterName = null;
        this.dirty = result?.updated === true;
        this.renderValidationErrors();
        this.updateStatus();
    }

    handleGeometryImageBoundsChanged(bounds) {
        this.geometryImageBounds = bounds &&
            Number.isFinite(Number(bounds.width)) &&
            Number.isFinite(Number(bounds.height)) &&
            Number(bounds.width) > 0 &&
            Number(bounds.height) > 0
            ? { width: Number(bounds.width), height: Number(bounds.height) }
            : null;
    }

    syncGeometryEditorFromParams() {
        this.roiEditorPanel?.refreshFromOperator?.({ forceSyncOverlay: true });
    }

    handleGeometryClearRoi() {
        if (!this.currentNodeId || !this.currentOperator) {
            return;
        }

        const config = this.getGeometryConfig();
        const values = config?.clearValues || null;
        if (!values) {
            return;
        }

        let result;
        try {
            result = this.propertyAdapter.writeParameters(this.currentNodeId, values);
        } catch (error) {
            this.validationErrors = [];
            this.statusMessage = getParameterWriteExceptionMessage(error);
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }
        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.render();
            return;
        }

        const writeFailureMessage = getParameterWriteFailureMessage(result, this.currentOperator?.parameters || []);
        if (writeFailureMessage) {
            this.validationErrors = [];
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            this.renderValidationErrors();
            this.updateStatus();
            return;
        }

        this.applyValuesToForm(values);
        Object.entries(values).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.statusMessage = '搜索 ROI 已清除';
        this.dirty = result?.updated === true;
        this.updateStatus();
        this.syncGeometryEditorFromParams();
    }

    teardownGeometryEditor() {
        if (!this.roiEditorPanel) {
            return;
        }

        this.roiEditorPanel.destroy?.();
        this.roiEditorPanel = null;
    }

    initGeometryEditor() {
        this.teardownGeometryEditor();

        if (!this.currentOperator || this.currentConnection) {
            return;
        }

        const config = this.getGeometryConfig();
        if (!config?.supported) {
            return;
        }

        const container = this.container.querySelector('[data-property-geometry-editor-container]');
        if (!container) {
            return;
        }

        this.roiEditorPanel = new RoiEditorPanel(container, {
            getOperator: () => this.getGeometryEditorOperator(),
            getPreviewOperator: () => this.currentOperator,
            getRoiConfig: operator => {
                if (this.isCaliperToolOperator()) {
                    return this.getGeometryConfig(this.currentOperator);
                }

                return getOperatorRoiConfig(operator, this.getGeometryEditorOptions());
            },
            previewCoordinator: this.previewCoordinator,
            getFallbackImageSource: () => this.getPreviewInputImageSource(this.currentNodeId),
            previewResourcesEnabled: this.previewResourcesEnabled,
            onOpenPreviewImage: this.onOpenPreviewImage,
            onRectChanged: (values, phase) => this.handleGeometryChanged(values, phase),
            onImageBoundsChanged: bounds => this.handleGeometryImageBoundsChanged(bounds),
            onRequestSyncFromParams: () => this.syncGeometryEditorFromParams()
        });
    }

    renderGeometrySection(config) {
        if (!config?.supported) {
            return '';
        }

        return `
            <section class="property-summary-section property-geometry-section" data-property-geometry-section="true">
                <div class="property-geometry-header">
                    <h5>图上几何</h5>
                    ${config.clearValues ? `
                        <button type="button"
                                class="btn btn-secondary btn-sm"
                                data-property-geometry-clear-roi="true">
                            清除 ROI
                        </button>
                    ` : ''}
                </div>
                ${config.description ? `<p class="property-geometry-description">${escapeHtml(config.description)}</p>` : ''}
                <div data-property-geometry-editor-container></div>
            </section>
        `;
    }

    renderImageAcquisitionCaptureSection(operator) {
        const type = String(operator?.type || operator?.operatorType || '').trim();
        if (type !== 'ImageAcquisition') {
            return '';
        }

        return `
            <section class="property-summary-section property-camera-capture-section" data-property-camera-capture-section="true">
                <div class="property-camera-capture-header">
                    <h5>相机单帧预览</h5>
                    <span data-property-camera-capture-binding>尚未选择相机</span>
                </div>
                <p class="property-camera-capture-description">显式抓取当前绑定的一帧图像，不执行完整检测流程，也不会放宽普通节点预览的设备访问安全策略。</p>
                <button type="button"
                        class="btn btn-primary btn-sm property-camera-capture-button"
                        data-property-camera-capture-frame="true"
                        disabled
                        aria-disabled="true">获取单帧图像</button>
                <p class="property-camera-capture-hint" data-property-camera-capture-hint>请先选择相机绑定。</p>
            </section>
        `;
    }

    validateValues(values) {
        if (!this.currentOperator) {
            return [];
        }

        return this.validateOperatorModel(this.currentOperator, { values });
    }

    validateOperatorModel(operator, options = {}) {
        const values = options.values || null;
        const parameters = Array.isArray(operator?.parameters) ? operator.parameters : [];
        const errors = [];

        collectEffectiveRequiredParameterErrors(operator, parameters, {
            values,
            getLabel: (parameter, fallbackName) => getParameterLabel(parameter, fallbackName)
        }).forEach(error => {
            errors.push({
                ...error,
                name: error.name,
                message: buildValidationErrorMessage(error, operator, parameters, values)
            });
        });

        collectMutuallyExclusiveParameterErrors(operator, parameters, values)
            .forEach(error => errors.push(error));

        parameters.forEach(parameter => {
            const name = getParameterName(parameter);
            if (!name) {
                return;
            }

            const state = getPanelParameterEffectiveState(operator, parameter, { values });
            if (state.effectiveDisabled) {
                return;
            }

            const dataType = getParameterDataType(parameter);
            const value = values && Object.prototype.hasOwnProperty.call(values, name)
                ? values[name]
                : getParameterValue(parameter);

            if (!['int', 'integer', 'double', 'float', 'number'].includes(dataType) || isEmptyValue(value)) {
                return;
            }

            const parsed = parseNumericValue(value);
            const label = getParameterLabel(parameter, name);
            if (!parsed.valid) {
                errors.push({ name, message: `${label} 必须是有效数字` });
                return;
            }

            if ((dataType === 'int' || dataType === 'integer') && !Number.isInteger(parsed.value)) {
                errors.push({ name, message: `${label} 必须是整数` });
                return;
            }

            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            if (min !== null && parsed.value < min) {
                errors.push({ name, message: `${label} 不能小于 ${min}` });
                return;
            }
            if (max !== null && parsed.value > max) {
                errors.push({ name, message: `${label} 不能大于 ${max}` });
            }
        });

        return errors;
    }

    validateCurrentOperator(options = {}) {
        const errors = this.validateValues(this.collectNormalizedFormValues());
        this.validationErrors = errors;

        if (errors.length > 0) {
            this.statusMessage = '参数校验失败';
            if (options.showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        this.renderValidationErrors();
        this.updateStatus();
        return true;
    }

    validateFlowForAction(flowCanvas, options = {}) {
        const action = options.action || '执行';
        if (this.currentOperator && this.validateCurrentOperator({
            showToast: options.showToast === true
        }) === false) {
            return false;
        }

        const nodes = flowCanvas?.nodes instanceof Map
            ? Array.from(flowCanvas.nodes.values())
            : Array.from(this.propertyAdapter.flowCanvasAdapter?.nodes?.values?.() || []);
        for (const node of nodes) {
            if (!node?.id || node.id === this.currentNodeId) {
                continue;
            }

            const operator = this.propertyAdapter.getSelectedOperatorSnapshot(node.id);
            const errors = this.validateOperatorModel(operator);
            if (errors.length === 0) {
                continue;
            }

            this.propertyAdapter.selectNode(node.id);
            if (options.showToast) {
                this.showToast(`${action}前请先修正参数：${errors[0].message}`, 'error');
            }
            return false;
        }

        return true;
    }

    applyChanges(options = {}) {
        const showToast = options.showToast !== false;
        if (!this.currentNodeId || !this.currentOperator) {
            return true;
        }

        const values = this.collectNormalizedFormValues();
        const errors = this.validateValues(values);
        if (errors.length > 0) {
            this.validationErrors = errors;
            this.statusMessage = '参数校验失败';
            if (showToast) {
                this.showToast(`参数校验失败：${errors[0].message}`, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        const writeValues = this.buildEffectiveWriteValues(values);
        if (Object.keys(writeValues).length === 0) {
            this.validationErrors = [];
            this.statusMessage = '参数未变更';
            this.lastChangedParameterName = null;
            this.dirty = false;
            this.syncParameterDependencyControls();
            this.renderValidationErrors();
            this.updateStatus();
            return true;
        }

        let result;
        try {
            result = this.propertyAdapter.writeParameters(this.currentNodeId, writeValues);
        } catch (error) {
            const writeFailureMessage = getParameterWriteExceptionMessage(error);
            this.validationErrors = [];
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            if (showToast) {
                this.showToast(writeFailureMessage, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }
        if (result?.reason === 'node_not_found') {
            this.currentOperator = null;
            this.currentNodeId = null;
            this.validationErrors = [];
            this.statusMessage = '';
            this.render();
            return false;
        }

        const writeFailureMessage = getParameterWriteFailureMessage(result, this.currentOperator?.parameters || []);
        if (writeFailureMessage) {
            this.validationErrors = [];
            this.statusMessage = writeFailureMessage;
            this.dirty = false;
            if (showToast) {
                this.showToast(writeFailureMessage, 'error');
            }
            this.renderValidationErrors();
            this.updateStatus();
            return false;
        }

        const updated = result?.updated === true;
        this.validationErrors = [];
        this.statusMessage = updated ? '参数已更新' : '参数未变更';
        this.lastChangedParameterName = null;
        this.dirty = updated;
        Object.entries(writeValues).forEach(([name, value]) => this.updateCurrentOperatorParameterValue(name, value));
        this.syncParameterDependencyControls();
        if (showToast && updated) {
            this.showToast('参数已更新', 'success');
        }
        this.renderValidationErrors();
        this.updateStatus();
        return true;
    }

    syncDraftChanges(options = {}) {
        return this.applyChanges({
            showToast: options.showToast === true
        });
    }

    render() {
        if (this.disposed) {
            return;
        }

        this.teardownGeometryEditor();

        if (!this.currentOperator) {
            if (this.currentConnection) {
                this.renderConnectionSummary();
                return;
            }

            this.container.innerHTML = `
                <section class="property-capability-owner property-capability-empty" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                    <div class="property-capability-title">属性面板</div>
                    <p class="property-capability-empty-title">未选择算子</p>
                    <p class="empty-text">请选择一个算子</p>
                </section>
            `;
            return;
        }

        const operator = this.currentOperator;
        const title = operator.title || operator.displayName || operator.type || '算子';
        const type = operator.type || '';
        const typeDisplay = getOperatorTypeDisplayName(type, { includeType: true }) || type;
        const parameters = Array.isArray(operator.parameters) ? operator.parameters : [];
        const geometryConfig = this.getGeometryConfig(operator);

        this.container.innerHTML = `
            <section class="property-capability-owner" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}">
                <header class="property-header property-capability-header">
                    <div class="header-text">
                        <div class="property-capability-title">属性面板</div>
                        <h4>${escapeHtml(title)}</h4>
                        <span class="property-type">${escapeHtml(typeDisplay)}</span>
                    </div>
                </header>
                <div class="property-capability-scroll" data-low-height-scroll="true">
                    <section class="property-summary-section">
                        <h5>基础信息</h5>
                        <dl class="property-capability-meta">
                            <div>
                                <dt>当前算子</dt>
                                <dd>${escapeHtml(title)}</dd>
                            </div>
                            <div>
                                <dt>类型</dt>
                                <dd>${escapeHtml(typeDisplay)}</dd>
                            </div>
                        </dl>
                    </section>
                    ${this.renderGeometrySection(geometryConfig)}
                    <section class="property-summary-section">
                        <h5>参数</h5>
                        ${parameters.length === 0
                            ? '<p class="property-summary-empty">当前算子没有可编辑参数</p>'
                            : `<form class="property-form property-capability-form" data-property-capability-form="true">
                                ${parameters.map(parameter => this.renderParameterField(parameter)).join('')}
                                <button type="submit" class="hidden" aria-hidden="true" tabindex="-1">应用</button>
                            </form>`}
                    </section>
                    ${this.renderImageAcquisitionCaptureSection(operator)}
                </div>
                <div class="property-capability-status" data-property-capability-status aria-live="polite">
                    ${escapeHtml(this.statusMessage)}
                </div>
            </section>
        `;
        this.renderValidationErrors();
        this.syncParameterDependencyControls();
        void this.loadCameraBindingsForSelects();
        this.updateStatus();
        this.initGeometryEditor();
    }

    renderConnectionSummary() {
        const connection = this.currentConnection || {};
        this.container.innerHTML = `
            <section class="property-capability-owner inspector-connection-summary" data-owner="${PROPERTY_PANEL_CAPABILITY_OWNER_ID}" data-selection-kind="connection">
                <div class="property-capability-title">属性面板</div>
                <h4>当前选中连线</h4>
                <p>连线当前没有可编辑参数。可在画布中删除或重新连接端口。</p>
                <dl class="inspector-connection-meta">
                    <div>
                        <dt>输出节点</dt>
                        <dd>${escapeHtml(connection.sourceTitle || '-')} / ${escapeHtml(connection.sourcePortName || '-')} (${escapeHtml(connection.sourcePortType || '-')})</dd>
                    </div>
                    <div>
                        <dt>输入节点</dt>
                        <dd>${escapeHtml(connection.targetTitle || '-')} / ${escapeHtml(connection.targetPortName || '-')} (${escapeHtml(connection.targetPortType || '-')})</dd>
                    </div>
                    <div>
                        <dt>连线 ID</dt>
                        <dd>${escapeHtml(connection.id || '-')}</dd>
                    </div>
                </dl>
            </section>
        `;
    }

    renderParameterField(parameter) {
        const name = getParameterName(parameter);
        if (!name) {
            return '';
        }

        const label = getParameterLabel(parameter, name);
        const dataType = getParameterDataType(parameter);
        const description = getParameterDescription(parameter);
        const value = getParameterValue(parameter);
        const values = this.currentOperator ? null : {};
        const effectiveState = getPanelParameterEffectiveState(this.currentOperator, parameter, { values });
        const requiredMark = effectiveState.effectiveRequired ? '<span class="required">*</span>' : '';
        const isDisabled = effectiveState.effectiveDisabled;
        const disabled = isDisabled ? 'disabled aria-disabled="true"' : '';
        const disabledHint = '';

        return `
            <div class="form-group ${effectiveState.effectiveDisabled ? 'is-rule-disabled' : ''}" data-parameter-name="${escapeAttribute(name)}">
                <label for="${escapeAttribute(getParameterInputId(name))}" class="form-label">${escapeHtml(label)} ${requiredMark}</label>
                ${this.renderInput(parameter, { name, label, dataType, value, disabled, isDisabled })}
                ${description ? `<p class="form-description">${escapeHtml(description)}</p>` : ''}
                ${disabledHint}
            </div>
        `;
    }

    renderInput(parameter, { name, label, dataType, value, disabled, isDisabled = false }) {
        const inputId = escapeAttribute(getParameterInputId(name));
        const common = `id="${inputId}" name="${escapeAttribute(name)}" data-property-parameter="true"`;
        const currentValue = value ?? '';
        const controlType = resolveParameterControlType(parameter);
        const safeLabel = label || name;
        const filePickerLabel = getPickerActionLabel(safeLabel, '文件');
        const colorPickerLabel = getPickerActionLabel(safeLabel, '颜色');

        if (controlType === 'boolean' || controlType === 'bool') {
            return `
                <label class="property-toggle">
                    <input type="checkbox" ${common} data-type="boolean" ${toBoolean(currentValue) ? 'checked' : ''} ${disabled}>
                    <span class="toggle-slider"></span>
                </label>
            `;
        }

        if (controlType === 'enum' || controlType === 'select') {
            const options = getParameterOptions(parameter);
            if (options.length > 0) {
                return `
                    <select ${common} class="form-select" data-type="enum" ${disabled}>
                        ${options.map(option => {
                            const optionLabel = typeof option === 'string' ? option : (option.label ?? option.Label ?? option.value ?? option.Value ?? '');
                            const optionValue = typeof option === 'string' ? option : (option.value ?? option.Value ?? optionLabel);
                            return `<option value="${escapeAttribute(optionValue)}" ${String(optionValue) === String(currentValue) ? 'selected' : ''}>${escapeHtml(optionLabel)}</option>`;
                        }).join('')}
                    </select>
                `;
            }
        }

        if (controlType === 'file') {
            return `
                <div class="file-picker-wrapper">
                    <input type="text"
                           ${common}
                           value="${escapeAttribute(currentValue)}"
                           class="form-input"
                           readonly
                           data-type="file"
                           ${disabled}>
                    <button type="button"
                            class="btn btn-sm btn-secondary btn-pick-file"
                            data-param="${escapeAttribute(name)}"
                            aria-label="${escapeAttribute(filePickerLabel)}"
                            title="${escapeAttribute(filePickerLabel)}"
                            ${isDisabled ? 'disabled aria-disabled="true"' : 'aria-disabled="false"'}>...</button>
                </div>
            `;
        }

        if (controlType === 'cameraBinding') {
            const bindings = this.cameraBindingsCache || [];
            const hasBindings = bindings.length > 0;
            return `
                <select ${common}
                        class="form-select"
                        data-type="string"
                        data-camera-binding-select="true"
                        data-current-value="${escapeAttribute(currentValue)}"
                        ${disabled}>
                    <option value="">-- Select camera --</option>
                    ${hasBindings
                        ? bindings.map(binding => `
                            <option value="${escapeAttribute(binding.id)}" ${String(binding.id) === String(currentValue) ? 'selected' : ''}>
                                ${escapeHtml(binding.displayName || binding.name || binding.id)} (${escapeHtml(binding.serialNumber || '-')})
                            </option>
                        `).join('')
                        : '<option value="" disabled>Loading...</option>'}
                </select>
                ${hasBindings ? '' : '<p class="form-description error" data-camera-binding-hint>Loading camera bindings...</p>'}
            `;
        }

        if (controlType === 'color') {
            const colorValue = currentValue || '#000000';
            return `
                <div class="color-picker-wrapper">
                    <input type="color"
                           ${common}
                           value="${escapeAttribute(colorValue)}"
                           class="form-color-hidden"
                           data-type="color"
                           ${disabled}>
                    <div class="color-preview-box"
                         role="button"
                         tabindex="${isDisabled ? '-1' : '0'}"
                         aria-disabled="${isDisabled ? 'true' : 'false'}"
                         aria-label="${escapeAttribute(colorPickerLabel)}"
                         title="${escapeAttribute(colorPickerLabel)}"
                         style="background-color: ${escapeAttribute(colorValue)}">
                        <span class="color-value">${escapeHtml(colorValue)}</span>
                    </div>
                </div>
            `;
        }

        if (['int', 'integer', 'double', 'float', 'number'].includes(controlType)) {
            const min = getParameterRangeValue(parameter, 'min', 'Min', 'minValue', 'MinValue');
            const max = getParameterRangeValue(parameter, 'max', 'Max', 'maxValue', 'MaxValue');
            const step = parameter?.step ?? parameter?.Step ?? (controlType === 'int' || controlType === 'integer' ? 1 : 0.1);
            const hasRange = min !== null && max !== null;
            const shouldRenderSlider = hasRange && this.shouldRenderParameterSlider(parameter);
            return `
                <div class="number-input-wrapper">
                    <input type="number"
                           ${common}
                           value="${escapeAttribute(currentValue)}"
                           ${min !== null ? `min="${min}"` : ''}
                           ${max !== null ? `max="${max}"` : ''}
                           step="${escapeAttribute(step)}"
                           class="form-input number-input"
                           data-type="${controlType}"
                           ${disabled}>
                    ${shouldRenderSlider ? `
                        <input type="range"
                               class="param-slider"
                               name="${escapeAttribute(name)}"
                               min="${min}"
                               max="${max}"
                               step="${escapeAttribute(step)}"
                               value="${escapeAttribute(currentValue)}"
                               ${disabled}>
                    ` : ''}
                </div>
            `;
        }

        return `
            <input type="text"
                   ${common}
                   value="${escapeAttribute(currentValue)}"
                   class="form-input"
                   data-type="${escapeAttribute(dataType || controlType || 'string')}"
                   ${disabled}>
        `;
    }

    findInputForError(name) {
        return Array.from(this.container.querySelectorAll('[data-property-parameter="true"]'))
            .find(input => String(input.name || '').toLowerCase() === String(name || '').toLowerCase()) || null;
    }

    findInputsForError(error) {
        const names = Array.isArray(error?.parameterNames) && error.parameterNames.length > 0
            ? error.parameterNames
            : [error?.name].filter(Boolean);
        const inputs = names
            .map(name => this.findInputForError(name))
            .filter(Boolean);

        return Array.from(new Set(inputs));
    }

    renderValidationErrors() {
        this.container.querySelectorAll('.form-group.invalid').forEach(group => {
            group.classList.remove('invalid');
        });
        this.container.querySelectorAll('[data-property-parameter="true"][aria-invalid="true"]').forEach(input => {
            input.removeAttribute('aria-invalid');
        });
        this.container.querySelectorAll('[data-validation-error="true"]').forEach(error => {
            error.remove();
        });

        this.validationErrors.forEach(error => {
            const inputs = this.findInputsForError(error);
            if (inputs.length === 0) {
                return;
            }

            const renderedGroups = new Set();
            inputs.forEach(input => {
                input.setAttribute('aria-invalid', 'true');
                const group = input.closest?.('.form-group');
                if (!group || renderedGroups.has(group)) {
                    return;
                }

                renderedGroups.add(group);
                group.classList.add('invalid');
                const message = document.createElement('p');
                message.className = 'form-description validation-error';
                message.dataset.validationError = 'true';
                message.textContent = error.message;
                group.appendChild(message);
            });
        });
    }

    updateStatus() {
        const status = this.container.querySelector('[data-property-capability-status]');
        if (!status) {
            return;
        }

        status.textContent = this.statusMessage || '';
        const isErrorStatus = this.statusMessage === '参数校验失败' ||
            this.statusMessage.includes('写入失败') ||
            this.statusMessage.includes('未写回');
        status.classList.toggle(
            'is-error',
            isErrorStatus
        );
        status.classList.toggle(
            'is-success',
            this.statusMessage === '参数已更新' || this.statusMessage.startsWith('已获取单帧')
        );
    }

    destroy() {
        this.dispose();
    }

    dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        this.cameraBindingsLoadToken += 1;
        this.cancelCameraCapture();
        this.unsubscribes.forEach(unsubscribe => {
            try {
                unsubscribe?.();
            } catch {
                // Ignore best-effort subscription cleanup failures.
            }
        });
        this.unsubscribes = [];
        this.container.removeEventListener('change', this.handleContainerChange);
        this.container.removeEventListener('click', this.handleContainerClick);
        this.container.removeEventListener('input', this.handleContainerInput);
        this.container.removeEventListener('keydown', this.handleContainerKeyDown);
        this.container.removeEventListener('submit', this.handleContainerSubmit);
        this.teardownGeometryEditor();
        delete this.container.dataset.propertyPanelOwner;
        this.container.innerHTML = '';
        this.currentOperator = null;
        this.currentNodeId = null;
        this.currentConnection = null;
    }
}

export default PropertyPanelCapabilityOwner;
