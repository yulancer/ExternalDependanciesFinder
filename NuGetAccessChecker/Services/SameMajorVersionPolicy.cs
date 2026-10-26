using NuGet.Versioning;

namespace NuGetAccessChecker.Services;

/// <summary>
/// Политика совместимости: допускается любая скачиваемая версия
/// в пределах той же major, не ниже требуемой.
/// </summary>
public static class SameMajorVersionPolicy
{
    public static bool IsCompatible(NuGetVersion required, NuGetVersion candidate)
    {
        if (candidate.Major != required.Major)
            return false;

        if (candidate < required)
            return false;

        if (!required.IsPrerelease && candidate.IsPrerelease)
            return false;

        return true;
    }

    /// <summary>
    /// Выбирает лучшую совместимую версию: точное совпадение, иначе максимальную.
    /// </summary>
    public static NuGetVersion? SelectBest(NuGetVersion required, IEnumerable<NuGetVersion> candidates)
    {
        var compatible = candidates
            .Where(c => IsCompatible(required, c))
            .Distinct()
            .OrderByDescending(c => c)
            .ToList();

        if (compatible.Count == 0)
            return null;

        var exact = compatible.FirstOrDefault(c => c == required);
        return exact ?? compatible[0];
    }
}
