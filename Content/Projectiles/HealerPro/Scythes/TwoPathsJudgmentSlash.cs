using System;
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
    // 【审判斩】一道快速划过的金白斩线。
    // 谁会放：右键每刀（穿过光标）、平衡旋转（每 0.5 秒穿过光标）、平衡裂隙（交叉成 X）、平衡终结的大 X。
    // - 生成方把它放在目标点后方 480 像素处，以 16 像素/更新的速度飞过去。extraUpdates = 6，每 tick 更新 7 次，
    //   一生 60 次更新（约 8.6 tick）共飞 960 像素，正好在生命中点、最长最亮的时候经过目标点。
    // - 长度和亮度按正弦包络先变大再变小；判定是沿飞行方向的一条线段，长度也跟着包络变。
    // - 每个敌人只会被同一道审判斩打中一次。
    // ai[0] = 出手编号（充能用），ai[1] = 长度倍数（0 表示 1 倍；生成方会把速度和出发距离按同一倍数放大，保证仍在中点经过目标），
    // ai[2] = 1 表示来自平衡旋转：打中的敌人身上会接着浮现侵蚀斩痕（TwoPathsErosion）。
    public class TwoPathsJudgmentSlash : ModProjectile
    {
        // 一生的更新次数（不是 tick，见上面 extraUpdates 的说明）。
        private const int Lifetime = 60;
        // 1 倍时的视觉长度（像素）。
        private const float FullLength = 480f;
        // 侵蚀斩痕的伤害 = 审判斩伤害 × 0.3。
        private const float ErosionDamage = 0.3f;

        // 原版的一根细长光线贴图，拉伸后当斩线用。
        public override string Texture => "Terraria/Images/Extra_98";

        private float VisualLength => FullLength * (Projectile.ai[1] > 0f ? Projectile.ai[1] : 1f);
        // 生命进度 0~1。
        private float Life => 1f - Projectile.timeLeft / (float)Lifetime;
        // 正弦包络：0 → 1 → 0，中点最大。
        private float Envelope => MathF.Sin(MathHelper.Pi * Life);

        public override void SetDefaults()
        {
            Projectile.width = 16;
            Projectile.height = 16;
            Projectile.friendly = true;
            Projectile.DamageType = ThoriumDamageBase<HealerDamage>.Instance;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.extraUpdates = 6;
            Projectile.timeLeft = Lifetime;
            // 每个敌人单独计无敌帧；-1 表示同一个弹幕对同一个敌人只打一次。
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        // 朝飞行方向，沿途偶尔撒金色尘埃。
        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            if (Main.rand.NextBool(3))
            {
                Dust dust = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(8f, 8f), DustID.GoldFlame, Projectile.velocity * 0.05f);
                dust.noGravity = true;
                dust.scale = 1.1f;
            }
        }

        // 判定：以弹幕为中心、沿飞行方向的线段，长度为视觉长度的 80% × 包络，粗 20。
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float half = VisualLength * 0.4f * Envelope;
            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - direction * half, Projectile.Center + direction * half, 20f, ref collisionPoint);
        }

        // 命中：报光能量（同一次出手只算一次），命中音（有限流），平衡旋转放的还会在敌人身上挂侵蚀斩痕，最后一圈金色尘埃。
        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            TwoPathsHelper.Credit(Projectile, TwoPathsSide.Light, (int)Projectile.ai[0]);
            TwoPathsHelper.PlayImpactSound(Projectile, TwoPathsHelper.LightImpactSound, target.Center);
            if (Projectile.ai[2] == 1f && Projectile.owner == Main.myPlayer)
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), target.Center, Vector2.Zero, ModContent.ProjectileType<TwoPathsErosion>(), (int)(Projectile.damage * ErosionDamage), 0f, Projectile.owner, Projectile.ai[0], target.whoAmI);
            for (int i = 0; i < 8; i++)
            {
                Dust dust = Dust.NewDustPerfect(target.Center, DustID.GoldFlame, Main.rand.NextVector2Circular(6f, 6f));
                dust.noGravity = true;
            }
        }

        // 画两层叠加混合：外层宽的金色，内层窄的白芯。长度和亮度都乘包络。
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            float length = VisualLength * Envelope / texture.Height + 0.05f;
            // 贴图是竖着的，转 90° 让它沿飞行方向。
            float rotation = Projectile.rotation + MathHelper.PiOver2;
            Vector2 position = Projectile.Center - Main.screenPosition;

            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, Envelope), rotation, origin, new Vector2(0.7f, length), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, Envelope), rotation, origin, new Vector2(0.3f, length * 0.85f), SpriteEffects.None, 0f);
            return false;
        }
    }
}
