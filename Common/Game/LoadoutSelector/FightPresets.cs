using System.Collections.Generic;
using Terraria.ID;
using Terraria.ModLoader.Config;

namespace PvPArenas.Common.Game.LoadoutSelector;

internal static class FightPresets
{
    internal const int Count = 4;

    // Plantera generates its jungle; Golem still needs its authored temple.
    internal static bool IsAvailable(int index) => index is 0 or 1 or 2;

    internal static int BossType(int index) => index switch
    {
        0 => NPCID.KingSlime,
        1 => NPCID.EyeofCthulhu,
        2 => NPCID.Plantera,
        3 => NPCID.Golem,
        _ => NPCID.None
    };

    internal static BossFightPreset Create(int index) => new()
    {
        Boss = new NPCDefinition(BossType(index)),
        ArenaKind = index switch
        {
            2 => ArenaKind.UndergroundJungle,
            3 => ArenaKind.JungleTemple,
            _ => ArenaKind.ArenasV10
        },
        MaxHealth = index < 2 ? 200 : index == 2 ? 400 : 500,
        MaxMana = index < 2 ? 100 : index == 2 ? 180 : 200,
        Loadouts = index switch
        {
            2 => CreatePostMechLoadouts(),
            3 => CreatePostPlanteraLoadouts(),
            _ => CreatePreBossLoadouts()
        }
    };

    public static List<BossFightPreset> CreateFightPresets() => [Create(0), Create(1), Create(2), Create(3)];

    #region Pre-Hardmode loadouts

    // Shared pre-boss utility/base items, mirroring the post-mech base kit but limited
    // to what is obtainable before defeating any boss (and with saner stack sizes).
    private static LoadoutItem[] PreBossBase() =>
    [
        Item(ItemID.Binoculars),
        Item(ItemID.Teacup, 9999),
        Item(ItemID.IceRod),
        Item(ItemID.HoneyBucket),
        Item(ItemID.LavaBucket),
        Item(ItemID.Bomb, 500),
        Item(ItemID.HealingPotion, 9999),
        Item(ItemID.EncumberingStone),
        Item(ItemID.MoltenPickaxe),
        Item(ItemID.Campfire, 5),
        Item(ItemID.HeartLantern, 5),
        Item(ItemID.Bed, 5),
        Item(ItemID.Wood, 500),
        Item(ItemID.StoneBlock, 500),
        Item(ItemID.Torch, 9999),
        Item(ItemID.Glowstick, 9999)
    ];

    private static List<ArenaLoadoutOption> CreatePreBossLoadouts() =>
    [
        new()
        {
            Name = "Molten Warrior",
            Loadout = CreateMoltenWarrior()
        },
        new()
        {
            Name = "Meteor Mage",
            Loadout = CreateMeteorMage()
        }
    ];

    private static Loadout CreateMoltenWarrior() => new()
    {
        Armor = new()
        {
            Head = Def(ItemID.MoltenHelmet),
            Body = Def(ItemID.MoltenBreastplate),
            Legs = Def(ItemID.MoltenGreaves)
        },
        Accessories = new()
        {
            Accessory1 = Def(ItemID.HermesBoots),
            Accessory2 = Def(ItemID.CloudinaBottle),
            Accessory3 = Def(ItemID.EoCShield),
            Accessory4 = Def(ItemID.BandofRegeneration),
            Accessory5 = Def(ItemID.FeralClaws)
        },
        Equipment = new()
        {
            GrapplingHook = Def(ItemID.EmeraldHook)
        },
        Inventory = WithBaseRow(
        [
            Item(ItemID.FieryGreatsword),
            Item(ItemID.Valor),
            Item(ItemID.MoltenFury),
            Item(ItemID.HellfireArrow, 9999),
            Item(ItemID.DarkLance),
            Item(ItemID.Sunfury),
            Item(ItemID.Ale, 9999)
        ], PreBossBase())
    };

