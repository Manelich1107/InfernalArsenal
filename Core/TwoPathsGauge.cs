using System;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Core
{
    // The light/dark gauge under the player (scheme A; test art in Assets/Textures/TwoPathsGauge, made by
    // tools/make_gauge_textures.py). Dark fills from the left end and light from the right end toward the center node.
    // Entering and leaving balance bursts the node (the merge of the two halves plays on the player, see TwoPathsBalanceSpin).
    // During balance both halves glow and what is left of the energy drains toward the node.
    // Updated and drawn on the local client only.
    public static class TwoPathsGauge
    {
        public const int StackHeight = 30;

        private const string TexturePath = "InfernalEclipseWeaponsDLC/Assets/Textures/TwoPathsGauge/";
        private const float Gap = 20f;
        private const int Width = 120;
        private const int Height = 28;
        private const int FillLength = 36;
        private const int FillTop = 12;
        private const int FillHeight = 8;
        private const int DarkFillLeft = 12;
        private const int LightFillRight = 108;
        private const int NodeLeft = 48;
        private const int NodeSize = 24;
        private const int FadeTime = 40;
        private const int FlashTime = 12;

        private static float visibility;
        private static int ticks;
        private static bool wasBalance;
        private static bool wasDarkFull;
        private static bool wasLightFull;
        private static int balanceFlash;
        private static int darkCapFlash;
        private static int lightCapFlash;
        private static float lastDark;
        private static float lastLight;
        private static float darkGainFrom;
        private static float lightGainFrom;
        private static int darkGainFlash;
        private static int lightGainFlash;

        public static bool Visible => visibility > 0f;

        public static void Update()
        {
            Player player = Main.LocalPlayer;
            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            bool shown = modPlayer.HoldingTwoPaths && !player.dead && !player.ghost;
            visibility = shown ? MathF.Min(visibility + 0.1f, 1f) : MathF.Max(visibility - 1f / FadeTime, 0f);
            ticks++;

            if (modPlayer.Balance != wasBalance)
            {
                wasBalance = modPlayer.Balance;
                balanceFlash = FlashTime;
            }

            // A side filling up lights its cap with a flash and a chime; reaching balance bursts the node instead.
            bool darkFull = modPlayer.Dark >= TwoPathsPlayer.MaxEnergy;
            bool lightFull = modPlayer.Light >= TwoPathsPlayer.MaxEnergy;
            if (darkFull && !wasDarkFull && !modPlayer.Balance)
            {
                darkCapFlash = FlashTime;
                if (shown)
                    SoundEngine.PlaySound(SoundID.MaxMana with { Pitch = -0.4f });
            }
            if (lightFull && !wasLightFull && !modPlayer.Balance)
            {
                lightCapFlash = FlashTime;
                if (shown)
                    SoundEngine.PlaySound(SoundID.MaxMana with { Pitch = 0.2f });
            }
            wasDarkFull = darkFull;
            wasLightFull = lightFull;

            // The part just gained flashes white.
            if (modPlayer.Dark > lastDark + 0.5f && !modPlayer.Balance)
            {
                darkGainFrom = lastDark;
                darkGainFlash = FlashTime;
            }
            if (modPlayer.Light > lastLight + 0.5f && !modPlayer.Balance)
            {
                lightGainFrom = lastLight;
                lightGainFlash = FlashTime;
            }
            lastDark = modPlayer.Dark;
            lastLight = modPlayer.Light;
            darkCapFlash = Math.Max(darkCapFlash - 1, 0);
            lightCapFlash = Math.Max(lightCapFlash - 1, 0);
            darkGainFlash = Math.Max(darkGainFlash - 1, 0);
            lightGainFlash = Math.Max(lightGainFlash - 1, 0);
            balanceFlash = Math.Max(balanceFlash - 1, 0);
        }

        // stackOffset is the Thorium gauge stack offset; the capture tool passes 0.
        public static void Draw(Player player, float stackOffset)
        {
            if (visibility <= 0f)
                return;

            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            SpriteBatch spriteBatch = Main.spriteBatch;
            Vector2 topLeft = (player.Bottom + new Vector2(0f, Gap + stackOffset) - Main.screenPosition - new Vector2(Width / 2, Height / 2)).Floor();
            Vector2 node = topLeft + new Vector2(NodeLeft + NodeSize / 2, Height / 2);
            float alpha = visibility;
            bool balance = modPlayer.Balance;
            bool darkFull = modPlayer.Dark >= TwoPathsPlayer.MaxEnergy;
            bool lightFull = modPlayer.Light >= TwoPathsPlayer.MaxEnergy;
            float breath = 0.5f + 0.5f * MathF.Sin(ticks * 0.15f);

            if (balance)
            {
                // Breathing halo: violet behind the dark half, gold behind the light half.
                DrawGlow(topLeft + new Vector2(30f, 14f), new Vector2(1.3f, 0.7f), TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, (0.25f + 0.2f * breath) * alpha));
                DrawGlow(topLeft + new Vector2(90f, 14f), new Vector2(1.3f, 0.7f), TwoPathsHelper.Additive(TwoPathsHelper.LightGold, (0.25f + 0.2f * breath) * alpha));
            }

            spriteBatch.Draw(Texture("Frame"), topLeft, Color.White * alpha);
            DrawFills(modPlayer, topLeft, alpha, balance, darkFull, lightFull);

            Texture2D caps = Texture("CapsLit");
            if (darkFull || balance)
                spriteBatch.Draw(caps, topLeft + new Vector2(0f, 1f), new Rectangle(0, 0, 18, 26), Color.White * alpha);
            if (lightFull || balance)
                spriteBatch.Draw(caps, topLeft + new Vector2(102f, 1f), new Rectangle(18, 0, 18, 26), Color.White * alpha);
            if (darkCapFlash > 0)
                DrawGlow(topLeft + new Vector2(9f, 14f), new Vector2(0.8f), TwoPathsHelper.Additive(TwoPathsHelper.DarkCrimson, darkCapFlash / (float)FlashTime * alpha));
            if (lightCapFlash > 0)
                DrawGlow(topLeft + new Vector2(111f, 14f), new Vector2(0.8f), TwoPathsHelper.Additive(TwoPathsHelper.LightGold, lightCapFlash / (float)FlashTime * alpha));

            int frame = balance ? 3 + ticks / 8 % 4 : lightFull ? 1 : darkFull ? 2 : 0;
            if (balanceFlash > FlashTime - 4)
                frame = 7;
            spriteBatch.Draw(Texture("Node"), topLeft + new Vector2(NodeLeft, 0f), new Rectangle(frame * NodeSize, 0, NodeSize, Height), Color.White * alpha);
            if (balanceFlash > 0)
                DrawBurst(node, balanceFlash / (float)FlashTime, alpha);
        }

        private static void DrawFills(TwoPathsPlayer modPlayer, Vector2 topLeft, float alpha, bool balance, bool darkFull, bool lightFull)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            int darkWidth = (int)MathF.Round(FillLength * MathHelper.Clamp(modPlayer.Dark / TwoPathsPlayer.MaxEnergy, 0f, 1f));
            int lightWidth = (int)MathF.Round(FillLength * MathHelper.Clamp(modPlayer.Light / TwoPathsPlayer.MaxEnergy, 0f, 1f));
            int scroll = ticks / 2 % FillLength;

            // Outside balance each side fills from its outer end; in balance what is left sits against the node.
            // The strips scroll so both flow toward the node.
            int darkLeft = balance ? DarkFillLeft + FillLength - darkWidth : DarkFillLeft;
            int lightLeft = balance ? LightFillRight - FillLength : LightFillRight - lightWidth;
            if (darkWidth > 0)
            {
                int source = (FillLength - scroll) % FillLength + darkLeft - DarkFillLeft;
                spriteBatch.Draw(Texture("FillDark"), topLeft + new Vector2(darkLeft, FillTop), new Rectangle(source, darkFull || balance ? FillHeight : 0, darkWidth, FillHeight), Color.White * alpha);
            }
            if (lightWidth > 0)
            {
                int source = scroll + lightLeft - (LightFillRight - FillLength);
                spriteBatch.Draw(Texture("FillLight"), topLeft + new Vector2(lightLeft, FillTop), new Rectangle(source, lightFull || balance ? FillHeight : 0, lightWidth, FillHeight), Color.White * alpha);
            }

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            if (balance)
            {
                // Sparks run along the top rail toward the node.
                for (int x = 0; x < FillLength; x++)
                {
                    if (((x - ticks / 2) % 6 + 6) % 6 == 0)
                        spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + DarkFillLeft + x, (int)topLeft.Y + 10, 1, 1), new Color(236, 200, 255) * alpha);
                    if ((x + ticks / 2) % 6 == 0)
                        spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + LightFillRight - FillLength + x, (int)topLeft.Y + 10, 1, 1), Color.White * alpha);
                }
                return;
            }

            if (darkGainFlash > 0)
            {
                int from = (int)MathF.Round(FillLength * MathHelper.Clamp(darkGainFrom / TwoPathsPlayer.MaxEnergy, 0f, 1f));
                if (darkWidth > from)
                    spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + DarkFillLeft + from, (int)topLeft.Y + FillTop, darkWidth - from, FillHeight), TwoPathsHelper.Additive(Color.White, 0.8f * darkGainFlash / FlashTime * alpha));
            }
            if (lightGainFlash > 0)
            {
                int from = (int)MathF.Round(FillLength * MathHelper.Clamp(lightGainFrom / TwoPathsPlayer.MaxEnergy, 0f, 1f));
                if (lightWidth > from)
                    spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + LightFillRight - lightWidth, (int)topLeft.Y + FillTop, lightWidth - from, FillHeight), TwoPathsHelper.Additive(Color.White, 0.8f * lightGainFlash / FlashTime * alpha));
            }

            // Bright leading edges (Front.png: dark at x 2 with its glow at x 3, light glow at x 4 and light at x 5).
            Texture2D front = Texture("Front");
            if (darkWidth > 0 && darkWidth < FillLength)
                spriteBatch.Draw(front, topLeft + new Vector2(DarkFillLeft + darkWidth - 1, FillTop), new Rectangle(2, 0, 2, FillHeight), Color.White * alpha);
            if (lightWidth > 0 && lightWidth < FillLength)
                spriteBatch.Draw(front, topLeft + new Vector2(LightFillRight - lightWidth - 1, FillTop), new Rectangle(4, 0, 2, FillHeight), Color.White * alpha);
        }

        // White flash, a four-point star and a ring of gold and violet sparks.
        private static void DrawBurst(Vector2 node, float flash, float alpha)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            DrawGlow(node, new Vector2(0.35f + 0.55f * (1f - flash)), TwoPathsHelper.Additive(Color.White, 0.55f * flash * alpha));

            Texture2D ray = ModContent.Request<Texture2D>("Terraria/Images/Extra_98", AssetRequestMode.ImmediateLoad).Value;
            Vector2 rayScale = new(0.35f, 90f / ray.Height * (0.5f + 0.5f * flash));
            for (int i = 0; i < 2; i++)
                spriteBatch.Draw(ray, node, null, TwoPathsHelper.Additive(Color.White, flash * alpha), MathHelper.PiOver2 * i, ray.Size() / 2f, rayScale, SpriteEffects.None, 0f);
            for (int i = 0; i < 2; i++)
                spriteBatch.Draw(ray, node, null, TwoPathsHelper.Additive(i == 0 ? TwoPathsHelper.LightGold : TwoPathsHelper.DarkViolet, flash * alpha), MathHelper.PiOver4 + MathHelper.PiOver2 * i, ray.Size() / 2f, rayScale * 0.6f, SpriteEffects.None, 0f);

            Texture2D pixel = TextureAssets.MagicPixel.Value;
            for (int i = 0; i < 16; i++)
            {
                Vector2 position = node + (MathHelper.TwoPi * i / 16f + 0.2f).ToRotationVector2() * (4f + 38f * (1f - flash));
                Color color = i % 2 == 0 ? TwoPathsHelper.LightGold : TwoPathsHelper.DarkViolet;
                spriteBatch.Draw(pixel, new Rectangle((int)position.X - 1, (int)position.Y - 1, 2, 2), color * (flash * alpha));
            }
        }

        private static void DrawGlow(Vector2 position, Vector2 scale, Color color)
        {
            Texture2D glow = Texture("Glow");
            Main.spriteBatch.Draw(glow, position, null, color, 0f, glow.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private static Texture2D Texture(string name) => ModContent.Request<Texture2D>(TexturePath + name, AssetRequestMode.ImmediateLoad).Value;
    }
}
