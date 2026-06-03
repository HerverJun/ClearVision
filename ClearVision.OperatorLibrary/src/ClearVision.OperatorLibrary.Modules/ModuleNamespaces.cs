// ModuleNamespaces.cs
// 模块命名空间映射
// 维护算子模块与命名空间的静态映射关系
// 作者：蘅芜君
using System.Collections.Generic;
using ClearVision.Product.Core.Enums;
using ClearVision.OperatorLibrary.Modules;

namespace ClearVision.OperatorLibrary.ImageProcessing
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.ImageProcessingTypes;
    }
}

namespace ClearVision.OperatorLibrary.Measurement
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.MeasurementTypes;
    }
}

namespace ClearVision.OperatorLibrary.Calibration
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.CalibrationTypes;
    }
}

namespace ClearVision.OperatorLibrary.Communication
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.CommunicationTypes;
    }
}

namespace ClearVision.OperatorLibrary.FlowControl
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.FlowControlTypes;
    }
}

namespace ClearVision.OperatorLibrary.AI
{
    public static class Operators
    {
        public static IReadOnlyList<OperatorType> Types => OperatorModuleCatalog.AiTypes;
    }
}
