namespace ClearVision.Product.Core.Enums;

public enum OperatorLifecycle
{
    Stable,
    Experimental,
    Reference,
    Legacy,
    Deprecated
}

public static class OperatorLifecyclePolicy
{
    public static bool IsHiddenByDefault(OperatorLifecycle lifecycle) =>
        lifecycle is OperatorLifecycle.Legacy or OperatorLifecycle.Deprecated;
}
