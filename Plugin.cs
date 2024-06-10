using System.Reflection;
using BepInEx;
using RuneLover.Casts;
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

    public static Item runeA = null!;
    public static Item runeB = null!;

    private void Awake()
    {
        CreateMod(this, ModName, ModAuthor, ModVersion, ModGuid);
        LoadAssetBundle("runelover");

        Localizer.Load();

        runeA = new Item(bundle, "Rune_A");
        runeA.Name.English("Rune A").Russian("Руна А");
        runeA.Description.English("").Russian("");
        runeA.Crafting.Add(CraftingTable.Inventory, 1);
        runeA.RequiredItems.Add("Wood", 1);
        runeA.CraftAmount = 1;

        runeB = new Item(bundle, "Rune_B");
        runeB.Name.English("Rune B").Russian("Руна Б");
        runeB.Description.English("").Russian("");
        runeB.Crafting.Add(CraftingTable.Inventory, 1);
        runeB.RequiredItems.Add("Wood", 1);
        runeB.CraftAmount = 1;

        foreach (var t in Assembly.GetExecutingAssembly().GetTypes())
        {
            // inherit from CastBase
            if (t.IsSubclassOf(typeof(CastBase)))
            {
                var instance = Activator.CreateInstance(t) as CastBase;
                CastPaternManager.RegisterCast(instance!);
            }
        }
    }
}