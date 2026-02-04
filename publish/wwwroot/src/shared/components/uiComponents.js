/**
 * UI组件库 - 基础UI组件
 * ClearVision 视觉检测软件
 */

// ==================== Button组件 ====================

/**
 * 创建按钮
 * @param {Object} options - 配置选项
 * @param {string} options.text - 按钮文本
 * @param {string} options.type - 按钮类型: primary, secondary, danger, icon
 * @param {Function} options.onClick - 点击回调
 * @param {string} options.className - 额外CSS类
 * @param {boolean} options.disabled - 是否禁用
 * @returns {HTMLButtonElement}
 */
export function createButton(options = {}) {
    const {
        text = '',
        type = 'primary',
        onClick = null,
        className = '',
        disabled = false,
        icon = null
    } = options;

    const button = document.createElement('button');
    button.className = `cv-btn cv-btn-${type} ${className}`;
    button.disabled = disabled;

    if (icon) {
        const iconSpan = document.createElement('span');
        iconSpan.className = 'cv-btn-icon';
        iconSpan.textContent = icon;
        button.appendChild(iconSpan);
    }

    if (text) {
        const textSpan = document.createElement('span');
        textSpan.className = 'cv-btn-text';
        textSpan.textContent = text;
        button.appendChild(textSpan);
    }

    if (onClick) {
        button.addEventListener('click', onClick);
    }

    return button;
}

// ==================== Input组件 ====================

/**
 * 创建输入框
 * @param {Object} options - 配置选项
 * @param {string} options.type - 输入类型: text, number, email, password
 * @param {string} options.placeholder - 占位文本
 * @param {string} options.value - 初始值
 * @param {Function} options.onChange - 变更回调
 * @param {Function} options.onBlur - 失焦回调
 * @returns {HTMLInputElement}
 */
export function createInput(options = {}) {
    const {
        type = 'text',
        placeholder = '',
        value = '',
        onChange = null,
        onBlur = null,
        className = '',
        min,
        max,
        step,
        disabled = false
    } = options;

    const input = document.createElement('input');
    input.type = type;
    input.className = `cv-input ${className}`;
    input.placeholder = placeholder;
    input.value = value;
    input.disabled = disabled;

    if (type === 'number') {
        if (min !== undefined) input.min = min;
        if (max !== undefined) input.max = max;
        if (step !== undefined) input.step = step;
    }

    if (onChange) {
        input.addEventListener('input', (e) => onChange(e.target.value, e));
    }

    if (onBlur) {
        input.addEventListener('blur', (e) => onBlur(e.target.value, e));
    }

    return input;
}

/**
 * 创建带标签的输入框
 * @param {Object} options
 * @returns {HTMLDivElement}
 */
export function createLabeledInput(options = {}) {
    const { label = '', required = false, ...inputOptions } = options;

    const container = document.createElement('div');
    container.className = 'cv-input-group';

    const labelEl = document.createElement('label');
    labelEl.className = 'cv-input-label';
    labelEl.innerHTML = `${label}${required ? '<span class="required">*</span>' : ''}`;

    const input = createInput(inputOptions);
    input.id = `input_${Date.now()}_${Math.random().toString(36).substr(2, 5)}`;
    labelEl.htmlFor = input.id;

    container.appendChild(labelEl);
    container.appendChild(input);

    return container;
}

// ==================== Select组件 ====================

/**
 * 创建下拉选择框
 * @param {Object} options
 * @param {Array<{value, label}>} options.options - 选项列表
 * @returns {HTMLSelectElement}
 */
export function createSelect(options = {}) {
    const {
        options: items = [],
        value = '',
        onChange = null,
        className = '',
        placeholder = '请选择...'
    } = options;

    const select = document.createElement('select');
    select.className = `cv-select ${className}`;

    // 占位选项
    const placeholderOption = document.createElement('option');
    placeholderOption.value = '';
    placeholderOption.textContent = placeholder;
    placeholderOption.disabled = true;
    placeholderOption.selected = !value;
    select.appendChild(placeholderOption);

    items.forEach(item => {
        const option = document.createElement('option');
        option.value = item.value;
        option.textContent = item.label;
        if (item.value === value) {
            option.selected = true;
        }
        select.appendChild(option);
    });

    if (onChange) {
        select.addEventListener('change', (e) => onChange(e.target.value, e));
    }

    return select;
}

