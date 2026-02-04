/**
 * 主应用入口 - S4-006: 端到端集成
 * Sprint 4: 前后端集成与用户体验闭环
 */

import webMessageBridge from './core/messaging/webMessageBridge.js';
import httpClient from './core/messaging/httpClient.js';
import { createSignal } from './core/state/store.js';
import FlowCanvas from './core/canvas/flowCanvas.js';
import { ImageViewerComponent } from './features/image-viewer/imageViewer.js';
import { OperatorLibraryPanel } from './features/operator-library/operatorLibrary.js';
import inspectionController from './features/inspection/inspectionController.js';
import { showToast } from './shared/components/uiComponents.js';

// 全局状态
const [getCurrentView, setCurrentView, subscribeView] = createSignal('flow');
const [getSelectedOperator, setSelectedOperator, subscribeSelectedOperator] = createSignal(null);
const [getOperatorLibrary, setOperatorLibrary, subscribeOperatorLibrary] = createSignal([]);
const [getCurrentProject, setCurrentProject, subscribeCurrentProject] = createSignal(null);

// 组件实例
let imageViewer = null;
let operatorLibraryPanel = null;
let flowCanvas = null;

/**
 * 初始化应用
 */
function initializeApp() {
    console.log('[App] 初始化应用...');
    
    // 添加调试标记到页面
    const debugIndicator = document.createElement('div');
    debugIndicator.id = 'js-loaded-indicator';
    debugIndicator.style.cssText = 'position:fixed;top:5px;right:5px;background:#52c41a;color:white;padding:4px 8px;border-radius:4px;font-size:12px;z-index:99999;cursor:pointer;';
    debugIndicator.textContent = 'JS已加载 ✓';
    debugIndicator.onclick = () => {
        alert('JavaScript运行正常！\n按钮数量: ' + document.querySelectorAll('button').length);
    };
    document.body.appendChild(debugIndicator);
    
    // 初始化导航
    initializeNavigation();
    
    // 初始化算子库面板
    initializeOperatorLibraryPanel();
    
    // 初始化流程编辑器
    initializeFlowEditor();
    
    // 初始化图像查看器
    initializeImageViewer();
    
    // 初始化 WebMessage 通信
    initializeWebMessage();
    
    // 初始化检测控制器
    initializeInspectionController();
    
    console.log('[App] 应用初始化完成');
    
    // 初始化工具栏按钮
    initializeToolbar();
    
    // 显示欢迎消息
    showToast('ClearVision 已就绪', 'success');
}

/**
 * 初始化导航
 */
function initializeNavigation() {
    const navButtons = document.querySelectorAll('.nav-btn');
    
    navButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            // 更新活动状态
            navButtons.forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            
            // 切换视图
            const view = btn.dataset.view;
            setCurrentView(view);
            switchView(view);
        });
    });
}

/**
 * 切换视图
 */
function switchView(view) {
    const flowEditor = document.getElementById('flow-editor');
    const imageViewerContainer = document.getElementById('image-viewer');
    
    switch (view) {
        case 'flow':
            flowEditor.classList.remove('hidden');
            if (imageViewerContainer) imageViewerContainer.classList.add('hidden');
            break;
        case 'inspection':
            flowEditor.classList.add('hidden');
            if (imageViewerContainer) imageViewerContainer.classList.remove('hidden');
            break;
        case 'results':
            // TODO: 显示结果视图
            break;
        default:
            break;
    }
}

/**
 * 初始化算子库面板
 */
function initializeOperatorLibraryPanel() {
    const container = document.getElementById('operator-library');
    if (!container) {
        console.error('[App] 找不到算子库容器');
        return;
    }
    
    operatorLibraryPanel = new OperatorLibraryPanel('operator-library');
    
    // 设置拖拽回调
    operatorLibraryPanel.onOperatorDragStart = (operatorData) => {
        console.log('[App] 开始拖拽算子:', operatorData.type);
    };
    
    // 设置选中回调
    operatorLibraryPanel.onOperatorSelected = (operatorData) => {
        console.log('[App] 选中算子:', operatorData.type);
        setSelectedOperator(operatorData);
    };
    
    console.log('[App] 算子库面板初始化完成');
}

