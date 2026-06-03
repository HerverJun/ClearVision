// OperatorBase.cs
// 绠楀瓙鎵ц鍣ㄦ娊璞″熀绫?- 鎻愪緵缁熶竴鐨勫弬鏁拌幏鍙栥€佽緭鍏ユ鏌ャ€佹棩蹇楄褰曞拰鎵ц璁℃椂鍔熻兘
// 浣滆€咃細铇呰姕鍚?

using System.Diagnostics;
using System.Runtime.CompilerServices;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 绠楀瓙鎵ц鍣ㄦ娊璞″熀绫?
/// 鎻愪緵缁熶竴鐨勫弬鏁拌幏鍙栥€佽緭鍏ユ鏌ャ€佹棩蹇楄褰曞拰鎵ц璁℃椂鍔熻兘
/// </summary>
public abstract class OperatorBase : IOperatorExecutor
{
    private sealed record ParameterLookupCache(DateTime? ModifiedAt, int ParameterCount, Dictionary<string, Parameter> Lookup);
    private static readonly ConditionalWeakTable<Operator, ParameterLookupCache> ParameterLookupCaches = new();

    /// <summary>
    /// 鏃ュ織璁板綍鍣?
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// 算子类型
    /// </summary>
    public abstract OperatorType OperatorType { get; }

    /// <summary>
    /// 鏋勯€犲嚱鏁?
    /// </summary>
    /// <param name="logger">鏃ュ織璁板綍鍣?/param>
    protected OperatorBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 鎵ц绠楀瓙锛堝寘瑁呮柟娉曪紝鎻愪緵缁熶竴鐨勮鏃跺拰鏃ュ織锛?
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="inputs">杈撳叆鏁版嵁</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>执行结果</returns>
    public async Task<OperatorExecutionOutput> ExecuteAsync(
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        // Sprint 1 Task 1.1: 浣跨敤甯︾敓鍛藉懆鏈熺鐞嗙殑鏂规硶
        return await ExecuteWithLifecycleAsync(@operator, inputs, cancellationToken);
    }

