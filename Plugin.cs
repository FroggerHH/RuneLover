using System.Reflection;
using BepInEx;
using RuneLover.Casts;
using RuneLover.Casts.Mono;
using RuneLover.ItemManager;
using RuneLover.LocalizationManager;

namespace RuneLover;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public class Plugin : BaseUnityPlugin
{
    private const string
        ModName = "RuneLover",
        ModVersion = "0.1.0",
        ModAuthor = "Frogger",
        ModGuid = $"com.{ModAuthor}.{ModName}";

    //TODO: It may be better to use custom mono for Prefab. For example to cash some references
    public static Item RuneA { get; private set; } = null!;
    public static Item RuneB { get; private set; } = null!;
    public static CastPrefabMono ProjectilePrefab { get; private set; } = null!;
    public static CastPrefabMono AoEPrefab { get; private set; } = null;
    public static CastPrefabMono DomePrefab { get; private set; } = null;

    private void Awake()
    {
        CreateMod(this, ModName, ModAuthor, ModVersion, ModGuid);
        LoadAssetBundle("runelover");

        Localizer.Load();

        InitCastPrefabs();
        InitRunes();
        CastPaternManager.Init();
    }

    private void InitCastPrefabs()
    {
        ProjectilePrefab = bundle.LoadAsset<GameObject>("rl_ProjectilePrefab").GetOrAddComponent<CastPrefabMono>();
        // foreach (var renderer in ProjectilePrefab.GetComponentsInChildren<Renderer>(true))
        //     renderer.material.shader = Shader.Find(renderer.material.shader.name);
        ProjectilePrefab.InitReferences();

        //TODO: Add other prefabs: rl_AoEPrefab and rl_DomePrefab do not exist yet
        // AoEPrefab = bundle.LoadAsset<GameObject>("rl_AoEPrefab");
        // DomePrefab = bundle.LoadAsset<GameObject>("rl_DomePrefab");
    }

    private static void InitRunes()
    {
        RuneA = new Item(bundle, "rl_Rune_Fire");
        RuneA.Name.English("Fire Rune").Russian("Руна Огня");
        RuneA.Description.English("").Russian("");
        RuneA.Crafting.Add(CraftingTable.Inventory, 1);
        RuneA.RequiredItems.Add("Wood", 1);
        RuneA.CraftAmount = 1;

        RuneB = new Item(bundle, "rl_Rune_Earth");
        RuneB.Name.English("Rune of Earth").Russian("Руна Земли");
        RuneB.Description.English("").Russian("");
        RuneB.Crafting.Add(CraftingTable.Inventory, 1);
        RuneB.RequiredItems.Add("Wood", 1);
        RuneB.CraftAmount = 1;
    }
}