    /// <summary>
    /// Pre-Hardmode mage loadout at approximately the same progression stage
    /// as Molten Warrior. It uses Meteor armor for Space Gun efficiency while
    /// also carrying late pre-Hardmode dungeon, jungle, underworld and bee weapons.
    /// </summary>
    private static Loadout CreateMeteorMage() => new()
    {
        Armor = new()
        {
            Head = Def(ItemID.MeteorHelmet),
            Body = Def(ItemID.MeteorSuit),
            Legs = Def(ItemID.MeteorLeggings)
        },
        Accessories = new()
        {
            Accessory1 = Def(ItemID.HermesBoots),
            Accessory2 = Def(ItemID.CloudinaBottle),
            Accessory3 = Def(ItemID.EoCShield),
            Accessory4 = Def(ItemID.BandofStarpower),
            Accessory5 = Def(ItemID.ManaFlower)
        },
        Equipment = new()
        {
            GrapplingHook = Def(ItemID.EmeraldHook)
        },
        Inventory = WithBaseRow(
        [
            Item(ItemID.SpaceGun),
            Item(ItemID.DemonScythe),
            Item(ItemID.WaterBolt),
            Item(ItemID.BeeGun),
            Item(ItemID.Flamelash),
            Item(ItemID.FlowerofFire),
            Item(ItemID.AquaScepter),
            Item(ItemID.MagicMissile),
            Item(ItemID.ManaPotion, 9999),
            Item(ItemID.CrystalBall)
        ], PreBossBase())
    };

    #endregion

    #region Post-mechanical-boss loadouts

    private static List<ArenaLoadoutOption> CreatePostMechLoadouts() =>
    [
        new()
        {
            Name = "Melee",
            Loadout = CreateMelee()
        },
        new()
        {
            Name = "Ranged",
            Loadout = CreateRanger()
        },
        new()
        {
            Name = "Mage",
            Loadout = CreateMage()
        },
        new()
        {
            Name = "Summoner",
            Loadout = CreateSummoner()
        }
    ];

    private static Loadout CreateMelee() => CreatePostMechBase(
        ItemID.ChlorophyteMask,
        ItemID.ChlorophytePlateMail,
        ItemID.ChlorophyteGreaves,
        ItemID.FireGauntlet,
        ItemID.WarriorEmblem,
        [
            Item(ItemID.ShadowFlameKnife),
            Item(ItemID.ChlorophytePartisan),
            Item(ItemID.TrueExcalibur),
            Item(ItemID.LightDisc),
            Item(ItemID.ChainGuillotines),
            Item(ItemID.ChlorophyteClaymore),
            Item(ItemID.BouncingShield),
            Item(ItemID.Ale, 9999)
        ]);

    private static Loadout CreateRanger() => CreatePostMechBase(
        ItemID.ChlorophyteHelmet,
        ItemID.ChlorophytePlateMail,
        ItemID.ChlorophyteGreaves,
        ItemID.RangerEmblem,
        ItemID.MagicQuiver,
        [
            Item(ItemID.HolyArrow, 9999),
            Item(ItemID.CursedArrow, 9999),
            Item(ItemID.HellfireArrow, 9999),
            Item(ItemID.Gel, 9999),
            Item(ItemID.CrystalDart, 9999),
            Item(ItemID.CursedDart, 9999),
            Item(ItemID.HighVelocityBullet, 9999),
            Item(ItemID.ChlorophyteShotbow),
            Item(ItemID.PulseBow),
            Item(ItemID.OnyxBlaster),
            Item(ItemID.Flamethrower),
            Item(ItemID.DaedalusStormbow),
            Item(ItemID.DartRifle)
        ]);

    private static Loadout CreateMage() => CreatePostMechBase(
        ItemID.ChlorophyteHeadgear,
        ItemID.ChlorophytePlateMail,
        ItemID.ChlorophyteGreaves,
        ItemID.SorcererEmblem,
        ItemID.CelestialEmblem,
        [
            Item(ItemID.ClingerStaff),
            Item(ItemID.NimbusRod),
            Item(ItemID.MeteorStaff),
            Item(ItemID.VenomStaff),
            Item(ItemID.CursedFlames),
            Item(ItemID.UnholyTrident),
            Item(ItemID.RainbowRod),
            Item(ItemID.CrystalVileShard),
            Item(ItemID.ZapinatorOrange),
            Item(ItemID.CrystalBall),
            Item(ItemID.MagicCuffs)
        ]);

