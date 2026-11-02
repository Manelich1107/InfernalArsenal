using System;
using System.Collections.Generic;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // 刀光拖影横截面上的一个颜色节点：Radius 是到圆心的距离占攻击距离的比例（0 是手，1 是刀尖），
    // Color 是预乘颜色（普通混合的节点照常写，叠加混合的节点 A = 0，见 TwoPathsHelper.Additive）。
    public readonly record struct TrailStop(float Radius, Color Color);

    // 【神冥歧径的公用工具】几个攻击弹幕、平时手持、能量条都会用到：
    // - 贴图路径。
    // - 握点几何：在 TwoPaths.png 上量出的两端刀颈位置，由此算出杆身中点、每一半的握点、刀身朝向和攻击距离。
    // - 统一尺寸 SwingScale：所有攻击和平时手持都按这个大小画。
    // - 配色、拖影的分层配方，以及画刀光拖影的 DrawSwingTrail。
    // - 画半把镰刀（DrawScythe）、整把镰刀（DrawFullScythe）、手持帧（HeldFrameDrawData）。
    // - 命中报能量（Credit）和限流的命中音（PlayImpactSound）。
    public static class TwoPathsHelper
    {
        // 整把镰刀（攻击弹幕和合一开场用），以及光端、暗端两半。两半和整把在同一张 208×190 的画布上，叠在一起就是整把。
        public const string TexturePath = "InfernalEclipseWeaponsDLC/Content/Items/Weapons/Healer/Melee/Scythes/TwoPaths";
        public const string LightHalfPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsHalfLight";
        public const string DarkHalfPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsHalfDark";
        // 左键的大虚影：暗端刀头加一小截杆，杆往手的方向渐隐，画在暗端那一半的画布上（所以握点、角度和暗端那一半通用）。
        public const string PhantomDarkPath = "InfernalEclipseWeaponsDLC/Content/Projectiles/HealerPro/Scythes/TwoPathsPhantomDark";

        // 在 TwoPaths.png（208×190）上量出的两端刀颈（刀刃和杆身相接处）的贴图坐标。
        // 每一半都保留整根杆，在被去掉的另一端刀颈前 7 像素处收尾，并带一个柄头（由仓库外的 tools/split_halves.py 切出；
        // 这就是现在两半握把处的“灰色圆点”，在重绘清单里）。挥砍时手握在离柄尾 10 像素的地方。
        // 重画两半贴图时如果改了柄尾位置，要同步改 HandleEndBeforeNeck 和 GripFromHandleEnd。
        private static readonly Vector2 LightNeck = new(62f, 48f);
        private static readonly Vector2 DarkNeck = new(150f, 133f);
        private const float HandleEndBeforeNeck = 7f;
        private const float GripFromHandleEnd = 10f;

        // 从半把镰刀的握点到刀尖的距离（贴图像素，1 倍尺寸时）。判定、拖影、火花的长度都以它为基准。
        public const float BladeReach = 150f;
        // 所有攻击和平时手持都按这个尺寸画（再乘物品的近战尺寸加成），切换攻击时镰刀不会变大变小。
        public const float SwingScale = 1.2f;
        // 拖影相邻两个采样点之间的最大角度（弧度），超过就插值补点，保证弧线圆滑。
        private const float TrailMaxStep = 0.09f;
        // 两次命中音之间至少隔多少 tick。
        private const uint ImpactSoundGap = 12;

        private static uint lastImpactSound;

        // 画拖影用的着色器（顶点颜色），第一次用时创建，卸载时释放。
        private static BasicEffect trailEffect;

        // 由刀颈坐标推出的几何量：杆身中点、从暗端指向光端的单位向量、每一半握点离中点多远、
        // 两半各自的握点（光端那一半握在靠暗端的柄尾附近，暗端那一半反过来）、刀身在贴图里的朝向角。
        private static readonly Vector2 Middle = (LightNeck + DarkNeck) / 2f;
        private static readonly Vector2 LightDirection = Vector2.Normalize(LightNeck - DarkNeck);
        private static readonly float GripFromMiddle = Vector2.Distance(LightNeck, DarkNeck) / 2f - HandleEndBeforeNeck - GripFromHandleEnd;
        private static readonly Vector2 LightGrip = Middle - LightDirection * GripFromMiddle;
        private static readonly Vector2 DarkGrip = Middle + LightDirection * GripFromMiddle;
        private static readonly float LightAxis = LightDirection.ToRotation();
        private static readonly float DarkAxis = (-LightDirection).ToRotation();

        // 整把镰刀握在杆身中点时，从中点到任一端刀尖的距离（1 倍尺寸）。平衡旋转用。
        public static readonly float FullReach = BladeReach - GripFromMiddle;

        // 主题色：光端金色、暗端深红、暗能量紫色。能量条、裂隙、拖影等共用。
        public static readonly Color LightGold = new(255, 210, 110);
        public static readonly Color DarkCrimson = new(200, 20, 40);
        public static readonly Color DarkViolet = new(178, 102, 255);
        private static readonly Color DarkTrailTint = new(150, 0, 26);
        // 光的命中音是清脆的水晶声；暗的命中音用火焰爆裂声（写在各个暗攻击弹幕里）。
        public static readonly SoundStyle LightImpactSound = SoundID.DD2_CrystalCartImpact with { Pitch = 0.2f, Volume = 0.6f, PitchVariance = 0.15f };
        // 拖影外缘那条实线的颜色。
        private static readonly Color DarkRim = new(170, 0, 30);
        private static readonly Color LightRim = new(225, 150, 40);

        // 拖影的分层配方，给 DrawSwingTrail 用。每一层是一组从内到外的颜色节点，按顺序叠画。
        // 暗：① 普通混合的主体（靠手处透明，中段黑红，往外缘变成深红）；② 外缘一条实心红线；③ 外缘的叠加红光。
        // 外缘红线先用普通混合画，所以在亮背景上仍是红色；只用叠加混合的话，红色在亮背景上会变成肉色。
        public static readonly TrailStop[][] DarkTrail =
        {
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Shaded(DarkTrailTint, 0.25f, 0.57f)),
                new(0.75f, Shaded(DarkTrailTint, 0.6f, 1f)),
                new(0.95f, Shaded(DarkTrailTint, 1f, 1f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, DarkRim), new(0.995f, Color.Transparent) },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, Additive(DarkCrimson, 0.7f)), new(0.995f, Color.Transparent) },
        };

        // 光：① 半透明的金色主体；② 越往外越亮的叠加金光；③ 外缘一条实心金线；④ 外缘的白热细芯。
        // 普通混合的几层保证在亮背景上也看得清，只靠叠加的金色在亮背景上会被冲淡。
        public static readonly TrailStop[][] LightTrail =
        {
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Shaded(LightGold, 1f, 0.23f)),
                new(0.75f, Shaded(LightGold, 1f, 0.4f)),
                new(0.95f, Shaded(LightGold, 1f, 0.4f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[]
            {
                new(0.25f, Color.Transparent),
                new(0.5f, Additive(LightGold, 0.08f)),
                new(0.75f, Additive(LightGold, 0.33f)),
                new(0.95f, Additive(LightGold, 0.55f)),
                new(1f, Color.Transparent),
            },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, LightRim), new(0.995f, Color.Transparent) },
            new TrailStop[] { new(0.905f, Color.Transparent), new(0.95f, Additive(Color.White, 0.8f)), new(0.995f, Color.Transparent) },
        };

        // 叠加混合的颜色：在原版默认的预乘普通混合下，把 A 设成 0 就等于叠加发光。opacity 是亮度。
        public static Color Additive(Color color, float opacity = 1f)
        {
            color *= opacity;
            color.A = 0;
            return color;
        }

        // 普通混合拖影节点用的预乘颜色：tint 按 shade 压暗，不透明度为 alpha。
        public static Color Shaded(Color tint, float shade, float alpha) => new(new Vector4(tint.ToVector3() * shade * alpha, alpha));

        // 画一条刀光拖影：以 center 为圆心、半径 reach 的一段扇环，沿着刀身最近经过的角度（angles，新的在前，共 count 个）铺开。
        // - 拖影最多拖到刀身后方 maxAngle（弧度）处，越往后越淡，同时越旧越淡；所以它只覆盖刀真正扫过的地方，
        //   刀停下后会自己缩回刀身。
        // - opacity 是整体不透明度。layers 是分层配方（DarkTrail / LightTrail），每层按 TrailStop 在半径方向上铺颜色，按顺序叠画。
        // - 实现上是用顶点颜色画三角形网格，所以中途会结束 SpriteBatch，画完再按原版弹幕绘制的默认参数重新开始；
        //   只能在弹幕 PreDraw 这类默认绘制状态下调用。
        public static void DrawSwingTrail(Vector2 center, float[] angles, int count, float reach, float maxAngle, float opacity, params TrailStop[][] layers)
        {
            if (count < 2 || opacity <= 0f)
                return;

            // 补点：爆发时刀一 tick 能转约 48°，相邻两次记录之间插值补点，弧线才圆。
            // 同时算每个采样点的淡出系数 =（离刀身的角距离衰减）×（时间上的新旧衰减），到 maxAngle 处截断。
            List<float> sampleAngles = new();
            List<float> sampleFade = new();
            float travelled = 0f;
            bool capped = false;
            for (int i = 0; i < count - 1 && !capped; i++)
            {
                float span = angles[i + 1] - angles[i];
                float length = MathF.Abs(span);
                int steps = Math.Clamp((int)MathF.Ceiling(length / TrailMaxStep), 1, 16);
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    float distance = travelled + length * t;
                    if (distance >= maxAngle)
                        break;
                    sampleAngles.Add(angles[i] + span * t);
                    sampleFade.Add(MathF.Pow(1f - distance / maxAngle, 1.5f) * (1f - (i + t) / (count - 1)));
                }
                if (travelled + length >= maxAngle)
                {
                    sampleAngles.Add(angles[i] + MathF.Sign(span) * (maxAngle - travelled));
                    sampleFade.Add(0f);
                    capped = true;
                }
                travelled += length;
            }
            if (!capped)
            {
                sampleAngles.Add(angles[count - 1]);
                sampleFade.Add(0f);
            }

            // 设置着色器：世界坐标经过游戏的缩放矩阵，再投影到屏幕。
            GraphicsDevice device = Main.instance.GraphicsDevice;
            trailEffect ??= new BasicEffect(device) { VertexColorEnabled = true };
            trailEffect.World = Matrix.Identity;
            trailEffect.View = Main.GameViewMatrix.TransformationMatrix;
            trailEffect.Projection = Matrix.CreateOrthographicOffCenter(0f, Main.screenWidth, Main.screenHeight, 0f, -1f, 1f);

            Main.spriteBatch.End();
            device.BlendState = BlendState.AlphaBlend;
            device.RasterizerState = RasterizerState.CullNone;
            device.DepthStencilState = DepthStencilState.None;
            trailEffect.CurrentTechnique.Passes[0].Apply();

            // 每一层建一张网格：沿角度方向是 samples 个采样点，沿半径方向是这一层的节点数（rings），相邻四个顶点组成两个三角形。
            Vector2 origin = center - Main.screenPosition;
            int samples = sampleAngles.Count;
            foreach (TrailStop[] stops in layers)
            {
                int rings = stops.Length;
                var vertices = new VertexPositionColor[samples * rings];
                for (int k = 0; k < samples; k++)
                {
                    Vector2 direction = sampleAngles[k].ToRotationVector2();
                    for (int j = 0; j < rings; j++)
                        vertices[k * rings + j] = new VertexPositionColor(new Vector3(origin + direction * stops[j].Radius * reach, 0f), stops[j].Color * (sampleFade[k] * opacity));
                }

                var indices = new short[(samples - 1) * (rings - 1) * 6];
                int n = 0;
                for (int k = 0; k < samples - 1; k++)
                {
                    for (int j = 0; j < rings - 1; j++)
                    {
                        short a = (short)(k * rings + j);
                        short c = (short)(a + rings);
                        indices[n++] = a;
                        indices[n++] = (short)(a + 1);
                        indices[n++] = c;
                        indices[n++] = (short)(a + 1);
                        indices[n++] = (short)(c + 1);
                        indices[n++] = c;
                    }
                }
                device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, indices.Length / 3);
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        // 卸载模组时释放着色器（图形资源必须在主线程释放）。由 TwoPathsSystem.Unload 调用。
        public static void Unload()
        {
            BasicEffect effect = trailEffect;
            trailEffect = null;
            if (effect != null)
                Main.QueueMainThreadAction(effect.Dispose);
        }

        // 画半把镰刀：挥砍时只画出手的那一端（activeEnd），握点放在 gripWorld，刀身指向 bladeRotation。
        // 往回挥（角度在减小）时传 mirror = true，贴图竖直镜像，让弯刃的一侧始终在前。
        // texturePath 可以换成另一张按同一半的画布排版的贴图（比如左键虚影 PhantomDarkPath）。
        public static void DrawScythe(Vector2 gripWorld, float bladeRotation, TwoPathsSide activeEnd, bool mirror, float scale, Color color, string texturePath = null)
        {
            bool light = activeEnd == TwoPathsSide.Light;
            Texture2D texture = ModContent.Request<Texture2D>(texturePath ?? (light ? LightHalfPath : DarkHalfPath), AssetRequestMode.ImmediateLoad).Value;
            Vector2 origin = light ? LightGrip : DarkGrip;
            float axis = light ? LightAxis : DarkAxis;
            SpriteEffects effects = SpriteEffects.None;
            if (mirror)
            {
                origin.Y = texture.Height - origin.Y;
                axis = -axis;
                effects = SpriteEffects.FlipVertically;
            }

            // 旋转 = 目标朝向 - 刀身在贴图里本来的朝向。
            Main.EntitySpriteDraw(texture, gripWorld - Main.screenPosition, null, color, bladeRotation - axis, origin, scale, effects, 0f);
        }

        // 画整把双头镰：握在杆身中点（middleWorld），光刃指向 lightRotation。
        // 两半和整把共用 TwoPaths.png 的画布，所以 texturePath 传某一半时，就只在同一位置画出那一半（合一开场就是这样用的）。
        public static void DrawFullScythe(Vector2 middleWorld, float lightRotation, float scale, Color color, string texturePath = TexturePath)
        {
            Texture2D texture = ModContent.Request<Texture2D>(texturePath, AssetRequestMode.ImmediateLoad).Value;
            Main.EntitySpriteDraw(texture, middleWorld - Main.screenPosition, null, color, lightRotation - LightAxis, Middle, scale, SpriteEffects.None, 0f);
        }

        // 平时手持用：物品动态贴图里的一帧（排版和 TwoPaths.png 一样），握点在杆身中点往暗端 gripTowardDark 个贴图像素处，
        // 光刃指向 lightRotation。flip 是角色朝左时水平镜像。返回给玩家绘制层用的 DrawData。
        public static DrawData HeldFrameDrawData(Texture2D texture, Rectangle frame, Vector2 screenPosition, float lightRotation, float gripTowardDark, float scale, Color color, bool flip)
        {
            Vector2 origin = Middle - LightDirection * gripTowardDark;
            float axis = LightAxis;
            SpriteEffects effects = SpriteEffects.None;
            if (flip)
            {
                origin.X = frame.Width - origin.X;
                axis = MathHelper.Pi - LightAxis;
                effects = SpriteEffects.FlipHorizontally;
            }
            return new DrawData(texture, screenPosition, frame, color, lightRotation - axis, origin, scale, effects, 0f);
        }

        // 弹幕命中时报给能量系统。只在弹幕主人的客户端执行（能量是每个玩家自己的）。
        public static void Credit(Projectile projectile, TwoPathsSide side, int attackId)
        {
            if (projectile.owner == Main.myPlayer)
                Main.player[projectile.owner].GetModPlayer<TwoPathsPlayer>().CreditHit(side, attackId);
        }

        // 命中音：只由弹幕主人播放，并且至少隔 ImpactSoundGap tick 才响一次，
        // 免得一刀打中一群敌人、或者平衡旋转连续命中时，声音叠成一片噪音。
        public static void PlayImpactSound(Projectile projectile, in SoundStyle style, Vector2 position)
        {
            if (projectile.owner != Main.myPlayer || Main.GameUpdateCount - lastImpactSound < ImpactSoundGap)
                return;
            lastImpactSound = Main.GameUpdateCount;
            SoundEngine.PlaySound(style, position);
        }
    }
}
