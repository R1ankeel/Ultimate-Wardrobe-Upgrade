using Mutagen.Bethesda.Plugins;
using UltimateWardrobe.Core.Domain;

namespace UltimateWardrobe.Scanner;

public sealed class LoadOrderBuilder
{
    private readonly ModLoader _loader;

    public LoadOrderBuilder(ModLoader loader)
    {
        _loader = loader;
    }

    public IReadOnlyList<DiscoveredPlugin> Build(
        DiscoveryResult discovery,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken = default)
    {
        var pool = discovery.Plugins.ToDictionary(p => p.ModKey);
        var order = new List<DiscoveredPlugin>();
        var visited = new HashSet<ModKey>();
        var warnedMissing = new HashSet<ModKey>();

        foreach (var root in discovery.Plugins.OrderBy(p => p.ModKey.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Visit(root, pool, visited, order, warnedMissing, warnings, cancellationToken);
        }

        return order;
    }

    private void Visit(
        DiscoveredPlugin plugin,
        Dictionary<ModKey, DiscoveredPlugin> pool,
        HashSet<ModKey> visited,
        List<DiscoveredPlugin> order,
        HashSet<ModKey> warnedMissing,
        List<ScanWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (!visited.Add(plugin.ModKey))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ModKey> masters;
        try
        {
            masters = _loader.ReadMasters(plugin.AbsolutePath);
        }
        catch (Exception ex)
        {
            warnings.Add(new ScanWarning(
                $"[{(plugin.IsMainPlugin ? "main plugin" : "plugin")}] '{plugin.ModKey.FileName}' could not be read and was skipped: {ex.Message}"));
            return;
        }

        foreach (var master in masters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pool.TryGetValue(master, out var masterPlugin))
            {
                if (!visited.Contains(masterPlugin.ModKey))
                {
                    Visit(masterPlugin, pool, visited, order, warnedMissing, warnings, cancellationToken);
                }

                continue;
            }

            if (warnedMissing.Add(master))
            {
                warnings.Add(new ScanWarning(
                    $"[{(plugin.IsMainPlugin ? "main plugin" : "plugin")}] '{plugin.ModKey.FileName}' references missing master '{master.FileName}'. " +
                    "Records linked to it may not resolve; the scan continues without it."));
            }
        }

        order.Add(plugin);
    }
}