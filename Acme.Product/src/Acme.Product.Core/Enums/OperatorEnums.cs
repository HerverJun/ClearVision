namespace Acme.Product.Core.Enums;

/// <summary>
/// 算子类型枚举 - 定义所有支持的图像处理算子类型
/// </summary>
public enum OperatorType
{
    /// <summary>
    /// 图像采集 - 从相机或文件获取图像
    /// </summary>
    ImageAcquisition = 0,

    /// <summary>
    /// 预处理 - 图像预处理操作
    /// </summary>
    Preprocessing = 1,

    /// <summary>
    /// 滤波 - 图像滤波降噪
    /// </summary>
    Filtering = 2,

    /// <summary>
    /// 边缘检测 - 检测图像边缘
    /// </summary>
    EdgeDetection = 3,

    /// <summary>
    /// 二值化 - 图像阈值分割
    /// </summary>
    Thresholding = 4,

    /// <summary>
    /// 形态学 - 形态学操作（腐蚀、膨胀等）
    /// </summary>
    Morphology = 5,

    /// <summary>
    /// Blob分析 - 连通区域分析
    /// </summary>
    BlobAnalysis = 6,

    /// <summary>
    /// 模板匹配 - 图像模板匹配
    /// </summary>
    TemplateMatching = 7,

    /// <summary>
    /// 测量 - 几何测量
    /// </summary>
    Measurement = 8,

    /// <summary>
    /// 条码识别 - 一维码/二维码识别
    /// </summary>
    CodeRecognition = 9,

    /// <summary>
    /// 深度学习 - AI缺陷检测
    /// </summary>
    DeepLearning = 10,

    /// <summary>
    /// 结果输出 - 输出检测结果
    /// </summary>
    ResultOutput = 11,

    /// <summary>
    /// 轮廓检测
    /// </summary>
    ContourDetection = 12
}

/// <summary>
/// 算子执行状态
/// </summary>
public enum OperatorExecutionStatus
{
    /// <summary>
    /// 未执行
    /// </summary>
    NotExecuted = 0,

    /// <summary>
    /// 执行中
    /// </summary>
    Executing = 1,

    /// <summary>
    /// 执行成功
    /// </summary>
    Success = 2,

    /// <summary>
    /// 执行失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 已跳过
    /// </summary>
    Skipped = 4
}

/// <summary>
/// 检测结果状态
/// </summary>
public enum InspectionStatus
{
    /// <summary>
    /// 未检测
    /// </summary>
    NotInspected = 0,

    /// <summary>
    /// 检测中
    /// </summary>
    Inspecting = 1,

    /// <summary>
    /// 合格（OK）
    /// </summary>
    OK = 2,

    /// <summary>
    /// 不合格（NG）
    /// </summary>
    NG = 3,

    /// <summary>
    /// 检测错误
    /// </summary>
    Error = 4
}

/// <summary>
/// 缺陷类型
/// </summary>
public enum DefectType
{
    /// <summary>
    /// 划痕
    /// </summary>
    Scratch = 0,

    /// <summary>
    /// 污渍
    /// </summary>
    Stain = 1,

    /// <summary>
    /// 异物
    /// </summary>
    ForeignObject = 2,

    /// <summary>
    /// 缺失
    /// </summary>
    Missing = 3,

    /// <summary>
    /// 变形
    /// </summary>
    Deformation = 4,

    /// <summary>
    /// 尺寸偏差
    /// </summary>
    DimensionalDeviation = 5,

    /// <summary>
    /// 颜色异常
    /// </summary>
    ColorAbnormality = 6,

    /// <summary>
    /// 其他
    /// </summary>
    Other = 99
}

/// <summary>
/// 端口数据类型
/// </summary>
public enum PortDataType
{
    /// <summary>
    /// 图像
    /// </summary>
    Image = 0,

    /// <summary>
    /// 整数
    /// </summary>
    Integer = 1,

    /// <summary>
    /// 浮点数
    /// </summary>
    Float = 2,

    /// <summary>
    /// 布尔值
    /// </summary>
    Boolean = 3,

    /// <summary>
    /// 字符串
    /// </summary>
    String = 4,

    /// <summary>
    /// 点坐标
    /// </summary>
    Point = 5,

    /// <summary>
    /// 矩形
    /// </summary>
    Rectangle = 6,

    /// <summary>
    /// 轮廓
    /// </summary>
    Contour = 7,

    /// <summary>
    /// 任意类型
    /// </summary>
    Any = 99
}

/// <summary>
/// 端口方向
/// </summary>
public enum PortDirection
{
    /// <summary>
    /// 输入端口
    /// </summary>
    Input = 0,

    /// <summary>
    /// 输出端口
    /// </summary>
    Output = 1
}
