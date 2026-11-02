using System;
using InfernalEclipseWeaponsDLC.Core.Players;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using ThoriumMod;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // 【侵蚀斩痕】平衡旋转的审判斩打中敌人后，由审判斩生成，跟在这个敌人身上。
    // 每 20 tick 浮现一道短的紫黑斩痕，每道角度随机，共 3 道（约 1 秒），只伤害这一个敌人。
    // 作用是让光攻击在平衡时也能持续输出（光暗互补）。敌人死亡或消失时提前结束。
    // ai[0] = 出手编号（平衡旋转的编号，不充能），ai[1] = 目标 NPC 的索引。
    public class TwoPathsErosion : ModProjectile
    {
        // 斩痕道数、每道的间隔、每道显示多久（tick）。
        private const int Marks = 3;
        private const int MarkInterval = 20;
        private const int MarkTime = 14;

        // 斩痕边缘的深紫色。
        private static readonly Color DeepViolet = new(90, 20, 160);

        // 原版的一根细长光线贴图，拉伸后当斩痕用。
        public override string Texture => "Terraria/Images/Extra_98";

        private NPC Target => Main.npc[(int)Projectile.ai[1]];
        // 存在了多少 tick、当前这道斩痕的角度。
        private ref float Age => ref Projectile.localAI[0];
        private ref float MarkAngle => ref Projectile.localAI[1];
        // 当前这道斩痕的显示强度：每个间隔的前 MarkTime tick 按正弦先亮后暗，其余时间为 0。
        private float Envelope => Age % MarkInterval < MarkTime ? MathF.Sin(MathHelper.Pi * (Age % MarkInterval) / MarkTime) : 0f;
        // 斩痕长度随敌人体型变化。
        private float MarkLength => MathHelper.Max(Target.width, Target.height) * 1.5f + 90f;

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Marks * MarkInterval;
            // 每道斩痕最多打一次。
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = MarkInterval;
        }

        // 位置每 tick 直接设到敌人中心，不按 velocity 移动。
        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            if (!Target.active || Target.life <= 0)
            {
                Projectile.Kill();
                return;
            }

            Projectile.Center = Target.Center;
            // 每道斩痕开始时：换一个随机角度，播放音效（同时只响一个），沿斩痕撒一排暗影焰尘埃。
            if (Age % MarkInterval == 0f)
            {
                MarkAngle = Main.rand.NextFloat(MathHelper.TwoPi);
                SoundEngine.PlaySound(SoundID.Item103 with { Pitch = 0.2f, Volume = 0.3f, MaxInstances = 1, SoundLimitBehavior = SoundLimitBehavior.IgnoreNew }, Projectile.Center);
                Vector2 direction = MarkAngle.ToRotationVector2();
                for (int i = 0; i < 12; i++)
                {
                    Dust dust = Dust.NewDustPerfect(Projectile.Center + direction * Main.rand.NextFloat(-0.5f, 0.5f) * MarkLength, DustID.Shadowflame, direction.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-4f, 4f));
                    dust.noGravity = true;
                    dust.scale = 1.4f;
                }
            }
            Age++;
        }

        // 只能打中自己跟着的那个敌人。
        public override bool? CanHitNPC(NPC target) => target.whoAmI == (int)Projectile.ai[1] ? null : false;

        // 斩痕亮到一半以上时才造成伤害。
        public override bool? CanDamage() => Envelope > 0.5f ? null : false;

        // 判定：以敌人为中心、沿斩痕方向的线段，粗 24。
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 half = MarkAngle.ToRotationVector2() * MarkLength * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - half, Projectile.Center + half, 24f, ref collisionPoint);
        }

        // 报给能量系统（平衡旋转的编号，不充能，只重置衰减计时）。
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, (int)Projectile.ai[0]);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float envelope = Envelope;
            if (envelope <= 0f)
                return false;

            // 三层：叠加混合的宽紫光 → 普通混合的深紫边 → 普通混合的黑芯。
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            Vector2 position = Projectile.Center - Main.screenPosition;
            float rotation = MarkAngle + MathHelper.PiOver2;
            float length = MarkLength / texture.Height;
            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, envelope), rotation, origin, new Vector2(2.4f * envelope, length * 1.05f), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, DeepViolet * envelope, rotation, origin, new Vector2(1.4f * envelope, length), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, Color.Black * envelope, rotation, origin, new Vector2(0.8f * envelope, length * 0.92f), SpriteEffects.None, 0f);
            return false;
        }
    }
}
