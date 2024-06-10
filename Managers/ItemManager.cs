#nullable enable
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Trader_1 = Trader;

// ReSharper disable All

namespace RuneLover.ItemManager;

[PublicAPI]
public enum CraftingTable
{
    Disabled,
    Inventory,
    [InternalName("piece_workbench")] Workbench,
    [InternalName("piece_cauldron")] Cauldron,
    [InternalName("forge")] Forge,
    [InternalName("piece_artisanstation")] ArtisanTable,
    [InternalName("piece_stonecutter")] StoneCutter,
    [InternalName("piece_magetable")] MageTable,
    [InternalName("blackforge")] BlackForge,
    Custom,
}

[PublicAPI]
public enum ConversionPiece
{
    Disabled,
    [InternalName("smelter")] Smelter,
    [InternalName("charcoal_kiln")] CharcoalKiln,
    [InternalName("blastfurnace")] BlastFurnace,
    [InternalName("windmill")] Windmill,
    [InternalName("piece_spinningwheel")] SpinningWheel,
    [InternalName("eitrrefinery")] EitrRefinery,
    Custom,
}

public class InternalName : Attribute
{
    public readonly string internalName;
    public InternalName(string internalName) => this.internalName = internalName;
}

[PublicAPI]
public class RequiredResourceList
{
    public readonly List<Requirement> Requirements = [];

    public bool
        Free; // If Requirements empty and Free is true, then it costs nothing. If Requirements empty and Free is false, then it won't be craftable.

    public void Add(string itemName, int amount, int quality = 0) =>
        Requirements.Add(new Requirement { itemName = itemName, amount = amount, quality = quality });

    public void Add(string itemName, ConfigEntry<int> amountConfig, int quality = 0) =>
        Requirements.Add(new Requirement { itemName = itemName, amountConfig = amountConfig, quality = quality });
}

[PublicAPI]
public class CraftingStationList
{
    public readonly List<CraftingStationConfig> Stations = [];

    public void Add(CraftingTable table, int level) =>
        Stations.Add(new CraftingStationConfig { Table = table, level = level });

    public void Add(string customTable, int level) =>
        Stations.Add(new CraftingStationConfig { Table = CraftingTable.Custom, level = level, custom = customTable });
}

[PublicAPI]
public class ItemRecipe
{
    public readonly RequiredResourceList RequiredItems = new();
    public readonly RequiredResourceList RequiredUpgradeItems = new();
    public readonly CraftingStationList Crafting = new();
    public int CraftAmount = 1;
    public bool RequireOnlyOneIngredient;
    public float QualityResultAmountMultiplier = 1;
    public ConfigEntryBase? RecipeIsActive;
}

[PublicAPI]
public class Trade
{
    public Trader Trader = Trader.None;
    public uint Price;
    public uint Stack = 1;
    public string? RequiredGlobalKey;
}

[PublicAPI]
[Flags]
public enum Trader
{
    None = 0,
    Haldor = 1 << 0,
    Hildir = 1 << 1,
}

public struct Requirement
{
    public string itemName;
    public int amount;
    public ConfigEntry<int>? amountConfig;

    [Description("Set to a non-zero value to apply the requirement only for a specific quality")]
    public int quality;
}

public struct CraftingStationConfig
{
    public CraftingTable Table;
    public int level;
    public string? custom;
}

[Flags]
public enum Configurability
{
    Disabled = 0,
    Recipe = 1,
    Stats = 2,
    Drop = 4,
    Trader = 8,
    Full = Recipe | Drop | Stats | Trader,
}

[PublicAPI]
public class DropTargets
{
    public readonly List<DropTarget> Drops = [];

    public void Add(string creatureName, float chance, int min = 1, int? max = null, bool levelMultiplier = true)
    {
        Drops.Add(new DropTarget
        {
            creature = creatureName, chance = chance, min = min, max = max ?? min, levelMultiplier = levelMultiplier
        });
    }
}

public struct DropTarget
{
    public string creature;
    public int min;
    public int max;
    public float chance;
    public bool levelMultiplier;
}

public enum Toggle
{
    On = 1,
    Off = 0,
}

[PublicAPI]
public class Item
{
    private class ItemConfig
    {
        public ConfigEntry<string>? craft;
        public ConfigEntry<string>? upgrade;
        public ConfigEntry<CraftingTable> table = null!;
        public ConfigEntry<int> tableLevel = null!;
        public ConfigEntry<string> customTable = null!;
        public ConfigEntry<int>? maximumTableLevel;
        public ConfigEntry<Toggle> requireOneIngredient = null!;
        public ConfigEntry<float> qualityResultAmountMultiplier = null!;
    }

    private class TraderConfig
    {
        public ConfigEntry<Trader> trader = null!;
        public ConfigEntry<uint> price = null!;
        public ConfigEntry<uint> stack = null!;
        public ConfigEntry<string> requiredGlobalKey = null!;
    }

    private class RequirementQuality
    {
        public int quality;
    }

    private static readonly List<Item> registeredItems = [];
    private static readonly Dictionary<ItemDrop, Item> itemDropMap = new();
    private static Dictionary<Item, Dictionary<string, List<Recipe>>> activeRecipes = new();
    private static Dictionary<Recipe, ConfigEntryBase?> hiddenCraftRecipes = new();
    private static Dictionary<Recipe, ConfigEntryBase?> hiddenUpgradeRecipes = new();
    private static Dictionary<Item, Dictionary<string, ItemConfig>> itemCraftConfigs = new();
    private static Dictionary<Item, ConfigEntry<string>> itemDropConfigs = new();
    private Dictionary<CharacterDrop, CharacterDrop.Drop> characterDrops = new();
    private readonly Dictionary<ConfigEntryBase, Action> statsConfigs = new();
    private static readonly ConditionalWeakTable<Piece.Requirement, RequirementQuality> requirementQuality = new();

    public static Configurability DefaultConfigurability = Configurability.Full;
    public Configurability? Configurable;
    private Configurability configurability => Configurable ?? DefaultConfigurability;
    private Configurability configurationVisible = Configurability.Full;

    private TraderConfig? traderConfig;

    public readonly GameObject Prefab;

    [Description(
        "Specifies the resources needed to craft the item.\nUse .Add to add resources with their internal ID and an amount.\nUse one .Add for each resource type the item should need.")]
    public RequiredResourceList RequiredItems => this[""].RequiredItems;

    [Description(
        "Specifies the resources needed to upgrade the item.\nUse .Add to add resources with their internal ID and an amount. This amount will be multipled by the item quality level.\nUse one .Add for each resource type the upgrade should need.")]
    public RequiredResourceList RequiredUpgradeItems => this[""].RequiredUpgradeItems;

    [Description(
        "Specifies the crafting station needed to craft the item.\nUse .Add to add a crafting station, using the CraftingTable enum and a minimum level for the crafting station.\nUse one .Add for each crafting station.")]
    public CraftingStationList Crafting => this[""].Crafting;

    [Description("Specifies a config entry which toggles whether a recipe is active.")]
    public ConfigEntryBase? RecipeIsActive
    {
        get => this[""].RecipeIsActive;
        set => this[""].RecipeIsActive = value;
    }

    [Description(
        "Specifies the number of items that should be given to the player with a single craft of the item.\nDefaults to 1.")]
    public int CraftAmount
    {
        get => this[""].CraftAmount;
        set => this[""].CraftAmount = value;
    }

    [Description(
        "Specifies the maximum required crafting station level to upgrade and repair the item.\nDefault is calculated from crafting station level and maximum quality.")]
    public int MaximumRequiredStationLevel = int.MaxValue;

    public bool RequireOnlyOneIngredient
    {
        get => this[""].RequireOnlyOneIngredient;
        set => this[""].RequireOnlyOneIngredient = value;
    }

    public float QualityResultAmountMultiplier
    {
        get => this[""].QualityResultAmountMultiplier;
        set => this[""].QualityResultAmountMultiplier = value;
    }

    [Description(
        "Assigns the item as a drop item to a creature.\nUses a creature name, a drop chance and a minimum and maximum amount.")]
    public readonly DropTargets DropsFrom = new();

    [Description(
        "Configures whether the item can be bought at the trader.\nDon't forget to set cost to something above 0 or the item will be sold for free.")]
    public readonly Trade Trade = new();

    internal List<Conversion> Conversions = [];
    internal List<Smelter.ItemConversion> conversions = [];
    public Dictionary<string, ItemRecipe> Recipes = new();

    public ItemRecipe this[string name]
    {
        get
        {
            if (Recipes.TryGetValue(name, out var recipe))
            {
                return recipe;
            }

            return Recipes[name] = new ItemRecipe();
        }
    }

    private LocalizeKey? _name;

    public LocalizeKey Name
    {
        get
        {
            if (_name is { } name)
            {
                return name;
            }

            var data = Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            if (data.m_name.StartsWith("$"))
            {
                _name = new LocalizeKey(data.m_name);
            }
            else
            {
                var key = "$item_" + Prefab.name.Replace(" ", "_");
                _name = new LocalizeKey(key).English(data.m_name);
                data.m_name = key;
            }

            return _name;
        }
    }

    private LocalizeKey? _description;

    public LocalizeKey Description
    {
        get
        {
            if (_description is { } description)
            {
                return description;
            }

            var data = Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            if (data.m_description.StartsWith("$"))
            {
                _description = new LocalizeKey(data.m_description);
            }
            else
            {
                var key = "$itemdesc_" + Prefab.name.Replace(" ", "_");
                _description = new LocalizeKey(key).English(data.m_description);
                data.m_description = key;
            }

            return _description;
        }
    }

    public Item(string assetBundleFileName, string prefabName, string folderName = "assets") : this(
        PrefabManager.RegisterAssetBundle(assetBundleFileName, folderName), prefabName)
    {
    }

    public Item(AssetBundle bundle, string prefabName) : this(PrefabManager.RegisterPrefab(bundle, prefabName, true),
        true)
    {
    }

    public Item(GameObject prefab, bool skipRegistering = false)
    {
        if (!skipRegistering)
        {
            PrefabManager.RegisterPrefab(prefab, true);
        }

        Prefab = prefab;
        registeredItems.Add(this);
        itemDropMap[Prefab.GetComponent<ItemDrop>()] = this;
        Prefab.GetComponent<ItemDrop>().m_itemData.m_dropPrefab = Prefab;
    }

