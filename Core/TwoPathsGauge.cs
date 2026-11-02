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
    // 【光暗能量条】画在玩家脚下（方案 A“汇流”，左暗右光）。
    // 测试美术在 Assets/Textures/TwoPathsGauge/，由仓库外的 tools/make_gauge_textures.py 生成。
    // 整条 120×28，1 个贴图像素画成 1 个屏幕像素（注意：和武器贴图的 2×2 像素块风格不一致，重绘时可以改）。
    // - 左端蝠翼和暗槽，右端天使羽翼和光槽，中间是平衡节点。暗能量从左端、光能量从右端向中间节点填充。
    // - 新增的那一段白闪；一端充满时这一端的端饰点亮、闪光并播提示音。
    // - 进入和退出平衡时节点爆闪（两半镰刀合一的动画在人物身上播放，见 TwoPathsBalanceSpin）。
    // - 平衡期间两半透出呼吸光，顶边火星向节点流动，剩余能量贴着节点向中间收拢。
    // - 手持这把武器时淡入，切走后淡出。
    // 只在本地客户端更新和绘制：TwoPathsSystem 每帧调用 Update，在界面层里调用 Draw。数值全部读自 TwoPathsPlayer。
    public static class TwoPathsGauge
    {
        // 在瑟银计量条栈里占的高度，画完后栈往下推这么多。
        public const int StackHeight = 30;

        private const string TexturePath = "InfernalEclipseWeaponsDLC/Assets/Textures/TwoPathsGauge/";
        // 能量条中心离玩家脚底的距离（像素）。
        private const float Gap = 20f;
        private const int Width = 120;
        private const int Height = 28;
        // 每一边的能量槽：长 36、高 8，顶边在 y = 12。暗槽从 x = 12 往右，光槽到 x = 108 为止。
        private const int FillLength = 36;
        private const int FillTop = 12;
        private const int FillHeight = 8;
        private const int DarkFillLeft = 12;
        private const int LightFillRight = 108;
        // 中间节点：从 x = 48 开始，宽 24（Node.png 里每帧也是 24 宽）。
        private const int NodeLeft = 48;
        private const int NodeSize = 24;
        // 淡出用时、各种闪光的持续时间（tick）。
        private const int FadeTime = 40;
        private const int FlashTime = 12;

        // 0~1 的整体透明度，手持时淡入、切走时淡出。
        private static float visibility;
        private static int ticks;
        // 上一帧的状态，用来检测“刚刚充满”“刚刚进入/退出平衡”“刚刚涨了一段”。
        private static bool wasBalance;
        private static bool wasDarkFull;
        private static bool wasLightFull;
        // 各种闪光的剩余 tick。
        private static int balanceFlash;
        private static int darkCapFlash;
        private static int lightCapFlash;
        private static float lastDark;
        private static float lastLight;
        // 涨能量前的数值，白闪从这里画到当前值。
        private static float darkGainFrom;
        private static float lightGainFrom;
        private static int darkGainFlash;
        private static int lightGainFlash;

        public static bool Visible => visibility > 0f;

        // 每帧更新一次（TwoPathsSystem.PostUpdateEverything）：淡入淡出、检测状态变化、倒数闪光。
        public static void Update()
        {
            Player player = Main.LocalPlayer;
            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            bool shown = modPlayer.HoldingTwoPaths && !player.dead && !player.ghost;
            // 10 tick 淡入，FadeTime tick 淡出。
            visibility = shown ? MathF.Min(visibility + 0.1f, 1f) : MathF.Max(visibility - 1f / FadeTime, 0f);
            ticks++;

            // 进入或退出平衡：节点爆闪。
            if (modPlayer.Balance != wasBalance)
            {
                wasBalance = modPlayer.Balance;
                balanceFlash = FlashTime;
            }

            // 一端刚充满：端饰闪光并播提示音（暗端音调低、光端音调高）。进入平衡那一下改由节点爆闪表现。
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

            // 刚涨的那一段白闪（平衡期间不充能，不会触发）。
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

        // 绘制顺序：平衡呼吸光 → 框体 → 能量槽 → 端饰 → 节点 → 节点爆闪。
        // stackOffset 是瑟银计量条栈当前的偏移（多条计量条上下叠放用）；录制工具调用时传 0。
        public static void Draw(Player player, float stackOffset)
        {
            if (visibility <= 0f)
                return;

            TwoPathsPlayer modPlayer = player.GetModPlayer<TwoPathsPlayer>();
            SpriteBatch spriteBatch = Main.spriteBatch;
            // 能量条中心在脚底下方 Gap 处，取整到像素，像素画才不会糊。
            Vector2 topLeft = (player.Bottom + new Vector2(0f, Gap + stackOffset) - Main.screenPosition - new Vector2(Width / 2, Height / 2)).Floor();
            Vector2 node = topLeft + new Vector2(NodeLeft + NodeSize / 2, Height / 2);
            float alpha = visibility;
            bool balance = modPlayer.Balance;
            bool darkFull = modPlayer.Dark >= TwoPathsPlayer.MaxEnergy;
            bool lightFull = modPlayer.Light >= TwoPathsPlayer.MaxEnergy;
            float breath = 0.5f + 0.5f * MathF.Sin(ticks * 0.15f);

            if (balance)
            {
                // 呼吸光晕：暗的一半后面透紫光，光的一半后面透金光。
                DrawGlow(topLeft + new Vector2(30f, 14f), new Vector2(1.3f, 0.7f), TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, (0.25f + 0.2f * breath) * alpha));
                DrawGlow(topLeft + new Vector2(90f, 14f), new Vector2(1.3f, 0.7f), TwoPathsHelper.Additive(TwoPathsHelper.LightGold, (0.25f + 0.2f * breath) * alpha));
            }

            spriteBatch.Draw(Texture("Frame"), topLeft, Color.White * alpha);
            DrawFills(modPlayer, topLeft, alpha, balance, darkFull, lightFull);

            // 端饰点亮：CapsLit.png 左 18 像素是暗端（蝠翼），右 18 像素是光端（天使羽翼）。一端满了或平衡时点亮。
            Texture2D caps = Texture("CapsLit");
            if (darkFull || balance)
                spriteBatch.Draw(caps, topLeft + new Vector2(0f, 1f), new Rectangle(0, 0, 18, 26), Color.White * alpha);
            if (lightFull || balance)
                spriteBatch.Draw(caps, topLeft + new Vector2(102f, 1f), new Rectangle(18, 0, 18, 26), Color.White * alpha);
            if (darkCapFlash > 0)
                DrawGlow(topLeft + new Vector2(9f, 14f), new Vector2(0.8f), TwoPathsHelper.Additive(TwoPathsHelper.DarkCrimson, darkCapFlash / (float)FlashTime * alpha));
            if (lightCapFlash > 0)
                DrawGlow(topLeft + new Vector2(111f, 14f), new Vector2(0.8f), TwoPathsHelper.Additive(TwoPathsHelper.LightGold, lightCapFlash / (float)FlashTime * alpha));

            // Node.png 横排 8 帧：0 普通、1 光端满、2 暗端满、3~6 平衡呼吸（每帧 8 tick 循环）、7 白闪。
            // 进入或退出平衡后的前 4 tick 显示白闪帧。
            int frame = balance ? 3 + ticks / 8 % 4 : lightFull ? 1 : darkFull ? 2 : 0;
            if (balanceFlash > FlashTime - 4)
                frame = 7;
            spriteBatch.Draw(Texture("Node"), topLeft + new Vector2(NodeLeft, 0f), new Rectangle(frame * NodeSize, 0, NodeSize, Height), Color.White * alpha);
            if (balanceFlash > 0)
                DrawBurst(node, balanceFlash / (float)FlashTime, alpha);
        }

        // 两边的能量槽。FillDark.png / FillLight.png 上下两行：第 1 行是普通状态，第 2 行是充满或平衡时更亮的版本；
        // 横向是可以循环滚动的纹理。
        private static void DrawFills(TwoPathsPlayer modPlayer, Vector2 topLeft, float alpha, bool balance, bool darkFull, bool lightFull)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            int darkWidth = (int)MathF.Round(FillLength * MathHelper.Clamp(modPlayer.Dark / TwoPathsPlayer.MaxEnergy, 0f, 1f));
            int lightWidth = (int)MathF.Round(FillLength * MathHelper.Clamp(modPlayer.Light / TwoPathsPlayer.MaxEnergy, 0f, 1f));
            int scroll = ticks / 2 % FillLength;

            // 平常各自从外端往里填；平衡时剩余的能量贴着节点，随消耗从外侧往节点收拢。
            // 纹理每 2 tick 滚动 1 像素，两边都朝节点方向流动。
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
                // 平衡时：顶边一排火星向节点流动（暗的一半淡紫，光的一半白色），每 6 像素一颗。平衡时不画白闪和前沿亮点。
                for (int x = 0; x < FillLength; x++)
                {
                    if (((x - ticks / 2) % 6 + 6) % 6 == 0)
                        spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + DarkFillLeft + x, (int)topLeft.Y + 10, 1, 1), new Color(236, 200, 255) * alpha);
                    if ((x + ticks / 2) % 6 == 0)
                        spriteBatch.Draw(pixel, new Rectangle((int)topLeft.X + LightFillRight - FillLength + x, (int)topLeft.Y + 10, 1, 1), Color.White * alpha);
                }
                return;
            }

            // 刚涨的那一段叠一层逐渐消失的白色。
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

            // 能量前沿的亮点（没满时才画）。Front.png 里：x 2 是暗端亮边、x 3 是它的光晕，x 4 是光端光晕、x 5 是光端亮边。
            Texture2D front = Texture("Front");
            if (darkWidth > 0 && darkWidth < FillLength)
                spriteBatch.Draw(front, topLeft + new Vector2(DarkFillLeft + darkWidth - 1, FillTop), new Rectangle(2, 0, 2, FillHeight), Color.White * alpha);
            if (lightWidth > 0 && lightWidth < FillLength)
                spriteBatch.Draw(front, topLeft + new Vector2(LightFillRight - lightWidth - 1, FillTop), new Rectangle(4, 0, 2, FillHeight), Color.White * alpha);
        }

        // 节点爆闪：flash 从 1 降到 0。一团白光、一个四芒星（白色十字加金紫两色的斜十字），再加一圈向外扩散的金紫火星。
        private static void DrawBurst(Vector2 node, float flash, float alpha)
        {
            SpriteBatch spriteBatch = Main.spriteBatch;
            DrawGlow(node, new Vector2(0.35f + 0.55f * (1f - flash)), TwoPathsHelper.Additive(Color.White, 0.55f * flash * alpha));

            // Extra_98 是原版的一根细长光线贴图，拉伸后当星芒用。
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

        // 以 Glow.png（一团柔和光斑）为中心画一个光晕。
        private static void DrawGlow(Vector2 position, Vector2 scale, Color color)
        {
            Texture2D glow = Texture("Glow");
            Main.spriteBatch.Draw(glow, position, null, color, 0f, glow.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private static Texture2D Texture(string name) => ModContent.Request<Texture2D>(TexturePath + name, AssetRequestMode.ImmediateLoad).Value;
    }
}
