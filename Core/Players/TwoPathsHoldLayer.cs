using System;
using InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Core.Players
{
    // Scythes set noUseGraphic, so vanilla never draws Two Paths in hand. While it is not attacking, this layer holds the
    // animated sprite just below the middle of the shaft in the front hand, slanted the way the item art is drawn: the light blade,
    // halo and angel wing up behind the head, the dark blade and bat wing down in front (it may dip into the ground, which
    // the user is fine with), swaying a little. It is drawn behind the body so the blades and wings never cover the
    // player; the front arm is drawn over it. WeaponOutLite draws held weapons itself (the mod
    // registers Two Paths with it), so the layer stays out of the way when that mod is loaded.
    public class TwoPathsHoldLayer : PlayerDrawLayer
    {
        private const float Lean = 0.8f;          // from straight up toward the back; about the item art's own slant
        private const float HandAngle = 0.35f;
        private const float GripTowardDark = 8f;   // texture pixels; shifts the whole scythe a little toward its light end

        public static bool ShowsIdleHold(Player player)
        {
            if (player.dead || player.itemAnimation > 0 || player.HeldItem.type != ModContent.ItemType<TwoPaths>() || ModLoader.HasMod("WeaponOutLite"))
                return false;
            return player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsDarkHeld>()] == 0
                && player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsLightHeld>()] == 0
                && player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsBalanceSpin>()] == 0;
        }

        // The front arm reaches forward, a little below level, to the grip (0 is straight down in composite arm terms).
        public static float ArmRotation(Player player) => (MathHelper.PiOver2 - HandAngle) * -player.direction;

        public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.Skin);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) => drawInfo.shadow == 0f && ShowsIdleHold(drawInfo.drawPlayer);

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            int type = ModContent.ItemType<TwoPaths>();
            Texture2D texture = TextureAssets.Item[type].Value;
            Rectangle frame = Main.itemAnimations[type]?.GetFrame(texture) ?? texture.Frame();

            Vector2 hand = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, ArmRotation(player));
            Vector2 position = (hand + new Vector2(0f, player.gfxOffY) - Main.screenPosition).Floor();
            float sway = 0.04f * MathF.Sin(Main.GlobalTimeWrappedHourly * 2f);
            float lightRotation = -MathHelper.PiOver2 - (Lean + sway) * player.direction;
            Color color = Color.Lerp(Lighting.GetColor(hand.ToTileCoordinates()), Color.White, 0.4f);
            bool flip = player.direction < 0;
            // The same size as the attacks, so the scythe doesn't jump in size when a swing starts.
            float scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            drawInfo.DrawDataCache.Add(TwoPathsHelper.HeldFrameDrawData(texture, frame, position, lightRotation, GripTowardDark, scale, color, flip));

            // The emissive parts (lightning, halo glow, edge glint) at full brightness. An extra additive pass washed the
            // gold and violet out to white, so there is none.
            Texture2D glow = ModContent.Request<Texture2D>(ModContent.GetInstance<TwoPaths>().Texture + "_Glow").Value;
            drawInfo.DrawDataCache.Add(TwoPathsHelper.HeldFrameDrawData(glow, frame, position, lightRotation, GripTowardDark, scale, Color.White, flip));
        }
    }
}