    public void ToggleConfigurationVisibility(Configurability visible)
    {
        void Toggle(ConfigEntryBase cfg, Configurability check)
        {
            foreach (var tag in cfg.Description.Tags)
            {
                if (tag is ConfigurationManagerAttributes attrs)
                {
                    attrs.Browsable = (visible & check) != 0 && (attrs.browsability is null || attrs.browsability());
                }
            }
        }

        void ToggleObj(object obj, Configurability check)
        {
            foreach (var field in obj.GetType().GetFields())
            {
                if (field.GetValue(obj) is ConfigEntryBase cfg)
                {
                    Toggle(cfg, check);
                }
            }
        }

        configurationVisible = visible;
        if (itemDropConfigs.TryGetValue(this, out var dropCfg))
        {
            Toggle(dropCfg, Configurability.Drop);
        }

        if (itemCraftConfigs.TryGetValue(this, out var craftCfgs))
        {
            foreach (var craftCfg in craftCfgs.Values)
            {
                ToggleObj(craftCfg, Configurability.Recipe);
            }
        }

        foreach (var conversion in Conversions)
        {
            if (conversion.config is not null)
            {
                ToggleObj(conversion.config, Configurability.Recipe);
            }
        }

        foreach (var cfg in statsConfigs)
        {
            Toggle(cfg.Key, Configurability.Stats);
            if ((visible & Configurability.Stats) != 0)
            {
                cfg.Value();
            }
        }

        reloadConfigDisplay();
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order;
        [UsedImplicitly] public bool? Browsable;
        [UsedImplicitly] public string? Category;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        public Func<bool>? browsability;
    }

    [PublicAPI]
    public enum DamageModifier
    {
        Normal,
        Resistant,
        Weak,
        Immune,
        Ignore,
        VeryResistant,
        VeryWeak,
        None,
    }

    private static object? configManager;

    private delegate void setDmgFunc(ref HitData.DamageTypes dmg, float value);

    internal static void reloadConfigDisplay()
    {
        if (configManager?.GetType().GetProperty("DisplayingWindow")!.GetValue(configManager) is true)
        {
            configManager.GetType().GetMethod("BuildSettingList")!.Invoke(configManager, []);
        }
    }

    private void UpdateItemTableConfig(string recipeKey, CraftingTable table, string customTableValue)
    {
        if (activeRecipes.ContainsKey(this) && activeRecipes[this].TryGetValue(recipeKey, out var recipes))
        {
            recipes.First().m_enabled = table != CraftingTable.Disabled;

            if (table is CraftingTable.Inventory or CraftingTable.Disabled)
            {
                recipes.First().m_craftingStation = null;
            }
            else if (table is CraftingTable.Custom)
            {
                recipes.First().m_craftingStation =
                    ZNetScene.instance.GetPrefab(customTableValue)?.GetComponent<CraftingStation>();
            }
            else
            {
                recipes.First().m_craftingStation = ZNetScene.instance.GetPrefab(getInternalName(table))
                    .GetComponent<CraftingStation>();
            }
        }
    }

    private void UpdateCraftConfig(string recipeKey, SerializedRequirements craftRequirements,
        SerializedRequirements upgradeRequirements)
    {
        if (ObjectDB.instance && activeRecipes.ContainsKey(this)
                              && activeRecipes[this].TryGetValue(recipeKey, out var recipes))
        {
            foreach (var recipe in recipes)
            {
                recipe.m_resources =
                    SerializedRequirements.toPieceReqs(ObjectDB.instance, craftRequirements, upgradeRequirements);
            }
        }
    }

