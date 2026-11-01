using System.Collections.Generic;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using ThoriumMod.Items.HealerItems;
using ThoriumMod.UI.ResourceBars;

namespace InfernalEclipseWeaponsDLC.Core
{
    public class TwoPathsSystem : ModSystem
    {
        public override void PostSetupContent()
        {
            // Thorium only maps soul essence for Item.shoot; the right click swing and the balance spin are other projectiles.
            ScytheItem.ProToScytheCharge?.TryAdd(ModContent.ProjectileType<TwoPathsLightHeld>(), 4);
            ScytheItem.ProToScytheCharge?.TryAdd(ModContent.ProjectileType<TwoPathsBalanceSpin>(), 4);
        }

        public override void Unload()
        {
            TwoPathsHelper.Unload();
        }

        public override void PostUpdateEverything()
        {
            if (!Main.dedServ)
                TwoPathsGauge.Update();
        }

        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
        {
            int index = layers.FindIndex(layer => layer.Name == "Vanilla: Ingame Options");
            if (index == -1)
                return;

            layers.Insert(index, new LegacyGameInterfaceLayer("InfernalEclipseWeaponsDLC: Two Paths Gauge", DrawGauge, InterfaceScaleType.Game));
        }

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
