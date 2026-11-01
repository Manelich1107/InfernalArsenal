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
    // ai[0] = attack id, ai[1] = length scale (0 means full length; spawners scale the speed by the same factor),
    // ai[2] = 1 from the balance spin: every enemy it strikes keeps getting erosion marks (TwoPathsErosion).
    public class TwoPathsJudgmentSlash : ModProjectile
    {
        private const int Lifetime = 60;
        private const float FullLength = 480f;
        private const float ErosionDamage = 0.3f;

        public override string Texture => "Terraria/Images/Extra_98";

        private float VisualLength => FullLength * (Projectile.ai[1] > 0f ? Projectile.ai[1] : 1f);
        private float Life => 1f - Projectile.timeLeft / (float)Lifetime;
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
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

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

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float half = VisualLength * 0.4f * Envelope;
            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitX);
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - direction * half, Projectile.Center + direction * half, 20f, ref collisionPoint);
        }

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

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;
            float length = VisualLength * Envelope / texture.Height + 0.05f;
            float rotation = Projectile.rotation + MathHelper.PiOver2;
            Vector2 position = Projectile.Center - Main.screenPosition;

            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(TwoPathsHelper.LightGold, Envelope), rotation, origin, new Vector2(0.7f, length), SpriteEffects.None, 0f);
            Main.EntitySpriteDraw(texture, position, null, TwoPathsHelper.Additive(Color.White, Envelope), rotation, origin, new Vector2(0.3f, length * 0.85f), SpriteEffects.None, 0f);
            return false;
        }
    }
}
