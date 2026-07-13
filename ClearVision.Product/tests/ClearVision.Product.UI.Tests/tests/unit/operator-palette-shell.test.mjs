import test from 'node:test';
import assert from 'node:assert/strict';
import {
  GLOBAL_SEARCH_GROUP_KEY,
  buildOperatorSearchText,
  buildOperatorGroups,
  buildPaletteGroups,
  clampScrollState,
  createFlyoutViewModel,
  createOperatorPayload,
  filterOperatorsForFlyout
} from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorPaletteShell.js';
import { getOperatorTypeDisplayName } from '../../../../src/ClearVision.Product.Desktop/wwwroot/src/shared/operatorDisplayNames.js';

const operators = [
  {
    type: 'Thresholding',
    displayName: '全局阈值处理',
    category: '预处理',
    description: '执行全局阈值处理',
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Mask', dataType: 'Image' }],
    parameters: [{ name: 'Threshold', displayName: '阈值', dataType: 'int' }]
  },
  {
    type: 'ImageAcquisition',
    displayName: '图像采集',
    category: '输入',
    description: '从相机或文件读取图像',
    inputPorts: [],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
    keywords: ['camera']
  },
  {
    type: 'GaussianBlur',
    displayName: '高斯滤波',
    category: '预处理',
    description: '平滑图像噪声',
    inputPorts: [{ name: 'Image', dataType: 'Image' }],
    outputPorts: [{ name: 'Image', dataType: 'Image' }],
    parameters: [{ name: 'Sigma', displayName: 'Sigma', dataType: 'double' }]
  },
  {
    type: 'CircleMeasurement',
    displayName: '圆测量',
    category: '测量',
    description: '根据区域边界拟合圆并输出半径',
    inputPorts: [{ name: 'Region', displayName: '区域', dataType: 'Region' }],
    outputPorts: [{ name: 'Circle', displayName: '圆', dataType: 'Geometry' }],
    parameters: [
      {
        name: 'FitMode',
        displayName: '拟合模式',
        dataType: 'enum',
        options: [{ label: '最小二乘', value: 'LeastSquares' }]
      }
    ],
    tags: ['geometry'],
    keywords: ['radius']
  }
];

test('OperatorPaletteShell groups operators by category for the rail', () => {
  const groups = buildOperatorGroups(operators);

  assert.deepEqual(groups.map(group => [group.label, group.operators.length]), [
    ['测量', 1],
    ['输入', 1],
    ['预处理', 2]
  ]);
  assert.equal(groups[2].operators[0].displayName, '高斯滤波');
  assert.equal(groups[2].operators[1].displayName, '全局阈值处理');
});

test('OperatorPaletteShell search keeps name, type, description, port, parameter and keyword matches', () => {
  assert.match(buildOperatorSearchText(operators[0]), /thresholding/);
  assert.match(buildOperatorSearchText(operators[0]), /mask/);
  assert.match(buildOperatorSearchText(operators[3]), /leastsquares/);

  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'camera').map(operator => operator.type),
    ['ImageAcquisition']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'Mask').map(operator => operator.type),
    ['Thresholding']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, '阈值').map(operator => operator.type),
    ['Thresholding']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'Region').map(operator => operator.type),
    ['CircleMeasurement']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, 'radius').map(operator => operator.type),
    ['CircleMeasurement']
  );
  assert.deepEqual(
    filterOperatorsForFlyout(operators, '不存在').map(operator => operator.type),
    []
  );
});

test('OperatorPaletteShell keeps corrected operators searchable by legacy display names', () => {
  const renamedOperators = [
    {
      type: 'BlobLabeling',
      displayName: 'Blob分类标注',
      keywords: ['连通域标注']
    },
    {
      type: 'PointAlignment',
      displayName: '点位偏差计算',
      keywords: ['点位对齐']
    },
    {
      type: 'RoiTransform',
      displayName: 'ROI位姿变换',
      keywords: ['ROI跟踪']
    },
    {
      type: 'CoordinateTransform',
      displayName: '像素到物理坐标（单点）',
      keywords: ['坐标转换', 'Coordinate Transform']
    },
    {
      type: 'RoiManager',
      displayName: 'ROI裁剪与掩膜',
      keywords: ['ROI管理器']
    },
    {
      type: 'TryCatch',
      displayName: 'Try分支透传',
      keywords: ['异常捕获']
    },
    {
      type: 'ModbusCommunication',
      displayName: 'Modbus TCP通信',
      keywords: ['Modbus通信']
    },
    {
      type: 'Thresholding',
      displayName: '全局阈值处理',
      keywords: ['二值化']
    },
    {
      type: 'FFT1D',
      displayName: '信号/图像傅里叶变换（FFT）',
      keywords: ['一维FFT']
    },
    {
      type: 'InverseFFT1D',
      displayName: '信号/图像逆傅里叶变换（IFFT）',
      keywords: ['一维逆FFT']
    },
    {
      type: 'PhaseClosure',
      displayName: '相位解缠绕',
      keywords: ['Phase Closure']
    }
  ];

  const expectations = [
    ['连通域标注', 'BlobLabeling'],
    ['点位对齐', 'PointAlignment'],
    ['ROI跟踪', 'RoiTransform'],
    ['坐标转换', 'CoordinateTransform'],
    ['ROI管理器', 'RoiManager'],
    ['异常捕获', 'TryCatch'],
    ['Modbus通信', 'ModbusCommunication'],
    ['二值化', 'Thresholding'],
    ['一维FFT', 'FFT1D'],
    ['一维逆FFT', 'InverseFFT1D'],
    ['Phase Closure', 'PhaseClosure']
  ];

  for (const [legacyName, expectedType] of expectations) {
    assert.deepEqual(
      filterOperatorsForFlyout(renamedOperators, legacyName).map(operator => operator.type),
      [expectedType]
    );
  }
});

