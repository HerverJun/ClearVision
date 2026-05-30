using Acme.Product.Core.Enums;

namespace Acme.Product.Application.Services;

public static class InspectionImagePersistencePolicy
{
    public static bool ShouldPersistImage(string? savePolicy, InspectionStatus status)
    {
        var policy = (savePolicy ?? "NgOnly").Trim();
        if (policy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (policy.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return status == InspectionStatus.NG;
    }
}
