using System.Collections.Generic;
using CalamityMod;
using CalamityMod.Items;
using CalamityMod.Items.Materials;
using CalamityMod.Rarities;
using CalamityMod.Tiles.Furniture.CraftingStations;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using ThoriumMod.Items.BossThePrimordials.Dream;
using ThoriumMod.Items.HealerItems;

namespace InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes
{
    // ===== 神冥歧径（TwoPaths）重做：模块地图 =====
    // 本文件是物品本体，也是整套武器的入口：物品属性、配方、左右键分派、地上掉落时的发光层。
    // 全部操作只用鼠标左右键，不用任何额外按键：
    //   左键 → TwoPathsDarkHeld：暗攻击，正手、反手、720° 大旋转三段循环，每刀放出冤魂，大旋转结束时在附近敌人身上开暗裂隙。
    //   右键 → TwoPathsLightHeld：光攻击，瞄准光标挥砍，审判斩穿过光标，0.5 秒后同一条线上开光裂隙；每三刀一次 X。
    //   平衡状态下左右键都 → TwoPathsBalanceSpin：拿出整把双头镰持续旋转，合并光暗两边的攻击，同时消耗两边能量。
    // 其他模块：
    //   Core/Players/TwoPathsPlayer.cs     暗、光两条能量（0~100）：命中充能、脱战衰减、进入和退出平衡。
    //   Core/TwoPathsGauge.cs              脚下的光暗能量条 UI（只在本地客户端更新和绘制）。
    //   Core/TwoPathsSystem.cs             注册和接线：灵魂精华映射、能量条界面层、卸载清理。
    //   Core/Players/TwoPathsHoldLayer.cs  不攻击时握在手里的动态贴图（装了 WeaponOutLite 时让给它画）。
    //   Core/ModIntegrationsSystem.cs      向 WeaponOutLite 注册这把武器（不属于这次重做，沿用原代码）。
    //   Content/Projectiles/HealerPro/Scythes/ 下：
    //     TwoPathsHelper.cs        公用工具：贴图路径、握点几何、统一尺寸 SwingScale、刀光拖影绘制、配色、命中音。
    //     TwoPathsJudgmentSlash.cs 审判斩：一道快速划过的金白斩线。
    //     TwoPathsRift.cs          裂隙：光裂隙、暗裂隙、平衡裂隙（金边暗裂隙，再被一道审判斩交叉成 X）。
    //     TwoPathsSkull.cs         冤魂：追踪骷髅，借用灾厄盖尔鲜血头颅（GaelSkull）的贴图。
    //     TwoPathsErosion.cs       侵蚀斩痕：平衡旋转的审判斩命中后，在敌人身上反复浮现的暗色斩痕。
    //     TwoPathsPro.cs、TwoPathsPro2.cs 是重做前的旧弹幕，现在没有任何代码引用，留待清理。
    // 贴图（都是测试美术，等待重绘；生成脚本在仓库外的 TwoPathsWork/tools，不在仓库里）：
    //   TwoPaths.png               攻击弹幕画的静态整把镰刀，208×190，整张按 2×2 像素块绘制。
    //   TwoPathsAnimated.png       物品栏、地上、手持用的 20 帧动态贴图（每帧 3 tick）；_Glow 是同排版的发光层。
    //   TwoPathsHalfLight/Dark.png 光端、暗端两半，和 TwoPaths.png 同一张画布（叠在一起就是整把镰刀）。
    //   TwoPathsPhantomDark.png    左键的大虚影：暗端刀头加一小截杆，杆往手的方向渐隐。
    //   Assets/Textures/TwoPathsGauge/ 能量条的各个部件。
    // 通用约定：1 tick = 1/60 秒；角度一般是弧度（个别数组用度数，会注明）；长度单位是世界像素。
    // 伤害类型是瑟银的治疗者（镰刀继承瑟银的 ScytheItem / ScythePro），直接命中给灵魂精华。
    public class TwoPaths : ScytheItem
    {
        // 左键一刀的时长 = 物品使用时间 × 这个倍数。useTime 是 30，所以基础攻速下一刀约 60 tick，
        // 节奏参照 Calamity Hunt 的 Scythe of the Old God（设计记录里叫“旧神巨镰”）。
        private const float DarkTimeScale = 2f;