    /// <summary>
    /// 鎵ц绠楀瓙锛堝甫鐢熷懡鍛ㄦ湡绠＄悊 - Sprint 1 Task 1.1锛夈€?
    /// 
    /// 自动管理 ImageWrapper 的引用计数：
    /// 1. 执行前：输入中的 ImageWrapper 引用计数已由上游 AddRef
    /// 2. 鎵ц鍚庯細鑷姩 Release 鎵€鏈夎緭鍏ヤ腑鐨?ImageWrapper
    /// 
    /// 绠楀瓙寮€鍙戠害瀹氾紙妗嗘灦灞傞€氳繃 Code Review 寮哄埗妫€鏌ワ級锛?
    ///
    /// 璇绘搷浣滐細image.MatReadOnly  鈥?涓嶈Е鍙?Clone/Pool锛屽骞跺彂瀹夊叏
    ///
    /// 鍐欐搷浣滐細var dst = image.GetWritableMat(); // 鍙兘浠?Pool 鍙栵紙CoW锛?
    ///          Cv2.SomeFilter(dst, dst, ...);    // 灏卞湴淇敼
    ///          return new ImageWrapper(dst);     // 输出，引用计数重置为 1
    ///          // 若不作为输出，需归还：MatPool.Shared.Return(dst)
    ///
    /// 绂佹鍦ㄧ畻瀛愬唴閮ㄦ墜鍔ㄨ皟鐢?AddRef() / Release()銆?
    /// 绂佹鍦ㄧ畻瀛愬唴閮ㄧ洿鎺ヨ皟鐢?mat.Dispose()銆?
    /// </summary>
    public async Task<OperatorExecutionOutput> ExecuteWithLifecycleAsync(
        Operator @operator,
        Dictionary<string, object>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug("[{OperatorType}] 开始执行, 算子ID={OperatorId}, 名称={OperatorName}",
            OperatorType, @operator.Id, @operator.Name);

        try
        {
            // 妫€鏌ュ彇娑堣姹?
            cancellationToken.ThrowIfCancellationRequested();

            // 执行核心逻辑
            var result = await ExecuteCoreAsync(@operator, inputs, cancellationToken);
            stopwatch.Stop();

            // 设置执行时间
            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (result.IsSuccess)
            {
                Logger.LogDebug(
                    "[{OperatorType}] 执行完成, 算子ID={OperatorId}, 耗时={ElapsedMs}ms, 成功={IsSuccess}",
                    OperatorType, @operator.Id, stopwatch.ElapsedMilliseconds, true);
            }
            else
            {
                Logger.LogWarning(
                    "[{OperatorType}] 执行失败, 算子ID={OperatorId}, 耗时={ElapsedMs}ms, 错误={ErrorMessage}",
                    OperatorType, @operator.Id, stopwatch.ElapsedMilliseconds, result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Logger.LogDebug(
                "[{OperatorType}] 执行被取消, 算子ID={OperatorId}, 耗时={ElapsedMs}ms",
                OperatorType, @operator.Id, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex,
                "[{OperatorType}] 执行异常, 算子ID={OperatorId}, 耗时={ElapsedMs}ms, 错误={ErrorMessage}",
                OperatorType, @operator.Id, stopwatch.ElapsedMilliseconds, ex.Message);

            return OperatorExecutionOutput.Failure($"执行失败: {ex.Message}");
        }
        finally
        {
            // Sprint 1 Task 1.1: 閲婃斁杈撳叆涓殑 ImageWrapper 寮曠敤
            if (inputs != null)
            {
                // 鍚屼竴涓?ImageWrapper 鍙兘浠ュ涓敭閫忎紶鍒颁笅娓革紱鎸夊紩鐢ㄥ幓閲嶅悗鍐?Release锛岄伩鍏嶅弻閲嶉噴鏀?
                var releasedImages = new HashSet<ImageWrapper>(ReferenceEqualityComparer.Instance);
                foreach (var value in inputs.Values)
                {
                    if (value is ImageWrapper img && releasedImages.Add(img))
                    {
                        img.Release();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 执行算子核心逻辑（子类实现）
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="inputs">杈撳叆鏁版嵁</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>执行结果</returns>
    protected abstract Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken);

    /// <summary>
    /// 验证算子参数（子类实现）
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <returns>楠岃瘉缁撴灉</returns>
    public abstract ValidationResult ValidateParameters(Operator @operator);

    #region 鍙傛暟鑾峰彇鏂规硶

    /// <summary>
    /// 鑾峰彇鍙傛暟鍊硷紙鏀寔绫诲瀷杞崲锛?
    /// </summary>
    /// <typeparam name="T">鐩爣绫诲瀷</typeparam>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected T GetParam<T>(Operator @operator, string paramName, T defaultValue)
    {
        var param = FindParameter(@operator, paramName);
        var rawValue = param?.Value;
        if (rawValue == null)
        {
            return defaultValue;
        }

        try
        {
            // 澶勭悊 System.Text.Json 鍙嶅簭鍒楀寲鐨?JsonElement
            if (rawValue is System.Text.Json.JsonElement jsonElement)
            {
                var converted = ConvertJsonElement<T>(jsonElement, defaultValue);
                return converted is null ? defaultValue : converted;
            }

            return (T)Convert.ChangeType(rawValue, typeof(T));
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex,
                "[{OperatorType}] 参数转换失败: {ParamName}, 值={Value}, 目标类型={TargetType}",
                OperatorType, paramName, rawValue, typeof(T).Name);
            return defaultValue;
        }
    }

    /// <summary>
    /// 鑾峰彇鍙€夊弬鏁板€硷紙鍙兘涓?null锛?
    /// </summary>
    /// <typeparam name="T">鐩爣绫诲瀷</typeparam>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <returns>鍙傛暟鍊硷紝涓嶅瓨鍦ㄦ椂杩斿洖 null</returns>
    protected T? GetParamOrNull<T>(Operator @operator, string paramName) where T : struct
    {
        var param = FindParameter(@operator, paramName);
        var rawValue = param?.Value;
        if (rawValue == null)
        {
            return null;
        }

        try
        {
            if (rawValue is System.Text.Json.JsonElement jsonElement)
            {
                return ConvertJsonElement<T?>(jsonElement, null);
            }

            return (T)Convert.ChangeType(rawValue, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 鑾峰彇瀛楃涓插弬鏁?
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected string GetStringParam(Operator @operator, string paramName, string defaultValue = "")
    {
        return GetParam(@operator, paramName, defaultValue);
    }

    /// <summary>
    /// 鑾峰彇鏁村瀷鍙傛暟
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <param name="min">鏈€灏忓€?/param>
    /// <param name="max">鏈€澶у€?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected int GetIntParam(Operator @operator, string paramName, int defaultValue, int? min = null, int? max = null)
    {
        var value = GetParam(@operator, paramName, defaultValue);

        if (min.HasValue && value < min.Value)
            value = min.Value;
        if (max.HasValue && value > max.Value)
            value = max.Value;

        return value;
    }

    /// <summary>
    /// 鑾峰彇鍙岀簿搴︽诞鐐瑰弬鏁?
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <param name="min">鏈€灏忓€?/param>
    /// <param name="max">鏈€澶у€?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected double GetDoubleParam(Operator @operator, string paramName, double defaultValue, double? min = null, double? max = null)
    {
        var value = GetParam(@operator, paramName, defaultValue);

        if (min.HasValue && value < min.Value)
            value = min.Value;
        if (max.HasValue && value > max.Value)
            value = max.Value;

        return value;
    }

    /// <summary>
    /// 鑾峰彇鍗曠簿搴︽诞鐐瑰弬鏁?
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <param name="min">鏈€灏忓€?/param>
    /// <param name="max">鏈€澶у€?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected float GetFloatParam(Operator @operator, string paramName, float defaultValue, float? min = null, float? max = null)
    {
        var value = GetParam(@operator, paramName, defaultValue);

        if (min.HasValue && value < min.Value)
            value = min.Value;
        if (max.HasValue && value > max.Value)
            value = max.Value;

        return value;
    }

    /// <summary>
    /// 鑾峰彇甯冨皵鍙傛暟
    /// </summary>
    /// <param name="operator">算子实体</param>
    /// <param name="paramName">鍙傛暟鍚嶇О</param>
    /// <param name="defaultValue">榛樿鍊?/param>
    /// <returns>鍙傛暟鍊?/returns>
    protected bool GetBoolParam(Operator @operator, string paramName, bool defaultValue)
    {
        return GetParam(@operator, paramName, defaultValue);
    }

    #endregion

    #region 杈撳叆澶勭悊鏂规硶

    /// <summary>
    /// 灏濊瘯浠庤緭鍏ヤ腑鑾峰彇鍥惧儚鏁版嵁
    /// 鏀寔 ImageWrapper銆乥yte[] 鎴?Mat 绫诲瀷
    /// </summary>
    /// <param name="inputs">杈撳叆瀛楀吀</param>
    /// <param name="key">图像键名，默认为 "Image"</param>
    /// <param name="image">杈撳嚭鍥惧儚鍖呰鍣?/param>
    /// <returns>鏄惁鎴愬姛鑾峰彇</returns>
    protected bool TryGetInputImage(Dictionary<string, object>? inputs, string key, out ImageWrapper? image)
    {
        image = null;

        if (inputs == null)
        {
            Logger.LogDebug("[{OperatorType}] 输入字典为空", OperatorType);
            return false;
        }

        if (!inputs.TryGetValue(key, out var value))
        {
            Logger.LogDebug("[{OperatorType}] 未找到图像输入键: {Key}", OperatorType, key);
            return false;
        }

        if (ImageWrapper.TryGetFromObject(value, out image, out var ownsCreatedWrapper))
        {
            if (ownsCreatedWrapper && image != null)
            {
                inputs[key] = image;
            }

            Logger.LogDebug("[{OperatorType}] 成功获取图像: {Key}, 类型={Type}",
                OperatorType, key, value?.GetType().Name ?? "null");
            return true;
        }

        Logger.LogWarning("[{OperatorType}] 图像类型不支持: {Key}, 类型={Type}",
            OperatorType, key, value?.GetType().Name ?? "null");
        return false;
    }

    /// <summary>
    /// 灏濊瘯浠庤緭鍏ヤ腑鑾峰彇鍥惧儚鏁版嵁锛堥粯璁ら敭鍚?"Image"锛?
    /// </summary>
    /// <param name="inputs">杈撳叆瀛楀吀</param>
    /// <param name="image">杈撳嚭鍥惧儚鍖呰鍣?/param>
    /// <returns>鏄惁鎴愬姛鑾峰彇</returns>
    protected bool TryGetInputImage(Dictionary<string, object>? inputs, out ImageWrapper? image)
    {
        return TryGetInputImage(inputs, "Image", out image);
    }

    /// <summary>
    /// 鑾峰彇杈撳叆鍊?
    /// </summary>
    /// <typeparam name="T">鐩爣绫诲瀷</typeparam>
    /// <param name="inputs">杈撳叆瀛楀吀</param>
    /// <param name="key">閿悕</param>
    /// <param name="value">杈撳嚭鍊?/param>
    /// <returns>鏄惁鎴愬姛鑾峰彇</returns>
    protected bool TryGetInputValue<T>(Dictionary<string, object>? inputs, string key, out T? value)
    {
        value = default;

        if (inputs == null)
            return false;
        if (!inputs.TryGetValue(key, out var obj))
            return false;

        try
        {
            if (obj is T t)
            {
                value = t;
                return true;
            }

            value = (T?)Convert.ChangeType(obj, typeof(T));
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region 杈撳嚭澶勭悊鏂规硶 (P0: ImageWrapper闆舵嫹璐濊緭鍑?

    /// <summary>
    /// 鍒涘缓鍥惧儚杈撳嚭瀛楀吀 - 浣跨敤ImageWrapper瀹炵幇闆舵嫹璐濅紶閫?(P0浼樺厛绾?
    /// </summary>
    /// <param name="mat">杈撳嚭鍥惧儚Mat</param>
    /// <param name="additionalData">闄勫姞鏁版嵁</param>
    /// <returns>输出字典，包含ImageWrapper</returns>
    protected Dictionary<string, object> CreateImageOutput(Mat mat, Dictionary<string, object>? additionalData = null)
    {
        var output = new Dictionary<string, object>
        {
            { "Image", new ImageWrapper(mat) },
            { "Width", mat.Width },
            { "Height", mat.Height }
        };

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                if (!output.ContainsKey(kvp.Key))
                {
                    output[kvp.Key] = kvp.Value;
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Creates an image output dictionary with explicitly named image size keys.
    /// Use this when business outputs already need keys like Width/Height.
    /// </summary>
    protected Dictionary<string, object> CreateImageOutput(
        Mat mat,
        string imageWidthKey,
        string imageHeightKey,
        Dictionary<string, object>? additionalData = null)
    {
        var output = new Dictionary<string, object>
        {
            { "Image", new ImageWrapper(mat) },
            { imageWidthKey, mat.Width },
            { imageHeightKey, mat.Height }
        };

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                if (!output.ContainsKey(kvp.Key))
                {
                    output[kvp.Key] = kvp.Value;
                }
            }
        }

        return output;
    }

    /// <summary>
    /// 鍒涘缓鍥惧儚杈撳嚭瀛楀吀锛堝吋瀹规ā寮忥級- 鏀寔ImageWrapper鎴朾yte[] (P0浼樺厛绾?
    /// </summary>
    /// <param name="mat">杈撳嚭鍥惧儚Mat</param>
    /// <param name="useZeroCopy">鏄惁浣跨敤闆舵嫹璐濓紙ImageWrapper锛?/param>
    /// <param name="additionalData">闄勫姞鏁版嵁</param>
    /// <returns>杈撳嚭瀛楀吀</returns>
    protected Dictionary<string, object> CreateImageOutput(Mat mat, bool useZeroCopy, Dictionary<string, object>? additionalData = null)
    {
        var output = new Dictionary<string, object>();

        if (useZeroCopy)
        {
            output["Image"] = new ImageWrapper(mat);
        }
        else
        {
            // 兼容模式：编码为byte[]
            output["Image"] = mat.ToBytes(".png");
        }

        output["Width"] = mat.Width;
        output["Height"] = mat.Height;

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                if (!output.ContainsKey(kvp.Key))
                {
                    output[kvp.Key] = kvp.Value;
                }
            }
        }

        return output;
    }

    #endregion

    #region 杈呭姪鏂规硶

    protected static Position CreatePosition(double x, double y)
    {
        return new Position(x, y);
    }

    protected static Dictionary<string, object> CreatePointData(
        string pointKey,
        Position position,
        string? xKey = "X",
        string? yKey = "Y")
    {
        var data = new Dictionary<string, object>
        {
            [pointKey] = position
        };

        if (!string.IsNullOrWhiteSpace(xKey))
        {
            data[xKey] = position.X;
        }

        if (!string.IsNullOrWhiteSpace(yKey))
        {
            data[yKey] = position.Y;
        }

        return data;
    }

    protected static Dictionary<string, object> MergeData(params IDictionary<string, object>?[] fragments)
    {
        var merged = new Dictionary<string, object>();
        foreach (var fragment in fragments)
        {
            if (fragment == null)
            {
                continue;
            }

            foreach (var (key, value) in fragment)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>
    /// 杞崲 JsonElement 涓虹洰鏍囩被鍨?
    /// </summary>
    private static Parameter? FindParameter(Operator @operator, string paramName)
    {
        if (string.IsNullOrWhiteSpace(paramName) || @operator.Parameters.Count == 0)
        {
            return null;
        }

        if (@operator.Parameters.Count == 1)
        {
            var single = @operator.Parameters[0];
            return single.Name == paramName ? single : null;
        }

        if (ParameterLookupCaches.TryGetValue(@operator, out var cache) &&
            cache.ModifiedAt == @operator.ModifiedAt &&
            cache.ParameterCount == @operator.Parameters.Count)
        {
            cache.Lookup.TryGetValue(paramName, out var cachedParam);
            return cachedParam;
        }

        var lookup = new Dictionary<string, Parameter>(@operator.Parameters.Count, StringComparer.Ordinal);
        foreach (var parameter in @operator.Parameters)
        {
            if (parameter?.Name == null || lookup.ContainsKey(parameter.Name))
            {
                continue;
            }

            lookup[parameter.Name] = parameter;
        }

        var newCache = new ParameterLookupCache(@operator.ModifiedAt, @operator.Parameters.Count, lookup);
        lock (ParameterLookupCaches)
        {
            ParameterLookupCaches.Remove(@operator);
            ParameterLookupCaches.Add(@operator, newCache);
        }

        lookup.TryGetValue(paramName, out var param);
        return param;
    }

    private static T? ConvertJsonElement<T>(System.Text.Json.JsonElement element, T? defaultValue)
    {
        try
        {
            var targetType = typeof(T);

            if (targetType == typeof(string))
                return (T?)(object?)(element.ToString() ?? string.Empty);
            if (targetType == typeof(int) || targetType == typeof(int?))
                return (T?)(object)element.GetInt32();
            if (targetType == typeof(double) || targetType == typeof(double?))
                return (T?)(object)element.GetDouble();
            if (targetType == typeof(float) || targetType == typeof(float?))
                return (T?)(object)element.GetSingle();
            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return (T?)(object)element.GetBoolean();
            if (targetType == typeof(long) || targetType == typeof(long?))
                return (T?)(object)element.GetInt64();

            // 鍏朵粬绫诲瀷灏濊瘯瀛楃涓茶浆鎹?
            return (T?)Convert.ChangeType(element.ToString(), targetType);
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 鍦ㄥ悗鍙扮嚎绋嬫墽琛?CPU 瀵嗛泦鍨嬫搷浣?
    /// </summary>
    /// <typeparam name="T">杩斿洖绫诲瀷</typeparam>
    /// <param name="action">瑕佹墽琛岀殑鎿嶄綔</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    /// <returns>鎿嶄綔缁撴灉</returns>
    protected Task<T> RunCpuBoundWork<T>(Func<T> action, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }, cancellationToken);
    }

    /// <summary>
    /// 鍦ㄥ悗鍙扮嚎绋嬫墽琛?CPU 瀵嗛泦鍨嬫搷浣滐紙鏃犺繑鍥炲€硷級
    /// </summary>
    /// <param name="action">瑕佹墽琛岀殑鎿嶄綔</param>
    /// <param name="cancellationToken">鍙栨秷浠ょ墝</param>
    protected Task RunCpuBoundWork(Action action, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }, cancellationToken);
    }

    #endregion
}
