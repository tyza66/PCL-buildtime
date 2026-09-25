namespace PCL.Avalonia.Services.Mods;

public static class ResourceTagCatalog
{
    public static IReadOnlyList<ResourceTagOption> ForType(ResourceType type) => type switch
    {
        ResourceType.Mod => ModTags,
        ResourceType.ModPack => ModPackTags,
        ResourceType.ResourcePack => ResourcePackTags,
        ResourceType.Shader => ShaderTags,
        ResourceType.DataPack => DataPackTags,
        _ => [],
    };

    private static readonly ResourceTagOption[] ModTags =
    [
        new("全部", ""),
        new("世界元素", "406/worldgen"),
        new("科技", "412/technology"),
        new("食物与烹饪", "436/food"),
        new("游戏机制", "/game-mechanics"),
        new("运输", "414/transportation"),
        new("仓储", "420/storage"),
        new("魔法", "419/magic"),
        new("冒险", "422/adventure"),
        new("装饰", "424/decoration"),
        new("生物", "411/mobs"),
        new("实用", "5191/utility"),
        new("装备与工具", "434/equipment"),
        new("性能优化", "6814/optimization"),
        new("服务器", "435/social"),
        new("支持库", "421/library"),
    ];

    private static readonly ResourceTagOption[] ModPackTags =
    [
        new("全部", ""),
        new("多人", "4484/"),
        new("性能优化", "/optimization"),
        new("硬核", "4479/challenging"),
        new("战斗", "4483/combat"),
        new("任务", "4478/quests"),
        new("科技", "4472/technology"),
        new("魔法", "4473/magic"),
        new("冒险", "4475/adventure"),
        new("水槽包", "/kitchen-sink"),
        new("探索", "4476/"),
        new("小游戏", "4477/"),
        new("科幻", "4474/"),
        new("空岛", "4736/"),
        new("FTB", "4487/"),
        new("基于地图", "4480/"),
        new("轻量整合", "4481/lightweight"),
        new("大型整合", "4482/"),
    ];

    private static readonly ResourceTagOption[] ResourcePackTags =
    [
        new("全部", ""),
        new("原版风", "403/vanilla-like"),
        new("写实风", "400/realistic"),
        new("现代风", "401/"),
        new("中世纪", "402/"),
        new("蒸汽朋克", "399/"),
        new("主题化", "/themed"),
        new("简洁", "/simplistic"),
        new("装饰", "/decoration"),
        new("战斗", "/combat"),
        new("实用", "/utility"),
        new("改良", "/tweaks"),
        new("鬼畜", "/cursed"),
        new("含实体", "/entities"),
        new("含声音", "/audio"),
        new("含字体", "5244/fonts"),
        new("含模型", "/models"),
        new("含语言", "/locale"),
        new("含 UI", "/gui"),
        new("核心着色器", "/core-shaders"),
        new("兼容 Mod", "4465/modded"),
        new("8x 或更低", "/8x-"),
        new("16x", "393/16x"),
        new("32x", "394/32x"),
        new("48x", "/48x"),
        new("64x", "395/64x"),
        new("128x", "396/128x"),
        new("256x", "397/256x"),
        new("512x 或更高", "398/512x+"),
    ];

    private static readonly ResourceTagOption[] ShaderTags =
    [
        new("全部", ""),
        new("原版风", "6555/vanilla-like"),
        new("幻想风", "6554/fantasy"),
        new("写实风", "6553/realistic"),
        new("半写实风", "/semi-realistic"),
        new("卡通风", "/cartoon"),
        new("彩色光照", "/colored-lighting"),
        new("路径追踪", "/path-tracing"),
        new("PBR", "/pbr"),
        new("反射", "/reflections"),
        new("极低", "/potato"),
        new("低", "/low"),
        new("中", "/medium"),
        new("高", "/high"),
        new("原版可用", "/vanilla"),
        new("Iris", "/iris"),
        new("OptiFine", "/optifine"),
    ];

    private static readonly ResourceTagOption[] DataPackTags =
    [
        new("全部", ""),
        new("世界元素", "/worldgen"),
        new("科技", "6951/technology"),
        new("游戏机制", "/game-mechanics"),
        new("运输", "/transportation"),
        new("仓储", "/storage"),
        new("魔法", "6952/magic"),
        new("冒险", "6948/adventure"),
        new("幻想", "6949/"),
        new("装饰", "/decoration"),
        new("生物", "/mobs"),
        new("实用", "6953/utility"),
        new("装备与工具", "/equipment"),
        new("性能优化", "/optimization"),
        new("服务器", "/social"),
        new("支持库", "6950/library"),
        new("Mod 相关", "6946/"),
    ];
}
