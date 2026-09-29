using System.Text.Json;
using System.Text.Json.Serialization;
using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Infrastructure;

/// <summary>Versioned local JSON. Same-name saves replace one preset; invalid files are never silently reset.</summary>
public sealed class JsonSearchPresetRepository(string filePath) : ISearchPresetRepository
{
    public async Task<IReadOnlyList<SearchPreset>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        try
        {
            var document = JsonSerializer.Deserialize<PresetDocument>(json, CreateOptions())
                ?? throw new InvalidDataException("搜尋條件檔案不可為空。");
            if (document.Version != 1 || document.Presets is null)
                throw new InvalidDataException("不支援的搜尋條件檔案格式。");
            if (document.Presets.Any(p => p is null)) throw new InvalidDataException("搜尋條件項目不可為 null。");
            var presets = document.Presets.Select(p => p.ToPreset()).ToArray();
            if (presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Length)
                throw new InvalidDataException("搜尋條件檔案包含重複名稱。");
            return Array.AsReadOnly(presets);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidDataException("搜尋條件檔案內容無效，請保留原檔並修正。", ex);
        }
    }

    public async Task SaveAsync(SearchPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var presets = (await LoadAsync(cancellationToken)).ToList();
        var index = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) presets.Add(preset); else presets[index] = preset;
        await WriteAsync(presets, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var presets = (await LoadAsync(cancellationToken)).ToList();
        if (presets.RemoveAll(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
            await WriteAsync(presets, cancellationToken);
    }

    private async Task WriteAsync(List<SearchPreset> presets, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = new PresetDocument(1, presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(StoredPreset.FromPreset).ToArray());
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document, CreateOptions()), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter<ItemCategory>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<EquipmentStat>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<DetailMatchMode>(allowIntegerValues: false));
        return options;
    }

    private sealed record PresetDocument(int Version, StoredPreset[] Presets);
    // Explicit storage DTO avoids making Domain models depend on System.Text.Json attributes.
    private sealed record StoredPreset(string Name, ItemCategory? Category, string? Classification,
        string? Subclassification, string? Potential, string? AdditionalPotential, string? EquipmentName,
        bool ExactName, int? MinimumLevel, int? MaximumLevel, decimal? MinimumPrice, decimal? MaximumPrice,
        DetailMatchMode DetailMode, StoredCondition[] Conditions, decimal DiscountThreshold, StatCriteria Stats)
    {
        public static StoredPreset FromPreset(SearchPreset preset)
        {
            var f = preset.Rule.Criteria.Filters;
            var details = preset.Rule.Criteria.Details;
            return new(preset.Name, f.Category, f.Classification, f.Subclassification, f.Potential, f.AdditionalPotential,
                f.EquipmentName, f.ExactName, f.Levels?.Minimum, f.Levels?.Maximum, f.Prices?.Minimum?.Amount,
                f.Prices?.Maximum?.Amount, details.Mode, details.Conditions.Select(c => new StoredCondition(c.Stat, c.Minimum)).ToArray(),
                preset.Rule.DiscountThreshold, preset.Rule.Criteria.Stats);
        }
        public SearchPreset ToPreset()
        {
            if (Category.HasValue && !Enum.IsDefined(Category.Value)) throw new InvalidDataException("未知的道具分類。");
            if (MinimumLevel < 0 || MaximumLevel < 0) throw new InvalidDataException("等級不可為負數。");
            if (Conditions is null || Stats is null || Conditions.Any(c => c is null)) throw new InvalidDataException("搜尋條件欄位缺失。");
            var filters = new AuctionSearchFilters(Category, Classification, Subclassification, Potential, AdditionalPotential,
                EquipmentName, ExactName, new(MinimumLevel, MaximumLevel),
                new(MinimumPrice.HasValue ? new Price(MinimumPrice.Value) : null, MaximumPrice.HasValue ? new Price(MaximumPrice.Value) : null));
            var details = new DetailSearch(DetailMode, Conditions.Select(c => new MinimumStatCondition(c.Stat, c.Minimum)));
            return new(Name, new(new SearchCriteria(filters, details, Stats), DiscountThreshold));
        }
    }
    private sealed record StoredCondition(EquipmentStat Stat, int Minimum);
}