        // 右键每挥一刀换一次方向（正手、反手交替），由 TwoPathsLightHeld 读 Projectile.direction 使用。
        private int swingDirection = 1;
        // 右键计数，在 1~3 之间循环。TwoPathsLightHeld 生成时读它，读到 3 时这一刀是 X 字终结（两道审判斩交叉）。
        // 生成弹幕时先读后加，刚拿起武器时从 0 开始，所以第一次 X 是第 4 刀，之后每 3 刀一次。
        internal int spin;
        // 左键段数：0 正手、1 反手、2 大旋转。每次出手推进一段，点按也会接着连段走，中途停手不会重置。
        private int darkStage;

        // 物品用动态贴图（20 帧 × 3 tick：两端各自颜色的闪电、呼吸的光环、滴血、刃口高光；由 tools/make_held_animation.py 生成）。
        // TwoPaths.png 仍是攻击弹幕画的那张静态单帧。
        public override string Texture => base.Texture + "Animated";

        public override void SetStaticDefaults()
        {
            SetStaticDefaultsToScythe();
            // 每 3 tick 换一帧，共 20 帧。AnimatesAsSoul 让物品栏里也播放动画。
            Main.RegisterItemAnimation(Type, new DrawAnimationVertical(3, 20));
            ItemID.Sets.AnimatesAsSoul[Type] = true;
            // 允许按住右键连续出手。
            ItemID.Sets.ItemsThatAllowRepeatedRightClick[Type] = true;
            // 不播放物品自带的使用音效，挥砍音效由各个攻击弹幕在出刀那一刻自己播放。
            ItemID.Sets.SkipsInitialUseSound[Type] = true;
        }

        public override void SetDefaults()
        {
            SetDefaultsToScythe();
            Item.damage = 1550;
            // 直接命中给 4 层灵魂精华（瑟银治疗者机制）。
            scytheSoulCharge = 4;
            Item.width = 208;
            Item.height = 190;
            Item.value = CalamityGlobalItem.RarityHotPinkBuyPrice;
            Item.rare = ModContent.RarityType<HotPink>();
            // 左键默认发射暗攻击；右键和平衡状态在 Shoot 里另行生成。
            Item.shoot = ModContent.ProjectileType<TwoPathsDarkHeld>();
            Item.useTime = Item.useAnimation = 30;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.holdStyle = 6;
            // 原版不画手里的物品：攻击时由弹幕自己画，平时由 TwoPathsHoldLayer 画。
            Item.noUseGraphic = true;
            Item.autoReuse = true;
            Item.Calamity().devItem = true;
        }

        // 配方沿用旧版：按是否装了 Ragnarok 和 Calamity Bard & Healer 两个模组，换用不同的前置武器和材料，在嘉登熔炉制作。
        public override void AddRecipes()
        {
            InfernalEclipseWeaponsDLC IEWeaponDLC = InfernalEclipseWeaponsDLC.Instance;

            Recipe recipe = Recipe.Create(Item.type, 1);

            if (IEWeaponDLC.ragnarok != null && IEWeaponDLC.calamitybardhealer != null)
            {
                recipe.AddIngredient(IEWeaponDLC.ragnarok.Find<ModItem>("AuricGreatscythe").Type, 1);
                recipe.AddIngredient(IEWeaponDLC.calamitybardhealer.Find<ModItem>("Disaster").Type, 1);
            }
            else if (IEWeaponDLC.ragnarok != null)
            {
                recipe.AddIngredient(IEWeaponDLC.ragnarok.Find<ModItem>("AuricGreatscythe").Type, 1);
                recipe.AddIngredient(ModContent.ItemType<AshesofAnnihilation>(), 10);
            }
            else if (IEWeaponDLC.calamitybardhealer != null)
            {
                recipe.AddIngredient(ModContent.ItemType<RealitySlasher>(), 1);
                recipe.AddIngredient(IEWeaponDLC.calamitybardhealer.Find<ModItem>("Disaster").Type, 1);
            }
            else
            {
                recipe.AddIngredient(ModContent.ItemType<RealitySlasher>(), 1);
                recipe.AddIngredient(ModContent.ItemType<AshesofAnnihilation>(), 10);
            }
            recipe.AddIngredient(ModContent.ItemType<ShadowspecBar>(), 5);
            recipe.AddTile(ModContent.TileType<DraedonsForge>());
            recipe.Register();
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            // 两行紫色背景故事文本（本地化键 ItemTooltip.TwoSideLore1/2）。
            tooltips.Add(new TooltipLine(Mod, "TwoSideLore1", Language.GetTextValue("Mods.InfernalEclipseWeaponsDLC.ItemTooltip.TwoSideLore1")) { OverrideColor = Color.MediumPurple });
            tooltips.Add(new TooltipLine(Mod, "TwoSideLore2", Language.GetTextValue("Mods.InfernalEclipseWeaponsDLC.ItemTooltip.TwoSideLore2")) { OverrideColor = Color.MediumPurple });
        }

