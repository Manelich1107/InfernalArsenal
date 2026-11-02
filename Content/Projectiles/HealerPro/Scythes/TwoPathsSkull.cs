using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // 【冤魂】左键每刀和平衡旋转放出的血色骷髅：从玩家胸口迸出，带一小团血尘，先减速，然后追踪附近的敌人。
    // - 贴图在运行时借用灾厄盖尔大剑的“盖尔鲜血头颅”（CalamityMod/Projectiles/Melee/GaelSkull，5 帧），缩小到 0.6 倍。
    //   不能把这张图复制进本仓库（许可问题），只能像这样运行时引用。这一项在重绘清单里，等原创贴图替换。
    // - 飞出后前 14 tick 每 tick 减速 10%；之后找 720 像素内最近的敌人，以 17 的速度平滑转向追过去；找不到敌人就在 30 tick 内淡出消失。
    // - 命中一次就消失，给敌人挂 2 秒暗影焰。
    // ai[0] = 出手编号（充能用：左键放的充暗能量，平衡旋转放的不充能）。
    public class TwoPathsSkull : ModProjectile
    {
        private const int LaunchTicks = 14;
        private const int FadeTicks = 30;
        private const float SeekRange = 720f;
        private const float SeekSpeed = 17f;

        // 存在了多少 tick。
        private ref float Age => ref Projectile.localAI[0];
        // 最后 FadeTicks tick 里淡出。
        private float Opacity => Projectile.timeLeft < FadeTicks ? Projectile.timeLeft / (float)FadeTicks : 1f;

        public override string Texture => "CalamityMod/Projectiles/Melee/GaelSkull";

        public override void SetStaticDefaults()
        {
            // 竖排 5 帧；记录最近 6 个位置给残影用。
            Main.projFrames[Type] = 5;
            ProjectileID.Sets.TrailCacheLength[Type] = 6;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.scale = 0.6f;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            // 命中一个敌人就消失。
            Projectile.penetrate = 1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            // 最长存在 200 tick。
            Projectile.timeLeft = 200;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            Age++;
            // 刚出生时朝飞行方向喷一小团血尘。
            if (Age == 1)
            {
                for (int i = 0; i < 8; i++)
                {
                    Dust dust = Dust.NewDustPerfect(Projectile.Center, i % 2 == 0 ? DustID.Blood : DustID.CrimsonTorch, Projectile.velocity.RotatedByRandom(0.6f) * Main.rand.NextFloat(0.15f, 0.45f), 0, default, 1.3f);
                    dust.noGravity = i % 2 == 1;
                }
            }
            // 迸出阶段：减速。之后追踪：速度向“指向目标、大小 SeekSpeed”的方向插值 12%，转弯是平滑的弧线。
            if (Age <= LaunchTicks)
                Projectile.velocity *= 0.9f;
            else
            {
                NPC target = FindTarget();
                if (target != null)
                {
                    Vector2 desired = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX) * SeekSpeed;
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.12f);
                }
                else if (Projectile.timeLeft > FadeTicks)
                    Projectile.timeLeft = FadeTicks;
            }

            // 贴图的骷髅脸朝右；往左飞时水平翻转，保证脸朝前、不倒过来。
            Projectile.spriteDirection = Projectile.velocity.X >= 0f ? 1 : -1;
            Projectile.rotation = Projectile.spriteDirection > 0 ? Projectile.velocity.ToRotation() : (-Projectile.velocity).ToRotation();
            // 每 5 tick 换一帧。
            if (++Projectile.frameCounter % 5 == 0)
                Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];

            // 身后偶尔飘出粉红色的血雨尘埃。
            if (Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(6f, 6f), DustID.Rain_BloodMoon, -Projectile.velocity * 0.15f, 0, new Color(188, 126, 154), 1.1f * Opacity);
                dust.noGravity = true;
            }
        }

        // SeekRange 内最近的可追踪敌人（不检查视线，可以穿墙追）。
        private NPC FindTarget()
        {
            NPC best = null;
            float bestDistance = SeekRange * SeekRange;
            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (!npc.CanBeChasedBy())
                    continue;
                float distance = npc.DistanceSQ(Projectile.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = npc;
                }
            }
            return best;
        }

        // 命中：报暗能量（同一次出手只算一次），挂 120 tick 暗影焰。
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Dark, (int)Projectile.ai[0]);
            target.AddBuff(BuffID.ShadowFlame, 120);
        }

        // 消失时散开一圈血雨尘埃。
        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 8; i++)
            {
                Dust dust = Dust.NewDustPerfect(Projectile.Center, DustID.Rain_BloodMoon, Main.rand.NextVector2Circular(3f, 3f), 0, new Color(188, 126, 154), 1.2f);
                dust.noGravity = true;
            }
        }

        // 先画最近几个位置的半透明残影（越旧越淡），再画本体。受环境光影响，但提亮 50%。
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Rectangle frame = texture.Frame(1, Main.projFrames[Type], 0, Projectile.frame);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Color color = Color.Lerp(lightColor, Color.White, 0.5f) * Opacity;

            for (int i = Projectile.oldPos.Length - 1; i >= 1; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                float fade = 1f - i / (float)Projectile.oldPos.Length;
                Main.EntitySpriteDraw(texture, Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition, frame, color * (0.5f * fade), Projectile.rotation, origin, Projectile.scale, effects, 0f);
            }
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, color, Projectile.rotation, origin, Projectile.scale, effects, 0f);
            return false;
        }
    }
}
