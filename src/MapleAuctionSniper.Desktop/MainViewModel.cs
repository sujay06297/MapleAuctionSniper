using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MapleAuctionSniper.Application;
using MapleAuctionSniper.Domain;

namespace MapleAuctionSniper.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ScanAuction workflow;
    private readonly ISearchPresetRepository presetRepository;
    private readonly ScheduledAuctionMonitor monitor;
    private readonly IMonitoringScheduleRepository scheduleRepository;
    private CancellationTokenSource? monitoringCancellation;
    private bool isMonitoring;
    private string intervalMinutes = "5";
    private string monitoringStatus = "尚未開始自動監控。";
    private DateTimeOffset? nextScanAt;
    public ObservableCollection<MonitoredPresetSelection> MonitorSelections { get; } = [];
    public bool IsMonitoring { get => isMonitoring; private set { isMonitoring = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditSearch)); RefreshCommands(); } }
    public bool CanEditSearch => !busy && !IsMonitoring;
    public string IntervalMinutes { get => intervalMinutes; set { intervalMinutes = value; OnPropertyChanged(); } }
    public string MonitoringStatus { get => monitoringStatus; private set { monitoringStatus = value; OnPropertyChanged(); } }
    public DateTimeOffset? NextScanAt { get => nextScanAt; private set { nextScanAt = value; OnPropertyChanged(); } }
    private MonitoringRule? rule;
    private bool busy = true;
    private string status = "先建立監控條件，再執行掃描。預設資料會找到一筆低價裝備。";
    public string EquipmentName { get; set; } = "模擬長劍";
    public IReadOnlyList<SearchOption<ItemCategory>> Categories { get; } =
        [new("全體", null), new("防具", ItemCategory.Armor), new("武器", ItemCategory.Weapon), new("消耗", ItemCategory.Consumable)];
    public SearchOption<ItemCategory> SelectedCategory { get; set; }
    public IReadOnlyList<string> UnrestrictedOptions { get; } = ["全體"];
    public IReadOnlyList<string> PotentialOptions { get; } = ["全體", "特殊", "稀有", "罕見", "傳說"];
    public string Classification { get; set; } = "全體";
    public string Subclassification { get; set; } = "全體";
    public string Potential { get; set; } = "全體";
    public string AdditionalPotential { get; set; } = "全體";
    public bool ExactName { get; set; } = true;
    public string MinimumLevel { get; set; } = "";
    public string MaximumLevel { get; set; } = "";
    public string MinimumPrice { get; set; } = "";
    public string MaximumPrice { get; set; } = "100000000";
    public string DiscountThreshold { get; set; } = "0.8";
    public IReadOnlyList<DetailConditionRow> DetailRows { get; private set; }
    private readonly IReadOnlyList<SearchOption<EquipmentStat>> statOptions;
    public ObservableCollection<SearchPreset> Presets { get; } = [];
    private string presetName = "";
    public string PresetName
    {
        get => presetName;
        set { presetName = value; OnPropertyChanged(); SavePresetCommand.RaiseCanExecuteChanged(); }
    }
    private SearchPreset? selectedPreset;
    public SearchPreset? SelectedPreset
    {
        get => selectedPreset;
        set { selectedPreset = value; OnPropertyChanged(); RefreshCommands(); }
    }
    private bool isAnd = true;
    public bool IsAnd
    {
        get => isAnd;
        set { isAnd = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOr)); }
    }
    public bool IsOr { get => !IsAnd; set => IsAnd = !value; }
    public ObservableCollection<ListingAnalysis> Results { get; } = [];
    public string Status { get => status; private set { status = value; OnPropertyChanged(); } }
    public UiCommand CreateRuleCommand { get; }
    public UiCommand ScanCommand { get; }
    public UiCommand SavePresetCommand { get; }
    public UiCommand ApplyPresetCommand { get; }
    public UiCommand DeletePresetCommand { get; }
    public UiCommand StartMonitoringCommand { get; }
    public UiCommand StopMonitoringCommand { get; }
    public MainViewModel(ScanAuction workflow, ISearchPresetRepository presetRepository,
        ScheduledAuctionMonitor monitor, IMonitoringScheduleRepository scheduleRepository)
    {
        this.workflow = workflow;
        this.presetRepository = presetRepository;
        this.monitor = monitor; this.scheduleRepository = scheduleRepository;
        SelectedCategory = Categories[2];
        statOptions = [new("不選擇", null),
            new("STR", EquipmentStat.STR), new("DEX", EquipmentStat.DEX), new("INT", EquipmentStat.INT),
            new("LUK", EquipmentStat.LUK), new("物理攻擊力", EquipmentStat.WeaponAttack),
            new("魔法攻擊力", EquipmentStat.MagicAttack), new("剩餘升級次數", EquipmentStat.UpgradeSlots)];
        DetailRows = [new(statOptions, statOptions[1], "10"), new(statOptions, statOptions[5], "90"), new(statOptions, statOptions[0], "")];
        CreateRuleCommand = new(() => { CreateRule(); return Task.CompletedTask; }, () => CanEditSearch);
        ScanCommand = new(ScanAsync, () => CanEditSearch && rule is not null);
        SavePresetCommand = new(SavePresetAsync, () => CanEditSearch && !string.IsNullOrWhiteSpace(PresetName));
        ApplyPresetCommand = new(() => { ApplyPreset(); return Task.CompletedTask; }, () => CanEditSearch && SelectedPreset is not null);
        DeletePresetCommand = new(DeletePresetAsync, () => CanEditSearch && SelectedPreset is not null);
        StartMonitoringCommand = new(StartMonitoringAsync, () => CanEditSearch && MonitorSelections.Any(s => s.IsSelected));
        StopMonitoringCommand = new(() => { StopMonitoring(); return Task.CompletedTask; }, () => IsMonitoring);
    }
    public async Task LoadPresetsAsync()
    {
        try { await ReloadPresetsAsync(); }
        catch (Exception ex) { Status = $"讀取搜尋條件失敗：{ex.Message}；可繼續使用模擬掃描。"; }
        try
        {
            var schedule = await scheduleRepository.LoadAsync();
            IntervalMinutes = FormatNumber<decimal>(schedule.IntervalMinutes);
            foreach (var selection in MonitorSelections)
                selection.IsSelected = schedule.SearchNames.Contains(selection.Preset.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { MonitoringStatus = $"讀取排程設定失敗：{ex.Message}"; }
        finally { busy = false; RefreshCommands(); }
    }
    private MonitoringRule BuildRule()
    {
        var filters = new AuctionSearchFilters(SelectedCategory.Value, SelectedLabel(Classification),
            SelectedLabel(Subclassification), SelectedLabel(Potential), SelectedLabel(AdditionalPotential),
            EquipmentName, ExactName, new(ParseLevel(MinimumLevel), ParseLevel(MaximumLevel)),
            new(ParsePrice(MinimumPrice), ParsePrice(MaximumPrice)));
        var conditions = DetailRows.Where(row => row.SelectedStat.Value.HasValue)
            .Select(row => new MinimumStatCondition(row.SelectedStat.Value!.Value,
                int.Parse(row.Minimum.Trim(), CultureInfo.InvariantCulture))).ToArray();
        return new(new SearchCriteria(filters, new(IsAnd ? DetailMatchMode.And : DetailMatchMode.Or, conditions)),
            ParseDecimal(DiscountThreshold));
    }
    private void CreateRule()
    {
        try
        {
            rule = BuildRule();
            Status = $"已建立 {SelectedCategory.Label} 查詢，詳細條件 {(IsAnd ? "AND" : "OR")}，歷史折扣門檻 {rule.DiscountThreshold:P0}。修改後請按更新。";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            rule = null;
            Status = $"條件無效：{ex.Message}";
        }
        ScanCommand.RaiseCanExecuteChanged();
    }
    private async Task ScanAsync()
    {
        if (rule is null) return;
        busy = true; RefreshCommands(); Status = "掃描中…"; Results.Clear();
        try
        {
            var result = await workflow.ExecuteAsync(rule);
            DisplayResult(result);
        }
        catch (Exception ex) { Status = $"掃描失敗：{ex.Message}"; }
        finally { busy = false; RefreshCommands(); }
    }
    private async Task ReloadPresetsAsync(string? selectName = null)
    {
        var loaded = await presetRepository.LoadAsync();
        var selectedNames = MonitorSelections.Where(s => s.IsSelected).Select(s => s.Preset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in MonitorSelections) selection.PropertyChanged -= OnMonitorSelectionChanged;
        MonitorSelections.Clear();
        foreach (var preset in loaded)
        {
            var selection = new MonitoredPresetSelection(preset) { IsSelected = selectedNames.Contains(preset.Name) };
            selection.PropertyChanged += OnMonitorSelectionChanged;
            MonitorSelections.Add(selection);
        }
        SelectedPreset = null;
        Presets.Clear();
        foreach (var preset in loaded) Presets.Add(preset);
        SelectedPreset = Presets.FirstOrDefault(p => string.Equals(p.Name, selectName, StringComparison.OrdinalIgnoreCase));
    }
    private async Task SavePresetAsync()
    {
        busy = true; RefreshCommands();
        try
        {
            // Save the current inputs, even if the user has not pressed "update rule" yet.
            var preset = new SearchPreset(PresetName, BuildRule());
            await presetRepository.SaveAsync(preset);
            await ReloadPresetsAsync(preset.Name);
            PresetName = preset.Name;
            Status = $"已儲存搜尋條件「{preset.Name}」（同名條件會更新）。";
        }
        catch (Exception ex) { Status = $"儲存搜尋條件失敗：{ex.Message}"; }
        finally { busy = false; RefreshCommands(); }
    }
    private void ApplyPreset()
    {
        if (SelectedPreset is null) return;
        var preset = SelectedPreset;
        var filters = preset.Rule.Criteria.Filters;
        // Unsupported future dropdown values must not silently broaden a saved search.
        if ((filters.Potential is not null && !PotentialOptions.Contains(filters.Potential)) ||
            (filters.AdditionalPotential is not null && !PotentialOptions.Contains(filters.AdditionalPotential)))
        {
            Status = "無法套用：搜尋條件包含目前不支援的潛能選項。";
            return;
        }
        SelectedCategory = Categories.Single(c => c.Value == filters.Category);
        Classification = filters.Classification ?? "全體"; Subclassification = filters.Subclassification ?? "全體";
        Potential = filters.Potential ?? "全體"; AdditionalPotential = filters.AdditionalPotential ?? "全體";
        EquipmentName = filters.EquipmentName ?? ""; ExactName = filters.ExactName;
        MinimumLevel = FormatNumber(filters.Levels?.Minimum); MaximumLevel = FormatNumber(filters.Levels?.Maximum);
        MinimumPrice = FormatNumber(filters.Prices?.Minimum?.Amount); MaximumPrice = FormatNumber(filters.Prices?.Maximum?.Amount);
        DiscountThreshold = FormatNumber<decimal>(preset.Rule.DiscountThreshold);
        IsAnd = preset.Rule.Criteria.Details.Mode == DetailMatchMode.And;
        var conditions = preset.Rule.Criteria.Details.Conditions;
        DetailRows = Enumerable.Range(0, 3).Select(i => i < conditions.Count
            ? new DetailConditionRow(statOptions, statOptions.Single(o => o.Value == conditions[i].Stat), FormatNumber<int>(conditions[i].Minimum))
            : new DetailConditionRow(statOptions, statOptions[0], "")).ToArray();
        PresetName = preset.Name;
        rule = preset.Rule;
        Results.Clear();
        OnPropertyChanged(string.Empty);
        RefreshCommands();
        Status = $"已套用「{preset.Name}」，可直接執行掃描。";
    }
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset is null) return;
        var name = SelectedPreset.Name;
        busy = true; RefreshCommands();
        try
        {
            await presetRepository.DeleteAsync(name);
            await ReloadPresetsAsync();
            Status = $"已刪除搜尋條件「{name}」，目前查詢仍可使用。";
        }
        catch (Exception ex) { Status = $"刪除搜尋條件失敗：{ex.Message}"; }
        finally { busy = false; RefreshCommands(); }
    }
    private static string FormatNumber<T>(T? value) where T : struct, IFormattable => value?.ToString(null, CultureInfo.InvariantCulture) ?? "";
    private void OnMonitorSelectionChanged(object? sender, PropertyChangedEventArgs e) => StartMonitoringCommand.RaiseCanExecuteChanged();
    private void DisplayResult(ScanResult result)
    {
        Results.Clear();
        foreach (var row in result.Listings) Results.Add(row);
        Status = $"掃描完成：{Results.Count} 筆裝備，{Results.Count(r => r.Evaluation.IsPotentialDeal)} 筆低價，{Results.Count(r => r.NotificationTriggered)} 筆通知已送出，{Results.Count(r => r.NotificationError is not null)} 筆通知失敗；快照已保存於記憶體。";
    }
    private async Task StartMonitoringAsync()
    {
        using var cancellation = new CancellationTokenSource();
        monitoringCancellation = cancellation;
        var rounds = 0;
        try
        {
            var selected = MonitorSelections.Where(s => s.IsSelected).Select(s => s.Preset).ToArray();
            var schedule = new MonitoringScheduleSettings(ParseDecimal(IntervalMinutes), selected.Select(s => s.Name));
            if (selected.Length == 0) throw new ArgumentException("請至少勾選一組已儲存條件。");
            IsMonitoring = true;
            MonitoringStatus = $"正在開始監控 {selected.Length} 組條件…";
            await scheduleRepository.SaveAsync(schedule, cancellation.Token);
            await monitor.RunAsync(selected, TimeSpan.FromMinutes((double)schedule.IntervalMinutes),
                result =>
                {
                    DisplayResult(result);
                    rounds++;
                    MonitoringStatus = $"監控中：{selected.Length} 組條件，已完成 {rounds} 輪；每輪結束後間隔 {schedule.IntervalMinutes} 分鐘。";
                    return Task.CompletedTask;
                },
                ex =>
                {
                    Status = $"自動掃描失敗：{ex.Message}";
                    MonitoringStatus = "本輪失敗，將於設定間隔後重試。";
                    return Task.CompletedTask;
                }, time => NextScanAt = time?.ToLocalTime(), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { MonitoringStatus = $"監控已停止，共完成 {rounds} 輪。"; }
        catch (Exception ex) { MonitoringStatus = $"無法開始監控：{ex.Message}"; }
        finally
        {
            monitoringCancellation = null;
            NextScanAt = null;
            IsMonitoring = false;
        }
    }
    public void StopMonitoring()
    {
        monitoringCancellation?.Cancel();
        if (IsMonitoring) MonitoringStatus = "正在停止監控…";
    }
    private void RefreshCommands()
    {
        CreateRuleCommand.RaiseCanExecuteChanged(); ScanCommand.RaiseCanExecuteChanged();
        SavePresetCommand.RaiseCanExecuteChanged(); ApplyPresetCommand.RaiseCanExecuteChanged(); DeletePresetCommand.RaiseCanExecuteChanged();
        StartMonitoringCommand.RaiseCanExecuteChanged(); StopMonitoringCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditSearch));
    }
    private static decimal ParseDecimal(string value) => decimal.Parse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture);
    private static Price? ParsePrice(string value) => string.IsNullOrWhiteSpace(value) ? null : new(ParseDecimal(value));
    private static string? SelectedLabel(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "全體" ? null : value.Trim();
    private static int? ParseLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var level = int.Parse(value.Trim(), CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        return level;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class MonitoredPresetSelection(SearchPreset preset) : INotifyPropertyChanged
{
    public SearchPreset Preset { get; } = preset;
    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set { isSelected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record SearchOption<T>(string Label, T? Value) where T : struct;

public sealed class DetailConditionRow(IReadOnlyList<SearchOption<EquipmentStat>> options,
    SearchOption<EquipmentStat> selectedStat, string minimum)
{
    public IReadOnlyList<SearchOption<EquipmentStat>> Options { get; } = options;
    public SearchOption<EquipmentStat> SelectedStat { get; set; } = selectedStat;
    public string Minimum { get; set; } = minimum;
}

public sealed class UiCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