        // 启用右键（altFunctionUse == 2）。
        public override bool AltFunctionUse(Player player) => true;

        // 同一时间只能有一个攻击：左键、右键或平衡旋转的弹幕还在时，不能再次出手。
        // 所以挥砍途中的点击会被忽略，每一刀都会完整播完。
        public override bool CanUseItem(Player player)
        {
            if (player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsDarkHeld>()] > 0 || player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsLightHeld>()] > 0
                || player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsBalanceSpin>()] > 0)
                return false;
            return base.CanUseItem(player);
        }

        // 左右键分派。弹幕都在这里自己生成，返回 false 阻止原版再按 Item.shoot 发射一次。
        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Vector2 aim = (Main.MouseWorld - player.MountedCenter).SafeNormalize(Vector2.UnitX * player.direction);
            // 已经算上攻速的出手时长（tick），作为攻击弹幕的总时长传进去（ai[0] 和 ai[1]）。
            float time = player.itemAnimationMax > 0 ? (player.itemAnimationMax > player.itemTimeMax ? player.itemTimeMax : player.itemAnimationMax) : Item.useAnimation;
            // 平衡状态下左右键都拿出整把镰刀旋转。ai[0] 是旋转方向，跟随角色朝向。
            if (player.GetModPlayer<TwoPathsPlayer>().Balance)
            {
                Projectile.NewProjectile(source, player.MountedCenter, aim, ModContent.ProjectileType<TwoPathsBalanceSpin>(), damage, knockback, player.whoAmI, player.direction);
                return false;
            }
            // 右键：光攻击。挥砍方向交替；生成后再推进右键计数（见 spin 的注释）。
            if (player.altFunctionUse == 2)
            {
                int index = Projectile.NewProjectile(source, player.MountedCenter, aim, ModContent.ProjectileType<TwoPathsLightHeld>(), damage, knockback, player.whoAmI, time, time);
                Main.projectile[index].direction = swingDirection;
                Main.projectile[index].netUpdate = true;
                swingDirection = -swingDirection;
                if (++spin > 3)
                    spin = 1;
                return false;
            }

            // 左键：暗攻击。时长是右键的 DarkTimeScale 倍，ai[2] 传当前段数，然后推进到下一段。
            float darkTime = time * DarkTimeScale;
            Projectile.NewProjectile(source, player.MountedCenter, aim, type, damage, knockback, player.whoAmI, darkTime, darkTime, darkStage);
            darkStage = (darkStage + 1) % 3;
            return false;
        }

        // 掉在地上时，把动态贴图的发光部分（闪电、光环光晕、刃口高光）以全亮叠画一遍，暗处也看得清。
        public override void PostDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, float rotation, float scale, int whoAmI)
        {
            Texture2D glow = ModContent.Request<Texture2D>(Texture + "_Glow").Value;
            Rectangle frame = Main.itemAnimations[Type].GetFrame(glow);
            Vector2 origin = frame.Size() / 2f;
            spriteBatch.Draw(glow, Item.position - Main.screenPosition + origin, frame, Color.White, rotation, origin, scale, SpriteEffects.None, 0f);
        }

        // holdStyle 6 下原版握持位置的微调。原版不会画出这把武器（noUseGraphic），
        // 手里看到的镰刀由 TwoPathsHoldLayer 或 WeaponOutLite 绘制。
        public override void HoldStyle(Player player, Rectangle heldItemFrame)
        {
            player.itemLocation += new Vector2(-15f, 12f) * player.Directions;
        }

        // 攻击期间清零原版的物品位置，攻击画面全部由攻击弹幕自己绘制。
        public override void UseStyle(Player player, Rectangle heldItemFrame)
        {
            player.itemLocation = Vector2.Zero;
        }
    }
}
