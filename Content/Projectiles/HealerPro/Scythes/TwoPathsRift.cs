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
using ThoriumMod.Buffs;

namespace InfernalEclipseWeaponsDLC.Content.Projectiles.HealerPro.Scythes
{
    // 【裂隙】一道固定在原地、先张开再合上的直线裂口，张开期间对沿线的敌人造成伤害。三种样式：
    // - 光裂隙（LightStyle）：白芯金边。右键每刀在光标处放一道，延迟 30 tick（0.5 秒）才裂开，算审判斩的回响。
    // - 暗裂隙（DarkStyle）：黑芯血边，夹一点紫色。左键大旋转终结时开在附近每个敌人身上，几乎立刻裂开（延迟 4 tick）。
    // - 平衡裂隙（BalanceDarkStyle）：暗裂隙换成金边，裂开 8 tick 后自己放一道半长的审判斩从垂直方向交叉过去，形成 X。
    //   平衡旋转的 X 脉冲和终结用这个。
    // 暗裂隙和平衡裂隙命中时给敌人挂 3 秒瑟银的光之咒。
    // ai[0] = 出手编号（充能用），ai[1] = 样式，ai[2] = 长度（像素）；velocity 只存方向（不移动）。
    public class TwoPathsRift : ModProjectile
    {
        public const int LightStyle = 0;
        public const int DarkStyle = 1;
        public const int BalanceDarkStyle = 2;
        // 从开始裂开到完全合上用 30 tick。
        private const int OpenTime = 30;
        // 平衡裂隙裂开后多久放交叉的审判斩，以及那道审判斩的长度倍数。
        private const int CrossDelay = 8;
        private const float CrossSlashScale = 0.5f;

        // 暗裂隙的暗红色主体。
        private static readonly Color DarkBody = new(70, 0, 14);

        // 原版的一根细长光线贴图，拉伸后当裂口用。
        public override string Texture => "Terraria/Images/Extra_98";

        private int Style => (int)Projectile.ai[1];
        private bool IsDark => Style == DarkStyle || Style == BalanceDarkStyle;
        // 裂开前的延迟（tick）：暗的几乎立刻裂开，光的晚 0.5 秒。
        private int Delay => IsDark ? 4 : 30;
        private float Length => Projectile.ai[2];
        // 存在了多少 tick。
        private ref float Age => ref Projectile.localAI[0];

        // 张开程度 0~1：延迟期间为 0，之后按正弦先张开再合上。
        private float Openness => Age < Delay ? 0f : MathF.Sin(MathHelper.Pi * MathHelper.Clamp((Age - Delay) / (float)OpenTime, 0f, 1f));

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            // 实际由 AI 里的 Age 结束，这里只是上限。
            Projectile.timeLeft = 120;
            // 每个敌人单独计无敌帧，张开期间每 12 tick 可以再打一次。
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
        }

        // 固定在原地，不按 velocity 移动。
        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            if (Age == Delay)
                // 裂开的音效。同时裂开的一批裂隙（终结或 X 脉冲）只响一声：MaxInstances = 1，新的直接忽略。
                SoundEngine.PlaySound((IsDark ? SoundID.Item103 : SoundID.Item45) with { Pitch = IsDark ? -0.3f : 0.3f, Volume = 0.7f, MaxInstances = 1, SoundLimitBehavior = SoundLimitBehavior.IgnoreNew }, Projectile.Center);

            // 平衡裂隙：裂开 CrossDelay tick 后，放一道半长的审判斩大致从垂直方向（±20° 随机）交叉过去，和裂隙在敌人身上组成 X。
            if (Style == BalanceDarkStyle && Age == Delay + CrossDelay && Projectile.owner == Main.myPlayer)
            {
                Vector2 cross = Projectile.velocity.SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.PiOver2 + Main.rand.NextFloat(-0.35f, 0.35f));
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center - cross * 480f * CrossSlashScale, cross * 16f * CrossSlashScale, ModContent.ProjectileType<TwoPathsJudgmentSlash>(), Projectile.damage, Projectile.knockBack, Projectile.owner, Projectile.ai[0], CrossSlashScale);
            }

            // 张开时沿裂口撒尘埃：暗的用暗影焰，光的用金焰。
            if (Openness > 0.2f && Main.rand.NextBool(2))
            {
                Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitX);
                Dust dust = Dust.NewDustPerfect(Projectile.Center + direction * Main.rand.NextFloat(-0.5f, 0.5f) * Length, IsDark ? DustID.Shadowflame : DustID.GoldFlame, direction.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-2f, 2f));
                dust.noGravity = true;
            }

            if (++Age >= Delay + OpenTime)
                Projectile.Kill();
        }

        // 张开超过 25% 才造成伤害。
        public override bool? CanDamage() => Openness > 0.25f ? null : false;

        // 判定：以中心为中点、长 Length 的线段，粗细随张开程度变化（最粗 30）。
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 half = Projectile.velocity.SafeNormalize(Vector2.UnitX) * Length * 0.5f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - half, Projectile.Center + half, 30f * Openness, ref collisionPoint);
        }

        // 命中：按样式报暗或光能量（同一次出手只算一次）。暗裂隙和平衡裂隙再挂 180 tick 光之咒。
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, IsDark ? TwoPathsSide.Dark : TwoPathsSide.Light, (int)Projectile.ai[0]);
            if (IsDark)
                target.AddBuff(ModContent.BuffType<LightCurse>(), 180);
        }

        // 用同一根光线贴图叠几层，宽度随张开程度变化。
        public override bool PreDraw(ref Color lightColor)
        {
            float open = Openness;
            if (open <= 0f)
                return false;

            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            Vector2 position = Projectile.Center - Main.screenPosition;
            // 贴图是竖着的，转 90° 让它沿裂口方向。
            float rotation = Projectile.rotation + MathHelper.PiOver2;
            float length = Length / texture.Height;

            if (IsDark)
            {
                // 暗裂隙从外到内：叠加混合的细边（平衡裂隙是金色，普通暗裂隙是深红）→ 普通混合的暗红主体 → 一点紫光 → 普通混合的黑芯。
                // 主体和黑芯用普通混合，亮背景上也是暗的；只有细边用叠加混合发光。
                Color edge = Style == BalanceDarkStyle ? TwoPathsHelper.LightGold : TwoPathsHelper.DarkCrimson;
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(edge, open * 0.8f), rotation, origin, new Vector2(1.5f * open, length * 1.02f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, DarkBody * open, rotation, origin, new Vector2(1.2f * open, length), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.DarkViolet, open * 0.35f), rotation, origin, new Vector2(0.8f * open, length * 0.95f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, Color.Black * open, rotation, origin, new Vector2(0.5f * open, length * 0.9f), SpriteEffects.None, 0f);
            }
            else
            {
                // 光裂隙：金色外层、白色中层、更亮的白色细芯，全部叠加混合。
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, open), rotation, origin, new Vector2(1.2f * open, length), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, open * 0.9f), rotation, origin, new Vector2(0.6f * open, length * 0.95f), SpriteEffects.None, 0f);
                Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, open), rotation, origin, new Vector2(0.25f * open, length * 0.9f), SpriteEffects.None, 0f);
            }
            return false;
        }
    }
}