test('Shared operator labels stay aligned with corrected runtime display names', () => {
  const expectedNames = {
    BlobLabeling: 'Blob分类标注',
    PointAlignment: '点位偏差计算',
    RoiTransform: 'ROI位姿变换',
    PositionCorrection: 'ROI位姿补偿（像素）',
    PointCorrection: '点位刚性补偿',
    EdgePairDefect: '边缘间距缺陷检测',
    StatisticalOutlierRemoval: '点云统计离群点去除（SOR）',
    PPFMatch: 'PPF点云粗匹配',
    PlanarMatching: '平面特征匹配',
    ColorDetection: '颜色分析',
    GeometricTolerance: '二维几何公差判定',
    DetectionSequenceJudge: '检测顺序判定',
    ImageDiff: '图像差异率分析',
    RectangleRegion: '矩形框定义',
    CoordinateTransform: '像素到物理坐标（单点）',
    ROIManager: 'ROI裁剪与掩膜',
    RoiManager: 'ROI裁剪与掩膜',
    TryCatch: 'Try分支透传',
    ModbusCommunication: 'Modbus TCP通信',
    Threshold: '全局阈值处理',
    Thresholding: '全局阈值处理',
    FFT1D: '信号/图像傅里叶变换（FFT）',
    InverseFFT1D: '信号/图像逆傅里叶变换（IFFT）',
    PhaseClosure: '相位解缠绕',
    DeepLearning: '深度学习',
    GeoMeasurement: '几何测量',
    Measurement: '测量',
    TcpCommunication: 'TCP通信',
    ImageAdd: '图像加法',
    ImageCompose: '图像组合',
    BlobAnalysis: 'Blob分析',
    Filtering: '滤波'
  };

  for (const [operatorType, displayName] of Object.entries(expectedNames)) {
    assert.equal(getOperatorTypeDisplayName(operatorType), displayName, operatorType);
  }
});

test('OperatorPaletteShell exposes a global search rail entry before categories', () => {
  const groups = buildPaletteGroups(operators);

  assert.equal(groups[0].key, GLOBAL_SEARCH_GROUP_KEY);
  assert.equal(groups[0].label, '搜索');
  assert.equal(groups[0].kind, 'global-search');
  assert.deepEqual(groups.slice(1, 3).map(group => group.label), ['最近', '收藏']);
});

test('OperatorPaletteShell separates global and category search scopes', () => {
  const groups = buildPaletteGroups(operators);
  const globalGroup = groups.find(group => group.key === GLOBAL_SEARCH_GROUP_KEY);
  const preprocessGroup = groups.find(group => group.label === '预处理');

  const globalVm = createFlyoutViewModel({
    activeGroup: globalGroup,
    allOperators: operators,
    searchTerm: 'camera'
  });
  assert.equal(globalVm.title, '全部算子');
  assert.equal(globalVm.placeholder, '搜索全部算子：名称、类型、端口、参数');
  assert.deepEqual(globalVm.operators.map(operator => operator.type), ['ImageAcquisition']);

  const categoryVm = createFlyoutViewModel({
    activeGroup: preprocessGroup,
    allOperators: operators,
    searchTerm: 'camera'
  });
  assert.equal(categoryVm.placeholder, '搜索本分类算子');
  assert.match(categoryVm.subtitle, /搜索范围：预处理/);
  assert.deepEqual(categoryVm.operators, []);

  const categoryParamVm = createFlyoutViewModel({
    activeGroup: preprocessGroup,
    allOperators: operators,
    searchTerm: 'Sigma'
  });
  assert.deepEqual(categoryParamVm.operators.map(operator => operator.type), ['GaussianBlur']);
});

test('OperatorPaletteShell keeps scroll offsets when possible and clamps after rerender shrink', () => {
  assert.deepEqual(
    clampScrollState(
      { scrollTop: 180, scrollLeft: 20 },
      { scrollHeight: 600, clientHeight: 240, scrollWidth: 180, clientWidth: 100 }
    ),
    { scrollTop: 180, scrollLeft: 20 }
  );

  assert.deepEqual(
    clampScrollState(
      { scrollTop: 480, scrollLeft: -8 },
      { scrollHeight: 360, clientHeight: 220, scrollWidth: 80, clientWidth: 120 }
    ),
    { scrollTop: 140, scrollLeft: 0 }
  );
});

test('OperatorPaletteShell drag payload keeps operator metadata from global search results', () => {
  const globalGroup = buildPaletteGroups(operators).find(group => group.key === GLOBAL_SEARCH_GROUP_KEY);
  const viewModel = createFlyoutViewModel({
    activeGroup: globalGroup,
    allOperators: operators,
    searchTerm: 'Region'
  });
  const payload = createOperatorPayload(viewModel.operators[0]);

  assert.notEqual(payload, viewModel.operators[0]);
  assert.equal(payload.type, 'CircleMeasurement');
  assert.deepEqual(payload.inputPorts, [{ name: 'Region', displayName: '区域', dataType: 'Region' }]);
  assert.deepEqual(payload.parameters, operators[3].parameters);
});