    internal static void Patch_FejdStartup()
    {
        var bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");

        var configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
        if (DefaultConfigurability != Configurability.Disabled)
        {
            var SaveOnConfigSet = plugin.Config.SaveOnConfigSet;
            plugin.Config.SaveOnConfigSet = false;

            foreach (var item in registeredItems.Where(i => i.configurability != Configurability.Disabled))
            {
                var nameKey = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                var englishName = new Regex("""['\["\]]""").Replace(english.Localize(nameKey), "").Trim();
                var localizedName = Localization.instance.Localize(nameKey).Trim();

                var order = 0;

                if ((item.configurability & Configurability.Recipe) != 0)
                {
                    itemCraftConfigs[item] = new Dictionary<string, ItemConfig>();
                    foreach (var configKey in item.Recipes.Keys.DefaultIfEmpty(""))
                    {
                        var configSuffix = configKey == "" ? "" : $" ({configKey})";

                        if (item.Recipes.ContainsKey(configKey) && item.Recipes[configKey].Crafting.Stations.Count > 0)
                        {
                            var cfg = itemCraftConfigs[item][configKey] = new ItemConfig();

                            List<ConfigurationManagerAttributes> hideWhenNoneAttributes = [];

                            cfg.table = config(englishName, "Crafting Station" + configSuffix,
                                item.Recipes[configKey].Crafting.Stations.First().Table,
                                new ConfigDescription($"Crafting station where {englishName} is available.", null,
                                    new ConfigurationManagerAttributes
                                    {
                                        Order = --order,
                                        Browsable = (item.configurationVisible & Configurability.Recipe) != 0,
                                        Category = localizedName
                                    }));
                            bool CustomTableBrowsability() => cfg.table.Value == CraftingTable.Custom;
                            ConfigurationManagerAttributes customTableAttributes = new()
                            {
                                Order = --order, browsability = CustomTableBrowsability,
                                Browsable = CustomTableBrowsability()
                                            && (item.configurationVisible & Configurability.Recipe) != 0,
                                Category = localizedName
                            };
                            cfg.customTable = config(englishName, "Custom Crafting Station" + configSuffix,
                                item.Recipes[configKey].Crafting.Stations.First().custom ?? "",
                                new ConfigDescription("", null, customTableAttributes));

                            void TableConfigChanged(object o, EventArgs e)
                            {
                                item.UpdateItemTableConfig(configKey, cfg.table.Value, cfg.customTable.Value);
                                customTableAttributes.Browsable = cfg.table.Value == CraftingTable.Custom;
                                foreach (var attributes in hideWhenNoneAttributes)
                                {
                                    attributes.Browsable = cfg.table.Value != CraftingTable.Disabled;
                                }

                                reloadConfigDisplay();
                            }

                            cfg.table.SettingChanged += TableConfigChanged;
                            cfg.customTable.SettingChanged += TableConfigChanged;

                            bool TableLevelBrowsability() => cfg.table.Value != CraftingTable.Disabled;
                            ConfigurationManagerAttributes tableLevelAttributes = new()
                            {
                                Order = --order, browsability = TableLevelBrowsability,
                                Browsable = TableLevelBrowsability()
                                            && (item.configurationVisible & Configurability.Recipe) != 0,
                                Category = localizedName
                            };
                            hideWhenNoneAttributes.Add(tableLevelAttributes);
                            cfg.tableLevel = config(englishName, "Crafting Station Level" + configSuffix,
                                item.Recipes[configKey].Crafting.Stations.First().level,
                                new ConfigDescription($"Required crafting station level to craft {englishName}.", null,
                                    tableLevelAttributes));
                            cfg.tableLevel.SettingChanged += (_, _) =>
                            {
                                if (activeRecipes.ContainsKey(item)
                                    && activeRecipes[item].TryGetValue(configKey, out var recipes))
                                {
                                    recipes.First().m_minStationLevel = cfg.tableLevel.Value;
                                }
                            };
                            if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1)
                            {
                                cfg.maximumTableLevel = config(englishName,
                                    "Maximum Crafting Station Level" + configSuffix,
                                    item.MaximumRequiredStationLevel == int.MaxValue
                                        ? item.Recipes[configKey].Crafting.Stations.First().level
                                        + item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality - 1
                                        : item.MaximumRequiredStationLevel,
                                    new ConfigDescription(
                                        $"Maximum crafting station level to upgrade and repair {englishName}.", null,
                                        tableLevelAttributes));
                            }

                            bool QualityResultBrowsability() => cfg.requireOneIngredient.Value == Toggle.On;
                            cfg.requireOneIngredient = config(englishName, "Require only one resource" + configSuffix,
                                item.Recipes[configKey].RequireOnlyOneIngredient ? Toggle.On : Toggle.Off,
                                new ConfigDescription(
                                    $"Whether only one of the ingredients is needed to craft {englishName}", null,
                                    new ConfigurationManagerAttributes { Order = --order, Category = localizedName }));
                            ConfigurationManagerAttributes qualityResultAttributes = new()
                            {
                                Order = --order, browsability = QualityResultBrowsability,
                                Browsable = QualityResultBrowsability()
                                            && (item.configurationVisible & Configurability.Recipe) != 0,
                                Category = localizedName
                            };
                            cfg.requireOneIngredient.SettingChanged += (_, _) =>
                            {
                                if (activeRecipes.ContainsKey(item)
                                    && activeRecipes[item].TryGetValue(configKey, out var recipes))
                                {
                                    foreach (var recipe in recipes)
                                    {
                                        recipe.m_requireOnlyOneIngredient = cfg.requireOneIngredient.Value == Toggle.On;
                                    }
                                }

                                qualityResultAttributes.Browsable = QualityResultBrowsability();
                                reloadConfigDisplay();
                            };
                            cfg.qualityResultAmountMultiplier = config(englishName, "Quality Multiplier" + configSuffix,
                                item.Recipes[configKey].QualityResultAmountMultiplier,
                                new ConfigDescription(
                                    $"Multiplies the crafted amount based on the quality of the resources when crafting {englishName}. Only works, if Require Only One Resource is true.",
                                    null, qualityResultAttributes));
                            cfg.qualityResultAmountMultiplier.SettingChanged += (_, _) =>
                            {
                                if (activeRecipes.ContainsKey(item)
                                    && activeRecipes[item].TryGetValue(configKey, out var recipes))
                                {
                                    foreach (var recipe in recipes)
                                    {
                                        recipe.m_qualityResultAmountMultiplier =
                                            cfg.qualityResultAmountMultiplier.Value;
                                    }
                                }
                            };

                            ConfigEntry<string> itemConfig(string name, string value, string desc, bool isUpgrade)
                            {
                                bool ItemBrowsability() => cfg.table.Value != CraftingTable.Disabled;
                                ConfigurationManagerAttributes attributes = new()
                                {
                                    CustomDrawer = drawRequirementsConfigTable(item, isUpgrade), Order = --order,
                                    browsability = ItemBrowsability,
                                    Browsable = ItemBrowsability()
                                                && (item.configurationVisible & Configurability.Recipe) != 0,
                                    Category = localizedName
                                };
                                hideWhenNoneAttributes.Add(attributes);
                                return config(englishName, name, value, new ConfigDescription(desc, null, attributes));
                            }

                            if ((!item.Recipes[configKey].RequiredItems.Free
                                 || item.Recipes[configKey].RequiredItems.Requirements.Count > 0) && item
                                    .Recipes[configKey].RequiredItems.Requirements.All(r => r.amountConfig is null))
                            {
                                cfg.craft = itemConfig("Crafting Costs" + configSuffix,
                                    new SerializedRequirements(item.Recipes[configKey].RequiredItems.Requirements)
                                        .ToString(), $"Item costs to craft {englishName}", false);
                            }

                            if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1
                                && (!item.Recipes[configKey].RequiredUpgradeItems.Free
                                    || item.Recipes[configKey].RequiredUpgradeItems.Requirements.Count > 0)
                                && item.Recipes[configKey].RequiredUpgradeItems.Requirements
                                    .All(r => r.amountConfig is null))
                            {
                                cfg.upgrade = itemConfig("Upgrading Costs" + configSuffix,
                                    new SerializedRequirements(
                                        item.Recipes[configKey].RequiredUpgradeItems.Requirements).ToString(),
                                    $"Item costs per level to upgrade {englishName}", true);
                            }

                            void ConfigChanged(object o, EventArgs e)
                            {
                                item.UpdateCraftConfig(configKey, new SerializedRequirements(cfg.craft?.Value ?? ""),
                                    new SerializedRequirements(cfg.upgrade?.Value ?? ""));
                            }

                            if (cfg.craft != null)
                            {
                                cfg.craft.SettingChanged += ConfigChanged;
                            }

                            if (cfg.upgrade != null)
                            {
                                cfg.upgrade.SettingChanged += ConfigChanged;
                            }
                        }
                    }

                    if ((item.configurability & Configurability.Drop) != 0)
                    {
                        var dropConfig = itemDropConfigs[item] = config(englishName, "Drops from",
                            new SerializedDrop(item.DropsFrom.Drops).ToString(),
                            new ConfigDescription($"{englishName} drops from this creature.", null,
                                new ConfigurationManagerAttributes
                                {
                                    CustomDrawer = drawDropsConfigTable, Category = localizedName,
                                    Browsable = (item.configurationVisible & Configurability.Drop) != 0
                                }));
                        dropConfig.SettingChanged += (_, _) => item.UpdateCharacterDrop();
                    }

                    for (var i = 0; i < item.Conversions.Count; ++i)
                    {
                        var prefix = item.Conversions.Count > 1 ? $"{i + 1}. " : "";
                        var conversion = item.Conversions[i];
                        conversion.config = new Conversion.ConversionConfig();
                        var index = i;

                        void UpdatePiece()
                        {
                            if (index >= item.conversions.Count || !ZNetScene.instance)
                            {
                                return;
                            }

                            var newPieceName = conversion.config.piece.Value is not ConversionPiece.Disabled
                                ? conversion.config.piece.Value == ConversionPiece.Custom
                                    ? conversion.config.customPiece.Value
                                    : getInternalName(conversion.config.piece.Value)
                                : null;
                            var activePiece = conversion.config.activePiece;
                            if (conversion.config.activePiece is not null)
                            {
                                var smelter = ZNetScene.instance.GetPrefab(conversion.config.activePiece)
                                    .GetComponent<Smelter>();
                                var removeIndex = smelter.m_conversion.IndexOf(item.conversions[index]);
                                if (removeIndex >= 0)
                                {
                                    foreach (var instantiatedSmelter in Resources.FindObjectsOfTypeAll<Smelter>())
                                    {
                                        if (Utils.GetPrefabName(instantiatedSmelter.gameObject) == activePiece)
                                        {
                                            instantiatedSmelter.m_conversion.RemoveAt(removeIndex);
                                        }
                                    }
                                }

                                conversion.config.activePiece = null;
                            }

                            if (item.conversions[index].m_from is not null
                                && conversion.config.piece.Value is not ConversionPiece.Disabled)
                            {
                                if (ZNetScene.instance.GetPrefab(newPieceName)?.GetComponent<Smelter>() is not null)
                                {
                                    conversion.config.activePiece = newPieceName;
                                    foreach (var instantiatedSmelter in Resources.FindObjectsOfTypeAll<Smelter>())
                                    {
                                        if (Utils.GetPrefabName(instantiatedSmelter.gameObject) == newPieceName)
                                        {
                                            instantiatedSmelter.m_conversion.Add(item.conversions[index]);
                                        }
                                    }
                                }
                            }
                        }

                        conversion.config.input = config(englishName, $"{prefix}Conversion Input Item",
                            conversion.Input,
                            new ConfigDescription($"Input item to create {englishName}", null,
                                new ConfigurationManagerAttributes
                                {
                                    Category = localizedName,
                                    Browsable = (item.configurationVisible & Configurability.Recipe) != 0
                                }));
                        conversion.config.input.SettingChanged += (_, _) =>
                        {
                            if (index < item.conversions.Count && ObjectDB.instance is { } objectDB)
                            {
                                var inputItem =
                                    SerializedRequirements.fetchByName(objectDB, conversion.config.input.Value);
                                item.conversions[index].m_from = inputItem;
                                UpdatePiece();
                            }
                        };
                        conversion.config.piece = config(englishName, $"{prefix}Conversion Piece", conversion.Piece,
                            new ConfigDescription($"Conversion piece used to create {englishName}", null,
                                new ConfigurationManagerAttributes
                                {
                                    Category = localizedName,
                                    Browsable = (item.configurationVisible & Configurability.Recipe) != 0
                                }));
                        conversion.config.piece.SettingChanged += (_, _) => UpdatePiece();
                        conversion.config.customPiece = config(englishName, $"{prefix}Conversion Custom Piece",
                            conversion.customPiece ?? "",
                            new ConfigDescription($"Custom conversion piece to create {englishName}", null,
                                new ConfigurationManagerAttributes
                                {
                                    Category = localizedName,
                                    Browsable = (item.configurationVisible & Configurability.Recipe) != 0
                                }));
                        conversion.config.customPiece.SettingChanged += (_, _) => UpdatePiece();
                    }
                }

                if ((item.configurability & Configurability.Stats) != 0)
                {
                    item.statsConfigs.Clear();

                    void statcfg<T>(string configName, string description,
                        Func<ItemData.SharedData, T> readDefault,
                        Action<ItemData.SharedData, T> setValue)
                    {
                        var shared = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                        var cfg = config(englishName, configName, readDefault(shared),
                            new ConfigDescription(description, null,
                                new ConfigurationManagerAttributes
                                {
                                    Category = localizedName,
                                    Browsable = (item.configurationVisible & Configurability.Stats) != 0
                                }));
                        if ((item.configurationVisible & Configurability.Stats) != 0)
                        {
                            setValue(shared, cfg.Value);
                        }

                        void ApplyConfig() => item.ApplyToAllInstances(item => setValue(item.m_shared, cfg.Value));

                        item.statsConfigs.Add(cfg, ApplyConfig);

                        cfg.SettingChanged += (_, _) =>
                        {
                            if ((item.configurationVisible & Configurability.Stats) != 0)
                            {
                                ApplyConfig();
                            }
                        };
                    }

                    var shared = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                    var itemType = shared.m_itemType;

                    statcfg("Weight", $"Weight of {englishName}.", shared => shared.m_weight,
                        (shared, value) => shared.m_weight = value);
                    statcfg("Trader Value", $"Trader value of {englishName}.", shared => shared.m_value,
                        (shared, value) => shared.m_value = value);

                    if (itemType is ItemType.Bow or ItemType.Chest
                        or ItemType.Hands or ItemType.Helmet
                        or ItemType.Legs or ItemType.Shield
                        or ItemType.Shoulder or ItemType.Tool
                        or ItemType.OneHandedWeapon or ItemType.TwoHandedWeapon
                        or ItemType.TwoHandedWeaponLeft)
                    {
                        statcfg("Durability", $"Durability of {englishName}.", shared => shared.m_maxDurability,
                            (shared, value) => shared.m_maxDurability = value);
                        statcfg("Durability per Level", $"Durability gain per level of {englishName}.",
                            shared => shared.m_durabilityPerLevel,
                            (shared, value) => shared.m_durabilityPerLevel = value);
                        statcfg("Movement Speed Modifier", $"Movement speed modifier of {englishName}.",
                            shared => shared.m_movementModifier, (shared, value) => shared.m_movementModifier = value);
                    }

                    if (itemType is ItemType.Bow or ItemType.Shield
                        or ItemType.OneHandedWeapon or ItemType.TwoHandedWeapon
                        or ItemType.TwoHandedWeaponLeft)
                    {
                        statcfg("Block Armor", $"Block armor of {englishName}.", shared => shared.m_blockPower,
                            (shared, value) => shared.m_blockPower = value);
                        statcfg("Block Armor per Level", $"Block armor per level for {englishName}.",
                            shared => shared.m_blockPowerPerLevel,
                            (shared, value) => shared.m_blockPowerPerLevel = value);
                        statcfg("Block Force", $"Block force of {englishName}.", shared => shared.m_deflectionForce,
                            (shared, value) => shared.m_deflectionForce = value);
                        statcfg("Block Force per Level", $"Block force per level for {englishName}.",
                            shared => shared.m_deflectionForcePerLevel,
                            (shared, value) => shared.m_deflectionForcePerLevel = value);
                        statcfg("Parry Bonus", $"Parry bonus of {englishName}.", shared => shared.m_timedBlockBonus,
                            (shared, value) => shared.m_timedBlockBonus = value);
                    }
                    else if (itemType is ItemType.Chest or ItemType.Hands
                             or ItemType.Helmet or ItemType.Legs
                             or ItemType.Shoulder)
                    {
                        statcfg("Armor", $"Armor of {englishName}.", shared => shared.m_armor,
                            (shared, value) => shared.m_armor = value);
                        statcfg("Armor per Level", $"Armor per level for {englishName}.",
                            shared => shared.m_armorPerLevel, (shared, value) => shared.m_armorPerLevel = value);
                    }

                    if (shared.m_skillType is SkillType.Axes or SkillType.Pickaxes)
                    {
                        statcfg("Tool tier", $"Tool tier of {englishName}.", shared => shared.m_toolTier,
                            (shared, value) => shared.m_toolTier = value);
                    }

                    if (itemType is ItemType.Shield or ItemType.Chest
                        or ItemType.Hands or ItemType.Helmet
                        or ItemType.Legs or ItemType.Shoulder)
                    {
                        var modifiers =
                            shared.m_damageModifiers.ToDictionary(d => d.m_type,
                                d => (DamageModifier)(int)d.m_modifier);
                        foreach (var damageType in
                                 ((HitData.DamageType[])Enum.GetValues(typeof(HitData.DamageType))).Except(new[]
                                 {
                                     HitData.DamageType.Chop, HitData.DamageType.Pickaxe, HitData.DamageType.Spirit,
                                     HitData.DamageType.Physical, HitData.DamageType.Elemental
                                 }))
                        {
                            statcfg($"{damageType.ToString()} Resistance",
                                $"{damageType.ToString()} resistance of {englishName}.",
                                _ => modifiers.TryGetValue(damageType, out var modifier)
                                    ? modifier
                                    : DamageModifier.None, (shared, value) =>
                                {
                                    HitData.DamageModPair modifier = new()
                                        { m_type = damageType, m_modifier = (HitData.DamageModifier)(int)value };
                                    for (var i = 0; i < shared.m_damageModifiers.Count; ++i)
                                    {
                                        if (shared.m_damageModifiers[i].m_type == damageType)
                                        {
                                            if (value == DamageModifier.None)
                                            {
                                                shared.m_damageModifiers.RemoveAt(i);
                                            }
                                            else
                                            {
                                                shared.m_damageModifiers[i] = modifier;
                                            }

                                            return;
                                        }
                                    }

                                    if (value != DamageModifier.None)
                                    {
                                        shared.m_damageModifiers.Add(modifier);
                                    }
                                });
                        }
                    }

                    if (itemType is ItemType.Consumable && shared.m_food > 0)
                    {
                        statcfg("Health", $"Health value of {englishName}.", shared => shared.m_food,
                            (shared, value) => shared.m_food = value);
                        statcfg("Stamina", $"Stamina value of {englishName}.", shared => shared.m_foodStamina,
                            (shared, value) => shared.m_foodStamina = value);
                        statcfg("Eitr", $"Eitr value of {englishName}.", shared => shared.m_foodEitr,
                            (shared, value) => shared.m_foodEitr = value);
                        statcfg("Duration", $"Duration of {englishName}.", shared => shared.m_foodBurnTime,
                            (shared, value) => shared.m_foodBurnTime = value);
                        statcfg("Health Regen", $"Health regen value of {englishName}.", shared => shared.m_foodRegen,
                            (shared, value) => shared.m_foodRegen = value);
                    }

                    if (shared.m_skillType is SkillType.BloodMagic)
                    {
                        statcfg("Health Cost", $"Health cost of {englishName}.",
                            shared => shared.m_attack.m_attackHealth,
                            (shared, value) => shared.m_attack.m_attackHealth = value);
                        statcfg("Health Cost Percentage", $"Health cost percentage of {englishName}.",
                            shared => shared.m_attack.m_attackHealthPercentage,
                            (shared, value) => shared.m_attack.m_attackHealthPercentage = value);
                    }

                    if (shared.m_skillType is SkillType.BloodMagic or SkillType.ElementalMagic)
                    {
                        statcfg("Eitr Cost", $"Eitr cost of {englishName}.", shared => shared.m_attack.m_attackEitr,
                            (shared, value) => shared.m_attack.m_attackEitr = value);
                    }

                    if (itemType is ItemType.OneHandedWeapon
                        or ItemType.TwoHandedWeapon or ItemType.TwoHandedWeaponLeft
                        or ItemType.Bow)
                    {
                        statcfg("Knockback", $"Knockback of {englishName}.", shared => shared.m_attackForce,
                            (shared, value) => shared.m_attackForce = value);
                        statcfg("Backstab Bonus", $"Backstab bonus of {englishName}.", shared => shared.m_backstabBonus,
                            (shared, value) => shared.m_backstabBonus = value);
                        statcfg("Attack Stamina", $"Attack stamina of {englishName}.",
                            shared => shared.m_attack.m_attackStamina,
                            (shared, value) => shared.m_attack.m_attackStamina = value);

                        void SetDmg(string dmgType, Func<HitData.DamageTypes, float> readDmg, setDmgFunc setDmg)
                        {
                            statcfg($"{dmgType} Damage", $"{dmgType} damage dealt by {englishName}.",
                                shared => readDmg(shared.m_damages),
                                (shared, val) => setDmg(ref shared.m_damages, val));
                            statcfg($"{dmgType} Damage Per Level",
                                $"{dmgType} damage dealt increase per level for {englishName}.",
                                shared => readDmg(shared.m_damagesPerLevel),
                                (shared, val) => setDmg(ref shared.m_damagesPerLevel, val));
                        }

                        SetDmg("True", dmg => dmg.m_damage,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_damage = val);
                        SetDmg("Slash", dmg => dmg.m_slash,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_slash = val);
                        SetDmg("Pierce", dmg => dmg.m_pierce,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_pierce = val);
                        SetDmg("Blunt", dmg => dmg.m_blunt,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_blunt = val);
                        SetDmg("Chop", dmg => dmg.m_chop, (ref HitData.DamageTypes dmg, float val) => dmg.m_chop = val);
                        SetDmg("Pickaxe", dmg => dmg.m_pickaxe,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_pickaxe = val);
                        SetDmg("Fire", dmg => dmg.m_fire, (ref HitData.DamageTypes dmg, float val) => dmg.m_fire = val);
                        SetDmg("Poison", dmg => dmg.m_poison,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_poison = val);
                        SetDmg("Frost", dmg => dmg.m_frost,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_frost = val);
                        SetDmg("Lightning", dmg => dmg.m_lightning,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_lightning = val);
                        SetDmg("Spirit", dmg => dmg.m_spirit,
                            (ref HitData.DamageTypes dmg, float val) => dmg.m_spirit = val);

                        if (itemType is ItemType.Bow)
                        {
                            statcfg("Projectiles", $"Number of projectiles that {englishName} shoots at once.",
                                shared => shared.m_attack.m_projectileBursts,
                                (shared, value) => shared.m_attack.m_projectileBursts = value);
                            statcfg("Burst Interval", $"Time between the projectiles {englishName} shoots at once.",
                                shared => shared.m_attack.m_burstInterval,
                                (shared, value) => shared.m_attack.m_burstInterval = value);
                            statcfg("Minimum Accuracy", $"Minimum accuracy for {englishName}.",
                                shared => shared.m_attack.m_projectileAccuracyMin,
                                (shared, value) => shared.m_attack.m_projectileAccuracyMin = value);
                            statcfg("Accuracy", $"Accuracy for {englishName}.",
                                shared => shared.m_attack.m_projectileAccuracy,
                                (shared, value) => shared.m_attack.m_projectileAccuracy = value);
                            statcfg("Minimum Velocity", $"Minimum velocity for {englishName}.",
                                shared => shared.m_attack.m_projectileVelMin,
                                (shared, value) => shared.m_attack.m_projectileVelMin = value);
                            statcfg("Velocity", $"Velocity for {englishName}.",
                                shared => shared.m_attack.m_projectileVel,
                                (shared, value) => shared.m_attack.m_projectileVel = value);
                            statcfg("Maximum Draw Time", $"Time until {englishName} is fully drawn at skill level 0.",
                                shared => shared.m_attack.m_drawDurationMin,
                                (shared, value) => shared.m_attack.m_drawDurationMin = value);
                            statcfg("Stamina Drain", $"Stamina drain per second while drawing {englishName}.",
                                shared => shared.m_attack.m_drawStaminaDrain,
                                (shared, value) => shared.m_attack.m_drawStaminaDrain = value);
                        }
                    }
                }

                if ((item.configurability & Configurability.Trader) != 0)
                {
                    List<ConfigurationManagerAttributes> traderAttributes = [];
                    bool TraderBrowsability() => item.traderConfig.trader.Value != 0;

                    item.traderConfig = new TraderConfig
                    {
                        trader = config(englishName, "Trader Selling", item.Trade.Trader,
                            new ConfigDescription($"Which traders sell {englishName}.", null,
                                new ConfigurationManagerAttributes
                                {
                                    Order = --order,
                                    Browsable = (item.configurationVisible & Configurability.Trader) != 0,
                                    Category = localizedName
                                })),
                    };
                    item.traderConfig.trader.SettingChanged += (_, _) =>
                    {
                        item.ReloadTraderConfiguration();
                        foreach (var attributes in traderAttributes)
                        {
                            attributes.Browsable = TraderBrowsability();
                        }

                        reloadConfigDisplay();
                    };

                    ConfigEntry<T> traderConfig<T>(string name, T value, string desc)
                    {
                        ConfigurationManagerAttributes attributes = new()
                        {
                            Order = --order, browsability = TraderBrowsability,
                            Browsable = TraderBrowsability()
                                        && (item.configurationVisible & Configurability.Trader) != 0,
                            Category = localizedName
                        };
                        traderAttributes.Add(attributes);
                        var cfg = config(englishName, name, value,
                            new ConfigDescription(desc, null, attributes));
                        cfg.SettingChanged += (_, _) => item.ReloadTraderConfiguration();
                        return cfg;
                    }

                    item.traderConfig.price = traderConfig("Trader Price", item.Trade.Price,
                        $"Price of {englishName} at the trader.");
                    item.traderConfig.stack = traderConfig("Trader Stack", item.Trade.Stack,
                        $"Stack size of {englishName} in the trader. Also known as the number of items sold by a trader in one transaction.");
                    item.traderConfig.requiredGlobalKey = traderConfig("Trader Required Global Key",
                        item.Trade.RequiredGlobalKey ?? "",
                        $"Required global key to unlock {englishName} at the trader.");

                    if (item.traderConfig.trader.Value != 0)
                    {
                        PrefabManager.AddItemToTrader(item.Prefab, item.traderConfig.trader.Value,
                            item.traderConfig.price.Value, item.traderConfig.stack.Value,
                            item.traderConfig.requiredGlobalKey.Value);
                    }
                }
                else if (item.Trade.Trader != 0)
                {
                    PrefabManager.AddItemToTrader(item.Prefab, item.Trade.Trader, item.Trade.Price, item.Trade.Stack,
                        item.Trade.RequiredGlobalKey);
                }
            }

