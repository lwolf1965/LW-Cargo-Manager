using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin.Policy;
using ClientPlugin.Settings.Elements;

namespace ClientPlugin;

/// <summary>
/// Plugin settings. Saved as Config.xml in the plugin data folder and editable in game
/// through Pulsar's plugin settings button (UI is generated from the attributes).
/// </summary>
public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enabled = true;
    private bool dryRun = false;
    private bool verboseLog = false;

    private UnloadMode unloadMode = UnloadMode.Tagged;
    private bool unloadIce = true;
    private string unloadAllKeeps = "Tools, Consumables, Ammo, Cells";
    private bool fallbackToUntaggedCargo = true;

    private bool sortEnabled = true;
    private bool drainProducerOutputs = true;
    private bool overflowToEmpty = true;
    private bool autoCategories = false;
    private int graceDelaySeconds = 60;
    private bool protectQuickDeposit = true;

    private bool tidyConsolidate = true;
    private bool tidyMergeExperimental = false;
    private bool autoAmmoProduction = true;
    private float ammoReserveMultiplier = 2f;
    private int ammoBatchSize = 25;
    private float ammoReserveMinutes = 5f;
    private bool ammoOrderWithoutMaterials = true;
    private TidyOrder tidyOrder = TidyOrder.Off;

    private float unloadIntervalSeconds = 1f;
    private float sortIntervalSeconds = 5f;
    private float tidyIntervalSeconds = 10f;
    private int maxTransferAttempts = 64;
    private int maxItemChecks = 256;
    private int softTimeBudgetMs = 10;
    private int defaultPriority = 5;
    private bool hudMessages = true;

    private string blockRoleOverrides = "";

    private bool lcdEnabled = true;
    private float lcdIntervalSeconds = 3f;
    private int lcdWidth = 30;
    private bool lcdAutoStyle = true;
    private float lcdFontSize = 16f;
    private int lcdMaxLines = 12;
    private bool lcdShowRates = false;
    private int deliveryShowMinutes = 60;
    private float lowOreTons = 1f;

    #endregion

    #region User interface

    [XmlIgnore]
    public readonly string Title = "LW Cargo Manager";

    [Separator("General")]

    [Checkbox(description: "Turn the whole plugin on or off")]
    public bool Enabled { get => enabled; set => SetField(ref enabled, value); }

    [Checkbox(label: "Dry run", description: "Only write to the log what would be moved. Nothing is moved.")]
    public bool DryRun { get => dryRun; set => SetField(ref dryRun, value); }

    [Checkbox(label: "Verbose log", description: "Log every planned and done transfer (for testing)")]
    public bool VerboseLog { get => verboseLog; set => SetField(ref verboseLog, value); }

    /// <summary>Not shown in settings: no game HUD call yet, messages go to [Alerts] and the log.</summary>
    public bool HudMessages { get => hudMessages; set => SetField(ref hudMessages, value); }

    [Separator("Unload docks")]

    [Dropdown(label: "Unload mode", description: "Tagged: only [Unload] connectors. All Station Connectors: every connector on a station, except [NoUnload]")]
    public UnloadMode UnloadMode { get => unloadMode; set => SetField(ref unloadMode, value); }

    [Checkbox(label: "Unload ice", description: "Also unload ice from docked ships")]
    public bool UnloadIce { get => unloadIce; set => SetField(ref unloadIce, value); }

    [Textbox(label: "[Unload All] keeps", description: "Categories the ship keeps at an [Unload All] dock, e.g. Tools, Consumables, Ammo, Cells")]
    public string UnloadAllKeeps { get => unloadAllKeeps; set => SetField(ref unloadAllKeeps, value ?? ""); }

    [Checkbox(label: "Fallback to untagged cargo", description: "If no tagged container has room (or none exists), docked ships and production outputs (refinery, smelter, assembler, fabricator, Gearforge) unload into any untagged cargo container")]
    public bool FallbackToUntaggedCargo { get => fallbackToUntaggedCargo; set => SetField(ref fallbackToUntaggedCargo, value); }

    [Separator("Sorting")]

    [Checkbox(label: "Sorting", description: "Sort items into tagged containers on grids that have at least one tagged container")]
    public bool SortEnabled { get => sortEnabled; set => SetField(ref sortEnabled, value); }

    [Checkbox(label: "Drain producer outputs", description: "Take finished items out of refinery/assembler outputs (inputs are never touched)")]
    public bool DrainProducerOutputs { get => drainProducerOutputs; set => SetField(ref drainProducerOutputs, value); }

    [Checkbox(label: "Overflow to empty", description: "When all tagged containers are full, use empty untagged containers for this pass")]
    public bool OverflowToEmpty { get => overflowToEmpty; set => SetField(ref overflowToEmpty, value); }

    [Checkbox(label: "Auto categories", description: "Untagged containers with only one kind of item act as destinations for it. Tags always win.")]
    public bool AutoCategories { get => autoCategories; set => SetField(ref autoCategories, value); }

    [Slider(0, 600, 10, SliderAttribute.SliderType.Integer, label: "Grace delay (s)", description: "Items a player puts into a container stay there this long. 0 = off")]
    public int GraceDelaySeconds { get => graceDelaySeconds; set => SetField(ref graceDelaySeconds, Math.Clamp(value, 0, 600)); }

    [Checkbox(label: "Protect quick deposit", description: "Alt + right-click deposit keeps hand tools and weapons on the character")]
    public bool ProtectQuickDeposit { get => protectQuickDeposit; set => SetField(ref protectQuickDeposit, value); }

    [Slider(1, 9, 1, SliderAttribute.SliderType.Integer, label: "Default priority", description: "Priority of tags without P1..P9 (1 fills first)")]
    public int DefaultPriority { get => defaultPriority; set => SetField(ref defaultPriority, Math.Clamp(value, 1, 9)); }

    [Separator("Tidy")]

    [Checkbox(label: "Consolidate", description: "Pack the same item into as few same-tag containers as possible")]
    public bool TidyConsolidate { get => tidyConsolidate; set => SetField(ref tidyConsolidate, value); }

    [Checkbox(label: "Merge stacks", description: "Join split stacks of the same item inside a container")]
    public bool TidyMergeExperimental { get => tidyMergeExperimental; set => SetField(ref tidyMergeExperimental, value); }

    /// <summary>Not shown in settings yet (item ordering inside containers is not done).</summary>
    public TidyOrder TidyOrder { get => tidyOrder; set => SetField(ref tidyOrder, value); }

    [Separator("Weapon ammo")]

    [Checkbox(label: "Auto ammo production", description: "Queue ammo for turrets and fixed weapons in Gearforges named [Make Ammo] when stock is below target (only when the Gearforge is idle)")]
    public bool AutoAmmoProduction { get => autoAmmoProduction; set => SetField(ref autoAmmoProduction, value); }

    [Slider(0, 30, 1, SliderAttribute.SliderType.Float, label: "Ammo reserve (minutes of fire)", description: "Stock target = every weapon reloaded once + ammo for this many minutes with all weapons firing (rate read from the game)")]
    public float AmmoReserveMinutes { get => ammoReserveMinutes; set => SetField(ref ammoReserveMinutes, Math.Clamp(value, 0f, 30f)); }

    [Checkbox(label: "Order ammo without materials", description: "Queue ammo even when the parts are missing; the game then asks connected Assemblers/Fabricators for them (needs those blocks, and ore or parts on the grid)")]
    public bool AmmoOrderWithoutMaterials { get => ammoOrderWithoutMaterials; set => SetField(ref ammoOrderWithoutMaterials, value); }

    [Slider(0, 5, 0.5, SliderAttribute.SliderType.Float, label: "Ammo reserve fallback (x reload)", description: "Used only when the game gives no fire rate: target = one reload + this many reloads in storage")]
    public float AmmoReserveMultiplier { get => ammoReserveMultiplier; set => SetField(ref ammoReserveMultiplier, Math.Clamp(value, 0f, 5f)); }

    [Slider(1, 100, 1, SliderAttribute.SliderType.Integer, label: "Ammo batch (recipes)", description: "Most recipes queued at once per Gearforge. Never more than the stock is missing. Bigger = stock fills faster, but Assemblers make the ammo parts first")]
    public int AmmoBatchSize { get => ammoBatchSize; set => SetField(ref ammoBatchSize, Math.Clamp(value, 1, 100)); }

    [Separator("LCD screens")]

    [Checkbox(label: "LCD screens", description: "Write cargo info to LCDs named [Cargo], [Cargo Ore], [Cargo Parts] ..., [Cargo Alerts]")]
    public bool LcdEnabled { get => lcdEnabled; set => SetField(ref lcdEnabled, value); }

    [Slider(1, 30, 1, SliderAttribute.SliderType.Float, label: "LCD update (s)")]
    public float LcdIntervalSeconds { get => lcdIntervalSeconds; set => SetField(ref lcdIntervalSeconds, Math.Clamp(value, 1f, 30f)); }

    [Slider(20, 80, 1, SliderAttribute.SliderType.Integer, label: "LCD width (chars)", description: "Characters per line; match your LCD font size")]
    public int LcdWidth { get => lcdWidth; set => SetField(ref lcdWidth, Math.Clamp(value, 20, 80)); }

    [Checkbox(label: "Auto-style LCDs", description: "When an LCD gets a [Cargo...] tag or its name changes: set font size, top-left alignment and a monospace font (copied from any LCD where you set RobotoMono once)")]
    public bool LcdAutoStyle { get => lcdAutoStyle; set => SetField(ref lcdAutoStyle, value); }

    [Slider(4, 64, 1, SliderAttribute.SliderType.Float, label: "LCD font size", description: "Used by auto-style. Smaller size = more characters; set LCD width to match ([Cargo Ruler])")]
    public float LcdFontSize { get => lcdFontSize; set => SetField(ref lcdFontSize, Math.Clamp(value, 4f, 64f)); }

    [Slider(3, 40, 1, SliderAttribute.SliderType.Integer, label: "LCD max item lines", description: "Item rows on [Cargo Ore] style screens")]
    public int LcdMaxLines { get => lcdMaxLines; set => SetField(ref lcdMaxLines, Math.Clamp(value, 3, 40)); }

    [Slider(5, 240, 5, SliderAttribute.SliderType.Integer, label: "Show last delivery (min)", description: "How long the last delivery (+ column) stays on [Cargo Ore] style screens")]
    public int DeliveryShowMinutes { get => deliveryShowMinutes; set => SetField(ref deliveryShowMinutes, Math.Clamp(value, 5, 240)); }

    [Slider(0, 50, 0.5, SliderAttribute.SliderType.Float, label: "Low ore (t)", description: "Ores below this amount get \"(Low)\" on [Ore] screens. 0 = off")]
    public float LowOreTons { get => lowOreTons; set => SetField(ref lowOreTons, Math.Clamp(value, 0f, 50f)); }

    [Checkbox(label: "Show rates per minute", description: "Show +/- per minute next to items. Off by default: ore arrives in batches by ship.")]
    public bool LcdShowRates { get => lcdShowRates; set => SetField(ref lcdShowRates, value); }

    [Separator("Performance")]

    [Slider(0.5, 10, 0.5, SliderAttribute.SliderType.Float, label: "Unload interval (s)")]
    public float UnloadIntervalSeconds { get => unloadIntervalSeconds; set => SetField(ref unloadIntervalSeconds, Math.Clamp(value, 0.5f, 10f)); }

    [Slider(1, 60, 1, SliderAttribute.SliderType.Float, label: "Sort interval (s)")]
    public float SortIntervalSeconds { get => sortIntervalSeconds; set => SetField(ref sortIntervalSeconds, Math.Clamp(value, 1f, 60f)); }

    [Slider(2, 120, 1, SliderAttribute.SliderType.Float, label: "Tidy interval (s)")]
    public float TidyIntervalSeconds { get => tidyIntervalSeconds; set => SetField(ref tidyIntervalSeconds, Math.Clamp(value, 2f, 120f)); }

    [Slider(1, 1024, 1, SliderAttribute.SliderType.Integer, label: "Max transfers per pass")]
    public int MaxTransferAttempts { get => maxTransferAttempts; set => SetField(ref maxTransferAttempts, Math.Clamp(value, 1, 1024)); }

    [Slider(1, 4096, 1, SliderAttribute.SliderType.Integer, label: "Max item checks per pass")]
    public int MaxItemChecks { get => maxItemChecks; set => SetField(ref maxItemChecks, Math.Clamp(value, 1, 4096)); }

    [Slider(1, 100, 1, SliderAttribute.SliderType.Integer, label: "Time budget (ms)", description: "Soft time limit per pass")]
    public int SoftTimeBudgetMs { get => softTimeBudgetMs; set => SetField(ref softTimeBudgetMs, Math.Clamp(value, 1, 100)); }

    [Separator("Advanced")]

    [Textbox(label: "Block role overrides", description: "BlockKind GUID=Storage|Producer|Consumer, separated by ';'. GUIDs come from the discovery report.")]
    public string BlockRoleOverrides { get => blockRoleOverrides; set => SetField(ref blockRoleOverrides, value ?? ""); }

    [Button(label: "Write discovery report", description: "Writes block kinds, item types, connector and physics members to discovery.txt in the plugin data folder. Useful for bug reports and modded blocks")]
    public void WriteDiscoveryReport() => Game.Discovery.Requested = true;

    [Button(label: "Reset error stop", description: "Re-enable the plugin after it stopped itself because of an error")]
    public void ResetErrorStop() => Game.CargoEngine.ResetFailure();

    #endregion

    #region Derived values (not saved)

    [XmlIgnore]
    public HashSet<Category> UnloadAllKeepSet => Routing.ParseCategories(unloadAllKeeps);

    public RoutingOptions ToRoutingOptions() => new()
    {
        OverflowToEmpty = overflowToEmpty,
        AutoCategories = autoCategories,
        FallbackToUntaggedCargo = fallbackToUntaggedCargo,
        DrainProducerOutputs = drainProducerOutputs,
    };

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new();
    public static Config Current { get; } = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
