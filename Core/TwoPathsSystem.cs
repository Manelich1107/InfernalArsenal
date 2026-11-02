using System.Collections.Generic;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using ThoriumMod.Items.HealerItems;
using ThoriumMod.UI.ResourceBars;

namespace InfernalEclipseWeaponsDLC.Core
{
    // 【神冥歧径的全局接线】
    // - 把右键攻击和平衡旋转登记进瑟银的灵魂精华映射，让它们直接命中也给灵魂精华。
    // - 每帧更新能量条状态，并把能量条作为一个界面层插进游戏 UI，叠在瑟银的计量条栈里。
    // - 卸载模组时释放刀光拖影用的绘图资源。
    public class TwoPathsSystem : ModSystem
    {
        public override void PostSetupContent()
        {
            // 瑟银只按物品的 Item.shoot（这里是左键的 TwoPathsDarkHeld）发灵魂精华；右键和平衡旋转是另外的弹幕，要手动登记，各给 4 层。
            ScytheItem.ProToScytheCharge?.TryAdd(ModContent.ProjectileType<TwoPathsLightHeld>(), 4);
            ScytheItem.ProToScytheCharge?.TryAdd(ModContent.ProjectileType<TwoPathsBalanceSpin>(), 4);
        }

        public override void Unload()
        {
            TwoPathsHelper.Unload();
        }

        // 能量条只属于本地玩家，服务器上不需要。
        public override void PostUpdateEverything()
        {
            if (!Main.dedServ)
                TwoPathsGauge.Update();
        }

        // 插在原版“游戏内设置”层之前，和其他 HUD 一起绘制。
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int index = layers.FindIndex(layer => layer.Name == "Vanilla: Ingame Options");
            if (index == -1)
                return;

            layers.Insert(index, new LegacyGameInterfaceLayer("InfernalEclipseWeaponsDLC: Two Paths Gauge", DrawGauge, InterfaceScaleType.Game));
        }

        // 打开设置或其他游戏内界面时不画。画完把瑟银计量条栈的偏移往下推 StackHeight，
        // 这样和瑟银自己画在脚下的计量条上下排开，不会重叠。
        private static bool DrawGauge()
        {
            if (Main.ingameOptionsWindow || Main.InGameUI.IsVisible || !TwoPathsGauge.Visible)
                return true;

            TwoPathsGauge.Draw(Main.LocalPlayer, InterfaceResource.ThoriumGaugeOffset);
            InterfaceResource.IncreaseThoriumGaugeOffset(TwoPathsGauge.StackHeight);
            return true;
        }
    }
}
