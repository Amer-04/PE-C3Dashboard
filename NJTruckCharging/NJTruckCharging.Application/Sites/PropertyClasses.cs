namespace NJTruckCharging.Application.Sites;

/// <summary>NJ MOD-IV property class → label. A merged site's class is its best class: 4B > 1 > 4A (as in SiteFilter).</summary>
public static class PropertyClasses
{
    public static string Label(string propClass) =>
        propClass.Contains("4B") ? "Industrial"
        : propClass == "1" || propClass.StartsWith("1/") ? "Vacant land"
        : "Commercial";
}