    private static Loadout CreateSummoner() => CreatePostMechBase(
        ItemID.ObsidianHelm,
        ItemID.ObsidianShirt,
        ItemID.ObsidianPants,
        ItemID.BerserkerGlove,
        ItemID.SummonerEmblem,
        [
            Item(ItemID.Smolstar),
            Item(ItemID.SpiderStaff),
            Item(ItemID.QueenSpiderStaff),
            Item(ItemID.CoolWhip),
            Item(ItemID.SwordWhip),
            Item(ItemID.FireWhip),
            Item(ItemID.ThornWhip),
            Item(ItemID.BoneWhip),
            Item(ItemID.BewitchingTable),
            Item(ItemID.Ale, 9999)
        ]);

    private static Loadout CreatePostMechBase(
        int head,
        int body,
        int legs,
        int classAccessory1,
        int classAccessory2,
        LoadoutItem[] classItems)
    {
        List<LoadoutItem> baseItems =
        [
            Item(ItemID.Binoculars),
            Item(ItemID.Teacup, 9999),
            Item(ItemID.IceRod),
            Item(ItemID.ChlorophyteDrill),
            Item(ItemID.HoneyBucket),
            Item(ItemID.Bomb, 500),
            Item(ItemID.GreaterHealingPotion, 9999),
            Item(ItemID.AegisFruit),
            Item(ItemID.Campfire, 5),
            Item(ItemID.HeartLantern, 5),
            Item(ItemID.Wood, 500),
            Item(ItemID.Bed, 5),
            Item(ItemID.PortalGun),
            Item(ItemID.CharmofMyths),
            Item(ItemID.StarVeil),
            Item(ItemID.VolatileGelatin),
            Item(ItemID.EncumberingStone)
        ];

        if (ItemID.Search.TryGetId("VitalCrystal", out int vitalCrystal))
            baseItems.Add(Item(vitalCrystal));

        return new Loadout
        {
            Armor = new()
            {
                Head = Def(head),
                Body = Def(body),
                Legs = Def(legs)
            },
            Accessories = new()
            {
                Accessory1 = Def(ItemID.FairyWings),
                Accessory2 = Def(ItemID.WormScarf),
                Accessory3 = Def(ItemID.EoCShield),
                Accessory4 = Def(classAccessory1),
                Accessory5 = Def(classAccessory2)
            },
            Equipment = new()
            {
                GrapplingHook = Def(ItemID.DualHook),
                Mount = Def(ItemID.SlimySaddle)
            },
            Inventory = WithBaseRow(classItems, [.. baseItems])
        };
    }

    #endregion

    #region Post-Plantera loadouts

    private static List<ArenaLoadoutOption> CreatePostPlanteraLoadouts() =>
    [
        new()
        {
            Name = "Magician",
            Loadout = CreateMagician()
        },
        new()
        {
            Name = "Warrior",
            Loadout = CreateWarrior()
        }
    ];