/**
 * 初始化图像查看器
 */
function initializeImageViewer() {
    const container = document.getElementById('image-viewer');
    if (!container) {
        console.error('[App] 找不到图像查看器容器');
        return;
    }
    
    // 清空容器并初始化图像查看器组件
    imageViewer = new ImageViewerComponent('image-viewer');
    
    // 设置图像加载回调
    imageViewer.onImageLoaded = (img) => {
        console.log('[App] 图像已加载:', img.width, 'x', img.height);
    };
    
    // 设置标注点击回调
    imageViewer.onAnnotationClicked = (annotation) => {
        console.log('[App] 点击标注:', annotation);
    };
    
    console.log('[App] 图像查看器初始化完成');
}

/**
 * 初始化检测控制器
 */
function initializeInspectionController() {
    // 设置检测完成回调
    inspectionController.onInspectionCompleted = (result) => {
        console.log('[App] 检测完成:', result);
        
        // 如果有缺陷，在图像查看器中显示
        if (result.defects && result.defects.length > 0 && imageViewer) {
            imageViewer.showDefects(result.defects);
        }
        
        // 显示结果提示
        const status = result.status === 'OK' ? 'success' : 'warning';
        const message = result.status === 'OK' 
            ? '检测通过 (OK)' 
            : `检测到 ${result.defects?.length || 0} 个缺陷`;
        showToast(message, status);
    };
    
    // 设置检测错误回调
    inspectionController.onInspectionError = (error) => {
        console.error('[App] 检测错误:', error);
        showToast('检测失败: ' + error.message, 'error');
    };
    
    console.log('[App] 检测控制器初始化完成');
}

/**
 * 初始化算子库
 */
async function initializeOperatorLibrary() {
    try {
        // 从后端获取算子库
        const operators = await httpClient.get('/operators/library');
        setOperatorLibrary(operators);
        renderOperatorLibrary(operators);
    } catch (error) {
        console.error('[App] 加载算子库失败:', error);
        // 使用默认算子数据
        renderOperatorLibrary(getDefaultOperators());
    }
}

/**
 * 渲染算子库
 */
function renderOperatorLibrary(operators) {
    const container = document.getElementById('operator-library');
    
    // 按类别分组
    const categories = groupByCategory(operators);
    
    container.innerHTML = Object.entries(categories).map(([category, items]) => `
        <div class="operator-category">
            <div class="category-title">${category}</div>
            ${items.map(op => `
                <div class="operator-item" draggable="true" data-type="${op.type}">
                    <div class="operator-icon">${op.iconName?.charAt(0).toUpperCase() || '?'}</div>
                    <span class="operator-name">${op.displayName}</span>
                </div>
            `).join('')}
        </div>
    `).join('');
    
    // 添加拖拽事件
    container.querySelectorAll('.operator-item').forEach(item => {
        item.addEventListener('dragstart', handleDragStart);
    });
}

/**
 * 按类别分组
 */
function groupByCategory(operators) {
    return operators.reduce((acc, op) => {
        const category = op.category || '其他';
        if (!acc[category]) {
            acc[category] = [];
        }
        acc[category].push(op);
        return acc;
    }, {});
}

/**
 * 获取默认算子数据
 */
function getDefaultOperators() {
    return [
        { type: 'ImageAcquisition', displayName: '图像采集', category: '输入', iconName: 'camera' },
        { type: 'Filtering', displayName: '滤波', category: '预处理', iconName: 'filter' },
        { type: 'EdgeDetection', displayName: '边缘检测', category: '特征提取', iconName: 'edge' },
        { type: 'Thresholding', displayName: '二值化', category: '预处理', iconName: 'threshold' },
        { type: 'ResultOutput', displayName: '结果输出', category: '输出', iconName: 'output' }
    ];
}

/**
 * 处理拖拽开始
 */
function handleDragStart(event) {
    const operatorType = event.target.dataset.type;
    event.dataTransfer.setData('operatorType', operatorType);
}