            if (SaveOnConfigSet)
            {
                plugin.Config.SaveOnConfigSet = true;
                plugin.Config.Save();
            }
        }

        configManager = configManagerType == null
            ? null
            : Chainloader.ManagerObject.GetComponent(configManagerType);

        foreach (var item in registeredItems)
        {
            foreach (var kv in item.Recipes)
            {
                foreach (var resourceList in new[]
                             { kv.Value.RequiredItems, kv.Value.RequiredUpgradeItems })
                {
                    for (var i = 0; i < resourceList.Requirements.Count; ++i)
                    {
                        if ((item.configurability & Configurability.Recipe) != 0
                            && resourceList.Requirements[i].amountConfig is { } amountCfg)
                        {
                            var resourceIndex = i;

                            void ConfigChanged(object o, EventArgs e)
                            {
                                if (ObjectDB.instance && activeRecipes.ContainsKey(item)
                                                      && activeRecipes[item]
                                                          .TryGetValue(kv.Key, out var recipes))
                                {
                                    foreach (var recipe in recipes)
                                    {
                                        recipe.m_resources[resourceIndex].m_amount = amountCfg.Value;
                                    }
                                }
                            }

                            amountCfg.SettingChanged += ConfigChanged;
                        }
                    }
                }
            }

            item.InitializeNewRegisteredItem();
        }
    }

    private void InitializeNewRegisteredItem()
    {
        foreach (var kv in Recipes)
        {
            if (kv.Value.RecipeIsActive is { } enabledCfg)
            {
                void ConfigChanged(object o, EventArgs e)
                {
                    if (ObjectDB.instance && activeRecipes.ContainsKey(this)
                                          && activeRecipes[this].TryGetValue(kv.Key, out var recipes))
                    {
                        foreach (var recipe in recipes)
                        {
                            recipe.m_enabled = (int)enabledCfg.BoxedValue != 0;
                        }
                    }
                }

                enabledCfg.GetType().GetEvent(nameof(ConfigEntry<int>.SettingChanged))
                    .AddEventHandler(enabledCfg, new EventHandler(ConfigChanged));
            }
        }
    }

    public void ReloadCraftingConfiguration()
    {
        if (ObjectDB.instance && ObjectDB.instance.GetItemPrefab(Prefab.name.GetStableHashCode()) is null)
        {
            registerRecipesInObjectDB(ObjectDB.instance);
            ObjectDB.instance.m_items.Add(Prefab);
            ObjectDB.instance.m_itemByHash.Add(Prefab.name.GetStableHashCode(), Prefab);
            ZNetScene.instance.m_prefabs.Add(Prefab);
            ZNetScene.instance.m_namedPrefabs.Add(Prefab.name.GetStableHashCode(), Prefab);
        }

        foreach (var configKey in Recipes.Keys.DefaultIfEmpty(""))
        {
            if (Recipes.TryGetValue(configKey, out var recipe) && recipe.Crafting.Stations.Count > 0)
            {
                UpdateItemTableConfig(configKey, recipe.Crafting.Stations.First().Table,
                    recipe.Crafting.Stations.First().custom ?? "");
                UpdateCraftConfig(configKey, new SerializedRequirements(recipe.RequiredItems.Requirements),
                    new SerializedRequirements(recipe.RequiredUpgradeItems.Requirements));
                // To be extended as needed
            }
        }
    }

    private void ReloadTraderConfiguration()
    {
        if (traderConfig!.trader.Value == 0)
        {
            PrefabManager.RemoveItemFromTrader(Prefab);
        }
        else
        {
            PrefabManager.AddItemToTrader(Prefab, traderConfig.trader.Value, traderConfig.price.Value,
                traderConfig.stack.Value, traderConfig.requiredGlobalKey.Value);
        }
    }

    public static void ApplyToAllInstances(GameObject prefab, Action<ItemData> callback)
    {
        callback(prefab.GetComponent<ItemDrop>().m_itemData);

        var itemName = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

        var inventories = s_players.Select(p => p.GetInventory())
            .Concat(FindObjectsOfType<Container>().Select(c => c.GetInventory()))
            .Where(c => c is not null).ToArray();
        foreach (var itemdata in ObjectDB.instance.m_items.Select(p => p.GetComponent<ItemDrop>())
                     .Where(c => c && c.GetComponent<ZNetView>()).Concat(s_instances).Select(i => i.m_itemData)
                     .Concat(inventories.SelectMany(i => i.GetAllItems())))
        {
            if (itemdata.m_shared.m_name == itemName)
            {
                callback(itemdata);
            }
        }
    }

    public void ApplyToAllInstances(Action<ItemData> callback) => ApplyToAllInstances(Prefab, callback);

    private static string getInternalName<T>(T value) where T : struct =>
        ((InternalName)typeof(T).GetMember(value.ToString())[0].GetCustomAttributes(typeof(InternalName)).First())
        .internalName;

    private void registerRecipesInObjectDB(ObjectDB objectDB)
    {
        activeRecipes[this] = new Dictionary<string, List<Recipe>>();

        itemCraftConfigs.TryGetValue(this, out var cfgs);
        foreach (var kv in Recipes)
        {
            List<Recipe> recipes = [];

            foreach (var station in kv.Value.Crafting.Stations)
            {
                var cfg = cfgs?[kv.Key];

                var recipe = CreateInstance<Recipe>();
                recipe.name = $"{Prefab.name}_Recipe_{station.Table.ToString()}";
                recipe.m_amount = kv.Value.CraftAmount;
                recipe.m_enabled = cfg is null
                    ? (int)(kv.Value.RecipeIsActive?.BoxedValue ?? 1) != 0
                    : cfg.table.Value != CraftingTable.Disabled;
                recipe.m_item = Prefab.GetComponent<ItemDrop>();
                recipe.m_resources = SerializedRequirements.toPieceReqs(objectDB,
                    cfg?.craft == null
                        ? new SerializedRequirements(kv.Value.RequiredItems.Requirements)
                        : new SerializedRequirements(cfg.craft.Value),
                    cfg?.upgrade == null
                        ? new SerializedRequirements(kv.Value.RequiredUpgradeItems.Requirements)
                        : new SerializedRequirements(cfg.upgrade.Value));
                if ((cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value) is CraftingTable.Inventory
                    or CraftingTable.Disabled)
                {
                    recipe.m_craftingStation = null;
                }
                else if ((cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value) is CraftingTable.Custom)
                {
                    if (ZNetScene.instance.GetPrefab(cfg == null || recipes.Count > 0
                            ? station.custom
                            : cfg.customTable.Value) is { } craftingTable)
                    {
                        recipe.m_craftingStation = craftingTable.GetComponent<CraftingStation>();
                    }
                    else
                    {
                        DebugWarning(
                            $"Custom crafting station '{(cfg == null || recipes.Count > 0 ? station.custom : cfg.customTable.Value)}' does not exist");
                    }
                }
                else
                {
                    recipe.m_craftingStation = ZNetScene.instance
                        .GetPrefab(getInternalName(cfg == null || recipes.Count > 0 ? station.Table : cfg.table.Value))
                        .GetComponent<CraftingStation>();
                }

                recipe.m_minStationLevel = cfg == null || recipes.Count > 0 ? station.level : cfg.tableLevel.Value;
                recipe.m_requireOnlyOneIngredient = cfg == null
                    ? kv.Value.RequireOnlyOneIngredient
                    : cfg.requireOneIngredient.Value == Toggle.On;
                recipe.m_qualityResultAmountMultiplier = cfg?.qualityResultAmountMultiplier.Value
                                                         ?? kv.Value.QualityResultAmountMultiplier;

                recipes.Add(recipe);
                if (kv.Value.RequiredItems is { Free: false, Requirements.Count: 0 })
                {
                    hiddenCraftRecipes.Add(recipe, kv.Value.RecipeIsActive);
                }

                if (kv.Value.RequiredUpgradeItems is { Free: false, Requirements.Count: 0 })
                {
                    hiddenUpgradeRecipes.Add(recipe, kv.Value.RecipeIsActive);
                }
            }

            activeRecipes[this].Add(kv.Key, recipes);
            //Add to the end
            // objectDB.m_recipes.AddRange(recipes);

            //Add to the start
            objectDB.m_recipes.InsertRange(0, recipes);
        }

        conversions = [];
        for (var i = 0; i < Conversions.Count; ++i)
        {
            var conversion = Conversions[i];
            conversions.Add(new Smelter.ItemConversion
            {
                m_from = SerializedRequirements.fetchByName(ObjectDB.instance,
                    conversion.config?.input.Value ?? conversion.Input),
                m_to = Prefab.GetComponent<ItemDrop>(),
            });
            var piece = conversion.config?.piece.Value ?? conversion.Piece;
            string? pieceName = null;
            if (piece is not ConversionPiece.Disabled && conversions[i].m_from is not null)
            {
                pieceName = piece == ConversionPiece.Custom
                    ? conversion.config?.customPiece.Value ?? conversion.customPiece
                    : getInternalName(piece);
                if (ZNetScene.instance.GetPrefab(pieceName)?.GetComponent<Smelter>() is { } smelter)
                {
                    smelter.m_conversion.Add(conversions[i]);
                }
                else
                {
                    pieceName = null;
                }
            }

            if (conversion.config is not null)
            {
                conversion.config.activePiece = pieceName;
            }
        }
    }

    [HarmonyPriority(Priority.Last)]
    internal static void Patch_ObjectDBInit(ObjectDB __instance)
    {
        if (__instance.GetItemPrefab("Wood") == null)
        {
            return;
        }

        hiddenCraftRecipes.Clear();
        hiddenUpgradeRecipes.Clear();

        foreach (var item in registeredItems)
        {
            item.registerRecipesInObjectDB(__instance);
        }
    }

    internal static void Patch_TraderGetAvailableItems(Trader_1 __instance,
        ref List<Trader_1.TradeItem> __result)
    {
        Trader trader = Utils.GetPrefabName(__instance.gameObject) switch
        {
            "Haldor" => Trader.Haldor,
            "Hildir" => Trader.Hildir,
            _ => 0,
        };
        __result.AddRange(PrefabManager.CustomTradeItems.Values.Where(tuple => (tuple.Item1 & trader) != 0)
            .Select(tuple => tuple.Item2).Where(tradeItem =>
                string.IsNullOrEmpty(tradeItem.m_requiredGlobalKey)
                || ZoneSystem.instance.GetGlobalKey(tradeItem.m_requiredGlobalKey)));
    }

    internal static void Patch_OnAddSmelterInput(ItemData item, bool __result)
    {
        if (__result)
        {
            m_localPlayer.UnequipItem(item);
        }
    }

    internal static void Patch_MaximumRequiredStationLevel(Recipe __instance, ref int __result, int quality)
    {
        if (itemDropMap.TryGetValue(__instance.m_item, out var item))
        {
            IEnumerable<ItemConfig> configs;
            if (!itemCraftConfigs.TryGetValue(item, out var itemConfigs))
            {
                configs = Enumerable.Empty<ItemConfig>();
            }
            else if (m_localPlayer.GetCurrentCraftingStation() is { } currentCraftingStation)
            {
                var stationName = Utils.GetPrefabName(currentCraftingStation.gameObject);
                configs = itemConfigs.Where(c => c.Value.table.Value switch
                {
                    CraftingTable.Inventory or CraftingTable.Disabled => false,
                    CraftingTable.Custom => c.Value.customTable.Value == stationName,
                    _ => getInternalName(c.Value.table.Value) == stationName,
                }).Select(c => c.Value);
            }
            else
            {
                configs = itemConfigs.Values;
            }

            __result = Min(Max(1, __instance.m_minStationLevel) + (quality - 1),
                configs.Where(cfg => cfg.maximumTableLevel is not null).Select(cfg => cfg.maximumTableLevel!.Value)
                    .DefaultIfEmpty(item.MaximumRequiredStationLevel).Max());
        }
    }

    internal static void Patch_GetAvailableRecipesPrefix(
        ref Dictionary<Assembly, Dictionary<Recipe, ConfigEntryBase?>>? __state)
    {
        __state ??= new Dictionary<Assembly, Dictionary<Recipe, ConfigEntryBase?>>();
        Dictionary<Recipe, ConfigEntryBase?>? hidden;
        if (InventoryGui.instance.InCraftTab())
        {
            hidden = hiddenCraftRecipes;
        }
        else if (InventoryGui.instance.InUpradeTab())
        {
            hidden = hiddenUpgradeRecipes;
        }
        else
        {
            return;
        }

        foreach (var recipe in hidden.Keys)
        {
            recipe.m_enabled = false;
        }

        __state[Assembly.GetExecutingAssembly()] = hidden;
    }

    internal static void Patch_GetAvailableRecipesFinalizer(
        Dictionary<Assembly, Dictionary<Recipe, ConfigEntryBase?>> __state)
    {
        if (__state.TryGetValue(Assembly.GetExecutingAssembly(), out var hidden))
        {
            foreach (var kv in hidden)
            {
                kv.Key.m_enabled = (int)(kv.Value?.BoxedValue ?? 1) != 0;
            }
        }
    }

    // Ensure valheim distinguishes between active and inactive (belonging to another upgrade level) requirements when cutting off at 4 items displayed 
    internal static IEnumerable<CodeInstruction> Transpile_SetupRequirementList(
        IEnumerable<CodeInstruction> instructionsEnumerable, ILGenerator ilg)
    {
        /*
        for (int index2 = num; index2 < requirementArray.Length; ++index2)
        {
         if (InventoryGui.SetupRequirement(this.m_recipeRequirementList[index1].transform, requirementArray[index2], player, true, quality))
          ++index1;
         if (index1 >= this.m_recipeRequirementList.Length)
          break;
        }

        // =>


        for (int index2 = [<check is 0> start: <pop>] num; index2 < requirementArray.Length; ++index2)
        {
         if (InventoryGui.SetupRequirement(this.m_recipeRequirementList[index1].transform, requirementArray[index2], player, true, quality))
          ++index1;
         if (index1 >= this.m_recipeRequirementList.Length)
          goto end;
        }
        if (!<result of check is 0> && index1 < this.m_recipeRequirementList.Length)
        {
         index1 = 0;
         goto start;
        }
        end:
        */

        var instructions = instructionsEnumerable.ToList();
        var setupRequirement =
            AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement));
        CodeInstruction index1Load = null!;
        CodeInstruction index1Store = null!;
        var skippedNum = ilg.DeclareLocal(typeof(int));
        Dictionary<Label, int> encounteredLabels = new();
        var inLoop = false;
        var loopEndInstruction = 0;
        var loopStartInstruction = 0;
        for (var i = 0; i < instructions.Count; ++i)
        {
            if (instructions[i].Calls(setupRequirement))
            {
                index1Load = instructions[i + 2];
                index1Store = instructions[i + 5];
                inLoop = true;
            }

            if (inLoop)
            {
                if (instructions[i].Branches(out var target)
                    && encounteredLabels.TryGetValue(target!.Value, out loopStartInstruction))
                {
                    loopEndInstruction = i;
                    break;
                }
            }
            else
            {
                foreach (var label in instructions[i].labels)
                {
                    encounteredLabels[label] = i;
                }
            }
        }

        if (instructions[loopStartInstruction - 3].opcode == OpCodes.Dup)
        {
            // Don't patch ourselves twice?!
            return instructions;
        }

        var loopSkipLabel = ilg.DefineLabel();
        var loopStartLabel = ilg.DefineLabel();
        instructions[loopEndInstruction + 1].labels.Add(loopSkipLabel);

        instructions.InsertRange(loopEndInstruction + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldloc, skippedNum),
            new CodeInstruction(OpCodes.Brfalse, loopSkipLabel),
            index1Load.Clone(),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld,
                AccessTools.DeclaredField(typeof(InventoryGui), nameof(InventoryGui.m_recipeRequirementList))),
            new CodeInstruction(OpCodes.Ldlen),
            new CodeInstruction(OpCodes.Bgt, loopSkipLabel),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            index1Store.Clone(),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Br, loopStartLabel),
        });

        instructions.InsertRange(loopStartInstruction - 2, new[]
        {
            new CodeInstruction(OpCodes.Dup) { labels = [loopStartLabel] },
            new CodeInstruction(OpCodes.Stloc, skippedNum),
        });

        return instructions;
    }

    internal static bool Patch_RequirementGetAmount(Piece.Requirement __instance, int qualityLevel, ref int __result)
    {
        if (requirementQuality.TryGetValue(__instance, out var quality))
        {
            __result = quality.quality == qualityLevel ? __instance.m_amountPerLevel : 0;
            return false;
        }

        return true;
    }

    internal static void Patch_ZNetSceneAwake(ZNetScene __instance)
    {
        foreach (var item in registeredItems)
        {
            item.AssignDropToCreature();
        }
    }

    public void AssignDropToCreature()
    {
        foreach (var kv in characterDrops)
        {
            if (kv.Key)
            {
                kv.Key.m_drops.Remove(kv.Value);
            }
        }

        characterDrops.Clear();

        SerializedDrop drops = new(DropsFrom.Drops);
        if (itemDropConfigs.TryGetValue(this, out var config))
        {
            drops = new SerializedDrop(config.Value);
        }

        foreach (var kv in drops.toCharacterDrops(s_instance, Prefab))
        {
            if (kv.Key.GetComponent<CharacterDrop>() is not { } characterDrop)
            {
                characterDrop = kv.Key.gameObject.AddComponent<CharacterDrop>();
            }

            characterDrop.m_drops.Add(kv.Value);

            characterDrops.Add(characterDrop, kv.Value);
        }
    }

    public void UpdateCharacterDrop()
    {
        if (ZNetScene.instance)
        {
            AssignDropToCreature();
        }
    }

    public void Snapshot(float lightIntensity = 1.3f, Quaternion? cameraRotation = null,
        Quaternion? itemRotation = null) =>
        SnapshotItem(Prefab.GetComponent<ItemDrop>(), lightIntensity, cameraRotation, itemRotation);

    public static void SnapshotItem(ItemDrop item, float lightIntensity = 1.3f, Quaternion? cameraRotation = null,
        Quaternion? itemRotation = null)
    {
        void Do()
        {
            const int layer = 30;

            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.backgroundColor = Color.clear;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 0.5f;
            camera.farClipPlane = 10000000;
            camera.cullingMask = 1 << layer;
            camera.transform.rotation = cameraRotation ?? Quaternion.Euler(90, 0, 45);

            var topLight = new GameObject("Light", typeof(Light)).GetComponent<Light>();
            topLight.transform.rotation = Quaternion.Euler(150, 0, -5f);
            topLight.type = LightType.Directional;
            topLight.cullingMask = 1 << layer;
            topLight.intensity = lightIntensity;

            Rect rect = new(0, 0, 64, 64);

            GameObject visual;
            if (item.transform.Find("attach") is { } attach)
            {
                visual = Instantiate(attach.gameObject);
            }
            else
            {
                ZNetView.m_forceDisableInit = true;
                visual = Instantiate(item.gameObject);
                ZNetView.m_forceDisableInit = false;
            }

            if (itemRotation is not null)
            {
                visual.transform.rotation = itemRotation.Value;
            }

            foreach (var child in visual.GetComponentsInChildren<Transform>())
            {
                child.gameObject.layer = layer;
            }

            var renderers = visual.GetComponentsInChildren<Renderer>();
            var min = renderers.Aggregate(Vector3.positiveInfinity,
                (cur, renderer) => renderer is ParticleSystemRenderer ? cur : Vector3.Min(cur, renderer.bounds.min));
            var max = renderers.Aggregate(Vector3.negativeInfinity,
                (cur, renderer) => renderer is ParticleSystemRenderer ? cur : Vector3.Max(cur, renderer.bounds.max));
            var size = max - min;

            camera.targetTexture = RenderTexture.GetTemporary((int)rect.width, (int)rect.height);
            var maxDim = Mathf.Max(size.x, size.z);
            var minDim = Mathf.Min(size.x, size.z);
            var yDist = (maxDim + minDim) / Sqrt(2) / Tan(camera.fieldOfView * Deg2Rad);
            var cameraTransform = camera.transform;
            cameraTransform.position = ((min + max) / 2) with { y = max.y } + new Vector3(0, yDist, 0);
            topLight.transform.position = cameraTransform.position + new Vector3(-2, 0, 0.2f) / 3 * -yDist;

            camera.Render();

            var currentRenderTexture = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;

            Texture2D texture = new((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(rect, 0, 0);
            texture.Apply();

            RenderTexture.active = currentRenderTexture;

            item.m_itemData.m_shared.m_icons = [Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f))];

            DestroyImmediate(visual);
            camera.targetTexture.Release();

            Destroy(camera);
            Destroy(topLight);
        }

        IEnumerator Delay()
        {
            yield return null;
            Do();
        }

        if (ObjectDB.instance)
        {
            Do();
        }
        else
        {
            plugin.StartCoroutine(Delay());
        }
    }

    private static bool CheckItemIsUpgrade(InventoryGui gui) => gui.m_selectedRecipe.Value?.m_quality > 0;

    internal static IEnumerable<CodeInstruction> Transpile_InventoryGui(IEnumerable<CodeInstruction> instructions)
    {
        var instrs = instructions.ToList();
        var amountField = AccessTools.DeclaredField(typeof(Recipe), nameof(Recipe.m_amount));
        for (var i = 0; i < instrs.Count; ++i)
        {
            yield return instrs[i];
            if (i > 1 && instrs[i - 2].opcode == OpCodes.Ldfld && instrs[i - 2].OperandIs(amountField)
                && instrs[i - 1].opcode == OpCodes.Ldc_I4_1 && instrs[i].operand is Label)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.DeclaredMethod(typeof(Item), nameof(CheckItemIsUpgrade)));
                yield return new CodeInstruction(OpCodes.Brtrue, instrs[i].operand);
            }
        }
    }

    private static Action<ConfigEntryBase> drawRequirementsConfigTable(Item item, bool isUpgrade)
    {
        return cfg =>
        {
            var locked = cfg.Description.Tags
                .Select(a =>
                    a.GetType().Name == "ConfigurationManagerAttributes"
                        ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a)
                        : null).FirstOrDefault(v => v != null) ?? false;

            List<Requirement> newReqs = [];
            var wasUpdated = false;

            var RightColumnWidth =
                (int)(configManager?.GetType()
                    .GetProperty("RightColumnWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetGetMethod(true)
                    .Invoke(configManager, []) ?? 130);

            GUILayout.BeginVertical();

            var reqs = new SerializedRequirements((string)cfg.BoxedValue).Reqs;

            var individualQualities = false;
            var maxQuality = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality;
            if (isUpgrade && maxQuality > 2)
            {
                individualQualities = reqs.Any(r => r.quality > 0);
                if (individualQualities)
                {
                    var numReqs = reqs.Count;
                    for (var i = 0; i < numReqs; ++i)
                    {
                        if (reqs[i].quality == 0)
                        {
                            reqs[i] = reqs[i] with { quality = 2 };
                            for (var quality = 3; quality <= maxQuality; ++quality)
                            {
                                reqs.Add(reqs[i] with { quality = quality });
                            }
                        }
                    }
                }

                if (individualQualities != GUILayout.Toggle(individualQualities, "Individual costs per upgrade level"))
                {
                    individualQualities = !individualQualities;
                    wasUpdated = true;

                    if (individualQualities)
                    {
                        var numReqs = reqs.Count;
                        for (var i = 0; i < numReqs; ++i)
                        {
                            reqs[i] = reqs[i] with { quality = 2 };
                            for (var quality = 3; quality <= maxQuality; ++quality)
                            {
                                reqs.Add(reqs[i] with { quality = quality });
                            }
                        }
                    }
                    else
                    {
                        reqs.RemoveAll(req => req.quality > 1);
                        for (var i = 0; i < reqs.Count; ++i)
                        {
                            reqs[i] = reqs[i] with { quality = 0 };
                        }
                    }
                }
            }

            void DisplayQuality(int quality)
            {
                foreach (var req in reqs.Where(r => r.quality == quality))
                {
                    GUILayout.BeginHorizontal();

                    var amount = req.amount;
                    if (int.TryParse(
                            GUILayout.TextField(amount.ToString(),
                                new GUIStyle(GUI.skin.textField) { fixedWidth = 40 }), out var newAmount)
                        && newAmount != amount && !locked)
                    {
                        amount = newAmount;
                        wasUpdated = true;
                    }

                    var newItemName = GUILayout.TextField(req.itemName,
                        new GUIStyle(GUI.skin.textField) { fixedWidth = RightColumnWidth - 40 - 21 - 21 - 9 });
                    var itemName = locked ? req.itemName : newItemName;
                    wasUpdated = wasUpdated || itemName != req.itemName;

                    if (GUILayout.Button("x", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
                    {
                        wasUpdated = true;
                    }
                    else
                    {
                        newReqs.Add(new Requirement { amount = amount, itemName = itemName, quality = quality });
                    }

                    if (GUILayout.Button("+", new GUIStyle(GUI.skin.button) { fixedWidth = 21 }) && !locked)
                    {
                        wasUpdated = true;
                        newReqs.Add(new Requirement { amount = 1, itemName = "", quality = quality });
                    }

                    GUILayout.EndHorizontal();
                }
            }

            if (individualQualities)
            {
                for (var quality = 2; quality <= maxQuality; ++quality)
                {
                    GUILayout.Label($"Upgrade level {quality - 1}:");
                    DisplayQuality(quality);
                }
            }
            else
            {
                DisplayQuality(0);
            }

            GUILayout.EndVertical();

            if (wasUpdated)
            {
                cfg.BoxedValue = new SerializedRequirements(newReqs).ToString();
            }
        };
    }

    private static void drawDropsConfigTable(ConfigEntryBase cfg)
    {
        var locked = cfg.Description.Tags
            .Select(a =>
                a.GetType().Name == "ConfigurationManagerAttributes"
                    ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a)
                    : null).FirstOrDefault(v => v != null) ?? false;

        List<DropTarget> newDrops = [];
        var wasUpdated = false;

        var RightColumnWidth =
            (int)(configManager?.GetType()
                .GetProperty("RightColumnWidth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetGetMethod(true)
                .Invoke(configManager, []) ?? 130);

        GUILayout.BeginVertical();
        foreach (var drop in new SerializedDrop((string)cfg.BoxedValue).Drops.DefaultIfEmpty(new DropTarget
                     { min = 1, max = 1, creature = "", chance = 1 }))
        {
            GUILayout.BeginHorizontal();

            var newCreature = GUILayout.TextField(drop.creature,
                new GUIStyle(GUI.skin.textField) { fixedWidth = RightColumnWidth - 21 - 21 - 6 });
            var creature = locked ? drop.creature : newCreature;
            wasUpdated = wasUpdated || creature != drop.creature;

            var wasDeleted = GUILayout.Button("x", new GUIStyle(GUI.skin.button) { fixedWidth = 21 });
            var wasAdded = GUILayout.Button("+", new GUIStyle(GUI.skin.button) { fixedWidth = 21 });

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            GUILayout.Label("Chance: ");
            var chance = drop.chance;
            if (float.TryParse(
                    GUILayout.TextField((chance * 100).ToString(CultureInfo.InvariantCulture),
                        new GUIStyle(GUI.skin.textField) { fixedWidth = 45 }), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var newChance) && !Approximately(newChance / 100, chance)
                                                                     && !locked)
            {
                chance = newChance / 100;
                wasUpdated = true;
            }

            GUILayout.Label("% Amount: ");

            var min = drop.min;
            if (int.TryParse(GUILayout.TextField(min.ToString(), new GUIStyle(GUI.skin.textField) { fixedWidth = 35 }),
                    out var newMin) && newMin != min && !locked)
            {
                min = newMin;
                wasUpdated = true;
            }

            GUILayout.Label(" - ");

            var max = drop.max;
            if (int.TryParse(GUILayout.TextField(max.ToString(), new GUIStyle(GUI.skin.textField) { fixedWidth = 35 }),
                    out var newMax) && newMax != max && !locked)
            {
                max = newMax;
                wasUpdated = true;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            var levelMultiplier = drop.levelMultiplier;
            if (GUILayout.Toggle(levelMultiplier, "Level scaling drop amount") != levelMultiplier)
            {
                levelMultiplier = !levelMultiplier;
                wasUpdated = true;
            }

            GUILayout.EndHorizontal();

            if (wasDeleted && !locked)
            {
                wasUpdated = true;
            }
            else
            {
                newDrops.Add(new DropTarget
                    { creature = creature, min = min, max = max, chance = chance, levelMultiplier = levelMultiplier });
            }

            if (wasAdded && !locked)
            {
                wasUpdated = true;
                newDrops.Add(new DropTarget { min = 1, max = 1, creature = "", chance = 1, levelMultiplier = true });
            }
        }

        GUILayout.EndVertical();

        if (wasUpdated)
        {
            cfg.BoxedValue = new SerializedDrop(newDrops).ToString();
        }
    }

    private class SerializedRequirements
    {
        public readonly List<Requirement> Reqs;

        public SerializedRequirements(List<Requirement> reqs) => Reqs = reqs;

        public SerializedRequirements(string reqs) : this(reqs.Split(',').Select(r =>
        {
            var parts = r.Split(':');
            return new Requirement
            {
                itemName = parts[0],
                amount = parts.Length > 1 && int.TryParse(parts[1], out var amount) ? amount : 1,
                quality = parts.Length > 2 && int.TryParse(parts[2], out var quality) ? quality : 0,
            };
        }).ToList())
        {
        }

        public override string ToString()
        {
            return string.Join(",",
                Reqs.Select(r => $"{r.itemName}:{r.amount}" + (r.quality > 0 ? $":{r.quality}" : "")));
        }

        public static ItemDrop? fetchByName(ObjectDB objectDB, string name)
        {
            var item = objectDB.GetItemPrefab(name)?.GetComponent<ItemDrop>();
            if (item == null)
            {
                DebugWarning($"The required item '{name}' does not exist.");
            }

            return item;
        }

        public static Piece.Requirement?[] toPieceReqs(ObjectDB objectDB, SerializedRequirements craft,
            SerializedRequirements upgrade)
        {
            ItemDrop? ResItem(Requirement r) => fetchByName(objectDB, r.itemName);

            var resources = craft.Reqs.Where(r => r.itemName != "")
                .ToDictionary(r => r.itemName,
                    r => ResItem(r) is { } item
                        ? new Piece.Requirement
                            { m_amount = r.amountConfig?.Value ?? r.amount, m_resItem = item, m_amountPerLevel = 0 }
                        : null);
            var allResources = resources.Values.Where(v => v != null).ToList()!;

            foreach (var req in upgrade.Reqs.Where(r => r.itemName != ""))
            {
                if (req.quality > 0)
                {
                    if (ResItem(req) is { } item)
                    {
                        Piece.Requirement requirement = new()
                        {
                            m_resItem = item, m_amountPerLevel = req.amountConfig?.Value ?? req.amount, m_amount = 0
                        };
                        allResources.Add(requirement);
                        requirementQuality.Add(requirement, new RequirementQuality { quality = req.quality });
                    }
                }
                else
                {
                    if ((!resources.TryGetValue(req.itemName, out var requirement)
                         || requirement == null) && ResItem(req) is { } item)
                    {
                        requirement = resources[req.itemName] = new Piece.Requirement
                            { m_resItem = item, m_amount = 0 };
                        allResources.Add(requirement);
                    }

                    if (requirement != null)
                    {
                        requirement.m_amountPerLevel = req.amountConfig?.Value ?? req.amount;
                    }
                }
            }

            return allResources.ToArray();
        }
    }

    private class SerializedDrop
    {
        public readonly List<DropTarget> Drops;

        public SerializedDrop(List<DropTarget> drops) => Drops = drops;

        public SerializedDrop(string drops)
        {
            Drops = (drops == "" ? [] : drops.Split(',')).Select(r =>
            {
                var parts = r.Split(':');
                if (parts.Length <= 2 || !int.TryParse(parts[2], out var min))
                {
                    min = 1;
                }

                if (parts.Length <= 3 || !int.TryParse(parts[3], out var max))
                {
                    max = min;
                }

                var levelMultiplier = parts.Length <= 4 || parts[4] != "0";
                return new DropTarget
                {
                    creature = parts[0],
                    chance = parts.Length > 1 && float.TryParse(parts[1], out var chance) ? chance : 1, min = min,
                    max = max, levelMultiplier = levelMultiplier
                };
            }).ToList();
        }

        public override string ToString()
        {
            return string.Join(",",
                Drops.Select(r =>
                    $"{r.creature}:{r.chance.ToString(CultureInfo.InvariantCulture)}:{r.min}:"
                    + (r.min == r.max ? "" : $"{r.max}") + (r.levelMultiplier ? "" : ":0")));
        }

        private static Character? fetchByName(ZNetScene netScene, string name)
        {
            var character = netScene.GetPrefab(name)?.GetComponent<Character>();
            if (character == null)
            {
                DebugWarning($"The drop target character '{name}' does not exist.");
            }

            return character;
        }

        public Dictionary<Character, CharacterDrop.Drop> toCharacterDrops(ZNetScene netScene, GameObject item)
        {
            Dictionary<Character, CharacterDrop.Drop> drops = new();
            foreach (var drop in Drops)
            {
                if (fetchByName(netScene, drop.creature) is { } character)
                {
                    drops[character] = new CharacterDrop.Drop
                    {
                        m_prefab = item, m_amountMin = drop.min, m_amountMax = drop.max, m_chance = drop.chance,
                        m_levelMultiplier = drop.levelMultiplier
                    };
                }
            }

            return drops;
        }
    }

    private static Localization? _english;
    private static Localization english => _english ??= LocalizationCache.ForLanguage("English");

    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin plugin
    {
        get
        {
            if (_plugin is null)
            {
                IEnumerable<TypeInfo> types;
                try
                {
                    types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).Select(t => t.GetTypeInfo());
                }

                _plugin = (BaseUnityPlugin)Chainloader.ManagerObject.GetComponent(types.First(t =>
                    t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));
            }

            return _plugin;
        }
    }

    private static bool hasConfigSync = true;
    private static object? _configSync;

    private static object? configSync
    {
        get
        {
            if (_configSync == null && hasConfigSync)
            {
                if (Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync") is { } configSyncType)
                {
                    _configSync = Activator.CreateInstance(configSyncType, plugin.Info.Metadata.GUID + " ItemManager");
                    configSyncType.GetField("CurrentVersion")
                        .SetValue(_configSync, plugin.Info.Metadata.Version.ToString());
                    configSyncType.GetProperty("IsLocked")!.SetValue(_configSync, true);
                }
                else
                {
                    hasConfigSync = false;
                }
            }

            return _configSync;
        }
    }

    private static ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description)
    {
        var configEntry = plugin.Config.Bind(group, name, value, description);

        configSync?.GetType().GetMethod("AddConfigEntry")!.MakeGenericMethod(typeof(T))
            .Invoke(configSync, [configEntry]);

        return configEntry;
    }

    private static ConfigEntry<T> config<T>(string group, string name, T value, string description) =>
        config(group, name, value, new ConfigDescription(description));
}

[PublicAPI]
public class LocalizeKey
{
    private static readonly List<LocalizeKey> keys = [];

    public readonly string Key;
    public readonly Dictionary<string, string> Localizations = new();

    public LocalizeKey(string key)
    {
        Key = key.Replace("$", "");
        keys.Add(this);
    }

    public void Alias(string alias)
    {
        Localizations.Clear();
        if (!alias.Contains("$"))
        {
            alias = $"${alias}";
        }

        Localizations["alias"] = alias;
        Localization.instance.AddWord(Key, Localization.instance.Localize(alias));
    }

    public LocalizeKey English(string key) => addForLang("English", key);
    public LocalizeKey Swedish(string key) => addForLang("Swedish", key);
    public LocalizeKey French(string key) => addForLang("French", key);
    public LocalizeKey Italian(string key) => addForLang("Italian", key);
    public LocalizeKey German(string key) => addForLang("German", key);
    public LocalizeKey Spanish(string key) => addForLang("Spanish", key);
    public LocalizeKey Russian(string key) => addForLang("Russian", key);
    public LocalizeKey Romanian(string key) => addForLang("Romanian", key);
    public LocalizeKey Bulgarian(string key) => addForLang("Bulgarian", key);
    public LocalizeKey Macedonian(string key) => addForLang("Macedonian", key);
    public LocalizeKey Finnish(string key) => addForLang("Finnish", key);
    public LocalizeKey Danish(string key) => addForLang("Danish", key);
    public LocalizeKey Norwegian(string key) => addForLang("Norwegian", key);
    public LocalizeKey Icelandic(string key) => addForLang("Icelandic", key);
    public LocalizeKey Turkish(string key) => addForLang("Turkish", key);
    public LocalizeKey Lithuanian(string key) => addForLang("Lithuanian", key);
    public LocalizeKey Czech(string key) => addForLang("Czech", key);
    public LocalizeKey Hungarian(string key) => addForLang("Hungarian", key);
    public LocalizeKey Slovak(string key) => addForLang("Slovak", key);
    public LocalizeKey Polish(string key) => addForLang("Polish", key);
    public LocalizeKey Dutch(string key) => addForLang("Dutch", key);
    public LocalizeKey Portuguese_European(string key) => addForLang("Portuguese_European", key);
    public LocalizeKey Portuguese_Brazilian(string key) => addForLang("Portuguese_Brazilian", key);
    public LocalizeKey Chinese(string key) => addForLang("Chinese", key);
    public LocalizeKey Japanese(string key) => addForLang("Japanese", key);
    public LocalizeKey Korean(string key) => addForLang("Korean", key);
    public LocalizeKey Hindi(string key) => addForLang("Hindi", key);
    public LocalizeKey Thai(string key) => addForLang("Thai", key);
    public LocalizeKey Abenaki(string key) => addForLang("Abenaki", key);
    public LocalizeKey Croatian(string key) => addForLang("Croatian", key);
    public LocalizeKey Georgian(string key) => addForLang("Georgian", key);
    public LocalizeKey Greek(string key) => addForLang("Greek", key);
    public LocalizeKey Serbian(string key) => addForLang("Serbian", key);
    public LocalizeKey Ukrainian(string key) => addForLang("Ukrainian", key);

    private LocalizeKey addForLang(string lang, string value)
    {
        Localizations[lang] = value;
        if (Localization.instance.GetSelectedLanguage() == lang)
        {
            Localization.instance.AddWord(Key, value);
        }
        else if (lang == "English" && !Localization.instance.m_translations.ContainsKey(Key))
        {
            Localization.instance.AddWord(Key, value);
        }

        return this;
    }

    [HarmonyPriority(Priority.LowerThanNormal)]
    internal static void AddLocalizedKeys(Localization __instance, string language)
    {
        foreach (var key in keys)
        {
            if (key.Localizations.TryGetValue(language, out var Translation)
                || key.Localizations.TryGetValue("English", out Translation))
            {
                __instance.AddWord(key.Key, Translation);
            }
            else if (key.Localizations.TryGetValue("alias", out var alias))
            {
                __instance.AddWord(key.Key, Localization.instance.Localize(alias));
            }
        }
    }
}

public static class LocalizationCache
{
    private static readonly Dictionary<string, Localization> localizations = new();

    internal static void LocalizationPostfix(Localization __instance, string language)
    {
        if (localizations.FirstOrDefault(l => l.Value == __instance).Key is { } oldValue)
        {
            localizations.Remove(oldValue);
        }

        if (!localizations.ContainsKey(language))
        {
            localizations.Add(language, __instance);
        }
    }

    public static Localization ForLanguage(string? language = null)
    {
        if (localizations.TryGetValue(language ?? PlayerPrefs.GetString("language", "English"),
                out var localization))
        {
            return localization;
        }

        localization = new Localization();
        if (language is not null)
        {
            localization.SetupLanguage(language);
        }

        return localization;
    }
}

[PublicAPI]
public static class PrefabManager
{
    static PrefabManager()
    {
        Harmony harmony = new("org.bepinex.helpers.ItemManager");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_ObjectDBInit))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(FejdStartup), nameof(FejdStartup.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_FejdStartup))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Patch_ZNetSceneAwake))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PrefabManager), nameof(Patch_ZNetSceneAwake))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe)),
            transpiler: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Transpile_InventoryGui))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList)),
            transpiler: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Transpile_SetupRequirementList))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount)),
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Patch_RequirementGetAmount))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Player), nameof(Player.GetAvailableRecipes)),
            prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Patch_GetAvailableRecipesPrefix))),
            finalizer: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Patch_GetAvailableRecipesFinalizer))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Recipe), nameof(Recipe.GetRequiredStationLevel)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Patch_MaximumRequiredStationLevel))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnAddFuel)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_OnAddSmelterInput))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Smelter), nameof(Smelter.OnAddOre)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item), nameof(Item.Patch_OnAddSmelterInput))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Trader_1), nameof(Trader_1.GetAvailableItems)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Item),
                nameof(Item.Patch_TraderGetAvailableItems))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Localization), nameof(Localization.SetupLanguage)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(LocalizationCache),
                nameof(LocalizationCache.LocalizationPostfix))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Localization), nameof(Localization.LoadCSV)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(LocalizeKey),
                nameof(LocalizeKey.AddLocalizedKeys))));
    }

    private struct BundleId
    {
        [UsedImplicitly] public string assetBundleFileName;
        [UsedImplicitly] public string folderName;
    }

    private static readonly Dictionary<BundleId, AssetBundle> bundleCache = new();

    public static AssetBundle RegisterAssetBundle(string assetBundleFileName, string folderName = "assets")
    {
        BundleId id = new() { assetBundleFileName = assetBundleFileName, folderName = folderName };
        if (!bundleCache.TryGetValue(id, out var assets))
        {
            assets = bundleCache[id] =
                Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(a => a.name == assetBundleFileName)
                ?? AssetBundle.LoadFromStream(Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(Assembly.GetExecutingAssembly().GetName().Name + $".{folderName}."
                        + assetBundleFileName));
        }

        return assets;
    }

    private static readonly List<GameObject> prefabs = [];
    private static readonly List<GameObject> ZnetOnlyPrefabs = [];

    public static GameObject
        RegisterPrefab(string assetBundleFileName, string prefabName, string folderName = "assets") =>
        RegisterPrefab(RegisterAssetBundle(assetBundleFileName, folderName), prefabName);

    public static GameObject RegisterPrefab(AssetBundle assets, string prefabName, bool addToObjectDb = false) =>
        RegisterPrefab(assets.LoadAsset<GameObject>(prefabName), addToObjectDb);

    public static GameObject RegisterPrefab(GameObject prefab, bool addToObjectDb = false)
    {
        if (addToObjectDb)
        {
            prefabs.Add(prefab);
        }
        else
        {
            ZnetOnlyPrefabs.Add(prefab);
        }

        return prefab;
    }

    internal static readonly Dictionary<GameObject, Tuple<Trader, Trader_1.TradeItem>> CustomTradeItems = new();

    public static void AddItemToTrader(GameObject prefab, Trader trader, uint price, uint stack = 1,
        string? requiredGlobalKey = null)
    {
        CustomTradeItems[prefab] = new Tuple<Trader, Trader_1.TradeItem>(trader, new Trader_1.TradeItem
        {
            m_prefab = prefab.GetComponent<ItemDrop>(),
            m_price = (int)price,
            m_stack = (int)stack,
            m_requiredGlobalKey = requiredGlobalKey ?? "",
        });
    }

    public static void RemoveItemFromTrader(GameObject prefab)
    {
        CustomTradeItems.Remove(prefab);
    }

    [HarmonyPriority(Priority.VeryHigh)]
    private static void Patch_ObjectDBInit(ObjectDB __instance)
    {
        foreach (var prefab in prefabs)
        {
            if (!__instance.m_items.Contains(prefab))
            {
                __instance.m_items.Add(prefab);
            }

            void RegisterStatusEffect(StatusEffect? statusEffect)
            {
                if (statusEffect is not null && !__instance.GetStatusEffect(statusEffect.name.GetStableHashCode()))
                {
                    __instance.m_StatusEffects.Add(statusEffect);
                }
            }

            var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            RegisterStatusEffect(shared.m_attackStatusEffect);
            RegisterStatusEffect(shared.m_consumeStatusEffect);
            RegisterStatusEffect(shared.m_equipStatusEffect);
            RegisterStatusEffect(shared.m_setStatusEffect);
        }

        __instance.UpdateItemHashes();
    }

    [HarmonyPriority(Priority.VeryHigh)]
    private static void Patch_ZNetSceneAwake(ZNetScene __instance)
    {
        foreach (var prefab in prefabs.Concat(ZnetOnlyPrefabs))
        {
            if (!__instance.m_prefabs.Contains(prefab))
            {
                __instance.m_prefabs.Add(prefab);
            }
            else
            {
                DebugWarning($"ZNetScene already contains {prefab.name} it cannot be added twice.");
            }
        }
    }
}

[PublicAPI]
public class Conversion
{
    internal class ConversionConfig
    {
        public ConfigEntry<string> input = null!;
        public string? activePiece;
        public ConfigEntry<ConversionPiece> piece = null!;
        public ConfigEntry<string> customPiece = null!;
    }

    public string Input = null!;
    public ConversionPiece Piece;
    internal string? customPiece;

    public string? Custom
    {
        get => customPiece;
        set
        {
            customPiece = value;
            Piece = ConversionPiece.Custom;
        }
    }

    internal ConversionConfig? config;

    public Conversion(Item outputItem)
    {
        outputItem.Conversions.Add(this);
    }
}