    private static Loadout CreateMagician() => new()
    {
        Armor = new()
        {
            Head = Def(ItemID.ChlorophyteHeadgear),
            Body = Def(ItemID.AdamantiteBreastplate),
            Legs = Def(ItemID.AncientBattleArmorPants)
        },
        Accessories = new()
        {
            Accessory1 = Def(ItemID.GhostWings),
            Accessory2 = Def(ItemID.Tabi),
            Accessory3 = Def(ItemID.SorcererEmblem),
            Accessory4 = Def(ItemID.CelestialEmblem),
            Accessory5 = Def(ItemID.WormScarf)
        },
        Equipment = new()
        {
            GrapplingHook = Def(ItemID.DualHook),
            Mount = Def(ItemID.QueenSlimeMountSaddle)
        },
        Inventory =
        [
            Item(ItemID.StoneBlock, 9999),
            Item(ItemID.SpectrePickaxe),
            Item(ItemID.Binoculars),
            Item(ItemID.IceRod),
            Item(ItemID.HoneyBucket),

            Item(ItemID.StaffofEarth),
            Item(ItemID.RainbowRod),
            Item(ItemID.MeteorStaff),
            Item(ItemID.LaserMachinegun),
            Item(ItemID.NettleBurst),
            Item(ItemID.FairyQueenMagicItem),
            Item(ItemID.BubbleGun),
            Item(ItemID.ChargedBlasterCannon),
            Item(ItemID.ShadowbeamStaff),
            Item(ItemID.HeatRay),

            Item(ItemID.Torch, 9999),
            Item(ItemID.Glowstick, 9999),
            Item(ItemID.LunarCraftingStation),
            Item(ItemID.GreaterHealingPotion, 1999),
            Item(ItemID.GreaterManaPotion, 9999),
            Item(ItemID.WoodPlatform, 9999),
            Item(ItemID.Wood, 9999),

            Item(ItemID.SpectreHood),
            Item(ItemID.SpectreRobe),
            Item(ItemID.SpectrePants),
            Item(ItemID.SpectreMask),
            Item(ItemID.QueenSlimeMountSaddle),
            Item(ItemID.ChlorophytePlateMail),
            Item(ItemID.ChlorophyteGreaves),
            Item(ItemID.CrystalBall),
            Item(ItemID.Teacup, 9999),
            Item(ItemID.HeartLantern, 1999)
        ]
    };

    private static Loadout CreateWarrior() => new()
    {
        Armor = new()
        {
            Head = Def(ItemID.ChlorophyteMask),
            Body = Def(ItemID.ChlorophytePlateMail),
            Legs = Def(ItemID.ChlorophyteGreaves)
        },
        Accessories = new()
        {
            Accessory1 = Def(ItemID.BeetleWings),
            Accessory2 = Def(ItemID.Tabi),
            Accessory3 = Def(ItemID.CelestialStone),
            Accessory4 = Def(ItemID.FrozenTurtleShell),
            Accessory5 = Def(ItemID.PaladinsShield)
        },
        Equipment = new()
        {
            GrapplingHook = Def(ItemID.DualHook),
            Mount = Def(ItemID.QueenSlimeMountSaddle)
        },
        Inventory =
        [
            Item(ItemID.PaladinsHammer),
            Item(ItemID.StoneBlock, 9999),
            Item(ItemID.PickaxeAxe),
            Item(ItemID.Binoculars),
            Item(ItemID.Wood, 9999),
            Item(ItemID.IceRod),
            Item(ItemID.HoneyBucket),

            Item(ItemID.Kraken),
            Item(ItemID.ShadowJoustingLance),
            Item(ItemID.LightDisc),
            Item(ItemID.SniperRifle),
            Item(ItemID.ShadowbeamStaff),
            Item(ItemID.HighVelocityBullet, 9999),

            Item(ItemID.GreaterHealingPotion, 8888),
            Item(ItemID.Torch, 1888),
            Item(ItemID.Glowstick, 1999),
            Item(ItemID.MoneyTrough),
            Item(ItemID.HeartLantern, 1999),
            Item(ItemID.ChlorophytePartisan),
            Item(ItemID.FireGauntlet),
            Item(ItemID.Ale, 9999),
            Item(ItemID.QueenSlimeMountSaddle),
            Item(ItemID.Teacup, 1999),

            Item(ItemID.BeetleHelmet),
            Item(ItemID.BeetleScaleMail),
            Item(ItemID.BeetleLeggings),
            Item(ItemID.GolemFist)
        ]
    };

    #endregion

    #region Helpers

    private static ItemDefinition Def(int type) => new(type);

    private static LoadoutItem Item(int type, int stack = 1) => new()
    {
        Item = Def(type),
        Stack = stack
    };

    // Puts class/combat items on the hotbar (first inventory row) and the shared
    // base/utility items from the second row onward, padding empty hotbar slots.
    private static List<LoadoutItem> WithBaseRow(LoadoutItem[] hotbar, LoadoutItem[] baseItems)
    {
        List<LoadoutItem> inventory = [.. hotbar];
        while (inventory.Count < 10)
            inventory.Add(Item(ItemID.None));
        inventory.AddRange(baseItems);
        return inventory;
    }

    #endregion
}