/**
 * 初始化流程编辑器
 */
function initializeFlowEditor() {
    const canvas = document.getElementById('flow-canvas');
    if (!canvas) {
        console.error('[App] 找不到流程编辑器画布');
        return;
    }
    
    // 使用 FlowCanvas 类初始化
    flowCanvas = new FlowCanvas('flow-canvas');
    
    // 保存到全局以便其他函数使用
    window.flowCanvas = flowCanvas;
    
    // 添加拖放支持
    canvas.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        canvas.classList.add('drag-over');
    });
    
    canvas.addEventListener('dragleave', () => {
        canvas.classList.remove('drag-over');
    });
    
    canvas.addEventListener('drop', (e) => {
        e.preventDefault();
        canvas.classList.remove('drag-over');
        
        // 尝试从 dataTransfer 获取算子类型
        let operatorType = e.dataTransfer.getData('operatorType');
        
        // 如果从 operator-library 拖拽，数据格式可能不同
        if (!operatorType) {
            try {
                const data = JSON.parse(e.dataTransfer.getData('application/json'));
                operatorType = data.type;
            } catch (err) {
                console.warn('[App] 无法解析拖拽数据');
            }
        }
        
        if (operatorType) {
            const rect = canvas.getBoundingClientRect();
            const x = (e.clientX - rect.left - flowCanvas.offset.x) / flowCanvas.scale;
            const y = (e.clientY - rect.top - flowCanvas.offset.y) / flowCanvas.scale;
            addOperatorToFlow(operatorType, x, y);
        }
    });
    
    console.log('[App] 流程编辑器初始化完成');
}

/**
 * 添加算子到流程
 */
function addOperatorToFlow(type, x, y) {
    console.log('[App] 添加算子:', type, '位置:', x, y);
    
    if (!window.flowCanvas) {
        console.error('[App] FlowCanvas 未初始化');
        return;
    }
    
    // 算子配置
    const operatorConfigs = {
        'ImageAcquisition': { title: '图像采集', color: '#52c41a', icon: '📷' },
        'Filtering': { title: '滤波', color: '#1890ff', icon: '🔍' },
        'EdgeDetection': { title: '边缘检测', color: '#722ed1', icon: '〰️' },
        'Thresholding': { title: '二值化', color: '#eb2f96', icon: '⚫' },
        'Morphology': { title: '形态学', color: '#fa8c16', icon: '🔄' },
        'BlobAnalysis': { title: 'Blob分析', color: '#13c2c2', icon: '🔵' },
        'TemplateMatching': { title: '模板匹配', color: '#f5222d', icon: '🎯' },
        'Measurement': { title: '测量', color: '#2f54eb', icon: '📏' },
        'DeepLearning': { title: '深度学习', color: '#a0d911', icon: '🧠' },
        'ResultOutput': { title: '结果输出', color: '#595959', icon: '📤' }
    };
    
    const config = operatorConfigs[type] || { title: type, color: '#1890ff', icon: '📦' };
    
    // 添加节点到画布
    const node = window.flowCanvas.addNode(type, x, y, {
        title: config.title,
        color: config.color,
        icon: config.icon,
        inputs: [{ name: 'input', type: 'any' }],
        outputs: [{ name: 'output', type: 'any' }]
    });
    
    console.log('[App] 算子已添加:', node);
    
    // 选中该节点
    window.flowCanvas.selectedNode = node.id;
    window.flowCanvas.render();
}

/**
 * 初始化 WebMessage 通信
 */
function initializeWebMessage() {
    // 注册消息处理器
    webMessageBridge.on('operatorExecuted', (data) => {
        console.log('[App] 算子执行完成:', data);
        updateResults(data);
    });
    
    webMessageBridge.on('inspectionCompleted', (data) => {
        console.log('[App] 检测完成:', data);
        updateResults(data);
    });
}

/**
 * 更新结果面板
 */
function updateResults(data) {
    // TODO: 更新结果面板
}

/**
 * 初始化工具栏按钮
 */