// ==================== Checkbox组件 ====================

/**
 * 创建复选框
 * @param {Object} options
 * @returns {HTMLLabelElement}
 */
export function createCheckbox(options = {}) {
    const {
        label = '',
        checked = false,
        onChange = null,
        className = ''
    } = options;

    const container = document.createElement('label');
    container.className = `cv-checkbox ${className}`;

    const input = document.createElement('input');
    input.type = 'checkbox';
    input.className = 'cv-checkbox-input';
    input.checked = checked;

    const checkmark = document.createElement('span');
    checkmark.className = 'cv-checkbox-mark';

    const labelEl = document.createElement('span');
    labelEl.className = 'cv-checkbox-label';
    labelEl.textContent = label;

    container.appendChild(input);
    container.appendChild(checkmark);
    container.appendChild(labelEl);

    if (onChange) {
        input.addEventListener('change', (e) => onChange(e.target.checked, e));
    }

    return container;
}

// ==================== Toast通知组件 ====================

const toastContainer = document.createElement('div');
toastContainer.id = 'cv-toast-container';
toastContainer.className = 'cv-toast-container';
document.body.appendChild(toastContainer);

/**
 * 显示Toast通知
 * @param {string} message - 消息内容
 * @param {string} type - 类型: success, error, warning, info
 * @param {number} duration - 显示时长(毫秒)
 */
export function showToast(message, type = 'info', duration = 3000) {
    const toast = document.createElement('div');
    toast.className = `cv-toast cv-toast-${type}`;

    const icons = {
        success: '✓',
        error: '✗',
        warning: '⚠',
        info: 'ℹ'
    };

    toast.innerHTML = `
        <span class="cv-toast-icon">${icons[type]}</span>
        <span class="cv-toast-message">${message}</span>
        <button class="cv-toast-close">×</button>
    `;

    // 关闭按钮
    toast.querySelector('.cv-toast-close').addEventListener('click', () => {
        removeToast(toast);
    });

    toastContainer.appendChild(toast);

    // 自动移除
    setTimeout(() => {
        removeToast(toast);
    }, duration);

    return toast;
}

function removeToast(toast) {
    toast.classList.add('cv-toast-hiding');
    setTimeout(() => {
        if (toast.parentNode) {
            toast.parentNode.removeChild(toast);
        }
    }, 300);
}

// ==================== Loading组件 ====================

/**
 * 创建加载动画
 * @param {Object} options
 * @returns {HTMLDivElement}
 */
export function createLoading(options = {}) {
    const {
        size = 'medium',
        text = '加载中...',
        fullscreen = false,
        className = ''
    } = options;

    const loading = document.createElement('div');
    loading.className = `cv-loading cv-loading-${size} ${fullscreen ? 'cv-loading-fullscreen' : ''} ${className}`;

    loading.innerHTML = `
        <div class="cv-loading-spinner">
            <div class="cv-loading-dot"></div>
            <div class="cv-loading-dot"></div>
            <div class="cv-loading-dot"></div>
        </div>
        ${text ? `<span class="cv-loading-text">${text}</span>` : ''}
    `;

    return loading;
}

/**
 * 显示全屏Loading
 * @param {string} text
 * @returns {HTMLDivElement}
 */
export function showFullscreenLoading(text = '加载中...') {
    const loading = createLoading({ fullscreen: true, text });
    document.body.appendChild(loading);
    return loading;
}

/**
 * 隐藏Loading
 * @param {HTMLDivElement} loading
 */
export function hideLoading(loading) {
    if (loading && loading.parentNode) {
        loading.parentNode.removeChild(loading);
    }
}

// ==================== 导出所有组件 ====================

export default {
    createButton,
    createInput,
    createLabeledInput,
    createSelect,
    createCheckbox,
    showToast,
    createLoading,
    showFullscreenLoading,
    hideLoading
};
