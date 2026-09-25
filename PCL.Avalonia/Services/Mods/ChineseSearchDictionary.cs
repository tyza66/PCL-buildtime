namespace PCL.Avalonia.Services.Mods;

public static class ChineseSearchDictionary
{
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.Ordinal)
    {
        ["匠魂"] = "Tinker's Construct",
        ["钠"] = "Sodium",
        ["锂"] = "Lithium",
        ["磷"] = "Phosphor",
        ["玉"] = "Jade",
        ["旅行地图"] = "Journey Map",
        ["世界地图"] = "Xaero's World Map",
        ["小地图"] = "Xaero's Minimap",
        ["高清修复"] = "OptiFine",
        ["夸克"] = "Quark",
        ["暮色森林"] = "Twilight Forest",
        ["帕秋莉手册"] = "Patchouli",
        ["帕秋莉"] = "Patchouli",
        ["机械动力"] = "Create",
        ["应用能源"] = "Applied Energistics 2",
        ["精炼存储"] = "Refined Storage",
        ["神秘时代"] = "Thaumcraft",
        ["植物魔法"] = "Botania",
        ["血魔法"] = "Blood Magic",
        ["龙之研究"] = "Draconic Evolution",
        ["星辉魔法"] = "Astral Sorcery",
        ["循环"] = "Cyclic",
        ["幸运方块"] = "Lucky Block",
        ["自定义NPC"] = "Custom NPCs",
        ["生物群系改动"] = "Biomes O' Plenty",
        ["多样世界"] = "Oh The Biomes We've Gone",
        ["冰与火之歌"] = "Ice and Fire",
        ["模拟殖民地"] = "Minecolonies",
        ["农夫乐事"] = "Farmer's Delight",
        ["附魔描述"] = "Enchantment Descriptions",
        ["物品栏整理"] = "Inventory Sorter",
        ["一键整理"] = "Inventory Sorter",
        ["更好的配方书"] = "Better Recipe Book",
        ["苹果核"] = "AppleSkin",
        ["我的世界工具"] = "Minecraft Tools",
    };

    public static bool ContainsChinese(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return input.Any(ch => ch is >= '\u4e00' and <= '\u9fbb');
    }

    public static bool TryTranslate(string input, out string english)
    {
        english = "";
        if (!ContainsChinese(input))
        {
            return false;
        }

        var normalized = input.Trim();
        foreach (var (chinese, translation) in KnownNames)
        {
            if (!normalized.Contains(chinese, StringComparison.Ordinal))
            {
                continue;
            }

            english = translation;
            return true;
        }

        return false;
    }
}
