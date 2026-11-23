using InfernalEclipseWeaponsDLC.Content.Items.Weapons.Healer.Melee.Scythes;
using InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Core.Players
{
    // 【平时手持】不攻击时把神冥歧径握在手里。
    // 瑟银的镰刀都设了 noUseGraphic，原版从来不画手里的这把武器，所以这里自己画：
    // - 前手握在杆身中点往暗端挪 GripTowardDark 个贴图像素的位置，斜度和物品贴图本身一致；
    //   光刃、光环和天使翼在头后上方，暗刃和蝠翼在身前下方（可以插进地面，用户说没关系），整体轻微摆动。
    // - 画在身体后面（BeforeParent Skin），刀刃和翅膀不会挡住人物；前臂画在镰刀上面，看起来是手握住了杆。
    // - 用的是 20 帧动态贴图和它的发光层，所以手里也能看到闪电、光环、滴血。
    // - 尺寸和攻击时一样（TwoPathsHelper.SwingScale），出手时镰刀不会突然变大变小。
    // 前臂的姿势在 TwoPathsPlayer.PostUpdate 里设置（用的是这里的 ShowsIdleHold 和 ArmRotation）。
    public class TwoPathsHoldLayer : PlayerDrawLayer
    {
        private const float Lean = 0.8f;           // 从竖直向上往身后倾斜的角度（弧度），大约等于物品贴图本身的斜度
        private const float HandAngle = 0.35f;     // 前臂比水平略往下多少（弧度）
        private const float GripTowardDark = 8f;   // 握点从杆身中点往暗端挪的贴图像素数；效果是整把镰刀相对手往光端移一点


        // 前臂向前伸、略低于水平，去够握点（复合手臂的角度里 0 是竖直朝下）。
        public static float ArmRotation(Player player) => (MathHelper.PiOver2 - HandAngle) * -player.direction;

        public override Position GetDefaultPosition() => new BeforeParent(PlayerDrawLayers.Skin);

        public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
        {
            //残影、玩家对应状态不绘制，有WeaponOutLite不绘制
            if (drawInfo.shadow == 0f)
            {
                Player player = drawInfo.drawPlayer;

                if (player.dead || player.itemAnimation > 0 || player.HeldItem.type != ModContent.ItemType<TwoPaths>() || ModLoader.HasMod("WeaponOutLite"))
                    return false;

                return player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsDarkHeld>()] == 0
                    && player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsLightHeld>()] == 0
                    && player.ownedProjectileCounts[ModContent.ProjectileType<TwoPathsBalanceSpin>()] == 0;
            }

            return false;

        }

        protected override void Draw(ref PlayerDrawSet drawInfo)
        {
            Player player = drawInfo.drawPlayer;
            int type = ModContent.ItemType<TwoPaths>();
            //此为帧图
            Texture2D texture = TextureAssets.Item[type].Value;

            //当前动画帧，和物品栏里播放的是同一帧。
            Rectangle frame = Main.itemAnimations[type]?.GetFrame(texture) ?? texture.Frame();

            Vector2 hand = player.GetFrontHandPosition(Player.CompositeArmStretchAmount.Full, ArmRotation(player));
            Vector2 position = (hand + new Vector2(0f, player.gfxOffY) - Main.screenPosition).Floor();

            // 轻微摆动：振幅 0.04 弧度。lightRotation 是光刃所指的方向。
            float sway = 0.04f * MathF.Sin(Main.GlobalTimeWrappedHourly * 2f);
            float lightRotation = -MathHelper.PiOver2 - (Lean + sway) * player.direction;

            // 受环境光影响，但提亮 40%，免得在暗处整把镰刀黑掉。
            Color color = Color.Lerp(Lighting.GetColor(hand.ToTileCoordinates()), Color.White, 0.4f);

            //玩家朝向
            bool flip = player.direction < 0;

            //先本体再亮层
            // 和攻击时同一尺寸，出手时镰刀不会突然变大变小。
            float scale = player.GetAdjustedItemScale(player.HeldItem) * TwoPathsHelper.SwingScale;
            drawInfo.DrawDataCache.Add(HeldFrameDrawData(texture, frame, position, lightRotation, GripTowardDark, scale, color, flip));

            // 发光部分（闪电、光环光晕、刃口高光）以全亮再画一遍。试过再加一层叠加混合，金色和紫色都被冲成了白色，所以没有加。
            Texture2D glow = ModContent.Request<Texture2D>(ModContent.GetInstance<TwoPaths>().Texture + "_Glow").Value;
            drawInfo.DrawDataCache.Add(HeldFrameDrawData(glow, frame, position, lightRotation, GripTowardDark, scale, Color.White, flip));
        }

        private DrawData HeldFrameDrawData(Texture2D texture, Rectangle frame, Vector2 screenPosition, float lightRotation, float gripTowardDark, float scale, Color color, bool flip)
        {
            //135° 左上角为光端方向
            Vector2 startDirection = Vector2.Normalize(-Vector2.One);
            Vector2 origin = frame.Size() * 0.5f - startDirection * gripTowardDark;
            float axis = startDirection.ToRotation();
            SpriteEffects effects = SpriteEffects.None;
            if (flip)
            {
                origin.X = frame.Width - origin.X;
                axis = MathHelper.Pi - startDirection.ToRotation();
                effects = SpriteEffects.FlipHorizontally;
            }
            return new DrawData(texture, screenPosition, frame, color, lightRotation - axis, origin, scale, effects, 0f);
        }
    }
}