function initializeToolbar() {
    // 保存按钮
    const saveBtn = document.getElementById('btn-save');
    if (saveBtn) {
        saveBtn.addEventListener('click', async () => {
            console.log('[App] 保存工程');
            try {
                const project = getCurrentProject();
                if (project) {
                    await httpClient.put(`/projects/${project.id}`, project);
                    showToast('工程已保存', 'success');
                } else {
                    showToast('请先创建或打开工程', 'warning');
                }
            } catch (error) {
                console.error('[App] 保存失败:', error);
                showToast('保存失败: ' + error.message, 'error');
            }
        });
    }
    
    // 运行按钮
    const runBtn = document.getElementById('btn-run');
    if (runBtn) {
        runBtn.addEventListener('click', async () => {
            console.log('[App] 运行检测');
            const project = getCurrentProject();
            
            if (!project) {
                showToast('请先打开或创建工程', 'warning');
                return;
            }
            
            if (!window.flowCanvas || window.flowCanvas.nodes.size === 0) {
                showToast('请先添加算子到流程', 'warning');
                return;
            }
            
            try {
                // 切换到检测视图
                setCurrentView('inspection');
                switchView('inspection');
                
                // 更新导航按钮状态
                document.querySelectorAll('.nav-btn').forEach(btn => {
                    btn.classList.remove('active');
                    if (btn.dataset.view === 'inspection') {
                        btn.classList.add('active');
                    }
                });
                
                // 设置当前工程
                inspectionController.setProject(project.id);
                
                // 如果有加载的图像，执行检测
                if (imageViewer && imageViewer.getCurrentImage()) {
                    showToast('开始执行检测流程...', 'info');
                    await inspectionController.executeSingle();
                } else {
                    showToast('请先加载图像', 'warning');
                }
            } catch (error) {
                console.error('[App] 运行检测失败:', error);
                showToast('检测失败: ' + error.message, 'error');
            }
        });
    }
    
    // 设置按钮
    const settingsBtn = document.getElementById('btn-settings');
    if (settingsBtn) {
        settingsBtn.addEventListener('click', () => {
            console.log('[App] 打开设置');
            showToast('设置功能开发中...', 'info');
        });
    }
}

/**
 * 加载工程
 */
async function loadProject(projectId) {
    try {
        const project = await httpClient.get(`/projects/${projectId}`);
        setCurrentProject(project);
        
        // 更新状态栏
        const projectNameEl = document.getElementById('project-name');
        if (projectNameEl) {
            projectNameEl.textContent = project.name;
        }
        
        // 加载流程到画布
        if (project.flow && window.flowCanvas) {
            window.flowCanvas.loadFromData(project.flow);
        }
        
        // 设置检测控制器的工程
        inspectionController.setProject(projectId);
        
        showToast(`工程 "${project.name}" 已加载`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 加载工程失败:', error);
        showToast('加载工程失败: ' + error.message, 'error');
        throw error;
    }
}

/**
 * 创建新工程
 */
async function createProject(name, description = '') {
    try {
        const project = await httpClient.post('/projects', {
            name,
            description
        });
        
        setCurrentProject(project);
        
        // 更新状态栏
        const projectNameEl = document.getElementById('project-name');
        if (projectNameEl) {
            projectNameEl.textContent = project.name;
        }
        
        // 清空画布
        if (window.flowCanvas) {
            window.flowCanvas.clear();
        }
        
        // 设置检测控制器的工程
        inspectionController.setProject(project.id);
        
        showToast(`工程 "${name}" 已创建`, 'success');
        return project;
    } catch (error) {
        console.error('[App] 创建工程失败:', error);
        showToast('创建工程失败: ' + error.message, 'error');
        throw error;
    }
}

// 启动应用
document.addEventListener('DOMContentLoaded', initializeApp);

export { 
    getCurrentView, 
    setCurrentView, 
    getSelectedOperator, 
    setSelectedOperator,
    getCurrentProject,
    setCurrentProject,
    loadProject,
    createProject,
    imageViewer,
    operatorLibraryPanel,
    flowCanvas
};
